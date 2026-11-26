<#
  Regenerates the SHA-256 allowlist in Core\Services\ToolIntegrity.cs from the tools in -ToolDir.
  Run this ONLY after you have deliberately replaced a bundled tool with a version you trust.

    .\Other\UpdateToolHashes.ps1                 # uses bin\Debug
    .\Other\UpdateToolHashes.ps1 -ToolDir C:\x   # another folder
#>
param([string]$ToolDir = "$PSScriptRoot\..\bin\Debug")
$ErrorActionPreference = 'Stop'
$file = "$PSScriptRoot\..\Core\Services\ToolIntegrity.cs"
$names = 'xextool.exe', 'X360PkgTool.exe', 'XbeTool.exe', 'ISO2God.exe', 'JRunner.exe', 'JungleFlasher.exe', 'XBBC.exe', 'abgx360_v1.0.7_setup_hadzz.exe'
$lines = foreach ($n in $names) {
    $p = Join-Path $ToolDir $n
    if (-not (Test-Path $p)) { throw "Missing tool: $p" }
    '            { "' + $n + '", "' + (Get-FileHash $p -Algorithm SHA256).Hash + '" },'
}
$src = Get-Content $file -Raw
$pattern = '(?s)(new Dictionary<string, string>\(StringComparer\.OrdinalIgnoreCase\)\s*\{\r?\n).*?(\r?\n\s*\};)'
if ($src -notmatch $pattern) { throw 'Could not find the allowlist table in ToolIntegrity.cs' }
$src = [regex]::Replace($src, $pattern, { param($m) $m.Groups[1].Value + ($lines -join "`r`n") + $m.Groups[2].Value })
Set-Content $file $src -Encoding UTF8 -NoNewline
Write-Host "Updated $($names.Count) tool hashes in ToolIntegrity.cs - rebuild to apply."
