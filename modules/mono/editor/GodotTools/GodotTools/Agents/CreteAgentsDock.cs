using Godot;
using GodotTools.Internals;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static GodotTools.Internals.Globals;

namespace GodotTools.Agents
{
    /// <summary>
    /// Bottom dock: run an external agent CLI (Muse, Claude Code, Codex,
    /// Gemini) non-interactively with editor context, stream its output,
    /// then report what it changed with a diff-before-keep review step
    /// (revert via git when the project is a repository).
    /// </summary>
    public partial class CreteAgentsDock : EditorDock
    {
        public const string TimeoutSetting = "crete/agents/timeout_minutes";

        private OptionButton _providerOption = null!;
        private Label _statusLabel = null!;
        private TextEdit _task = null!;
        private CheckButton _ctxScript = null!;
        private CheckButton _ctxScene = null!;
        private CheckButton _ctxGit = null!;
        private SpinBox _timeoutSpin = null!;
        private Button _runButton = null!;
        private Button _cancelButton = null!;
        private RichTextLabel _output = null!;
        private ItemList _changes = null!;
        private TextEdit _diff = null!;
        private Button _revertButton = null!;

        private readonly List<(AgentProvider Provider, string? Path)> _detected = new();
        private readonly List<string> _pendingLines = new();
        private readonly object _pendingLock = new();
        private CancellationTokenSource? _runCts;
        private List<AgentFileChange> _currentChanges = new();
        private bool _running;

        public CreteAgentsDock()
        {
            Name = "Agents".TTR();
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

            var root = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            AddChild(root);

            var toolbar = new HBoxContainer();
            toolbar.AddThemeConstantOverride("separation", 6);
            root.AddChild(toolbar);
            toolbar.AddChild(new Label { Text = "Provider:".TTR() });
            _providerOption = new OptionButton();
            toolbar.AddChild(_providerOption);
            var refreshButton = new Button { Text = "Refresh".TTR(), TooltipText = "Re-detect agent CLIs on PATH".TTR() };
            refreshButton.Pressed += DetectProviders;
            toolbar.AddChild(refreshButton);
            var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            toolbar.AddChild(spacer);
            _statusLabel = new Label { Text = "idle".TTR() };
            toolbar.AddChild(_statusLabel);

            root.AddChild(new Label { Text = "Task".TTR() });
            _task = new TextEdit
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2I(0, 64),
                PlaceholderText = "Describe what the agent should do...".TTR(),
            };
            root.AddChild(_task);

            var runBar = new HBoxContainer();
            runBar.AddThemeConstantOverride("separation", 6);
            root.AddChild(runBar);
            _ctxScript = new CheckButton { Text = "Script".TTR(), ButtonPressed = true, TooltipText = "Send the open script + selection".TTR() };
            _ctxScene = new CheckButton { Text = "Scene".TTR(), ButtonPressed = true, TooltipText = "Send a dump of the edited scene tree".TTR() };
            _ctxGit = new CheckButton { Text = "Git".TTR(), ButtonPressed = true, TooltipText = "Send git status".TTR() };
            runBar.AddChild(_ctxScript);
            runBar.AddChild(_ctxScene);
            runBar.AddChild(_ctxGit);
            runBar.AddChild(new VSeparator());
            runBar.AddChild(new Label { Text = "Timeout (min):".TTR() });
            _timeoutSpin = new SpinBox { MinValue = 1, MaxValue = 120, Step = 1, Value = GetTimeoutMinutes() };
            runBar.AddChild(_timeoutSpin);
            var runSpacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            runBar.AddChild(runSpacer);
            _runButton = new Button { Text = "Run agent".TTR() };
            _runButton.Pressed += OnRunPressed;
            runBar.AddChild(_runButton);
            _cancelButton = new Button { Text = "Cancel".TTR(), Disabled = true };
            _cancelButton.Pressed += OnCancelPressed;
            runBar.AddChild(_cancelButton);

            var split = new HSplitContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            root.AddChild(split);

            var left = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            split.AddChild(left);
            left.AddChild(new Label { Text = "Output".TTR() });
            _output = new RichTextLabel
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ScrollFollowing = true,
                SelectionEnabled = true,
            };
            left.AddChild(_output);

            var right = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            split.AddChild(right);
            right.AddChild(new Label { Text = "Changed files".TTR() });
            _changes = new ItemList
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SelectMode = ItemList.SelectModeEnum.Multi,
            };
            _changes.ItemSelected += OnChangeSelected;
            right.AddChild(_changes);
            right.AddChild(new Label { Text = "Diff".TTR() });
            _diff = new TextEdit
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                Editable = false,
            };
            right.AddChild(_diff);
            _revertButton = new Button { Text = "Revert selected".TTR(), Disabled = true, TooltipText = "Requires a git repository".TTR() };
            _revertButton.Pressed += OnRevertPressed;
            right.AddChild(_revertButton);

            DetectProviders();
        }

        private static string ProjectDir() => ProjectSettings.GlobalizePath("res://");

        private static int GetTimeoutMinutes()
        {
            int mins = EditorDef(TimeoutSetting, 10).AsInt32();
            return Math.Clamp(mins, 1, 120);
        }

        private void DetectProviders()
        {
            _detected.Clear();
            _providerOption.Clear();
            var found = new List<string>();
            var missing = new List<string>();
            int select = -1;
            for (int i = 0; i < AgentProvider.All.Count; i++)
            {
                AgentProvider p = AgentProvider.All[i];
                string? path = GodotTools.Utils.OS.PathWhich(p.CliName);
                _detected.Add((p, path));
                if (string.IsNullOrEmpty(path))
                {
                    _providerOption.AddItem($"{p.DisplayName} (not installed)", i);
                    _providerOption.SetItemDisabled(i, true);
                    missing.Add(p.Id);
                }
                else
                {
                    _providerOption.AddItem(p.DisplayName, i);
                    found.Add(p.Id);
                    if (select < 0)
                        select = i;
                }
            }
            if (select >= 0)
            {
                _providerOption.SetBlockSignals(true);
                _providerOption.Select(select);
                _providerOption.SetBlockSignals(false);
            }
            GD.Print($"CreteAgents: detected=[{string.Join(",", found)}] missing=[{string.Join(",", missing)}]");
        }

        private async void OnRunPressed()
        {
            if (_running)
                return;

            int index = _providerOption.Selected;
            if (index < 0 || index >= _detected.Count || string.IsNullOrEmpty(_detected[index].Path))
            {
                _statusLabel.Text = "No agent CLI installed. Install one and press Refresh.".TTR();
                return;
            }
            string task = _task.Text.Trim();
            if (task.Length == 0)
            {
                _statusLabel.Text = "Describe the task first.".TTR();
                return;
            }

            (AgentProvider provider, string? cliPath) = _detected[index];
            string dir = ProjectDir();
            AgentEditorContext ctx = AgentContext.Gather(
                _ctxScript.ButtonPressed, _ctxScene.ButtonPressed, _ctxGit.ButtonPressed);
            string prompt = AgentPrompt.Build(task, ctx.ScriptPath, ctx.ScriptText,
                ctx.Selection, ctx.SelectionLine, ctx.ScenePath, ctx.SceneDump, ctx.GitStatus);

            _running = true;
            _runButton.Disabled = true;
            _cancelButton.Disabled = false;
            _statusLabel.Text = $"Running {provider.DisplayName}...".TTR();
            _changes.Clear();
            _diff.Text = "";
            _revertButton.Disabled = true;
            _currentChanges = new List<AgentFileChange>();
            AppendOutput($"=== {provider.DisplayName} run ({DateTime.Now:HH:mm:ss}), prompt {prompt.Length} chars ===");

            Dictionary<string, string> before;
            try
            {
                before = AgentChangeTracker.Snapshot(dir);
            }
            catch (Exception e)
            {
                AppendOutput("Snapshot failed: " + e.Message);
                before = new Dictionary<string, string>();
            }

            _runCts = new CancellationTokenSource();
            TimeSpan timeout = TimeSpan.FromMinutes(Math.Clamp((int)_timeoutSpin.Value, 1, 120));
            AgentRunResult result;
            try
            {
                result = await AgentRunner.RunAsync(cliPath!, provider.BuildArgs(prompt), dir,
                    timeout, _runCts.Token, QueueLine, QueueLine);
            }
            catch (Exception e)
            {
                AppendOutput("Run failed: " + e.Message);
                result = new AgentRunResult(127, "", e.Message, false);
            }
            DrainPending();
            _runCts.Dispose();
            _runCts = null;

            string outcome = result.TimedOut ? "timed out" :
                result.ExitCode == 0 ? "finished" : $"exited {result.ExitCode}";
            AppendOutput($"=== {outcome} ===");

            try
            {
                Dictionary<string, string> after = AgentChangeTracker.Snapshot(dir);
                _currentChanges = new List<AgentFileChange>(AgentChangeTracker.Diff(before, after));
                foreach (AgentFileChange c in _currentChanges)
                    _changes.AddItem($"[{c.Kind}] {c.RelativePath}");
                _statusLabel.Text = $"{provider.DisplayName} {outcome}: {_currentChanges.Count} file(s) changed.";
                GD.Print($"CreteAgents: run provider={provider.Id} exit={result.ExitCode} " +
                    $"timed_out={result.TimedOut} changed={_currentChanges.Count}");
            }
            catch (Exception e)
            {
                _statusLabel.Text = "Change scan failed: " + e.Message;
            }

            _running = false;
            _runButton.Disabled = false;
            _cancelButton.Disabled = true;
        }

        private void OnCancelPressed()
        {
            _runCts?.Cancel();
            _statusLabel.Text = "Cancelling...".TTR();
        }

        private void QueueLine(string line)
        {
            lock (_pendingLock)
            {
                if (_pendingLines.Count == 0)
                    CallDeferred(nameof(DrainPending));
                _pendingLines.Add(line);
            }
        }

        private void DrainPending()
        {
            List<string> batch;
            lock (_pendingLock)
            {
                if (_pendingLines.Count == 0)
                    return;
                batch = new List<string>(_pendingLines);
                _pendingLines.Clear();
            }
            foreach (string line in batch)
                AppendOutput(line);
        }

        private void AppendOutput(string line)
        {
            _output.AppendText(line + "\n");
        }

        private void OnChangeSelected(long index)
        {
            bool isRepo = GitHub.GitCli.IsRepository(ProjectDir());
            _revertButton.Disabled = !isRepo || _changes.GetSelectedItems().Length == 0;
            if (index < 0 || index >= _currentChanges.Count)
                return;
            AgentFileChange change = _currentChanges[(int)index];
            if (isRepo)
            {
                _diff.Text = GitHub.GitCli.GetDiff(ProjectDir(), ToGitStatus(change));
            }
            else if (change.Kind == "deleted")
            {
                _diff.Text = $"(deleted: {change.RelativePath})";
            }
            else
            {
                try
                {
                    _diff.Text = $"(no git repo: current contents of {change.RelativePath})\n\n" +
                        System.IO.File.ReadAllText(System.IO.Path.Combine(ProjectDir(), change.RelativePath));
                }
                catch (Exception e)
                {
                    _diff.Text = $"(unreadable: {change.RelativePath}: {e.Message})";
                }
            }
        }

        private static GitHub.GitFileStatus ToGitStatus(AgentFileChange change) => change.Kind switch
        {
            "added" => new GitHub.GitFileStatus("?", "?", change.RelativePath, null),
            "deleted" => new GitHub.GitFileStatus("D", " ", change.RelativePath, null),
            _ => new GitHub.GitFileStatus(" ", "M", change.RelativePath, null),
        };

        private void OnRevertPressed()
        {
            var selected = new List<GitHub.GitFileStatus>();
            foreach (int i in _changes.GetSelectedItems())
            {
                if (i >= 0 && i < _currentChanges.Count)
                    selected.Add(ToGitStatus(_currentChanges[i]));
            }
            if (selected.Count == 0)
                return;
            GitHub.GitResult r = GitHub.GitCli.RevertFiles(ProjectDir(), selected);
            _statusLabel.Text = r.Ok ? "Reverted.".TTR() : "Revert failed: " + r.Stderr.Trim();
        }
    }
}
