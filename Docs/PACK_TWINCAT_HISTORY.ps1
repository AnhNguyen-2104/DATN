$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$src='C:\Users\aml\Documents'
$dest='C:\Users\aml\Desktop\DATN\PLC\Backups\TwinCAT_Stages'
New-Item -Path $dest -ItemType Directory -Force|Out-Null
$stages=Get-ChildItem -LiteralPath $src -Directory -Filter 'DATN_*STAGE_*' |
 Where-Object {$_.Name -match '^DATN_(CAMERA|VISION|XY_|ECHO|MD200|GLOBAL_STOP)'} |
 Sort-Object Name
$rows=New-Object System.Collections.Generic.List[object]
foreach($f in $stages){
 $zip=Join-Path $dest ($f.Name+'.zip')
 if(Test-Path -LiteralPath $zip) {
  $status='SKIPPED_EXISTS'
 }else{
  try{
   [System.IO.Compression.ZipFile]::CreateFromDirectory($f.FullName,$zip,[System.IO.Compression.CompressionLevel]::Fastest,$true)
   $status='COPIED'
  } catch {
   $status='ERROR:'+ $_.Exception.Message
   if(Test-Path $zip){Remove-Item $zip -Force}
  }
 }
 if(Test-Path $zip) {
  $it=Get-Item $zip
  $sha=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
  $rows.Add([PSCustomObject]@{Stage=$f.Name;Zip=$zip;Bytes=$it.Length;SHA256=$sha;Status=$status})
  'ARCHIVED '+$f.Name+' '+[math]::Round($it.Length/1MB,2)+' MB'
 }else{
  $rows.Add([PSCustomObject]@{Stage=$f.Name;Zip=$zip;Bytes=0;SHA256='';Status=$status})
  'FAILED '+$f.Name+' '+$status
 }
}
$rows | Export-Csv -Path (Join-Path $dest 'SHA256_Archive_Manifest.csv') -NoTypeInformation -Encoding UTF8
'STAGE_COUNT='+@($stages).Count
'ARCHIVED_COUNT='+@($rows|Where-Object{$_.Bytes -gt 0}).Count
