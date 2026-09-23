[CmdletBinding()]
param([switch]$SplitServer)
$ErrorActionPreference='Stop'
$kpRoot='G:\Steam\steamapps\common\KurtzPel\Game\runtime-state\relay-lobby-sandbox'
$kpExe=Join-Path $PSScriptRoot 'artifacts\relay-dev\publish\KpcLauncher.exe'
if($SplitServer){$kpExe=Join-Path $PSScriptRoot 'artifacts\relay-split-next\publish\KpcLauncher.exe'}
if(-not(Test-Path -LiteralPath $kpExe -PathType Leaf)){throw 'Build the standalone development executable first; see RELAY-DEVELOPMENT.md'}
foreach($kpPath in @($kpRoot,(Split-Path -Parent $kpExe))){
    for($kpDirectory=[IO.DirectoryInfo]::new($kpPath);$null -ne $kpDirectory;$kpDirectory=$kpDirectory.Parent){
        if($kpDirectory.Exists -and ($kpDirectory.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Development paths cannot contain links or junctions'}
    }
}
New-Item -ItemType Directory -Path $kpRoot -Force | Out-Null
$kpMarker=Join-Path $kpRoot '.relay-sandbox-owner.json'
if(-not(Test-Path -LiteralPath $kpMarker)){
    [pscustomobject]@{schema='kp-relay-sandbox/v1';baseline='23bbacb12f906a6d57ee0abcb80e02df901ed8b4';root=$kpRoot}|ConvertTo-Json|Set-Content -LiteralPath $kpMarker -Encoding UTF8
}
$kpOwner=Get-Content -Raw -LiteralPath $kpMarker|ConvertFrom-Json
if($kpOwner.schema -ne 'kp-relay-sandbox/v1' -or $kpOwner.root -ne $kpRoot -or $kpOwner.baseline -ne '23bbacb12f906a6d57ee0abcb80e02df901ed8b4'){throw 'Existing sandbox marker does not match this test'}
$kpRecords=Join-Path $kpRoot 'processes'
New-Item -ItemType Directory -Path $kpRecords -Force | Out-Null
$kpOldRoot=$env:KP_RELAY_SANDBOX_ROOT
$kpOldSplit=$env:KP_RELAY_SPLIT_MODE
try{
    $env:KP_RELAY_SANDBOX_ROOT=$kpRoot
    $env:KP_RELAY_SPLIT_MODE=if($SplitServer){'1'}else{'0'}
    $kpProcess=Start-Process -FilePath $kpExe -WorkingDirectory (Split-Path -Parent $kpExe) -ArgumentList '--relay-development=stable-2026-09-22' -PassThru
    [pscustomobject]@{role='relay-development-launcher';pid=$kpProcess.Id;startedAtUtc=$kpProcess.StartTime.ToUniversalTime().ToString('o');executable=$kpExe;sha256=(Get-FileHash -LiteralPath $kpExe).Hash;argumentMarker='--relay-development=stable-2026-09-22'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $kpRecords ('launcher-'+$kpProcess.Id+'.json')) -Encoding UTF8
    Write-Output ('RELAY_DEVELOPMENT_LAUNCHER pid='+$kpProcess.Id)
}finally{$env:KP_RELAY_SANDBOX_ROOT=$kpOldRoot;$env:KP_RELAY_SPLIT_MODE=$kpOldSplit}
