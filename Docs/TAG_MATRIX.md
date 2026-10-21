# MA TRAN TAG — DATN Line Control (TwinCAT ADS 851)
Legend: R = read-only on Dashboard; W = operator action only; RESERVED = planned (no PLC symbol).

## 1. TONG / MACHINE
| Symbol PLC | Direction | Purpose |
| MAIN.bSystemEStopOK | R | TRUE when auxiliary EStop feedback healthy |
| MAIN.bSystemStopRequested | R | Global normal PLC software stop request |
| GVL_MD200_IO.bEStopFeedback | R | ST-1228 slot 6 DI0 (input bit0) |
| MAIN.bPlcQConnected | R | PLC-Q Mitsubishi Ethernet MC socket |
| MAIN.bServo_Ready | R/Future | EziServo NC servo ready output |

## 2. BANG TAI CHUNG / ENC0
| MAIN.rConveyorSpeed | R | mm/s from ST-5114 encoder |
| MAIN.rConveyorDist | R | mm integrated distance since PLC reset |
| MAIN.bMD200_Start | R/W | MD200 run request; HMI START/normal STOP |
| MAIN.rMD200_SetFrequency_Hz | R/W | Hz setpoint limited 0..25 |
| MAIN.rMD200_Ramped_Hz | R | Ramp reference Hz |
| MAIN.rMD200_AO_VoltsVirtual | R | CALCULATED AO volts (not meter measurement) |
| GVL_MD200_IO.nAO1_Raw | R | ST-4424 slot11 AO1 0..4095 |
| GVL_MD200_IO.bRUN_DO | R | ST-2318 slot10 DO0 actual output image |

## 3. GANTRY / CAMERA 1
| MAIN.bVisionFeedVerified | R | Vision-to-PLC verification; not HMI Start |
| MAIN.nQueueCount | R | TwinCAT camera FIFO count |
| MAIN.nQueueHeadProductID | R | Head product ID |
| MAIN.nQueueAccepted | R | Accumulated queued products |
| MAIN.nQueueMissed | R | Missed product count |
| MAIN.bLegacyMotionPublisherAllow | R | Gate to legacy QD75 dispatcher, OFF by default |
| MAIN.bGlueCycleBusy | R | Robot execution state |
| MAIN.bGlueCycleDone | R | Completed 4-vertex operation |
| MAIN.bGlueCycleError | R | Gantry error status |
| MAIN.nGlueCurVertex | R | 1-4 vertex index |
| MAIN.nGlueErrorCode | R | Gantry diagnosis |
| MAIN.bStartGlueCycle | no HMI writing | QD75 motion; commissioning required |
| MAIN.bCameraTrigger / MAIN.rCameraCenterX,Y / MAIN.rCameraAngleDeg | vision bridge | Camera pose and trigger stage |
Contract on Mitsubishi: D100..D114; M1000 START held till M1001 ACK; M105 software cancel only.
Do not use M100/M101 (Mitsubishi homing); independent safety stop is hardwired.

## 4. SCARA / CAMERA 2 — RESERVED
Planned tags, NOT currently defined and NOT used for PLC writes:
GVL_SCARA.bConnected
GVL_SCARA.bReady
GVL_SCARA.bBusy
GVL_SCARA.bAlarm
GVL_SCARA.nActiveProductID
GVL_SCARA.nTraySlot
GVL_CAMERA2.bFrameValid
GVL_CAMERA2.rCenterX_mm / rCenterY_mm / rAngleDeg
GVL_CAMERA2.nQueueCount
Shared source: MAIN.rConveyorDist, MAIN.rConveyorSpeed
Future SCARA handshake must be separately audited before enabling.

## 5. ENCODER ST-5114 — OFFLINE CONFIG CHECKPOINT 2026-10-21
- Active source explicitly confirmed: `C:\Users\aml\orca\DATN\PLC\TwinCAT` (not `Desktop\DATN`). No new TwinCAT project was created.
- `DATN\PLC1\POUs\MAIN.TcPOU`: Ch0 encoder A/B x4; `GVL.aOut[0] := BYTE#16#0C;`. This sets CountMode0=0xC and keeps reset/stop/digital-output control bits low. The existing `GVL.aOut[0]` link to `Module 12 (ST-5114)^Output(s)^Byte 0` is preserved.
- `MAIN.fbConveyor.rK_mm` and the default in `FB_ConveyorEncoder.TcPOU` changed from `0.476475` to `0.11911875 mm/count` (assumes 360 PPR, x4, measurement wheel diameter 54.6 mm). No other I/O mapping or motion controls changed for this task.
- Verified via XML parsing, exact source checks, IO link check, Git diff and XAE offline Build (`LastBuildInfo=0`). XAE Error List showed 1 TMC version warning; physical device and runtime were NOT validated.
- Backup before changes (verified, includes original sources and this document): `D:\DATN_ARCHIVE_20261020\Orca_ST5114_x4_before_20261021.zip`.
- NOT activated, downloaded, forced or written to EtherCAT/PLC. Before live commissioning: confirm ST-5114 A/B wiring, 1440 counts per true encoder revolution, verify mm/count against measured belt travel, then independently approve controlled deployment. The current encoder FB uses the lower 16-bit count and signed modulo deltas per scan.

## 6. TWINCAT FIFO -> QD75 OFFLINE HANDOFF CHECKPOINT (2026-10-21)
- Source: `C:\Users\aml\orca\DATN\PLC\TwinCAT\DATN.sln`; application C# untouched.
- Modified existing `MAIN.TcPOU`: product-ID FIFO head to Publisher corner snapshot, source gate `cQueueMotionCommissioned : BOOL := FALSE` (compile-time, ADS cannot arm), legacy 4-point tracker conversion now 0.11911875 mm/count, output still controlled only by existing Publisher.
- Modified existing `FB_CameraProductQueue.TcPOU`: hold active head, remove only on exact `nCompleteProductID` after true four-vertex Publisher DONE, separate count `nRealCompleted`; BENCH SimCompleteHead remains separate; operator reset denied while Publisher owns a head.
- Offline TwinCAT XAE Build: `LastBuildInfo=0`, `BuildState=3`; XML valid; `git diff --check` clean. Actual gantry/PLC-Q application, actual camera Frame ID and QD75 running program NOT verified.
- Verified restorable backup: `D:\DATN_ARCHIVE_20261020\Orca_TwinCAT_FifoGantry_before_20261021.zip` (5 source/doc files).
- PLC future configuration/review file: `PLC\Mitsubishi\Sources_20261019\Docs\TWINCAT_MITSUBISHI_QD75_CONFIG_PENDING_20261021.md`. Mitsubishi .gxw and live PLC-Q were NOT modified.
- OPEN BLOCKERS: validated exposure/result identity and calibration, GX online Read/Verify, servo/brake/Y25 coil single-writer, glue pulse/hold timing, physical axis Z limits, live encoder distance validation, hardwired E-stop chain, safe supervised test plan.

## 7. HMI LINE MASTER — NEW OFFLINE SOURCE ONLY (2026-10-21)
New declarations in existing PLC/TwinCAT/DATN/PLC1/POUs/MAIN.TcPOU, compiled in TcXaeShell Release (LastBuildInfo=0); NOT downloaded/activated to running controller.
- Commands (planned W): MAIN.bHmiLineStartReq / bHmiLineStopReq / bHmiLineResetReq / udiHmiLineCmdSeq; heartbeat MAIN.bHmiHeartbeat; verified camera signals MAIN.bHmiCamera1Ready / bHmiTracking1Ready.
- Status (planned R): MAIN.udiLineLastAckSeq / nLineCommandResult / nLineMasterState / bLineAutoAccepted / bLineNormalStopLatched / bHmiLinkFresh.
- Belt commands (planned W): MAIN.bHmiBeltSetReq / bHmiBeltStartReq / bHmiBeltStopReq / rHmiBeltSetSpeed_mm_s; measured factor MAIN.rBeltMeasuredMmPerHz and calibration confirmation.
- Belt status (planned R): MAIN.nBeltCommandResult / rBeltCalculatedSet_Hz / rBeltAppliedSpeed_mm_s. Actual speed remains MAIN.rConveyorSpeed (mm/s).
- Compile gates cLineAutoCommissioned / cBeltMmSCommissioned / cQueueMotionCommissioned are all FALSE; runtime remains unchanged. DO NOT write new symbols via ADS until authorized deployment/commissioning.
Read Docs/TWINCAT_LINE_MASTER_HMI_CONTRACT_20261021.md for exact protocol and safety restrictions.
