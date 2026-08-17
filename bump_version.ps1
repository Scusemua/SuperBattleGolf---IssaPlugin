param([string]$Bump = "patch")

$csproj = "IssaPlugin.csproj"
$pluginInfo = "PluginInfo.cs"

$content = Get-Content $csproj -Raw
$current = [regex]::Match($content, '<Version>([^<]+)</Version>').Groups[1].Value
$parts = $current -split '\.'

switch ($Bump) {
    'major' { $parts[0] = [int]$parts[0] + 1; $parts[1] = 0; $parts[2] = 0 }
    'minor' { $parts[1] = [int]$parts[1] + 1; $parts[2] = 0 }
    default { $parts[2] = [int]$parts[2] + 1 }
}

$new = $parts -join '.'

$content = $content -replace '<Version>[^<]+</Version>', "<Version>$new</Version>"
Set-Content $csproj $content -NoNewline

$pic = Get-Content $pluginInfo -Raw
$pic = $pic -replace 'PLUGIN_VERSION = "[^"]*"', "PLUGIN_VERSION = `"$new`""
Set-Content $pluginInfo $pic -NoNewline

# The Thunderstore manifest was previously updated by hand and had drifted behind
# the other two files, which publishes the package under the wrong version.
$manifest = "ThunderStore/manifest.json"
if (Test-Path $manifest) {
    $mf = Get-Content $manifest -Raw
    $mf = $mf -replace '"version_number"\s*:\s*"[^"]*"', "`"version_number`": `"$new`""
    Set-Content $manifest $mf -NoNewline
} else {
    Write-Warning "manifest.json not found at $manifest - Thunderstore version NOT updated"
}

Write-Host "Bumped version $current -> $new (csproj, PluginInfo.cs, manifest.json)"
