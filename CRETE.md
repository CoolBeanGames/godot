# Crete engine build notes

Fork of `godotengine/godot`, branch `4.7`, developed on branch `crete`.
Upstream base: `fde53e7e592324072533f1f5251235d81a9ccae4`.

## Toolchain (verified Step 1)

- Python 3.12 (`py -3.12`; SCons requires >= 3.9)
- SCons 4.11.1 (`py -3.12 -m pip install scons`; requires >= 4.4)
- MSVC 14.44 (VS 2022) + Windows SDK 10.0.26100.0 (SCons auto-detects via vswhere)
- .NET SDK 9/10 (`modules/mono/global.json` pins 8.0.0 with `rollForward: latestMajor`)

## One-time Windows SDK blobs (land outside the repo)

- `py -3.12 misc/scripts/install_accesskit.py` (screen-reader driver)
- `py -3.12 misc/scripts/install_d3d12_sdk_windows.py` (D3D12 driver)
- `py -3.12 misc/scripts/install_angle.py` (ANGLE/GL compat driver; without
  it configure warns and (since Step 7) the warning is visible but harmless)

Both install to `%LOCALAPPDATA%\Godot\build_deps`. Without them, configure
fails and suggests `accesskit=no` / `d3d12=no`; Crete keeps both drivers.

## Canonical build

From this directory: `powershell -File crete.build.ps1`

1. `scons platform=windows module_mono_enabled=yes tests=yes -j<N>`
2. `<editor> --headless --generate-mono-glue ./modules/mono/glue`
3. `modules/mono/build_scripts/build_assemblies.py --godot-output-dir ./bin`

Current flags are stock plus .NET plus tests. Each removal step appends its
`module_*_enabled=no` flags to `crete.build.ps1`.

## Step 1 baseline (stock .NET editor, zero engine changes)

- Binary: `bin/godot.windows.editor.x86_64.mono.exe` (180 MB)
- Version string: `4.7.3.rc.mono.custom_build.fde53e7e5`
- Unit tests (`--headless --test`): **1380 cases, 1380 passed, 0 failed,
  3 skipped; 419,770 assertions passed. Status: SUCCESS.**
- Note: the build/test pipelines surface exit code 1 from captured stderr
  noise (expected-error prints, NuGet chatter) even on success. The doctest
  `Status: SUCCESS!` line and the presence of the binary + `bin/GodotSharp`
  assemblies are the real verdicts.

## Step 2: GDScript removed, Project Manager removed (DONE)

- Build flags add: `module_gdscript_enabled=no`.
- `modules/gdscript/` deleted outright (macro guards alone can leak the
  `GDScript` native class into ClassDB).
- `editor/project_manager/` deleted. `main.cpp`: no-project runs print
  `Error: No project found. Open a project from Crete Hub or pass
  --path <project_dir>.` and exit 1 instead of opening the manager.
- `TextServer.STRUCTURED_TEXT_GDSCRIPT` renamed to
  `STRUCTURED_TEXT_CODE` (same value 5): it was the last `GDScript`
  reference in the ClassDB/extension-API surface.
- New tests: `tests/core/test_crete_removals.cpp` (GDScript class absent,
  `.gd` unknown, project-manager sources gone).
- Results: build DONE with 0 errors; focused suite 3/3 passed; full suite
  **1374 passed, 0 failed, 3 skipped; 360,785 assertions. Status: SUCCESS.**
  (6 fewer cases than Step 1: the GDScript module's own tests are gone.)
  Extension-API dump contains 0 GDScript references; no-project runs exit 1
  with the Crete Hub message.

## Step 3: OpenXR removed, node replication removed (DONE)

- Build flags add: `module_openxr_enabled=no`.
- `modules/openxr/` deleted; `xr/openxr/*` project settings in `main.cpp`
  guarded by `#ifdef MODULE_OPENXR_ENABLED`. XR core (`XRServer`), the
  `mobile_vr`/`webxr` modules, and `thirdparty/openxr` (Android-only refs)
  stay. `BUILD_OPTION_OPENXR` stays as a build-profile passthrough.
- Multiplayer: narrow cut, NOT `module_multiplayer_enabled=no` (the base
  `MultiplayerAPI` is abstract; the full cut would kill RPC lobbies too).
  Deleted: spawner, synchronizer, replication interface + config, replication
  editor + docs + icons + spawner tests. `SceneMultiplayer` keeps RPCs, auth,
  peers, bandwidth/RPC profiling; per-peer RPC visibility is gone (no
  replication to drive it). Wire command numbers unchanged.
- Tests: `test_crete_removals.cpp` gains OpenXR-absent, replication-absent,
  and RPC-present cases; `test_scene_multiplayer.h` drops the removed API.
- Results: build DONE with 0 errors; focused suites 6/6 (Crete) and 7/7
  (multiplayer incl. auth flow) passed; full suite **1371 passed, 0 failed,
  3 skipped; 356,579 assertions. Status: SUCCESS.**
  Extension-API dump: 0 OpenXR refs, 0 replication refs, SceneMultiplayer
  still present.

## Step 4: Android/iOS/visionOS/macOS targets removed (DONE)

- Deleted `platform/android`, `platform/ios`, `platform/visionos`,
  `platform/macos`. Exporters unregister automatically: SCons builds
  `platform_exporters` from existing `platform/*/export/export.cpp`.
- Editor core: deleted the Apple export base (`EditorExportPlatform-
  AppleEmbedded`), Apple plugin config, and `codesign` (kept `macho`/`lipo`:
  the .NET export path still calls `LipOCreateFile`); removed their ClassDB
  registration and class doc.
- UI/logic: Android build-template menu + dialogs + `--install-android-
  build-template` CLI gone; template manager lists Windows/Linux/Web only;
  dead per-platform project settings removed; preset version check, patch
  filters, and remote-debug host simplified.
- Left inert: `#ifdef` platform guards tree-wide (never compiled for our
  targets), Metal shader baker (fenced `METAL_ENABLED`), GodotTools OS maps,
  `EditorExportPlugin` Android virtuals (plugin API), web UA sniffing.
- CI: deleted android/ios/macos workflows + runner jobs; deleted the Swappy
  and macOS Vulkan install scripts and the `apple_embedded_xcode` template.
- Tests: platform-absent/present ClassDB cases in `test_crete_removals.cpp`.
  Lesson: `--test` runs before `Main::setup()`, so `has_setting` checks for
  setup()-registered settings are vacuous in doctests (caught live: presence
  checks failed). Settings removal is verified by binary string scan instead:
  0 leaks on 15 removed setting/menu/CLI strings, all 5 kept strings present.
- Results: build DONE with 0 errors; focused suite 8/8 passed; full suite
  **1373 passed, 0 failed, 3 skipped; 356,513 assertions. Status: SUCCESS.**
  Extension-API dump: 0 removed platform classes, 5 kept exporter classes.

## Step 5: Asset Store removed (DONE)

- Deleted `editor/asset_library/` (plugin + installer) and its SCsub entry;
  removed tab registration, shortcut, main-screen enum, and feature-profile
  flag (profiles serialize by name, so old files load fine).
- Removed `asset_store/*` editor settings + compat shims, AssetLib theme
  blocks/variations in both themes, the `AssetThumbLoading` icon (kept
  `AssetStore.svg`: the template manager reuses it), and stale class docs
  (feature constants renumbered).
- Tests: `FEATURE_ASSET_LIB` absent / `FEATURE_GAME` present via ClassDB
  constant list; AssetLib class/setting/shortcut strings verified absent
  from the binary (the plugin classes were never GDREGISTER'd, so a
  `class_exists` check would pass vacuously even on stock).
- Results: build DONE with 0 errors; focused suite 9/9 passed; full suite
  **1374 passed, 0 failed, 3 skipped; 356,513 assertions. Status: SUCCESS.**
  Binary string scan: 10/10 removed strings absent (`FEATURE_ASSET_LIB`
  appears only inside the absence test itself), `AssetStore` icon kept.

## Step 6: touchscreen editor UI removed, core input untouched (DONE)

- Deleted `TouchActionsPanel` + its Android-gated editor_node wiring; removed
  all 6 `interface/touchscreen/*` editor settings and every reader
  (touch draggers, theme touch branches, gizmo scaling, curve editor radii).
- `emulate_touch_from_mouse` already defaults off upstream (verified, kept);
  `InputEventScreenTouch/Drag` and `SplitContainer` touch API stay (core).
- Dead mobile project settings removed: `display/window/handheld/`,
  `display/window/frame_pacing/android/*`, `display/window/ios/*`,
  `input_devices/pointing/android/*`, `audio/general/ios/*`,
  `rendering/scaling_3d/mode.{ios,macos}`, blender/fbx2gltf
  `.enabled.android` overrides (+ class docs); `/android/` dropped from the
  VCS .gitignore template.
- Tests: no doctest (settings absence is unverifiable in-process:
  `EditorSettings::create()` loads the machine's persisted settings file, so
  `has_setting()` reflects user state, not code). All removals verified via
  binary string scan instead.
- Results: build DONE with 0 errors; focused suite 9/9 passed; full suite
  **1374 passed, 0 failed, 3 skipped; 356,513 assertions. Status: SUCCESS.**
  Binary string scan: 0 leaks on 26 removed strings.

## Step 7: Groups dock + Project Settings tab removed, group API kept (DONE)

- Deleted `editor/docks/groups_dock.*`, `editor/docks/groups_editor.*`,
  `editor/scene/group_settings_editor.*`; removed the dock from `EditorNode`
  (creation, default `dock_5` layout, `_edit_current` refreshes,
  feature-profile toggles) and the Groups tab from Project Settings.
- Scene tree: removed the per-node group badges (`BUTTON_GROUPS` branch, group
  icon/tooltip listing; `BUTTON_PIN`/`BUTTON_UNIQUE` renumbered to 7/8).
  `BUTTON_GROUP` (children-selectable toggle) is unrelated and stays.
- `FEATURE_GROUPS_DOCK` removed from `EditorFeatureProfile` (constants
  renumbered, `FEATURE_MAX` now 9) + class doc. Profiles serialize by name,
  so old files load fine.
- `Node.add_to_group/remove_from_group/is_in_group` untouched: imported
  projects keep working; Tags (Step 8) replace Groups in the UI.
- Build tooling: `crete.build.ps1` now stringifies native stderr and judges
  each step by `$LASTEXITCODE` (a bare ANGLE warning used to abort the
  script under `$ErrorActionPreference='Stop'`).
- Tests: 2 new cases (flag absent/signals present; group API intact). UI
  absence verified via binary scan + extension-API dump (no ClassDB hook).
- Results: build DONE with 0 errors; focused suite 11/11 passed; full suite
  **1376 passed, 0 failed, 3 skipped; 356,516 assertions. Status: SUCCESS.**
  Binary scan: 11/11 removed strings absent (`FEATURE_GROUPS_DOCK` occurs
  2x, both inside the absence test's doctest strings), 5/5 kept strings
  present. Extension-API dump: 0 removed refs.

## Step 8: Node tags + inspector row; mono glue generation restored (DONE)

- Core: `Node.add_tag/remove_tag/has_tag/get_tags` stored as `_tags`
  metadata (a `PackedStringArray`), so tags serialize with the scene and work
  on and off the tree. Corrupt `_tags` values fail soft; the key is removed
  when the last tag goes. `SceneTree.get_nodes_with_tag` does an O(n)
  hierarchy walk (deliberate: no registry like groups). All bound + class
  docs (`Node.xml` x4, `SceneTree.xml` x1); also dropped a stale Groups-dock
  sentence from the `add_to_group` doc.
- Editor: `EditorInspectorPluginCreteTags` pins a comma-separated Tags row to
  the top of every Node inspector, with undo/redo and refresh on
  `history_changed`. Single-node only for now (`MultiNodeEdit` shows no row).
- Build fix 1 (main.cpp): `--generate-mono-glue` is a headless cmdline tool
  again (`cmdline_tool` + `main_args` forwarding + editor hint at the call
  site). Step 2's no-project exit had broken it, and the generator reads its
  flag back from `OS::get_cmdline_args()` (= `main_args`), so the parser
  branch must forward flag + dir or generation silently never starts.
- Build fix 2 (crete.build.ps1): `Invoke-Native` rewritten (direct call,
  scoped `Continue`, immediate `$LASTEXITCODE`, `.console.exe`). Two silent
  failure modes found: `Invoke-Expression "& '...'"` never executed at all,
  and GUI-subsystem exes never resolve under output capture. Glue had been
  stale since Step 2; it regenerates now (verified: `GetNodesWithTag`,
  `AddTag/HasTag/RemoveTag/GetTags` in the C# with converted docs).
- Test lesson: the runner only creates a `SceneTree` for cases tagged
  `[SceneTree]` (see `tests/test_main.cpp`); tree-touching cases must carry
  the tag (the tree case also `REQUIRE`s the singleton). Noted in the test
  file header.
- Results: build DONE with 0 errors; focused suites 7/7 (tags) + 11/11
  (removals); full suite **1383 passed, 0 failed, 3 skipped; 356,578
  assertions. Status: SUCCESS.** C# API verified in regenerated glue;
  assemblies rebuilt from it.

## Step 9: C# default, VS Code default, auto solution + .vscode (DONE)

- New-script dialog (`script_create_dialog.cpp`): default language is now
  `C#` instead of `GDScript` (which can't exist here); single-language select
  falls through to index 0 either way.
- GodotTools (`GodotSharpEditor.cs`):
  - `dotnet/editor/external_editor` default `None` -> `VsCode`, so C# files
    open in VS Code out of the box (`OverridesExternalEditor` goes true).
  - `_EnablePlugin` auto-creates the .sln + .csproj when both are missing
    (previously manual `C# > Create C# solution` only; the create call itself
    was dead code with no callers). Partial/broken states still go through
    the manual confirm-dialog path. Mirrors the existing build-button
    fallback; generation is template file writes, no `dotnet` CLI needed.
  - Fresh solutions also scaffold `.vscode/`: `tasks.json` (default Debug
    build + ExportRelease build), `launch.json` (launch Crete with
    `--path`, attach-to-process, build as pre-launch), `extensions.json`
    (recommends the C# Dev Kit). Best-effort, never overwrites.
- C# notes: `GodotTools.Utils.OS` shadows `Godot.OS` in this file (use the
  `Godot.OS` qualifier for `GetExecutablePath`); same for `System.IO.Directory`
  vs `GodotTools.Utils.Directory`.
- Tests: new `Crete C# defaults` suite (exactly one language, it is C# /
  `CSharpScript` / `cs`; no GDScript). GodotTools behaviors verified
  end-to-end instead: fresh headless `--editor` open of a scratch project
  produced `Step9E2E.sln` + `.csproj` + all three `.vscode` files (JSON
  parsed OK); reopen left the `.sln` byte-identical with no errors.
- Results: build DONE with 0 errors; focused 2/2; full suite **1385
  passed, 0 failed, 3 skipped; 356,585 assertions. Status: SUCCESS.**
- Test-harness note: `./bin/*.mono.exe --headless --test > file 2>&1` can
  silently produce a 0-byte log (GUI-subsystem capture quirk, same family
  as the Step 8 `Invoke-Expression` find). Pipe form
  `... --test 2>&1 | Select-Object -Last 5` works; use it for full runs.

## Step 10: GitHub bottom dock (git CLI, C#) (DONE)

- New `GodotTools/GitHub/` in the GodotTools assembly (ships built-in, no
  C++ changes — wired with one `AddDock` next to `MSBuildPanel`):
  - `GitCli.cs`: static wrapper over the git CLI. Detection via
    `crete/github/git_path` editor setting (default `git` on PATH);
    `GIT_TERMINAL_PROMPT=0` + `LC_ALL=C` on every call so network ops
    fail fast instead of hanging on auth prompts. Parses
    `status --porcelain=v1 --branch` (branch/upstream/ahead/behind,
    staged/unstaged/untracked/conflict, rename arrows, C-quoting),
    branch list, `diff HEAD`, stage-all, commit, push, ff-only pull,
    switch/create/merge, per-file revert (`checkout --` for tracked,
    `clean -f` for untracked).
  - `CreteGitHubDock.cs`: bottom `EditorDock` (VcsBranches icon):
    branch switcher + New/Merge dialogs, Push/Pull/Refresh, changes
    list with per-file diff view, commit-message box (stage-all +
    commit), revert-selected behind a confirmation dialog. Empty
    states for git-missing and not-a-repository. Every refresh prints
    one `CreteGitHub: git=.. repo=.. branch=.. files=..` console line
    (user feedback + E2E hook).
- C# lesson: editor-setting defaults must use `EditorDef` (via
  `using static GodotTools.Internals.Globals`). `SetInitialValue(...,
  update_current: false)` leaves NO current value, so `GetSetting`
  comes back empty — this shipped git-not-found until E2E caught it.
- Status-bar `branch ⇄ state` from the plan is deferred to P3, which
  owns the status bar (no C# API reaches it); P3 will run its own
  lightweight `git branch --show-current` poll.
- Verified: build DONE; E2E repo project ->
  `git=yes repo=yes branch=main files=6 ahead=0 behind=0`; E2E
  non-repo project -> `repo=no` empty state, exit 0, no errors. Every
  git shape the dock shells out to was executed by hand against the
  scratch repo (push-with-no-remote fails in 0.1 s, no hang). Button
  click paths are compile-checked signal wiring; not clickable
  headless. Full suite **1385 passed, 0 failed, 3 skipped; 356,585
  assertions. Status: SUCCESS.**
- Pre-existing note: headless editor open prints
  `WARNING: Property not found: 'xr/openxr/enabled'` (Step 3
  leftover), unrelated to this step.

## Step 11: Agents bottom dock (external CLIs, C#) (DONE)

- New `GodotTools/Agents/`, split into a pure BCL core (unit-tested
  outside the engine) and a thin Godot-coupled dock:
  - `AgentProvider.cs`: muse / claude / codex / gemini, one pattern.
    Flags confirmed at implementation time: `muse exec` (`muse exec
    --help` on this machine), `claude -p`, `codex exec`, `gemini -p`
    (vendor docs). Prompt travels on argv for all four.
  - `AgentPrompt.cs`: task + open-script/selection + scene dump + git
    status assembly with per-section caps and a 24k total cap (Windows
    argv limits), truncation markers throughout.
  - `AgentRunner.cs`: async run with live line streaming, timeout kill
    (exit 124, `TimedOut` flag), cancellation, exit passthrough, and
    `cmd /c` wrapping for .cmd/.bat CLIs (required: `muse` here is a
    `.cmd`, which .NET cannot spawn directly).
  - `AgentChangeTracker.cs`: SHA-256 snapshot/diff of project text
    files (skips `.git/.godot/bin/obj/...`, ignores non-project
    extensions), so changed files are reported with or without git.
  - `AgentContext.cs`: gathers open script + selection (ScriptEditor
    -> TextEdit), edited-scene dump (500-node cap, attached-script
    paths), git status head. Build-error context deliberately absent:
    the problems list is a private Tree with no accessor, and the
    agent has shell access to run `dotnet build` itself.
  - `CreteAgentsDock.cs`: provider picker with PATH auto-detect,
    multi-line task, script/scene/git toggles, timeout spinner
    (`crete/agents/timeout_minutes`, default 10), Run/Cancel,
    streaming log, changed-files list + diff view, revert-selected
    (via `GitCli` when the project is a repo).
- Standalone tests: `tests/csharp/agent_runner_test/` (dotnet console
  project compiling the real core files): **28/28 pass** — provider
  argv shapes, prompt sections + caps, `.cmd` run/capture/streaming,
  exit passthrough, missing-exe 127, timeout kill (3.3 s) keeping
  pre-kill output, cancel kill, snapshot add/modify/delete with
  skips. Run: `dotnet run --project
  engine/tests/csharp/agent_runner_test`.
- E2E: headless open prints
  `CreteAgents: detected=[muse] missing=[claude,codex,gemini]` (this
  machine), exit 0. Live check: real `muse exec --provider echo`
  returns exit 0 with the answer on stdout, proving the argv shape.
- C# note: log views are `RichTextLabel` with `ScrollFollowing`
  (see `BuildOutputView`); `TextEdit` has no `ScrollToLine`.
- Results: build DONE; full suite **1385 passed, 0 failed, 3
  skipped; 356,585 assertions. Status: SUCCESS.**

## Step 12: GodotSteam vendored + SDK-gated stub (DONE)

- Upstream moved: Gramps/GodotSteam on GitHub is a dead pointer; the
  real repo is `codeberg.org/godotsteam/godotsteam`, module branch
  `godot4` (HEAD 4eb6cce, Oct 2026 — actively maintained, 4.7-drift
  risk low; needs Steamworks SDK 1.65).
- Fork: [CoolBeanGames/GodotSteam](https://github.com/CoolBeanGames/GodotSteam)
  (`godot4` branch = upstream 4eb6cce, full history kept for future
  merges), added as `modules/godotsteam` submodule pinned to that
  commit. Clone with `--recurse-submodules`.
- SDK gate in `crete.build.ps1`: if
  `modules/godotsteam/sdk/redistributable_bin/win64/steam_api64.lib`
  exists, build the real module (`module_godotsteam_enabled=yes`);
  else build `crete_steam_stub` instead. Local builds always take the
  stub path (SDK is not redistributable); P4 CI injects the SDK as a
  secret for release builds. **Not yet verified: full GodotSteam
  compile against our 4.7 fork needs the SDK, so it happens in CI.**
- Stub (`modules/crete_steam_stub/`, mutually exclusive with the real
  module): registers the same `Steam` class + singleton with the same
  method shapes for `steamInitEx` (returns
  `{status: 1, verbal: ...}`, FAILED_GENERIC shape-compatible),
  `isSteamRunning` (false), `restartAppIfNecessary` (false),
  `getAppID` (0), plus an `isCreteStub()` marker the real module
  never has. Glue regen picked it up automatically: C# `Steam` class
  with static `SteamInitEx/GetAppID/IsCreteStub` exists in every
  build; SDK builds expand it to the full API.
- Tests: new `Crete Steam` suite (singleton registered; stub branch
  asserts the honest-failure values, SDK branch asserts surface
  existence without calling init).
- Results: build DONE; focused 2/2 (13 assertions); full suite
  **1387 passed, 0 failed, 3 skipped; 356,627 assertions. Status:
  SUCCESS.**

## Step 13: Publish dock (preset build + itch/butler + Steam/steamcmd) (DONE)

- New `GodotTools/Publish/`, pure core + thin dock (ships as a bottom
  dock; P3 owns layout and will place it in the center tab strip):
  - `ExportPresetsFile.cs`: minimal `export_presets.cfg` reader
    (`[preset.N]` name/platform/export_path) + platform->itch-channel
    defaults. Pure BCL.
  - `SteamVdf.cs`: SteamPipe `appbuild` VDF generator (appid, desc,
    content/buildoutput dirs, depot, file exclusion). Pure BCL.
  - `PublishTools.cs`: machine-local tool cache
    (`%LOCALAPPDATA%/Crete/tools` etc.); PATH preferred, fetch on
    demand with manual-install fallback errors. butler +
    steamcmd URLs, zip/tar.gz extraction, unix chmod. Pure BCL.
  - `PublishKeychain.cs`: Windows Credential Manager (P/Invoke),
    macOS `security`, Linux `secret-tool`. Secrets (itch API key,
    Steam user/pass) live here, never in the project. Pure BCL.
  - `CretePublishDock.cs`: preset picker + headless self-export
    (`--export-release`, 30-min cap, templates-missing advisory),
    itch section (user/game/channel persisted to ProjectSettings,
    keychain API key, `butler push dir user/game:channel` with
    `BUTLER_API_KEY` env), Steam section (real GodotSteam keys
    `steam/initialization/app_data/*` + init flag, depot id, VDF
    written under `.godot/`, steamcmd `+login +run_app_build +quit`,
    partner-dashboard link, stub-build notice via
    `Steam.IsCreteStub()`). All subprocesses stream through
    `Agents.AgentRunner` (extended with optional env passthrough).
- Achievements/cloud saves live on the Steam partner site (GodotSteam
  reads them from Steam, not settings), so the tab links the
  dashboard instead of editing them; the "in Project Settings" half
  is the AppID family + init flags, which SDK builds consume.
- Fetch lesson: `broth.itch.ovh` is RETIRED (NXDOMAIN, confirmed via
  itch.io admin notice); canonical butler source is
  `broth.itch.zone/butler/...`. No GitHub-release fallback exists.
  steamcmd: `steamcdn-a.akamaihd.net/.../steamcmd.zip` (+`_linux`
  tarball; no macOS build — tab errors honestly there).
- Tests: `tests/csharp/publish_core_test/` **32/32** (preset parse,
  VDF + escaping, URLs, zip/tar.gz nested extraction, live Windows
  keychain roundtrip, `Which`; live-gated: real butler v15.32.0
  fetched + runs, cached second call, real steamcmd fetched).
  Agent core grew to **29/29** (env passthrough).
- E2E: headless open with a preset file prints
  `presets=1 butler=<cached> steamcmd=<cached> steam_stub=True`,
  exit 0. Live proofs: `butler push --help` shape, steamcmd
  self-update + clean quit (exit 0), and the dock's exact export
  command resolving the preset to the templates-missing terminal
  state (templates are user-installed; the dock warns).
- C# note: qualify `System.Environment` (Godot.Environment collides).
- Repo lesson: GodotTools' `.gitignore` has a blanket `publish/`
  (ClickOnce output) rule that silently swallowed the new
  `GodotTools/Publish/*.cs` sources — the first Step 13 commit went
  out without them (broken fresh-clone build). Fixed with a
  same-file `!GodotTools/Publish/` + `!GodotTools/Publish/*.cs`
  negation in a followup commit; always check `git status` shows new
  dirs before committing (negation paths are relative to the
  `.gitignore`, not the repo root).
- Results: build DONE; full suite **1387 passed, 0 failed, 3
  skipped; 356,627 assertions. Status: SUCCESS.**

## Step 14 (P3A): Crete theme preset + title + File/Edit menus (DONE)

- Theme: new `Crete` color preset (accent #1e9df0, base ~#1e1e1f,
  contrast 0.25) added to `EditorThemeManager` + preset enum, and
  made the default for fresh installs. Existing users keep their
  stored preset (verified: the test machine's persisted `Default`
  won until flipped to `Crete`).
- Title: editor window is now `<project> - Crete` (floating dock
  windows and Game Workspace match). `GODOT_VERSION_NAME` untouched
  (console/about keep upstream identity).
- Menus: new File (New/Open/Recent/Save/Save As/Save All/Quit) and
  Edit (Undo/Redo) menus ahead of Scene; Scene keeps inherited/
  reopen/quick-open/export/reload/close items. Same action IDs and
  handlers, so shortcuts and behavior are unchanged; disabled-state
  updaters moved with their items. Order: File Edit Scene Project
  Debug Editor Help, per mockup.
- Verification is now visual: `C:/Users/callm/AppData/Local/Temp/crete-shot.ps1`
  launches the GUI maximized, minimizes/restores other windows,
  optionally clicks/keys, screenshots, kills. Dock bg samples
  #1F1F20 (mockup --bg1 exactly); File menu contents + shortcuts
  confirmed on a clicked-open shot. Known minor deviation: the
  title/menu row derives #121212 (Godot darkens the menu row from
  base; the mockup's #2b2b2d there would need per-control overrides
  — coherent dark look accepted).
- Results: build DONE; full suite **1387 passed, 0 failed, 3
  skipped; 356,627 assertions. Status: SUCCESS.**

## Step 15 (P3B): main strip `Scene | renderer dropdown | Game` (DONE)

- Strip: old `2D` button renamed `Scene`; old `3D` button detached
  from the strip (kept alive for shortcuts/selection) and replaced
  with an `OptionButton` (`crete_3d_dropdown`) listing 2D/3D;
  `Script` hidden from the strip when an external text editor is
  configured (`editor/external/text_editor_exec_path`); order
  Scene/dropdown/Game.
- Sync: new `selected_main_screen_changed(name)` signal on
  `EditorMainScreen`; node connects it to update the dropdown text on
  shortcut/screen switches (Ctrl+F1 etc); dropdown selection calls
  `editor_main_screen->select(name)`.
- Defaults: 2D is the startup screen on fresh installs and the
  dropdown's default text.
- Toolbar: canvas-item toolbar trimmed to left-half tools + View to
  leave top-right room for the transport strip (Step 18).
- Visual proof: `Scene | 3D dropdown | Game` with no Script button
  (`crete-step15b.png`); Ctrl+F1 switches to 2D + dropdown syncs
  (`crete-step15d.png`); dropdown opens with 2D/3D items
  (`crete-step15f.png`); clicking 2D switches views
  (`crete-step15g.png`).
- Results: build DONE; full suite **1387 passed, 0 failed, 3
  skipped; 356,627 assertions. Status: SUCCESS.**

## Step 16 (P3C): Publish center tab (DONE)

- C#: `GodotSharpEditor` is now a main-screen plugin named
  `Publish` (`_HasMainScreen`/`_MakeVisible`; no `_Handles`/
  `_GetState` overrides exist, so sub-editor dispatch and layout
  state are unaffected). Selecting the tab reparents the Publish
  dock's body (`CretePublishDock.ContentRoot`) into a padded
  `MarginContainer` under `GetEditorMainScreen()` and removes the
  bottom-dock tab; deselecting moves everything back. `_ExitTree`
  restores the body on shutdown. No new C++ interop was needed:
  stock `_has_main_screen`/`_make_visible` machinery plus the
  already-bound `set_main_screen_editor`/`add_dock`/`remove_dock`.
- C++: new `editor/editor_publish` shortcut (Ctrl+F5, macOS
  Meta+Ctrl+5) auto-attaches to the strip button + command
  palette; `shortcut_input` branch selects by name; the 2D/3D
  dropdown now stays sticky on non-Scene screens (Publish keeps
  the last renderer instead of forcing 2D).
- E2E: `-- --crete-e2e-publish-screen` headless run prints
  `visible=True content_parent=.../@EditorMainScreen@...` then
  `visible=False content_parent=.../@EditorBottomPanel@...`,
  exit 0. (The RID-leak ERROR lines in that log are pre-existing
  `--quit-after` teardown noise, identical without the flag.)
- GUI proof: `Scene | 3D dropdown | Game | Publish` strip
  (`crete-step16a.png`); Ctrl+F5 fills the center with the Publish
  UI, Publish pressed, bottom Publish tab gone (`crete-step16b*`);
  Ctrl+F1 restores Scene + dock tab (`crete-step16c*`); mouse
  click selects Publish, tooltip shows the Ctrl+F5 shortcut
  (`crete-step16g.png`).
- Harness lessons: GUI clicks need `-WaitForCSharp` (new
  `crete-shot.ps1` switch polling the bottom tab row) because the
  C# strip tab appears ~30 s after launch and shifts the centered
  strip; and click coordinates must be pixel-measured
  (`GetPixel` runs), never eyeballed — Publish sits at
  x 1390-1450, y 38-52, not where it looks.
- Results: build DONE; full suite **1387 passed, 0 failed, 3
  skipped; 356,627 assertions. Status: SUCCESS.**

## Step 17 (P3D): Hierarchy / Project / Console docks (DONE)

- Hierarchy (`scene_tree_dock`): tab renamed Scene -> Hierarchy
  (layout key pinned to "Scene"); toolbar split into a buttons
  row (instance/scripts/menu + Create "+" at the right) and a
  full-width filter row (`Filter nodes… (t:Texture, g:Group)`);
  tree never h-scrolls, long names ellipsis (TreeItem default);
  tab shows the edited scene (`Hierarchy — main.tscn`).
- Project (`filesystem_dock`): moved to the bottom panel, first
  tab (layout key pinned to "FileSystem"); tab shows the browsed
  path (`Project — res://`); horizontal mode already renders the
  thumbnail hsplit, but NOTIFICATION_READY forced LIST mode —
  fixed to respect the layout (`thumbs=on`). Import dock stays
  tabbed with Hierarchy (functional reimport home); History stays
  lower-left.
- Console (`editor_log`): Output -> Console (key pinned to
  "Output"); shortcut/shortcut-text paths unchanged.
- Canonical fresh bottom order: Project, Console, GitHub, Agents,
  Debugger, Animation, Shader Editor, Audio, MSBuild, Publish.
  Upstream NOTIFICATION_READY snapshots add-order into
  `default_layout` dock_9, so the snapshot is now
  canonical-sorted (`_crete_sorted_bottom_keys`) and fresh
  installs (no `editor_layout.cfg`) get the order applied live;
  saved layouts still win for existing users. Fresh installs
  also load `default_layout` fully (side slots + Project
  selected). No-config runs print `CreteDocks: fresh bottom=[…]`.
- Inspector (`inspector_dock`): Unity-style object header (icon +
  name + `Class · script.cs · %Unique`) above the search box,
  updated in `update()`; tags row from Step 8 already sits on
  top; script [Export]s already group (stock). `Meta` tab
  deliberately skipped: node metadata is already fully edited
  inline in the Inspector (section + Add Metadata dialog) — a
  second editor would duplicate/conflict.
- Signals (`signals_dock`): tab shows persist-connection count
  (`Signals (1)`, text-only — no emoji: font coverage), updated
  on selection + undo-redo history (connect/disconnect/undo).
  Counts only CONNECT_PERSIST like ConnectionsDock.
- Bottom-right `+ Import…` button: file dialog (multi) copies
  into the Project's current folder (skips existing with a
  warning) + filesystem rescan. Global bottom "filter" skipped:
  every tab keeps its own filter.
- E2E: `-- --crete-e2e-docks` headless run opens `res://main.tscn`
  (fixture-only, SKIP-guarded), selects Player then PlayButton,
  and prints hierarchy/project/header/tags/filter/rows/add_last/
  hscroll/thumbs/import/signals markers, exit 0. Badge reads
  after settle+reselect (scene connections land an idle after
  open; every real click recounts anyway).
- GUI proof (fresh layout): Hierarchy tabs/filter/`+`, History,
  canonical bottom tabs, Project tree+files, `+ Import…`
  (`crete-step17a*`). Pixel shots of thumbnails/tree/header/
  badge deferred: the machine's owner returned mid-step (stock
  Godot + OBS + browser active), so window-fighting screenshots
  were suspended in favor of headless E2E; `-OpenScene` +
  double-click-file support added to `crete-shot.ps1` for the
  follow-up.
- Results: build DONE; full suite **1387 passed, 0 failed, 3
  skipped; 356,627 assertions. Status: SUCCESS.**

## Step 18 (P3E): transport row + status bar (DONE)

- Transport cluster (new `CreteTransport` HBox between the
  centered strip and the run bar): the 2D/3D dropdown moved out
  of the strip (strip is now `Scene | Game | Publish`), a `Snap:
  8px` label polling `CanvasItemEditor::get_grid_step()`, and a
  Lock/Unlock button mirroring selection state that locks/
  unlocks selected CanvasItems via undo_redo (`_edit_lock_`
  metas + `item_lock_status_changed`), same semantics as the
  canvas toolbar buttons. 1 s tick, null-guarded.
- Status bar (viewport bottom edge: the bottom panel now lives
  in `CreteBottomVB` under the bar): `Errors: N`
  (red when > 0), `Warnings: N` (orange when > 0), `Output:
  <last console line, ellipsized>` (expands), renderer pretty
  name (`Forward+`), editor viewport size (`2560×1080`), git
  (`<branch> · clean|N`, `no repo` outside repos; one
  `git status --porcelain=v1 --branch` every 10th tick).
  Hides with the bottom region when fully collapsed (it is
  bottom chrome). `EditorLog` gained `crete_get_message_count`/
  `crete_get_last_message`/`crete_has_message` reads.
- GitHub dock tab shows the change count (`GitHub (3)`) via
  dynamic `Title` (node `Name` untouched, so layout keys stay
  stable); resets on empty states.
- Console banner on every startup: `Crete editor (Godot 4.7.3
  base) — Forward+ · External editor: VS Code` (version from
  `core/version.h`, editor id mapped from the Step 15 table).
- Correction: Step 15's "canvas toolbar trim" never happened
  (commit `27d993a3ac` only renamed the plugin) — the 2D
  toolbar is intact with all snap controls; no UX was removed.
- E2E (`--crete-e2e-docks` step 4): `transport=[2D|Snap:
  8px|lock_on]`, `status=[Errors: 0|Warnings: 0|Output:
  …|Forward+|1024×600|no repo]`, `banner=yes`,
  `lock_action=locked` (press path toggles the meta, then
  restores), exit 0.
- GUI pixel shots deferred (owner active); state fully proven
  headless.
- Results: build DONE; full suite **1387 passed, 0 failed, 3
  skipped; 356,627 assertions. Status: SUCCESS.**

## Step 19 (P4): CI (editor + templates, Linux/Windows/Web) (DONE)

- `linux_builds.yml`: rewritten into `build-linux-editor`
  (always: mono editor + doctest + source-generator tests +
  agent-runner tests + `--crete-e2e-docks` marker greps) and
  `build-linux-templates` (only when the `templates` input is
  true: release+debug template matrix, `fail-fast: false`).
  Crete SCons flags (`module_mono_enabled=yes`,
  `module_gdscript_enabled=no`, `module_openxr_enabled=no`,
  `tests=yes`, `compiledb=yes` on the editor).
- `windows_builds.yml` + `web_builds.yml`: were env-only stubs
  with no jobs; replaced with real jobs mirroring Linux
  (Windows editor on `windows-2022` adds the live-OS-keychain
  publish-core tests plus the upstream D3D12/ANGLE/AccessKit
  SDK steps; Web builds release+debug wasm templates via the
  official `emscripten-core/setup-emsdk@v16` pinned at 4.0.11
  (upstream 4.6 pin) with closure compiler, no editor job).
- `runner.yml`: PR/push flow calls Linux + Windows with
  `changed-files` + `secrets: inherit` (templates default off,
  so PR CI stays editor-only and fast); the Web call is removed
  (release-only, too heavy for PRs).
- `crete_release.yml` (new): on `*-stable` tags (validated
  `N.M...-stable`) calls all three builds with `templates:
  true`, then zips editor + templates per the launcher
  contract (`crete-<tag>-win64-dotnet.zip`,
  `-linux64-dotnet.zip`, `-templates-<win64|linux64|web>-
  dotnet.zip`) and `gh release create` + `upload --clobber`.
  Manual dispatches build everything but publish nothing.
- Steam: real SDK only via the `STEAMWORKS_SDK_URL` secret
  (unzipped to `modules/godotsteam/sdk/`, godotsteam on + stub
  off); empty secret keeps the Step 12 stub. Same step on all
  three platforms.
- Static gates: `pre-commit/mirrors-clang-format` pinned
  `v21.1.7`; clang-format fixed 4 fork-touched files
  (whitespace in `editor_node.cpp`/`main.cpp`, an include move
  in `script_text_editor.cpp`, a trailing line in
  `multiplayer_debugger.cpp`). `codespell` renamed one
  misnamed local in `agent_runner_test` (suite still 29/29).
- E2E marker greps in CI (`e2e DONE`, `dock_9=[...10 docks]`,
  `signals2=[Signals (1)]`, `lock_action=locked`,
  `banner=yes`, `thumbs=on`, `hscroll=off`, `import=landed`)
  match the locally proven headless run.
- Local proof: `yaml.safe_load` parses all five workflows,
  agent suite 29/29, E2E markers all green headless.
  Known-broken upstream `gitignore_check.sh`
  (`**/*` exceeds ARG_MAX, green by accident) left untouched
  as out of scope.
- Results: build DONE; full suite **1387 passed, 0 failed, 3
  skipped; 356,627 assertions. Status: SUCCESS.** Commit on
  `crete`; only CI itself can prove the Linux/Web/Steam-SDK
  paths.
