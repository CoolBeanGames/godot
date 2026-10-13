# Crete canonical engine build.
# Step 1: stock .NET editor, no engine changes. Later steps add module flags here.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$cores = $env:NUMBER_OF_PROCESSORS
Write-Host "Building Godot .NET editor with $cores jobs..."
py -3.12 -m SCons platform=windows module_mono_enabled=yes module_gdscript_enabled=no module_openxr_enabled=no tests=yes "-j$cores"
$exe = Get-ChildItem bin/godot.windows.editor.*.mono.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exe) { throw 'Editor binary not found after build.' }
Write-Host ('Generating C# glue with ' + $exe.Name)
& $exe.FullName --headless --generate-mono-glue ./modules/mono/glue
Write-Host 'Building C# assemblies...'
py -3.12 modules/mono/build_scripts/build_assemblies.py --godot-output-dir ./bin
Write-Host 'DONE.'
