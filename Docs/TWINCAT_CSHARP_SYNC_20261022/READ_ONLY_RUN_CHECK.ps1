# DATN AML-IPC: READ-ONLY readiness audit.
# This script never writes ADS symbols, never activates/downloads PLC,
# never changes network settings, and never commands motor, servo or robot.
$ErrorActionPreference = 'Continue'
$root = 'E:\Orca\DATN'
$workspace = Join-Path $root 'Docs\TWINCAT_CSHARP_SYNC_20261022'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$lines = New-Object 'System.Collections.Generic.List[string]'
function Log([string]$message) {
    $lines.Add($message)
    Write-Output $message
}
Log ('DATN READ-ONLY CHECK / HOST=' + $env:COMPUTERNAME + ' / TIME=' + (Get-Date -Format o))
Log 'SAFETY: audit only; hardware dual-channel EStop has NOT been verified by this script.'
Log '=== NIC ==='
foreach ($name in @('Ethernet 3','Ethernet 4')) {
    try {
        $n = Get-NetAdapter -Name $name -ErrorAction Stop
        Log ($n.Name + ' Status=' + $n.Status + ' Speed=' + $n.LinkSpeed + ' MAC=' + $n.MacAddress)
    } catch { Log ($name + ' UNKNOWN: ' + $_.Exception.Message) }
}
try {
    $n3 = Get-NetIPAddress -InterfaceAlias 'Ethernet 3' -AddressFamily IPv4 -ErrorAction Stop
    foreach ($ip in $n3) {Log ('PLC-Q NIC IPv4=' + $ip.IPAddress + '/' + $ip.PrefixLength)}
} catch {Log ('PLC-Q NIC IPv4 UNKNOWN: ' + $_.Exception.Message)}
try {
    $route = @(Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '192.168.3.0/24' -ErrorAction SilentlyContinue)
    Log ('Active local 192.168.3.0/24 route count=' + $route.Count)
} catch {Log ('PLC-Q route UNKNOWN: ' + $_.Exception.Message)}
Log '=== TWINCAT ADS STATES AND TAGS (READ ONLY) ==='
$assembly = 'C:\TwinCAT\3.1\Components\TcBlockDiagram\Common\TwinCAT.Ads.dll'
if (-not (Test-Path $assembly)) {Log 'TwinCAT.Ads library missing; no ADS tests run.'}
else {
    try {
        Add-Type -Path $assembly -ErrorAction Stop
        $client = New-Object TwinCAT.Ads.TcAdsClient
        try {
            foreach ($port in @(10000,851)) {
                try {
                    $client.Connect([int]$port)
                    $state = $client.ReadState()
                    Log ('Port ' + $port + ' ADS=' + $state.AdsState + ' DeviceState=' + $state.DeviceState)
                    [void]$client.Disconnect()
                } catch {Log ('Port '+$port+' READ FAIL '+$_.Exception.Message); try {[void]$client.Disconnect()} catch {}}
            }
            $client.Connect(851)
            $vars = @(
                @('MAIN.bSystemEStopOK','bool'),
                @('GVL_MD200_IO.bEStopFeedback','bool'),
                @('MAIN.bSystemStopRequested','bool'),
                @('MAIN.bMD200_Start','bool'),
                @('MAIN.bBeltSpeedMapReady','bool'),
                @('MAIN.nBeltSpeedMapState','uint16'),
                @('MAIN.bBeltCalibrationVerified','bool'),
                @('MAIN.rBeltMeasuredMmPerHz','double'),
                @('MAIN.rMD200_SetSpeed_mm_s','double'),
                @('MAIN.rMD200_SetFrequency_Hz','double'),
                @('MAIN.nMD200_SpeedInputState','uint16'),
                @('GVL_MD200_IO.bRUN_DO','bool'),
                @('GVL_MD200_IO.nAO1_Raw','uint16'),
                @('MAIN.rConveyorSpeed','double'),
                @('MAIN.rConveyorDist','double'),
                @('MAIN.nEncoderPulse','uint16'),
                @('MAIN.bPlcQConnected','bool'),
                @('MAIN.nPlcQErrorID','uint32'),
                @('MAIN.bServo_Enable','bool'),
                @('MAIN.bServo_Ready','bool'),
                @('MAIN.bHmiLinkFresh','bool'),
                @('MAIN.bHmiCamera1Ready','bool'),
                @('MAIN.bHmiTracking1Ready','bool'),
                @('MAIN.bVisionFeedVerified','bool'),
                @('MAIN.bLineAutoAccepted','bool'),
                @('MAIN.bLegacyMotionPublisherAllow','bool'),
                @('MAIN.bAutoDispatchArm','bool')
            )
            foreach ($spec in $vars) {
                $handle = 0
                try {
                    $handle = $client.CreateVariableHandle($spec[0])
                    $type = switch($spec[1]) {
                        'bool' {[bool]}
                        'double' {[double]}
                        'uint32' {[uint32]}
                        default {[uint16]}
                    }
                    $value = $client.ReadAny($handle,$type)
                    Log ('TAG '+ $spec[0] + ' = ' + $value)
                } catch {Log ('TAG '+$spec[0]+' UNKNOWN/READ ERROR '+$_.Exception.Message)}
                finally {if($handle -ne 0){try{$client.DeleteVariableHandle($handle)}catch{}}}
            }
        } finally {$client.Dispose()}
    } catch { Log ('ADS QUERY FAILED: ' + $_.Exception.Message) }
}
Log '=== OFFLINE CONFIGURATION ==='
$src = Join-Path $root 'PLC\TwinCAT\DATN\PLC1\POUs\MAIN.TcPOU'
$cs = Join-Path $root 'App\Vision\Vision\LineMailboxProtocol.cs'
try {
    $s = Get-Content $src -Raw -ErrorAction Stop
    foreach ($gate in @('cQueueMotionCommissioned','cLineAutoCommissioned','cBeltMmSCommissioned')) {
        $pattern = $gate + '\s*:\s*BOOL\s*:=\s*FALSE'
        Log ('Source '+$gate+'=FALSE : '+($s -match $pattern))
    }
    Log ('Encoder source 0.476475=' + ($s.Contains('rK_mm           := 0.476475')))
} catch {Log ('PLC SOURCE UNKNOWN '+$_.Exception.Message)}
try {
    $t = Get-Content $cs -Raw -ErrorAction Stop
    Log ('CSharp ProductionCommandSourceApproved disarmed=' + ($t -match 'ProductionCommandSourceApproved\s*=>\s*false'))
} catch {Log ('CSharp SOURCE UNKNOWN '+$_.Exception.Message)}
try {
    [xml]$project = Get-Content (Join-Path $root 'PLC\TwinCAT\DATN\DATN.tsproj') -Raw -ErrorAction Stop
    $links = @($project.SelectNodes('//Mappings//Link'))
    $io = @($links | Where-Object { $_.VarB -match '^Module ' })
    $old = @($project.SelectNodes('//Mappings//OwnerB')|Where-Object {$_.Name -match 'Device 2'})
    Log ('XML active_links=' + $links.Count + ' module_IO_links=' + $io.Count + ' old_device2_owners=' + $old.Count)
} catch {Log ('PLC PROJECT XML UNKNOWN '+$_.Exception.Message)}
Log '=== DECISION ==='
Log 'READ-ONLY / OFFLINE TESTS: eligible with normal software controls.'
Log 'PHYSICAL MOTION: NO-GO. EtherCAT per-slave OP, certified hardwired EStop, servo NC pairing, encoder calibration, PLC-Q connection and HMI command commissioning require independent verification.'
Log 'THIS SCRIPT DOES NOT AUTHORIZE START OR ALTER ANY PLC/HARDWARE STATE.'
try {
    $logPath = Join-Path $workspace ('READONLY_REPORT_' + $stamp + '.txt')
    $lines | Set-Content -LiteralPath $logPath -Encoding UTF8 -ErrorAction Stop
    Log ('REPORT SAVED: '+$logPath)
} catch {Log ('REPORT SAVE FAILED: '+$_.Exception.Message)}
