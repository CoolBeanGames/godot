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
