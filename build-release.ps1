<#
  Builds a release of X360GameHack 2026 and its installer.

    .\build-release.ps1                      # build Release + staging folder + MSI
    .\build-release.ps1 -SignThumbprint ABC  # also Authenticode-sign the binaries and the MSI

  Output:  dist\X360GameHack\          (portable folder)
           dist\X360GameHack-Setup.msi (installer)

  The third-party tools that ship with the app (xextool, X360PkgTool, xeBuild, J-Runner, firmware, ...) are copied
  from -PayloadDir (by default bin\Debug, where they live in this repo). Build outputs, debug files and junk from
  that folder are never shipped.
#>
param(
    [string]$PayloadDir = "$PSScriptRoot\X360GameHack\bin\Debug",
    [string]$SignThumbprint = $env:X360GH_SIGN_THUMBPRINT,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found (install Visual Studio with the .NET desktop workload).' }

Write-Host '== Building Release' -ForegroundColor Cyan
foreach ($p in "$PSScriptRoot\DiskReader\X360GameHack.DiskReader.csproj", "$PSScriptRoot\X360GameHack\X360GameHack.csproj") {
    & $msbuild $p -nologo -v:m -p:Configuration=Release -p:Platform=AnyCPU -t:Rebuild
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $p" }
}
$release = "$PSScriptRoot\X360GameHack\bin\Release"
# The Disk Reader is two programs: the native launcher Windows elevates (X360GameHack.DiskReader.exe) and the .NET
# helper it starts with a clean environment (X360GameHack.DiskReaderCore.exe). Files the older single-program Disk
# Reader left in the output folder are removed first, so nothing stale ships.
Remove-Item "$release\X360GameHack.DiskReader.exe.config", "$release\X360GameHack.DiskReader.pdb" -Force -ErrorAction SilentlyContinue
& "$PSScriptRoot\DiskReader\Launcher\build-launcher.ps1" -OutDir $release

Write-Host '== Staging' -ForegroundColor Cyan
$stage = "$PSScriptRoot\dist\X360GameHack"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null

# Never ship: debug symbols, VS hosting files, ClickOnce leftovers, logs, temp files, old release zips,
# the legacy base-file dump and the UI libraries the old version used.
$exclude = @('*.pdb', '*.vshost.*', '*.application', 'X360GameHack.exe.manifest', 'logs.txt', '*.zip', '*.bak',
             'X360GameHack_temp.bat', 'CurrentXEXBaseFile.exe', 'DevComponents.DotNetBar2.dll', 'Krypton*.dll', 'Krypton*.xml',
             'X360GameHack.exe', 'X360GameHack.exe.config', 'X360GameHack.DiskReader.exe', 'X360GameHack.DiskReader.exe.config',
             'X360GameHack.DiskReaderCore.exe', 'X360GameHack.DiskReaderCore.exe.config')
Get-ChildItem $PayloadDir -Force | Where-Object {
    $n = $_.Name
    -not ($exclude | Where-Object { $n -like $_ }) -and $n -ne 'app.publish'
} | ForEach-Object { Copy-Item $_.FullName $stage -Recurse -Force }
# Library XML docs next to their DLLs are not needed at runtime.
Get-ChildItem $stage -Filter *.xml -File | Where-Object { Test-Path (Join-Path $stage ($_.BaseName + '.dll')) } | Remove-Item -Force
# Fresh build outputs last so they always win.
Get-ChildItem $release -File | Where-Object {
    $_.Extension -notin '.pdb', '.application' -and $_.Name -notlike '*.xml' -and $_.Name -ne 'X360GameHack.exe.manifest'
} | Copy-Item -Destination $stage -Force
# Paid mods and anything of the licence / update servers NEVER ship: mods are downloaded by a licensed program from
# the licence server only. Remove a mods folder that slipped in, then refuse to build if anything secret is left.
Remove-Item (Join-Path $stage 'mods') -Recurse -Force -ErrorAction SilentlyContinue
$secret = Get-ChildItem $stage -Recurse -File -Force | Where-Object {
    $_.Name -match '\.(x360mod|keycard\.txt|sqlite3|pfx|p12|snk|pem|key)$' -or $_.Name -match 'server-payload|key-wrap|update-signing|licence_server|\.dev\.vars|wrangler\.toml|owner-licence' -or
    $_.FullName -match '\\(licence-server|licence-worker|update-worker|X360GameHack-private)\\'
}
if ($secret) { throw ("Refusing to package - these must never ship:`n" + (($secret | ForEach-Object FullName) -join "`n")) }
# The program Windows elevates must be the native launcher - never a .NET program (an elevated .NET program loads
# profilers and runtime options from the user's environment) - and the helper it starts must be there.
$launcher = Join-Path $stage 'X360GameHack.DiskReader.exe'
if (-not (Test-Path $launcher) -or -not (Test-Path (Join-Path $stage 'X360GameHack.DiskReaderCore.exe'))) { throw 'The Disk Reader launcher or its helper is missing from the package.' }
$isDotNet = $true
try { [void][System.Reflection.AssemblyName]::GetAssemblyName($launcher) } catch [System.BadImageFormatException] { $isDotNet = $false }
if ($isDotNet) { throw 'X360GameHack.DiskReader.exe is a .NET program - it must be the native launcher (DiskReader\Launcher).' }
# What is tested and what is BETA travels with the program (the Q & A points to it).
if (Test-Path "$PSScriptRoot\TESTING.md") { Copy-Item "$PSScriptRoot\TESTING.md" $stage -Force }
# Files downloaded from the internet carry a "mark of the web"; strip it so the shipped copy is clean.
Get-ChildItem $stage -Recurse -File | Unblock-File

if ($SignThumbprint) {
    Write-Host '== Signing binaries' -ForegroundColor Cyan
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe | Where-Object FullName -match '\\x64\\' | Select-Object -Last 1
    foreach ($f in 'X360GameHack.exe', 'X360GameHack.DiskReader.exe', 'X360GameHack.DiskReaderCore.exe') {
        & $signtool.FullName sign /sha1 $SignThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 (Join-Path $stage $f)
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $f" }
    }
}

Write-Host '== Building installer' -ForegroundColor Cyan
# the installer carries the program's own version, so the installer of a newer release replaces this one
$appVersion = [regex]::Match((Get-Content "$PSScriptRoot\X360GameHack\Web\Pastebin.cs" -Raw), 'const string Version = "v?(\d+\.\d+\.\d+)"').Groups[1].Value
if (-not $appVersion) { throw 'Could not read the version from Web\Pastebin.cs.' }
dotnet build "$PSScriptRoot\Installer\X360GameHack.Installer.wixproj" -c Release -p:StagingDir="$stage" -p:ProductVersion=$appVersion -o "$PSScriptRoot\dist\msi" -nologo
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$msi = Get-ChildItem "$PSScriptRoot\dist\msi" -Filter *.msi -Recurse | Select-Object -First 1
Copy-Item $msi.FullName "$PSScriptRoot\dist\X360GameHack-Setup.msi" -Force
if ($SignThumbprint) {
    & $signtool.FullName sign /sha1 $SignThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 "$PSScriptRoot\dist\X360GameHack-Setup.msi"
}
$size = '{0:N1} MB' -f ((Get-Item "$PSScriptRoot\dist\X360GameHack-Setup.msi").Length / 1MB)
Write-Host "== Done: dist\X360GameHack-Setup.msi ($size)" -ForegroundColor Green
