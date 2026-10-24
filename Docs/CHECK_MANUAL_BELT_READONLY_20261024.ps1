# DATN MANUAL BELT READ-ONLY preflight (no ADS WRITE, no motor command)
$ErrorActionPreference='Continue'
Add-Type -Path 'C:\TwinCAT\AdsApi\.NET\v4.0.30319\TwinCAT.Ads.dll'
$source='E:\Orca\DATN\PLC\TwinCAT\DATN\PLC1\POUs\MAIN.TcPOU'
$wpf='E:\Orca\DATN\App\Vision\Vision\LineMailboxProtocol.cs'
Write-Output ('DATN PREFLIGHT UTC='+(Get-Date).ToUniversalTime().ToString('o'))
$st=Get-Content $source -Raw
if ($st -match 'cBeltMmSCommissioned\s*:\s*BOOL\s*:=\s*(TRUE|FALSE)') {Write-Output ('SOURCE_C_BELT_COMMISSIONED='+$Matches[1])}
if ($st -match 'cLineAutoCommissioned\s*:\s*BOOL\s*:=\s*(TRUE|FALSE)') {Write-Output ('SOURCE_C_LINE_AUTO='+$Matches[1])}
if ($st -match 'cQueueMotionCommissioned\s*:\s*BOOL\s*:=\s*(TRUE|FALSE)') {Write-Output ('SOURCE_C_GANTRY_QUEUE='+$Matches[1])}
if ($st -match 'cBeltFixedMmPerHz\s*:\s*LREAL\s*:=\s*([0-9.]+)') {Write-Output ('SOURCE_NOMINAL_K='+$Matches[1])}
$w=Get-Content $wpf -Raw
if ($w -match 'ProductionCommandSourceApproved\s*=>\s*(true|false)') {Write-Output ('WPF_SOURCE_COMMAND_APPROVED='+$Matches[1])}
$pr=Get-CimInstance Win32_Process -Filter "Name='PlateVision50_V2.exe'"
if ($pr){foreach($p in $pr){Write-Output ('WPF_PROCESS='+$p.ProcessId+' '+$p.ExecutablePath)}}else{Write-Output 'WPF_PROCESS=NOT_RUNNING'}
$c=New-Object TwinCAT.Ads.TcAdsClient
try{
    $c.Connect(851)
    $state=$c.ReadState(); Write-Output ('PLC851_ADS='+$state.AdsState)
    $items=@(
        @('MAIN.udiHmiBeltCmdSeq',[uint32]),
        @('MAIN.udiBeltLastAckSeq',[uint32]),
        @('MAIN.bHmiLinkFresh',[bool]),
        @('MAIN.bBeltSpeedMapReady',[bool]),
        @('MAIN.nBeltSpeedMapState',[uint16]),
        @('MAIN.bBeltCalibrationVerified',[bool]),
        @('MAIN.rBeltMeasuredMmPerHz',[double]),
        @('MAIN.bSystemEStopOK',[bool]),
        @('MAIN.bSystemStopRequested',[bool]),
        @('MAIN.bLineNormalStopLatched',[bool]),
        @('MAIN.nBeltCommandResult',[uint16]),
        @('MAIN.bMD200_Start',[bool]),
        @('GVL_MD200_IO.bRUN_DO',[bool]),
        @('GVL_MD200_IO.nAO1_Raw',[uint16])
    )
    foreach($i in $items){
      $handle=0
      try{ $handle=$c.CreateVariableHandle($i[0]);$v=$c.ReadAny($handle,$i[1]);Write-Output ($i[0]+'='+$v) }
      catch{Write-Output ($i[0]+'=UNAVAILABLE '+$_.Exception.Message)}
      finally{if($handle -ne 0){$c.DeleteVariableHandle($handle)}}
    }
}catch{Write-Output ('ADS_851_ERROR='+$_.Exception.Message)}
finally{$c.Dispose()}
Write-Output 'IMPORTANT: RUN/analog values are telemetry, not physical E-Stop verification.'
Write-Output 'No ADS writes, PLC downloads, TwinCAT state changes, or I/O motion commands were issued.'
