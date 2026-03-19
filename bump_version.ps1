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

Write-Host "Bumped version $current -> $new"
