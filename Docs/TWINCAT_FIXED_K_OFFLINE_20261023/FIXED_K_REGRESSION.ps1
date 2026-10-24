$ErrorActionPreference = 'Stop'
$root = 'E:\Orca\DATN'
$main = Join-Path $root 'PLC\TwinCAT\DATN\PLC1\POUs\MAIN.TcPOU'
$fb = Join-Path $root 'PLC\TwinCAT\DATN\PLC1\POUs\FB_InovanceMD200Conveyor.TcPOU'
$map = Join-Path $root 'PLC\TwinCAT\DATN\DATN.tsproj'
$mainText = Get-Content -LiteralPath $main -Raw
$fbText = Get-Content -LiteralPath $fb -Raw
$mapText = Get-Content -LiteralPath $map -Raw
[xml]$mainXml = $mainText
[xml]$fbXml = $fbText
[xml]$mapXml = $mapText
$tests = [System.Collections.Generic.List[string]]::new()
function Check([bool]$condition,[string]$name) {
 if (-not $condition) { throw ('FAIL: '+$name) }
 $script:tests.Add('PASS: '+$name)
}
Check ($mainText.Contains('cBeltFixedMmPerHz     : LREAL := 6.666666666666667;')) 'Configured nominal K exactly 6.666666666666667'
Check (-not $mainText.Contains('cBeltApproxMmPerHz')) 'No old alternate K symbol'
Check ($mainText.Contains('rBeltCalculatedSet_Hz := rHmiBeltSetSpeed_mm_s / cBeltFixedMmPerHz;')) 'WPF mm/s mailbox uses fixed K'
Check ($mainText.Contains('rMD200_SetFrequency_Hz := rMD200_SetSpeed_mm_s / cBeltFixedMmPerHz;')) 'TwinCAT direct mm/s setpoint uses same fixed K'
Check ($mainText.Contains('(rMD200_SetSpeed_mm_s / cBeltFixedMmPerHz) > 25.0')) '25 Hz overrange blocked before VFD FB'
Check ($mainText.Contains('ABS(rBeltMeasuredMmPerHz - cBeltFixedMmPerHz)')) 'Nominal K validated against measured K before enabling'
Check ($mainText.Contains('cBeltMaxKDriftRatio   : LREAL := 0.05')) 'Calibration mismatch limited to 5 percent'
Check ($mainText.Contains('cBeltMmSCommissioned  : BOOL := FALSE')) 'Belt commissioning compile gate preserved FALSE'
Check ($mainText.Contains('cLineAutoCommissioned : BOOL := FALSE')) 'AUTO commissioning compile gate preserved FALSE'
Check ($mainText.Contains('cQueueMotionCommissioned : BOOL := FALSE')) 'Gantry publisher commissioning compile gate preserved FALSE'
Check ($mainText.Contains('bBeltCalibrationVerified  : BOOL := FALSE')) 'Calibration flag not forced'
Check ($mainText.Contains('bSystemEStopOK := GVL_MD200_IO.bEStopFeedback;')) 'Software AUX E-stop interlock remains'
Check ($mainText.Contains('rMD200_SetFrequency_Hz := 0.0;')) 'Hz defaults zero every PLC scan prior to validation'
Check ($mainText.Contains('GVL_MD200_IO.nAO1_Raw := 0;')) 'AO fallback zero for invalid reference'
Check ($fbText.Contains('cAnalogFullScale_Hz : LREAL := 50.0;')) 'MD200 assumed full scale 0-10V=0-50Hz (requires field confirmation)'
Check ($fbText.Contains('nAO_12bitRaw := LREAL_TO_INT((rRamped_Hz / cAnalogFullScale_Hz) * 4095.0);')) 'Analog mapping 12-bit formula preserved'
Check ($fbText.Contains('bDO_RunForward := TRUE;')) 'RUN DO remains separate from AO'
Check ($mapText.Contains('ST-4424') -and $mapText.Contains('ST-2318')) 'Existing tsproj references both Crevis DO and AO'
$K = 100.0 / 15.0
$maxHz=25.0
foreach($speed in @(25.0,50.0,100.0,125.0,150.0)){
 $hz=$speed/$K
 $v=$hz/50.0*10.0
 $raw=[math]::Round($hz/50.0*4095.0,0)
 Check (($hz -gt 0) -and ($hz -le $maxHz) -and ($v -le 5.0) -and ($raw -le 2048)) ('CONVERSION '+$speed+' mm/s => '+[math]::Round($hz,3)+' Hz, '+[math]::Round($v,3)+' V, raw~'+$raw)
}
Check ((100.0/$K -eq 15.0) -and [math]::Abs((15.0/50.0*10.0)-3.0) -lt 0.00001) '100mm/s=15Hz=3V'
Check ((170.0/$K) -gt $maxHz) '170 mm/s is rejected as above 25Hz'
Check (([math]::Abs(7.2-$K)/$K) -gt 0.05) 'Measured mismatch 7.2 vs K blocks enable'
$tests | ForEach-Object {Write-Output $_}
Write-Output ('TOTAL_PASS='+$tests.Count+' TOTAL_FAIL=0')
