# Crete canonical engine build.
# Step 1: stock .NET editor, no engine changes. Later steps add module flags here.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# Native tools (scons, NuGet) print warnings to stderr on success. Run each
# under a scoped 'Continue' so those lines print instead of aborting; the real
# verdict is $LASTEXITCODE read immediately after. (Invoke-Expression with a
# command string silently swallowed output AND exit code -- never use it here.)
function Invoke-Native {
	param([string]$Exe, [string[]]$CmdArgs)
	Write-Host (">>> $Exe $($CmdArgs -join ' ')")
	$prev = $ErrorActionPreference
	$ErrorActionPreference = 'Continue'
	try {
		$out = & $Exe @CmdArgs 2>&1
		$code = $LASTEXITCODE
	} finally {
		$ErrorActionPreference = $prev
	}
	foreach ($line in $out) { "$line" }
	if ($code -ne 0) { throw "FAILED ($code): $Exe $($CmdArgs -join ' ')" }
}

$cores = $env:NUMBER_OF_PROCESSORS
Write-Host "Building Godot .NET editor with $cores jobs..."
Invoke-Native "py" @("-3.12", "-m", "SCons", "platform=windows", "module_mono_enabled=yes", "module_gdscript_enabled=no", "module_openxr_enabled=no", "tests=yes", "-j$cores")
# NOTE: use the .console.exe for CLI work. The plain .exe is GUI-subsystem, so
# PowerShell never waits for it under output capture (silent no-op, null exit).
$exe = Get-ChildItem bin/godot.windows.editor.*.mono.console.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $exe) { throw 'Editor console binary not found after build.' }
Write-Host ('Generating C# glue with ' + $exe.Name)
Invoke-Native $exe.FullName @("--headless", "--generate-mono-glue", "./modules/mono/glue")
Write-Host 'Building C# assemblies...'
Invoke-Native "py" @("-3.12", "modules/mono/build_scripts/build_assemblies.py", "--godot-output-dir", "./bin")
Write-Host 'DONE.'
