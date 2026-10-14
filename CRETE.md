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
