# sign_release.ps1
# Signs a mod DLL with your private key, producing a .sig file to upload
# alongside the DLL as a GitHub release asset.
#
# Usage:
#   .\sign_release.ps1 -DllPath "path\to\IssaPlugin.dll"
#   .\sign_release.ps1 -DllPath "path\to\IssaPlugin.dll" -PrivateKeyPath "path\to\private_key.xml"
#
# Both IssaPlugin.dll and IssaPlugin.dll.sig must be uploaded as release assets.

param(
    [Parameter(Mandatory)]
    [string]$DllPath,

    [string]$PrivateKeyPath = "private_key.xml"
)

if (-not (Test-Path $DllPath)) {
    Write-Error "DLL not found: $DllPath"
    exit 1
}

if (-not (Test-Path $PrivateKeyPath)) {
    Write-Error "Private key not found: $PrivateKeyPath"
    exit 1
}

$privateKeyXml = Get-Content $PrivateKeyPath -Raw

$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString($privateKeyXml)

$fileBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $DllPath).Path)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$signature = $rsa.SignData($fileBytes, $sha256)

$sigPath = $DllPath + ".sig"
[System.IO.File]::WriteAllBytes($sigPath, $signature)

Write-Host ""
Write-Host "Signed successfully."
Write-Host "  DLL:       $DllPath"
Write-Host "  Signature: $sigPath"
Write-Host ""
Write-Host "Upload both files as assets to your GitHub release."
