# TWINCAT / LINE MASTER ADS SOURCE CONTRACT — 2026-10-21
STATUS: SOURCE COMPILED / NOT ACTIVATED / NOT COMMISSIONED.
SOLUTION: C:\Users\aml\orca\DATN\PLC\TwinCAT\DATN.sln
POU: PLC1\POUs\MAIN.TcPOU (existing project, NO NEW SOLUTION).
ADS port planned 851. NEW symbols do NOT exist on running runtime until a separately approved offline/online comparison and authorized deployment. WPF MUST fail closed if symbol unavailable.

## Inputs (HMI -> MAIN; not for live writing until separately approved)
| Source symbol | Type | Meaning |
| MAIN.bHmiLineStartReq | BOOL | Rising-edge AUTO START request |
| MAIN.bHmiLineStopReq | BOOL | NORMAL STOP request; level TRUE latches controlled stop |
| MAIN.bHmiLineResetReq | BOOL | Rising-edge clear NORMAL STOP latch when stopped; NOT robot fault reset |
| MAIN.udiHmiLineCmdSeq | UDINT | Nonzero unique command counter for each AUTO request |
| MAIN.bHmiHeartbeat | BOOL | Toggle every ~250-500ms from HMI. A fixed TRUE is INVALID; 1.5s timeout |
| MAIN.bHmiCamera1Ready | BOOL | Camera 1 acquisition is truly ready (not just UI button pressed) |
| MAIN.bHmiTracking1Ready | BOOL | Station 01 tracking is truly ready |
| MAIN.bHmiRequireScara | BOOL | Reserved, TRUE intentionally rejects START (not commissioned) |
| MAIN.bHmiBeltSetReq | BOOL | Rising-edge new speed setpoint (mm/s) |
| MAIN.bHmiBeltStartReq | BOOL | Rising-edge manual belt START; locked in AUTO |
| MAIN.bHmiBeltStopReq | BOOL | Normal manual belt STOP request |
| MAIN.rHmiBeltSetSpeed_mm_s | LREAL | Requested measured units mm/s, NOT Hz |
| MAIN.rBeltMeasuredMmPerHz | LREAL | Commissioned empirical factor mm/s per Hz; 0 until measured |
| MAIN.bBeltCalibrationVerified | BOOL | Calibration verified by engineering only; must be combined with compile-time source gate |

## Outputs (MAIN -> WPF)
| Source symbol | Type | Meaning |
| MAIN.udiLineLastAckSeq | UDINT | Most recent handled AUTO command ID; ACK means *processed*, not motion complete |
| MAIN.nLineCommandResult | UINT | Last command result: 1 processed/accepted, 10 invalid/replayed seq, 11 source NOT commissioned, 12 HMI stale, 13 vision unavailable, 14 PLC-Q / robot not ready, 15 stop/interlock, 16 manual belt/preset, 17 SCARA unavailable, 18 AUTO revoked |
| MAIN.nLineMasterState | UINT | 0 idle, 20 AUTO accepted logical state (NOT hardware-running proof), 30 normal STOP latched (NOT physical stopped proof) |
| MAIN.bLineAutoAccepted | BOOL | Logical AUTO acceptance, not drive-running confirmation |
| MAIN.bLineNormalStopLatched | BOOL | Global normal software STOP lock |
| MAIN.bHmiLinkFresh | BOOL | HMI heartbeat seen and not stale |
| MAIN.nBeltCommandResult | UINT | 1 accepted; 11 not commissioned, 12 interlock/stale/auto, 13 invalid mm/s calibration, 14 >25Hz/invalid Hz, 18 belt-run revoked |
| MAIN.rBeltCalculatedSet_Hz | LREAL | Internal calculated proposed motor Hz, not measured speed |
| MAIN.rBeltAppliedSpeed_mm_s | LREAL | Accepted setpoint, NOT actual speed |
| MAIN.rConveyorSpeed | LREAL | Encoder estimate of actual mm/s, quality/calibration must be checked |
| MAIN.rConveyorDist | LREAL | Encoder-integrated mm |

## Source-only safety and motion gates (NOT ADS-writable)
- cLineAutoCommissioned := FALSE.
- cBeltMmSCommissioned := FALSE.
- cQueueMotionCommissioned := FALSE (existing FIFO->QD75 compile gate).
- bLegacyMotionPublisherAllow, bAutoDispatchArm, bVisionFeedVerified, bVisionCalibrationOK still must be separately validated; no bypass of existing publisher or hardwired E-Stop.
- SCARA output is intentionally absent. Never map SCARA to Gantry M1000/M1001.

## WPF handshake logic after separate approval
1. Prepare and verify real camera / tracking first; never set READY merely because START pressed. HMI starts its camera subsystem, waits for verified READY, then sends AUTO command (unique seq + TRUE pulse); TwinCAT independently accepts/rejects. In no-frame/video-file mode, disable product-motion requests.
2. New HMI uses fresh seq and FALSE->TRUE->FALSE request edge; always read ACK seq and result, with timeout and UNKNOWN outcome handling. No silent retry / replay on reconnect.
3. TwinCAT owner, not C#, coordinates shared conveyor and existing PLC-Q publisher when ALL interlocks are commissioned; C# NEVER writes Mitsubishi device addresses directly.
4. STOP remains a controlled stop (not certified E-stop); STOP latch needs explicit reset. Reset does not clear actual robot/servo faults or auto restart.
5. Manual belt Set/Start only after real multi-point calibrated factor mm/s per Hz (within 0..25 Hz) and separate source commissioning; no guessed 15Hz=100mm/s assumption. One setpoint at a time, manual control locked in AUTO. Real actual speed is MAIN.rConveyorSpeed; do not confuse with setpoint or Hz.
6. For any ADS connection/quality lost, WPF disables controls and shows UNKNOWN. On heartbeat loss, new accepted AUTO stops; manual belt started through the new mailbox revokes run request. Historical direct writes to MAIN.bMD200_Start / MAIN.rMD200_SetFrequency_Hz are a separate legacy path requiring migration/commissioning audit.
7. The auxiliary ST-1228 EStop bit is NOT certified safety feedback. Hardwired rated safety chain and robot stop method require independent validation before live enable.

## Commissioning blockers
Confirm loaded runtime source vs repo; verify dual-channel hardwired E-stop for all drives, physical belt actual speed calibration vs encoder and frequency (>= several measurements), ADS symbol compatibility, operator permissions, PLC-Q online Read/Verify and QD75 handshake, camera exposure/result identity and calibration, offline fault-injection, guarded physical supervised commissioning. No live start without these gates cleared by explicit engineering review.

## 2026-10-21 SPEED MAP UPDATE — compiled in same MAIN.TcPOU (no new project)
- MAIN.rHmiBeltSetSpeed_mm_s (LREAL, proposed mm/s) is ONLY fed to conversion within TwinCAT.
- Actual conversion: MAIN.rBeltCalculatedSet_Hz := MAIN.rHmiBeltSetSpeed_mm_s / MAIN.rBeltMeasuredMmPerHz; it writes the old MAIN.rMD200_SetFrequency_Hz ONLY when bBeltSpeedMapReady is TRUE, belt is not running, AUTO not owning belt, heartbeat fresh, all checks pass, and calculated Hz in (0,25].
- MAIN.bBeltSpeedMapReady (BOOL, read-only to WPF) requires FALSE source constant cBeltMmSCommissioned to be separately reviewed/recompiled TRUE AND bBeltCalibrationVerified=TRUE AND physically measured valid rBeltMeasuredMmPerHz >0. Current source gate stays FALSE; therefore real SET denied result 11. WPF should gray out SET/START until READY and ADS is live/quality-valid.
- MAIN.nBeltSpeedMapState (UINT): 0 = not commissioned; 1 = commissioning gate enabled, calibration not verified; 2 = READY; WPF should display reason code.
- MAIN.rBeltPreviewUnverified_Hz (LREAL, read-only diagnostic) is approximate from UNVERIFIED historical report ~100 mm/s at 15 Hz (nominal 6.6666666667 mm/s/Hz). Not the actual set frequency; NEVER write it directly to the drive. For preview only, 50 mm/s -> ~7.5 Hz; 100 -> ~15 Hz; 150 -> ~22.5 Hz; above approx 166.7 mm/s returns 0 (out of historical 25Hz limit), not a verified limit on physical belt speed.
- MAIN.rBeltAppliedSpeed_mm_s = accepted mm/s setpoint, NOT measured actual. MAIN.rConveyorSpeed = encoder-derived measured mm/s, check validity separately.
- Existing WPF Dashboard.cs old LineAds.StartBelt(double hz) writes MAIN.rMD200_SetFrequency_Hz directly; preserve old page logic but do not use it for NEW mm/s controls. New mm/s SET must later pulse MAIN.bHmiBeltSetReq with rHmiBeltSetSpeed_mm_s (only when commissioned) and poll nBeltCommandResult. Manual START/STOP via new bHmiBeltStartReq/bHmiBeltStopReq, NOT old direct-Hz path in the new UI.
- Legacy WPF release currently has no new symbols on active ADS runtime; new WPF code must gracefully handle missing symbols, show estimated preview as NOT CALIBRATED (optionally compute locally for UI demo only if no matching ADS symbol), and keep controls disabled. Do not silently turn them on just because source code compiles.
- Physical commissioning needs reliable encoder scale and at least 3 nonzero speed measurements, operator acknowledgement, verify analog AO and 25Hz limit, independent hardwired safety circuit. In MAIN currently encoder source byte8/9 with rK_mm 0.476475; docs earlier claim channel0 x4 with 0.11911875, discrepancy must be resolved before trusting measured speed; do not arbitrarily change encoder here.

## DIRECT TWINCAT INPUT CHANGE (2026-10-21)
User specifically requested **editing speed in TwinCAT itself in mm/s**, while Orca independently implements WPF. MAIN now declares `rMD200_SetSpeed_mm_s : LREAL` as the primary PLC engineering input; DO NOT manually write `MAIN.rMD200_SetFrequency_Hz` (derived internal Hz only).
The WPF mailbox `MAIN.rHmiBeltSetSpeed_mm_s` when allowed updates this common mm/s setpoint; TwinCAT computes final Hz to feed FB_InovanceMD200Conveyor. `MAIN.nMD200_SpeedInputState` diagnoses accepted/blocked conversion and `MAIN.rMD200_AppliedSpeed_mm_s` is an accepted setpoint, NOT encoder measured speed. Build of new version remains unverified after TcXaeShell Rebuild reported "Unspecified error"; not online deployed; all calibration gates are FALSE.
