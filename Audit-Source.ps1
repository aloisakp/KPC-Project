[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$sources = @((Get-ChildItem -LiteralPath $root -File -Filter '*.cs')) +
    @((Get-ChildItem -LiteralPath (Join-Path $root 'Core') -File -Filter '*.cs'))
foreach ($directory in 'Linux', 'Worker', 'Installer/Windows', 'Installer/Linux') {
    $sources += @(Get-ChildItem -LiteralPath (Join-Path $root $directory) -File |
        Where-Object { $_.Extension -in '.cs', '.csproj', '.axaml' })
}
$forbidden = 'QRCoder|SteamKit2|BeginAuthSession|LoginWithCredentials|RefreshToken|PasswordBox|DangerousAcceptAnyServerCertificateValidator|BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|gh[pousr]_[A-Za-z0-9]{30,}|PakAesKey|ApplyPakReaderPatch|Build-Map2LegacyPatch|SkyIslandLobbyBuilder|frida-client-gateway-bootstrap'
$files = $sources + @((Get-ChildItem -LiteralPath $root -File -Filter '*.xaml')) +
    @((Get-Item -LiteralPath (Join-Path $root 'KpcLauncher.csproj')))
foreach ($file in $files) {
    if ($file.Name -ne 'TesterClient.cs' -and [IO.File]::ReadAllText($file.FullName) -match 'ServerCertificateCustomValidationCallback') {
        throw "Custom TLS validation is allowed only in the tested tester certificate-pin implementation."
    }
    if ([IO.File]::ReadAllText($file.FullName) -match $forbidden) {
        throw "Forbidden credential/authentication code or potential embedded secret in $($file.Name)."
    }
}
foreach ($obsolete in 'Core/SteamSession.cs','Core/Secrets.cs','Core/UiAuthenticator.cs',
        'Core/CmServers.cs','Core/DepotDownloader.cs','InverseBoolConverter.cs') {
    if (Test-Path -LiteralPath (Join-Path $root $obsolete)) { throw "Obsolete code remains: $obsolete" }
}
Write-Host 'Source audit passed: no legacy credential/QR implementation or obvious embedded secret patterns.'
