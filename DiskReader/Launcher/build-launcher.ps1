<#
  Builds the native Disk Reader launcher (X360GameHack.DiskReader.exe) into -OutDir.
  Needs Visual Studio's C++ tools ("Desktop development with C++"). Static runtime (/MT): nothing to install on the
  user's PC. Hardened: control-flow guard, ASLR, DEP, buffer checks, and DLLs loaded only from System32.
#>
param([Parameter(Mandatory = $true)][string]$OutDir,
      # test build: starts envdump.exe, reads a test registry key, runs without administrator rights (never shipped)
      [switch]$Test)
$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vs = & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
if (-not $vs) { throw 'Building the Disk Reader launcher needs Visual Studio "Desktop development with C++".' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
$src = $PSScriptRoot
$obj = Join-Path $src 'obj'
New-Item -ItemType Directory -Force $obj | Out-Null
New-Item -ItemType Directory -Force $OutDir | Out-Null
$exe = Join-Path (Resolve-Path $OutDir) $(if ($Test) { 'launcher-test.exe' } else { 'X360GameHack.DiskReader.exe' })
$def = if ($Test) { '/DLAUNCHER_TEST' } else { '' }
$tag = if ($Test) { 'test' } else { 'release' }
$bat = Join-Path $obj "build-$tag.cmd"
@"
@echo off
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "$vcvars" x86 >nul || exit /b 1
cd /d "$src" || exit /b 1
rc /nologo $def /fo "$obj\DiskReaderLauncher-$tag.res" DiskReaderLauncher.rc || exit /b 1
cl /nologo /c /O1 /MT /GS /guard:cf /sdl /W4 /WX /DUNICODE /D_UNICODE $def /Fo"$obj\DiskReaderLauncher-$tag.obj" DiskReaderLauncher.c || exit /b 1
link /nologo /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /GUARD:CF /DEPENDENTLOADFLAG:0x800 /MANIFEST:NO /OUT:"$exe" "$obj\DiskReaderLauncher-$tag.obj" "$obj\DiskReaderLauncher-$tag.res" kernel32.lib advapi32.lib || exit /b 1
"@ | Set-Content -Path $bat -Encoding ASCII
& cmd.exe /c "`"$bat`""
if ($LASTEXITCODE -ne 0) { throw 'Building the Disk Reader launcher failed.' }
Write-Host "built $exe"
