# TwinCAT live preflight — 23/10/2026 — READ ONLY
Machine: AML-IPC. User requested real machine operation. Scope: diagnostics only, NO ADS writes, no TwinCAT Activate/Download, Run/Stop changes, no motion or safety bypass.
Source of inspection: E:\Orca\DATN\PLC\TwinCAT\DATN.sln (current source, not asserted binary-identical to running PLC). Active ADS reads were executed against local TcAdsClient port 10000 and 851.

## Current observed live statuses
- SYSTEM (ADS 10000): RUN, device state 1; PLC (ADS 851): RUN, device state 0.
- C:\TwinCAT\3.1\Boot\Plc\Port_851.app timestamp 2026-10-23 11:21:12. Online symbols below exist on runtime, but source/binary provenance still needs independent comparison.
- MAIN.bSystemEStopOK=TRUE, GVL_MD200_IO.bEStopFeedback=TRUE: ordinary ST-1228 AUX digital feedback only, *NOT* a certified hardwired E-stop safety test.
- MAIN.bPlcQConnected=TRUE, MAIN.bServo_Ready=FALSE, MAIN.bGlueCycleBusy=FALSE, MAIN.bSystemStopRequested=FALSE.
- MAIN.bMD200_Start=FALSE, GVL_MD200_IO.bRUN_DO=FALSE, GVL_MD200_IO.nAO1_Raw=0, MAIN.rMD200_SetFrequency_Hz=0, MAIN.rMD200_SetSpeed_mm_s=0, MAIN.rConveyorSpeed=0, MAIN.nMD200_Status=0.
- MAIN.rBeltMeasuredMmPerHz=0, MAIN.bBeltCalibrationVerified=FALSE, MAIN.bBeltSpeedMapReady=FALSE, MAIN.nBeltSpeedMapState=0, MAIN.nMD200_SpeedInputState=10 (not commissioned).
- MAIN.bHmiLinkFresh=FALSE, MAIN.bHmiHeartbeat=FALSE, MAIN.nLineMasterState=0, MAIN.nLineCommandResult=0, MAIN.udiLineLastAckSeq=0.
- MAIN.bVisionFeedVerified=FALSE, MAIN.bVisionCalibrationOK=FALSE, MAIN.bHmiCamera1Ready=FALSE, MAIN.bHmiTracking1Ready=FALSE, MAIN.bAutoDispatchArm=FALSE, MAIN.bLegacyMotionPublisherAllow=FALSE.
- MAIN.nEncoderPulse=53978 (counter value is not evidence of actual motion); MAIN.rConveyorSpeed=0 mm/s.
- Recent Application event filtering did not yield a fresh AL fault in the last 50 min, but this does NOT establish all EtherCAT slaves are OP; earlier checkpoint documents 0x001b Crevis and 0x0014 Ezi-SERVO2.

## Software-source gates not released
MAIN.TcPOU declares cLineAutoCommissioned=FALSE, cBeltMmSCommissioned=FALSE; TwinCAT HMI contract also requires cQueueMotionCommissioned=FALSE until validated. Changing only the first two is not sufficient for automatic line control, and it defeats intended sign-off. Do not set rBeltMeasuredMmPerHz:=6.6667 or bBeltCalibrationVerified:=TRUE without measured proof. The estimate is based only on historic '15 Hz ~100 mm/s'. Servo mapping, actual encoder channel/scale, analog 0..10V calibration and VFD parameterization require inspection.

## Conditions to clear before actual motor motion
1. Qualified on-site operator confirms guarded line, no person in hazard zones, installed independent dual-channel hardware E-stop / STO or appropriately rated energy isolation for MD200 and Gantry, safety relay contactor tests and deliberate restart behavior. ST-1228 DI0 high ALONE does not demonstrate this.
2. Confirm correct EtherCAT master/network card, all relevant Crevis modules and Ezi-SERVO2 are OP without watchdog; reconcile prior 0x001b/0x0014 errors, drive fault and servo ready.
3. Under the approved controlled local commissioning procedure (not remote control), collect >=3 paired observations: VFD frequency actual vs measured belt displacement/time or verified encoder speed. Confirm encoder channel, mm-per-pulse, direction, 25Hz limit, AO scaling, drive Run wiring; derive and document measured mm/s/Hz and error bounds.
4. Validate ADS WPF tag availability / heartbeat / timeout and one-owner mailbox command sequence. For AUTO: genuine camera fresh and tracking calibrated; Gantry hardware handshake and all source gates reviewed and explicitly authorized.
5. Compare actual downloaded/runtime PLC and latest source; offline build, restorable pre-change backup, rollback, safety test plan and explicit operator sign-off. Only then consider changing commissioning gates and onsite controlled deployment. Do not deploy just to enable HMI START.

## Handoff
Current result is NOT READY FOR LIVE MOTION despite PLC RUN. No writes performed. Recheck runtime after any outside code/PLC changes; do not replay this diagnostic automatically as command changes.
