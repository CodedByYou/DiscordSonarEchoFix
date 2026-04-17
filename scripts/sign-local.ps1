# Local self-signing for sending the exe to friends without publishing.
# A self-signed cert removes the "Unknown publisher" line from the SmartScreen
# warning and shows your name instead. It does NOT bypass SmartScreen entirely.
# For that, use a real CA cert (SignPath for free OSS, or buy one).
#
# Usage from repo root:
#   pwsh ./scripts/sign-local.ps1
# Outputs:
#   dist/DiscordSonarEchoFix.exe (signed in place)
#   ./code-signing.pfx (cert backup, gitignored)

$ErrorActionPreference = 'Stop'
$exe = "$PSScriptRoot/../dist/DiscordSonarEchoFix.exe"
$pfx = "$PSScriptRoot/../code-signing.pfx"
$pwd = ConvertTo-SecureString 'changeit' -AsPlainText -Force
$subject = 'CN=Discord Echo Fix (self-signed)'

if (-not (Test-Path $exe)) { throw "Build first: dotnet publish src/DiscordSonarEchoFix.csproj -c Release -p:PublishMode=SelfContained -o dist" }

# Reuse existing cert if we already created one, otherwise generate a fresh code-signing cert
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating new self-signed code-signing cert..."
    $cert = New-SelfSignedCertificate `
        -Subject $subject `
        -Type CodeSigningCert `
        -KeyAlgorithm RSA -KeyLength 3072 `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(5) `
        -KeyExportPolicy Exportable `
        -KeyUsage DigitalSignature `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
    Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pwd | Out-Null
    Write-Host "Cert thumbprint: $($cert.Thumbprint)"
}

# Use PowerShell's built-in Authenticode signing — no Windows SDK / signtool needed.
$result = Set-AuthenticodeSignature -FilePath $exe -Certificate $cert `
    -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
if ($result.Status -ne 'Valid') {
    throw "Signing failed: $($result.Status) - $($result.StatusMessage)"
}
Write-Host "Signature status: $($result.Status)"

Write-Host "`nSigned. Friends will still see SmartScreen warning until they click 'More info' -> 'Run anyway'."
Write-Host "To remove the warning entirely, send them code-signing.pfx and have them install it as a Trusted Publisher (advanced)."
