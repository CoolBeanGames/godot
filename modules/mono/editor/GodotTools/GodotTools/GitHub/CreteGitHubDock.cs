using Godot;
using GodotTools.Internals;
using System;
using System.Collections.Generic;

namespace GodotTools.GitHub
{
    /// <summary>
    /// Bottom dock: git status/diff list, commit, push, pull, branch
    /// create/switch/merge, per-file revert. All work goes through
    /// <see cref="GitCli"/> (plain git CLI, no libgit).
    /// </summary>
    public partial class CreteGitHubDock : EditorDock
    {
        private enum PromptAction
        {
            None,
            NewBranch,
            MergeBranch,
        }

        private VBoxContainer _main = null!;
        private Label _emptyLabel = null!;
        private OptionButton _branchOption = null!;
        private Label _statusLabel = null!;
        private ItemList _changes = null!;
        private TextEdit _diff = null!;
        private LineEdit _commitMessage = null!;
        private Button _revertButton = null!;
        private AcceptDialog _promptDialog = null!;
        private LineEdit _promptLine = null!;
        private ConfirmationDialog _revertDialog = null!;
        private PromptAction _promptAction = PromptAction.None;
        private List<GitFileStatus> _currentFiles = new();

        public CreteGitHubDock()
        {
            Name = "GitHub".TTR();
            IconName = "VcsBranches";
            DefaultSlot = EditorDock.DockSlot.Bottom;
            AvailableLayouts = DockLayout.Horizontal | DockLayout.Floating;
            Global = false;
            Transient = true;
        }

        public override void _Ready()
        {
            base._Ready();

            var bottomPanelStylebox = EditorInterface.Singleton.GetBaseControl().GetThemeStylebox("BottomPanel", "EditorStyles");
            AddThemeConstantOverride("margin_top", -(int)bottomPanelStylebox.ContentMarginTop);
            AddThemeConstantOverride("margin_left", -(int)bottomPanelStylebox.ContentMarginLeft);
            AddThemeConstantOverride("margin_right", -(int)bottomPanelStylebox.ContentMarginRight);

            _emptyLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            AddChild(_emptyLabel);

            _main = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            AddChild(_main);

            var toolbar = new HBoxContainer();
            toolbar.AddThemeConstantOverride("separation", 6);
            _main.AddChild(toolbar);

            toolbar.AddChild(new Label { Text = "Branch:".TTR() });
            _branchOption = new OptionButton();
            _branchOption.ItemSelected += OnBranchSelected;
            toolbar.AddChild(_branchOption);

            var newBranchButton = new Button { Text = "New".TTR(), TooltipText = "Create and switch to a new branch".TTR() };
            newBranchButton.Pressed += () => ShowPrompt(PromptAction.NewBranch);
            toolbar.AddChild(newBranchButton);

            var mergeButton = new Button { Text = "Merge".TTR(), TooltipText = "Merge a branch into the current one".TTR() };
            mergeButton.Pressed += () => ShowPrompt(PromptAction.MergeBranch);
            toolbar.AddChild(mergeButton);

            toolbar.AddChild(new VSeparator());

            var pushButton = new Button { Text = "Push".TTR() };
            pushButton.Pressed += OnPushPressed;
            toolbar.AddChild(pushButton);

            var pullButton = new Button { Text = "Pull".TTR(), TooltipText = "Fast-forward pull".TTR() };
            pullButton.Pressed += OnPullPressed;
            toolbar.AddChild(pullButton);

            var refreshButton = new Button { Text = "Refresh".TTR() };
            refreshButton.Pressed += () => Refresh();
            toolbar.AddChild(refreshButton);

            var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            toolbar.AddChild(spacer);

            _statusLabel = new Label();
            toolbar.AddChild(_statusLabel);

            var split = new HSplitContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            _main.AddChild(split);

            var left = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            split.AddChild(left);
            left.AddChild(new Label { Text = "Changes".TTR() });
            _changes = new ItemList
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SelectMode = ItemList.SelectModeEnum.Multi,
            };
            _changes.ItemSelected += OnChangeSelected;
            left.AddChild(_changes);

            _revertButton = new Button { Text = "Revert selected".TTR(), Disabled = true };
            _revertButton.Pressed += OnRevertPressed;
            left.AddChild(_revertButton);

            var right = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            split.AddChild(right);
            right.AddChild(new Label { Text = "Diff".TTR() });
            _diff = new TextEdit
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                Editable = false,
            };
            right.AddChild(_diff);

            var commitBar = new HBoxContainer();
            commitBar.AddThemeConstantOverride("separation", 6);
            right.AddChild(commitBar);
            _commitMessage = new LineEdit
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                PlaceholderText = "Commit message".TTR(),
            };
            _commitMessage.TextSubmitted += _ => OnCommitPressed();
            commitBar.AddChild(_commitMessage);
            var commitButton = new Button { Text = "Commit".TTR(), TooltipText = "Stage all changes and commit".TTR() };
            commitButton.Pressed += OnCommitPressed;
            commitBar.AddChild(commitButton);

            _promptDialog = new AcceptDialog();
            _promptLine = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _promptDialog.AddChild(_promptLine);
            _promptDialog.Confirmed += OnPromptConfirmed;
            AddChild(_promptDialog);

            _revertDialog = new ConfirmationDialog();
            _revertDialog.Confirmed += OnRevertConfirmed;
            AddChild(_revertDialog);

            Refresh();
        }

        private static string ProjectDir() => ProjectSettings.GlobalizePath("res://");

        public void Refresh()
        {
            try
            {
                string? git = GitCli.FindGit();
                if (string.IsNullOrEmpty(git))
                {
                    ShowEmpty("git was not found on PATH. Set an explicit path in Editor Settings > Crete > GitHub.".TTR());
                    GD.Print("CreteGitHub: git=no repo=no branch= files=0");
                    return;
                }

                string dir = ProjectDir();
                if (!GitCli.IsRepository(dir))
                {
                    ShowEmpty("This project is not a git repository.".TTR());
                    GD.Print("CreteGitHub: git=yes repo=no branch= files=0");
                    return;
                }

                GitStatus? status = GitCli.GetStatus(dir);
                if (status == null)
                {
                    ShowEmpty("git status failed. See the console for details.".TTR());
                    GD.Print("CreteGitHub: git=yes repo=yes branch= files=0");
                    return;
                }

                _emptyLabel.Hide();
                _main.Show();

                IReadOnlyList<string> branches = GitCli.GetBranches(dir, out string current);
                _branchOption.Clear();
                int select = 0;
                for (int i = 0; i < branches.Count; i++)
                {
                    _branchOption.AddItem(branches[i], i);
                    if (branches[i] == current)
                        select = i;
                }
                if (branches.Count > 0)
                {
                    // Block the signal: filling the list is not a user switch.
                    _branchOption.SetBlockSignals(true);
                    _branchOption.Select(select);
                    _branchOption.SetBlockSignals(false);
                }

                _currentFiles = new List<GitFileStatus>(status.Files);
                _changes.Clear();
                foreach (GitFileStatus f in _currentFiles)
                    _changes.AddItem($"[{ShortCode(f)}] {f.Path}");
                _diff.Text = "";
                _revertButton.Disabled = true;

                string state = status.Files.Count == 0 ? "clean" : $"{status.Files.Count} changed";
                _statusLabel.Text = $"{status.Branch}  +{status.Ahead} -{status.Behind}  ({state})";
                SetTitle(status.Files.Count == 0 ? "GitHub" : $"GitHub ({status.Files.Count})");

                GD.Print($"CreteGitHub: git=yes repo=yes branch={status.Branch} " +
                    $"files={status.Files.Count} ahead={status.Ahead} behind={status.Behind}");
            }
            catch (Exception e)
            {
                GD.PushError("GitHub dock refresh failed: " + e.Message);
            }
        }

        private void ShowEmpty(string message)
        {
            _main.Hide();
            _emptyLabel.Text = message;
            _emptyLabel.Show();
            SetTitle("GitHub");
        }

        private static string ShortCode(GitFileStatus f)
        {
            if (f.IsUntracked)
                return "?";
            if (f.IsUnmerged)
                return "!";
            if (f.Staged != " " && f.Staged != "?")
                return "S";
            return "M";
        }

        private void OnChangeSelected(long index)
        {
            _revertButton.Disabled = _changes.GetSelectedItems().Length == 0;
            if (index < 0 || index >= _currentFiles.Count)
                return;
            _diff.Text = GitCli.GetDiff(ProjectDir(), _currentFiles[(int)index]);
        }

        private void OnCommitPressed()
        {
            string message = _commitMessage.Text.Trim();
            if (message.Length == 0)
            {
                _statusLabel.Text = "Type a commit message first.".TTR();
                return;
            }
            GitResult staged = GitCli.StageAll(ProjectDir());
            if (!staged.Ok)
            {
                _statusLabel.Text = "Stage failed: " + FirstLine(staged.Stderr);
                return;
            }
            GitResult committed = GitCli.Commit(ProjectDir(), message);
            if (!committed.Ok)
            {
                _statusLabel.Text = "Commit failed: " + FirstLine(committed.Stderr);
                return;
            }
            _commitMessage.Text = "";
            Refresh();
        }

        private void OnPushPressed()
        {
            GitResult r = GitCli.Push(ProjectDir());
            _statusLabel.Text = r.Ok ? "Pushed.".TTR() : "Push failed: " + FirstLine(r.Stderr);
            Refresh();
        }

        private void OnPullPressed()
        {
            GitResult r = GitCli.Pull(ProjectDir());
            _statusLabel.Text = r.Ok ? "Pulled.".TTR() : "Pull failed: " + FirstLine(r.Stderr);
            Refresh();
        }

        private void OnBranchSelected(long index)
        {
            string name = _branchOption.GetItemText((int)index);
            GitResult r = GitCli.SwitchBranch(ProjectDir(), name);
            if (!r.Ok)
                _statusLabel.Text = "Switch failed: " + FirstLine(r.Stderr);
            Refresh();
        }

        private void ShowPrompt(PromptAction action)
        {
            _promptAction = action;
            _promptLine.Text = "";
            if (action == PromptAction.NewBranch)
            {
                _promptDialog.Title = "New branch".TTR();
                _promptDialog.GetOkButton().Text = "Create".TTR();
            }
            else
            {
                _promptDialog.Title = "Merge branch into current".TTR();
                _promptDialog.GetOkButton().Text = "Merge".TTR();
            }
            _promptDialog.PopupCentered();
        }

        private void OnPromptConfirmed()
        {
            string name = _promptLine.Text.Trim();
            if (name.Length == 0)
                return;
            GitResult r = _promptAction == PromptAction.MergeBranch
                ? GitCli.MergeBranch(ProjectDir(), name)
                : GitCli.CreateBranch(ProjectDir(), name);
            if (!r.Ok)
                _statusLabel.Text = "Failed: " + FirstLine(r.Stderr);
            _promptAction = PromptAction.None;
            Refresh();
        }

        private void OnRevertPressed()
        {
            int count = _changes.GetSelectedItems().Length;
            if (count == 0)
                return;
            _revertDialog.Title = "Revert files".TTR();
            _revertDialog.DialogText = $"Discard all changes in {count} file(s)? Untracked files will be deleted.".TTR();
            _revertDialog.PopupCentered();
        }

        private void OnRevertConfirmed()
        {
            var selected = new List<GitFileStatus>();
            foreach (int i in _changes.GetSelectedItems())
            {
                if (i >= 0 && i < _currentFiles.Count)
                    selected.Add(_currentFiles[i]);
            }
            GitResult r = GitCli.RevertFiles(ProjectDir(), selected);
            if (!r.Ok)
                _statusLabel.Text = "Revert failed: " + FirstLine(r.Stderr);
            Refresh();
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "(no output)";
            int nl = text.IndexOf('\n');
            return (nl < 0 ? text : text.Substring(0, nl)).Trim();
        }
    }
}
