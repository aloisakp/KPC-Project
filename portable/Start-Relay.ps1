[CmdletBinding()]
param([switch]$EnrollOnly)
$ErrorActionPreference='Stop'
$bundle=[IO.Path]::GetFullPath($PSScriptRoot)
function NoLinks([string]$path){
 for($item=[IO.DirectoryInfo]::new($path);$null-ne$item;$item=$item.Parent){
  if($item.Exists-and($item.Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Extract into a regular folder, without links or junctions.'}
 }
 if((Test-Path -LiteralPath $path)-and((Get-Item -LiteralPath $path).Attributes-band[IO.FileAttributes]::ReparsePoint)){throw 'Linked file rejected'}
}
NoLinks $bundle
$manifest=Get-Content -Raw -LiteralPath (Join-Path $bundle 'bundle-manifest.json')|ConvertFrom-Json
if($manifest.schema-ne'kp-relay-portable/v1'){throw 'Wrong portable manifest'}
$required=@('KpcLauncher.exe','runtime/node.exe','transport/run.cjs','transport/transport.cjs','check-route.cjs')
foreach($rel in $required){
 $row=@($manifest.files|Where-Object path -eq $rel);if($row.Count-ne1){throw "Missing bundle file: $rel"}
 $file=Join-Path $bundle $rel;NoLinks $file
 if((Get-FileHash -LiteralPath $file).Hash-ne$row[0].sha256){throw "Bundle file changed: $rel"}
}
$exe=Join-Path $bundle 'KpcLauncher.exe';$node=Join-Path $bundle 'runtime/node.exe'
$state=Join-Path $bundle 'RelayState';NoLinks $state
$init=Start-Process -FilePath $exe -ArgumentList '--relay-device-init' -WindowStyle Hidden -PassThru -Wait
if($init.ExitCode-ne0){throw 'Device enrollment failed. Existing files were preserved; see README.'}
$request=Join-Path $state 'device-request.json'
Write-Host "Public device enrollment request: $request"
Write-Host 'Send only device-request.json to the server operator. Never send the device-identity folder.'
if($EnrollOnly){return}
foreach($port in 11102,11104,11107,11108){
 if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){throw "Local port $port is already in use. Close the previous relay launcher first; no process was stopped."}
}
$run=Join-Path $state ('transport/'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force|Out-Null
$config=Join-Path $run 'config.json'
@{schema='kp-development-tls/v1';role='client';certificateFile=(Join-Path $state 'device-identity/client-cert.pem');privateKeyFile=(Join-Path $state 'device-identity/client-key.pem')}|ConvertTo-Json|Set-Content -LiteralPath $config -Encoding UTF8
$helper=$null
try{
 $arguments='"'+(Join-Path $bundle 'transport/run.cjs')+'" "'+$config+'"'
 $helper=Start-Process -FilePath $node -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
 $null=$helper.SafeHandle
 $command=Get-CimInstance Win32_Process -Filter "ProcessId=$($helper.Id)"
 @{role='portable-vps-client';pid=$helper.Id;startedAtUtc=$helper.StartTime.ToUniversalTime().ToString('o');executable=$node;commandLine=$command.CommandLine;run=$run}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'process.json') -Encoding UTF8
 $deadline=[DateTime]::UtcNow.AddSeconds(10)
 while(-not(Test-Path -LiteralPath (Join-Path $run 'status.json'))){
  $helper.Refresh();if($helper.HasExited){throw 'Relay helper exited. Check this run stderr.log.'}
  if([DateTime]::UtcNow-gt$deadline){throw 'Relay helper startup timed out'}
  Start-Sleep -Milliseconds 100
 }
 & $node (Join-Path $bundle 'check-route.cjs')
 if($LASTEXITCODE-ne0){throw 'The pinned VPS route is unavailable or this device is not enrolled. Give the operator device-request.json; do not change certificate settings.'}
 $env:KP_RELAY_SANDBOX_ROOT=$state;$env:KP_RELAY_SPLIT_MODE='1';$env:KP_RELAY_MULTI_PLAYER='1'
 $records=Join-Path $state 'processes';New-Item -ItemType Directory -Path $records -Force|Out-Null
 $launcher=Start-Process -FilePath $exe -ArgumentList '--relay-development=stable-2026-09-22' -WorkingDirectory $bundle -WindowStyle Hidden -PassThru
 @{role='relay-development-launcher';pid=$launcher.Id;startedAtUtc=$launcher.StartTime.ToUniversalTime().ToString('o');executable=$exe;argumentMarker='--relay-development=stable-2026-09-22';multiPlayer=$true}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $records ('launcher-'+$launcher.Id+'.json')) -Encoding UTF8
 Write-Host 'Leave this window open while testing. Closing it ends this device relay.'
 $launcher.WaitForExit()
}finally{
 if($null-ne$helper){$helper.Refresh();if(-not$helper.HasExited){$helper.Kill();$helper.WaitForExit(5000)|Out-Null}}
}
