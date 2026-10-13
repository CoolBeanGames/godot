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
