# Crete canonical engine build.
# Step 1: stock .NET editor, no engine changes. Later steps add module flags here.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Native tools (scons, NuGet) print warnings to stderr on success. Piping
# 2>&1 through ForEach-Object stringifies those lines so they print without
# aborting the script; the real verdict is $LASTEXITCODE after each step.
function Invoke-Native($cmd) {
	Write-Host ">>> $cmd"
	Invoke-Expression "$cmd 2>&1" | ForEach-Object { "$_" }
	if ($LASTEXITCODE -ne 0) { throw "FAILED ($LASTEXITCODE): $cmd" }
}

$cores = $env:NUMBER_OF_PROCESSORS
Write-Host "Building Godot .NET editor with $cores jobs..."
Invoke-Native "py -3.12 -m SCons platform=windows module_mono_enabled=yes module_gdscript_enabled=no module_openxr_enabled=no tests=yes -j$cores"
$exe = Get-ChildItem bin/godot.windows.editor.*.mono.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exe) { throw 'Editor binary not found after build.' }
Write-Host ('Generating C# glue with ' + $exe.Name)
Invoke-Native "& '$($exe.FullName)' --headless --generate-mono-glue ./modules/mono/glue"
Write-Host 'Building C# assemblies...'
Invoke-Native "py -3.12 modules/mono/build_scripts/build_assemblies.py --godot-output-dir ./bin"
Write-Host 'DONE.'
