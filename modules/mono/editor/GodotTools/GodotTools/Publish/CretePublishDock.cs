using Godot;
using GodotTools.Agents;
using GodotTools.Internals;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GodotTools.Publish
{
    /// <summary>
    /// Bottom dock: pick an export preset, build it, push to itch.io
    /// (butler, fetched on demand) or Steam (steamcmd + generated app_build
    /// VDF). Secrets live in the OS keychain, never in the project.
    /// Steam AppID fields write the real GodotSteam project keys, so SDK
    /// builds consume them; stub builds just store the data.
    /// </summary>
    public partial class CretePublishDock : EditorDock
    {
        // GodotSteam project keys (see godotsteam_project_settings.cpp).
        private const string SteamAppIdKey = "steam/initialization/app_data/app_id";
        private const string SteamDemoIdKey = "steam/initialization/app_data/demo_id";
        private const string SteamInitKey = "steam/initialization/processes/initialize_on_startup";

        private const string ItchUserKey = "crete/publish/itch_user";
        private const string ItchGameKey = "crete/publish/itch_game";
        private const string ItchChannelKey = "crete/publish/itch_channel";
        private const string SteamDepotKey = "crete/publish/steam_depot";

        private OptionButton _presetOption = null!;
        private Label _presetInfo = null!;
        private LineEdit _itchUser = null!;
        private LineEdit _itchGame = null!;
        private LineEdit _itchChannel = null!;
        private LineEdit _itchKey = null!;
        private Label _itchKeyStatus = null!;
        private SpinBox _steamAppId = null!;
        private SpinBox _steamDemoId = null!;
        private CheckButton _steamInit = null!;
        private SpinBox _steamDepot = null!;
        private LineEdit _steamUser = null!;
        private LineEdit _steamPass = null!;
        private Label _steamStubNote = null!;
        private RichTextLabel _log = null!;
        private Label _status = null!;
        private Button _cancelButton = null!;

        private List<ExportPreset> _presets = new();
        private readonly List<string> _pendingLines = new();
        private readonly object _pendingLock = new();
        private CancellationTokenSource? _opCts;
        private bool _busy;

        public CretePublishDock()
        {
            Name = "Publish".TTR();
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

            // Preset row.
            var presetBar = new HBoxContainer();
            presetBar.AddThemeConstantOverride("separation", 6);
            root.AddChild(presetBar);
            presetBar.AddChild(new Label { Text = "Preset:".TTR() });
            _presetOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _presetOption.ItemSelected += OnPresetSelected;
            presetBar.AddChild(_presetOption);
            var presetRefresh = new Button { Text = "Refresh".TTR() };
            presetRefresh.Pressed += LoadPresets;
            presetBar.AddChild(presetRefresh);
            var buildButton = new Button { Text = "Build".TTR(), TooltipText = "Run a headless export of this preset".TTR() };
            buildButton.Pressed += OnBuildPressed;
            presetBar.AddChild(buildButton);
            _cancelButton = new Button { Text = "Cancel".TTR(), Disabled = true };
            _cancelButton.Pressed += () => _opCts?.Cancel();
            presetBar.AddChild(_cancelButton);
            _presetInfo = new Label();
            root.AddChild(_presetInfo);

            root.AddChild(new HSeparator());

            // itch.io section.
            root.AddChild(new Label { Text = "itch.io".TTR() });
            var itchGrid = new GridContainer { Columns = 4 };
            itchGrid.AddThemeConstantOverride("h_separation", 6);
            itchGrid.AddThemeConstantOverride("v_separation", 4);
            root.AddChild(itchGrid);
            itchGrid.AddChild(new Label { Text = "User:".TTR() });
            _itchUser = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            itchGrid.AddChild(_itchUser);
            itchGrid.AddChild(new Label { Text = "Game:".TTR() });
            _itchGame = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            itchGrid.AddChild(_itchGame);
            itchGrid.AddChild(new Label { Text = "Channel:".TTR() });
            _itchChannel = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            itchGrid.AddChild(_itchChannel);
            itchGrid.AddChild(new Label { Text = "API key:".TTR() });
            var keyBar = new HBoxContainer();
            keyBar.AddThemeConstantOverride("separation", 4);
            _itchKey = new LineEdit { Secret = true, PlaceholderText = "Stored in OS keychain".TTR(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            keyBar.AddChild(_itchKey);
            var keySave = new Button { Text = "Save".TTR() };
            keySave.Pressed += OnItchKeySave;
            keyBar.AddChild(keySave);
            itchGrid.AddChild(keyBar);
            var itchBar = new HBoxContainer();
            itchBar.AddThemeConstantOverride("separation", 6);
            root.AddChild(itchBar);
            var itchPush = new Button { Text = "Push to itch.io".TTR() };
            itchPush.Pressed += OnItchPushPressed;
            itchBar.AddChild(itchPush);
            _itchKeyStatus = new Label();
            itchBar.AddChild(_itchKeyStatus);

            root.AddChild(new HSeparator());

            // Steam section.
            root.AddChild(new Label { Text = "Steam".TTR() });
            var steamGrid = new GridContainer { Columns = 6 };
            steamGrid.AddThemeConstantOverride("h_separation", 6);
            steamGrid.AddThemeConstantOverride("v_separation", 4);
            root.AddChild(steamGrid);
            steamGrid.AddChild(new Label { Text = "App ID:".TTR() });
            _steamAppId = new SpinBox { MinValue = 0, MaxValue = 4000000000, Step = 1 };
            steamGrid.AddChild(_steamAppId);
            steamGrid.AddChild(new Label { Text = "Demo ID:".TTR() });
            _steamDemoId = new SpinBox { MinValue = 0, MaxValue = 4000000000, Step = 1 };
            steamGrid.AddChild(_steamDemoId);
            steamGrid.AddChild(new Label { Text = "Depot ID:".TTR() });
            _steamDepot = new SpinBox { MinValue = 0, MaxValue = 4000000000, Step = 1 };
            steamGrid.AddChild(_steamDepot);
            steamGrid.AddChild(new Label { Text = "User:".TTR() });
            _steamUser = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            steamGrid.AddChild(_steamUser);
            steamGrid.AddChild(new Label { Text = "Password:".TTR() });
            _steamPass = new LineEdit { Secret = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            steamGrid.AddChild(_steamPass);
            var credSave = new Button { Text = "Save login".TTR(), TooltipText = "Store in OS keychain".TTR() };
            credSave.Pressed += OnSteamCredSave;
            steamGrid.AddChild(credSave);
            _steamInit = new CheckButton { Text = "Init on startup".TTR() };
            steamGrid.AddChild(_steamInit);
            var steamBar = new HBoxContainer();
            steamBar.AddThemeConstantOverride("separation", 6);
            root.AddChild(steamBar);
            var steamUpload = new Button { Text = "Build VDF + upload".TTR(), TooltipText = "Generate the SteamPipe app_build file and run steamcmd".TTR() };
            steamUpload.Pressed += OnSteamUploadPressed;
            steamBar.AddChild(steamUpload);
            var dashButton = new Button { Text = "Partner dashboard".TTR(), Flat = true };
            dashButton.Pressed += () => OS.ShellOpen("https://partner.steamgames.com");
            steamBar.AddChild(dashButton);
            _steamStubNote = new Label();
            steamBar.AddChild(_steamStubNote);

            root.AddChild(new HSeparator());

            _log = new RichTextLabel
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                ScrollFollowing = true,
                SelectionEnabled = true,
            };
            root.AddChild(_log);
            _status = new Label { Text = "idle".TTR() };
            root.AddChild(_status);

            LoadFieldValues();
            LoadPresets();
            RefreshKeyStatus();
            ReportEnvironment();
        }

        private static string ProjectDir() => ProjectSettings.GlobalizePath("res://");

        private void LoadFieldValues()
        {
            _itchUser.Text = ProjectSettings.GetSetting(ItchUserKey, "").AsString();
            _itchGame.Text = ProjectSettings.GetSetting(ItchGameKey, "").AsString();
            _itchChannel.Text = ProjectSettings.GetSetting(ItchChannelKey, "").AsString();
            _steamAppId.Value = ProjectSettings.GetSetting(SteamAppIdKey, 0).AsInt32();
            _steamDemoId.Value = ProjectSettings.GetSetting(SteamDemoIdKey, 0).AsInt32();
            _steamInit.ButtonPressed = ProjectSettings.GetSetting(SteamInitKey, false).AsBool();
            _steamDepot.Value = ProjectSettings.GetSetting(SteamDepotKey, 0).AsInt32();
        }

        private void SaveFieldValues()
        {
            ProjectSettings.SetSetting(ItchUserKey, _itchUser.Text.Trim());
            ProjectSettings.SetSetting(ItchGameKey, _itchGame.Text.Trim());
            ProjectSettings.SetSetting(ItchChannelKey, _itchChannel.Text.Trim());
            ProjectSettings.SetSetting(SteamAppIdKey, (int)_steamAppId.Value);
            ProjectSettings.SetSetting(SteamDemoIdKey, (int)_steamDemoId.Value);
            ProjectSettings.SetSetting(SteamInitKey, _steamInit.ButtonPressed);
            ProjectSettings.SetSetting(SteamDepotKey, (int)_steamDepot.Value);
            ProjectSettings.Save();
        }

        private void LoadPresets()
        {
            _presets.Clear();
            _presetOption.Clear();
            string cfgPath = Path.Combine(ProjectDir(), "export_presets.cfg");
            if (File.Exists(cfgPath))
            {
                try
                {
                    _presets = new List<ExportPreset>(ExportPresetsFile.Parse(File.ReadAllText(cfgPath)));
                }
                catch (Exception e)
                {
                    AppendLog("Could not read export_presets.cfg: " + e.Message);
                }
            }
            for (int i = 0; i < _presets.Count; i++)
                _presetOption.AddItem($"{_presets[i].Name} ({_presets[i].Platform})", i);
            if (_presets.Count > 0)
            {
                _presetOption.Select(0);
                OnPresetSelected(0);
            }
            else
            {
                _presetInfo.Text = "No export presets. Add one in Project > Export.".TTR();
            }
        }

        private void OnPresetSelected(long index)
        {
            if (index < 0 || index >= _presets.Count)
                return;
            ExportPreset p = _presets[(int)index];
            _presetInfo.Text = $"{p.Platform} -> {p.ExportPath}";
            if (string.IsNullOrWhiteSpace(_itchChannel.Text))
                _itchChannel.Text = ExportPresetsFile.DefaultChannel(p.Platform);
        }

        private void RefreshKeyStatus()
        {
            try
            {
                _itchKeyStatus.Text = PublishKeychain.Exists(PublishKeychain.ItchApiKey)
                    ? "API key stored.".TTR() : "No API key stored.".TTR();
            }
            catch (Exception e)
            {
                _itchKeyStatus.Text = "Keychain unavailable: " + e.Message;
            }
            try
            {
                bool stub = Godot.Steam.IsCreteStub();
                _steamStubNote.Text = stub
                    ? "Note: this editor build stubs the Steam runtime API (uploads unaffected).".TTR()
                    : "Steam runtime API available in this build.".TTR();
            }
            catch (Exception)
            {
                _steamStubNote.Text = "";
            }
        }

        private void ReportEnvironment()
        {
            string butler = PublishTools.Which("butler") ?? (File.Exists(PublishTools.ButlerPath()) ? PublishTools.ButlerPath() : "missing");
            string steamcmd;
            try
            {
                steamcmd = PublishTools.Which("steamcmd") ?? (File.Exists(PublishTools.SteamCmdPath()) ? PublishTools.SteamCmdPath() : "missing");
            }
            catch (PlatformNotSupportedException)
            {
                steamcmd = "unsupported-on-macos";
            }
            bool stub;
            try { stub = Godot.Steam.IsCreteStub(); }
            catch (Exception) { stub = true; }
            GD.Print($"CretePublish: presets={_presets.Count} butler={butler} steamcmd={steamcmd} steam_stub={stub}");
        }

        private ExportPreset? SelectedPreset()
        {
            int i = _presetOption.Selected;
            return (i >= 0 && i < _presets.Count) ? _presets[i] : null;
        }

        private string? BuildOutputDir(ExportPreset preset)
        {
            if (string.IsNullOrWhiteSpace(preset.ExportPath))
                return null;
            string full = preset.ExportPath;
            if (!Path.IsPathRooted(full))
                full = Path.Combine(ProjectDir(), full);
            try
            {
                string? dir = Path.GetDirectoryName(Path.GetFullPath(full));
                return (dir != null && Directory.Exists(dir)) ? dir : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private bool BeginOp(string what)
        {
            if (_busy)
            {
                _status.Text = "Another operation is running.".TTR();
                return false;
            }
            _busy = true;
            _cancelButton.Disabled = false;
            _opCts = new CancellationTokenSource();
            _status.Text = what;
            SaveFieldValues();
            return true;
        }

        private void EndOp(string what)
        {
            _busy = false;
            _cancelButton.Disabled = true;
            _opCts?.Dispose();
            _opCts = null;
            _status.Text = what;
            DrainPending();
        }

        private async void OnBuildPressed()
        {
            ExportPreset? preset = SelectedPreset();
            if (preset == null)
            {
                _status.Text = "Select an export preset first.".TTR();
                return;
            }
            if (string.IsNullOrWhiteSpace(preset.ExportPath))
            {
                _status.Text = "Preset has no export path.".TTR();
                return;
            }
            WarnIfNoExportTemplates();
            if (!BeginOp($"Building {preset.Name}..."))
                return;
            try
            {
                AppendLog($"=== Build {preset.Name} ({preset.Platform}) ===");
                string exe = Godot.OS.GetExecutablePath();
                AgentRunResult r = await AgentRunner.RunAsync(exe,
                    new[] { "--headless", "--path", ProjectDir(), "--export-release", preset.Name },
                    ProjectDir(), TimeSpan.FromMinutes(30), _opCts!.Token, QueueLine, QueueLine);
                AppendLog(r.Ok ? "=== Build OK ===" : $"=== Build failed (exit {r.ExitCode}) ===");
                EndOp(r.Ok ? "Build finished.".TTR() : "Build failed; see log.".TTR());
            }
            catch (Exception e)
            {
                AppendLog("Build error: " + e.Message);
                EndOp("Build error.".TTR());
            }
        }

        private async void OnItchPushPressed()
        {
            ExportPreset? preset = SelectedPreset();
            string? dir = preset != null ? BuildOutputDir(preset) : null;
            if (dir == null)
            {
                _status.Text = "Build the preset first (no output folder found).".TTR();
                return;
            }
            string user = _itchUser.Text.Trim();
            string game = _itchGame.Text.Trim();
            string channel = _itchChannel.Text.Trim();
            if (user.Length == 0 || game.Length == 0 || channel.Length == 0)
            {
                _status.Text = "Fill in itch user, game, and channel.".TTR();
                return;
            }
            string? apiKey;
            try
            {
                apiKey = PublishKeychain.Load(PublishKeychain.ItchApiKey);
            }
            catch (Exception e)
            {
                _status.Text = "Keychain error: " + e.Message;
                return;
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                _status.Text = "Save the itch API key first.".TTR();
                return;
            }
            if (!BeginOp($"Pushing to {user}/{game}:{channel}..."))
                return;
            try
            {
                string butler = await PublishTools.EnsureButlerAsync(_opCts!.Token, QueueLine);
                var env = new Dictionary<string, string> { ["BUTLER_API_KEY"] = apiKey };
                AgentRunResult r = await AgentRunner.RunAsync(butler,
                    new[] { "push", dir, $"{user}/{game}:{channel}" },
                    ProjectDir(), TimeSpan.FromMinutes(60), _opCts.Token, QueueLine, QueueLine, env);
                AppendLog(r.Ok ? "=== Push OK ===" : $"=== Push failed (exit {r.ExitCode}) ===");
                EndOp(r.Ok ? "Pushed to itch.io.".TTR() : "Push failed; see log.".TTR());
            }
            catch (Exception e)
            {
                AppendLog("Push error: " + e.Message);
                EndOp("Push error.".TTR());
            }
        }

        private async void OnSteamUploadPressed()
        {
            ExportPreset? preset = SelectedPreset();
            string? dir = preset != null ? BuildOutputDir(preset) : null;
            if (dir == null)
            {
                _status.Text = "Build the preset first (no output folder found).".TTR();
                return;
            }
            uint appId = (uint)_steamAppId.Value;
            uint depot = (uint)_steamDepot.Value;
            if (appId == 0 || depot == 0)
            {
                _status.Text = "Set the Steam App ID and Depot ID.".TTR();
                return;
            }
            string? user;
            string? pass;
            try
            {
                user = PublishKeychain.Load(PublishKeychain.SteamUser);
                pass = PublishKeychain.Load(PublishKeychain.SteamPass);
            }
            catch (Exception e)
            {
                _status.Text = "Keychain error: " + e.Message;
                return;
            }
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                _status.Text = "Save the Steam login first.".TTR();
                return;
            }
            if (!BeginOp("Uploading to Steam..."))
                return;
            try
            {
                string steamcmd = await PublishTools.EnsureSteamCmdAsync(_opCts!.Token, QueueLine);
                string godotDir = Path.Combine(ProjectDir(), ".godot");
                Directory.CreateDirectory(godotDir);
                string vdfPath = Path.Combine(godotDir, $"crete_appbuild_{appId}.vdf");
                File.WriteAllText(vdfPath, SteamVdf.AppBuild(appId,
                    $"Crete build of {preset!.Name}", Path.Combine(godotDir, "steam_output"), dir, depot));
                AppendLog("Wrote " + vdfPath);
                AppendLog("First upload on a machine may need Steam Guard approval; watch for a prompt.");
                AgentRunResult r = await AgentRunner.RunAsync(steamcmd,
                    new[] { "+login", user, pass, "+run_app_build", vdfPath, "+quit" },
                    ProjectDir(), TimeSpan.FromMinutes(60), _opCts.Token, QueueLine, QueueLine);
                AppendLog(r.Ok ? "=== Upload OK (check Steampipe build status) ===" : $"=== Upload failed (exit {r.ExitCode}) ===");
                EndOp(r.Ok ? "Steam upload finished.".TTR() : "Upload failed; see log.".TTR());
            }
            catch (Exception e)
            {
                AppendLog("Upload error: " + e.Message);
                EndOp("Upload error.".TTR());
            }
        }

        private void OnItchKeySave()
        {
            try
            {
                if (string.IsNullOrEmpty(_itchKey.Text))
                    PublishKeychain.Delete(PublishKeychain.ItchApiKey);
                else
                    PublishKeychain.Save(PublishKeychain.ItchApiKey, _itchKey.Text);
                _itchKey.Text = "";
                RefreshKeyStatus();
                _status.Text = "API key updated.".TTR();
            }
            catch (Exception e)
            {
                _status.Text = "Keychain error: " + e.Message;
            }
        }

        private void OnSteamCredSave()
        {
            try
            {
                if (string.IsNullOrEmpty(_steamUser.Text))
                    PublishKeychain.Delete(PublishKeychain.SteamUser);
                else
                    PublishKeychain.Save(PublishKeychain.SteamUser, _steamUser.Text.Trim());
                if (string.IsNullOrEmpty(_steamPass.Text))
                    PublishKeychain.Delete(PublishKeychain.SteamPass);
                else
                    PublishKeychain.Save(PublishKeychain.SteamPass, _steamPass.Text);
                _steamUser.Text = "";
                _steamPass.Text = "";
                _status.Text = "Steam login updated.".TTR();
            }
            catch (Exception e)
            {
                _status.Text = "Keychain error: " + e.Message;
            }
        }

        private void WarnIfNoExportTemplates()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "Godot", "export_templates");
                if (!GodotTools.Utils.OS.IsWindows)
                {
                    dir = GodotTools.Utils.OS.IsMacOS
                        ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                            "Library", "Application Support", "Godot", "export_templates")
                        : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
                            ".local", "share", "godot", "export_templates");
                }
                if (!Directory.Exists(dir) || Directory.GetDirectories(dir).Length == 0)
                    AppendLog("Warning: no export templates installed (Editor > Manage Export Templates).");
            }
            catch (Exception)
            {
                // Advisory only.
            }
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
                AppendLog(line);
        }

        private void AppendLog(string line) => _log.AppendText(line + "\n");
    }
}
