# E-STOP TONG — TWINCAT 3 OFFLINE STAGE, 2026-10-19

Solution: C:\Users\aml\Documents\DATN_GLOBAL_STOP_STAGE_20261019\DATN.sln
Backup stage: C:\Users\aml\Documents\DATN_MD200_ESTOP_DI_STAGE_20261019
Build offline: LAST_BUILD_INFO=0. KHONG Activate/Download/Force tren phan cung.

## Dieu khien don gian
- MAIN.bSystemEStopOK : BOOL (read-only state from GVL_MD200_IO.bEStopFeedback).
- MAIN.bMD200_Start : BOOL (operator START / STOP).
- MAIN.rMD200_SetFrequency_Hz : LREAL (0..25Hz; 15Hz ~3V / 1229 raw for AI 50Hz/10V).
- GVL_MD200_IO.bRUN_DO to ST-2318 slot10 DO0; nAO1_Raw to ST-4424 slot11 AO1.

## 3 IO links verified in built DATN.tsproj
- EStop feedback: ST-1228 slot6 Input(s) Byte0, bit0 / DI0 / pin0 to GVL_MD200_IO.bEStopFeedback.
- Conveyor RUN: ST-2318 slot10 Output(s) Byte0 bit0 to GVL_MD200_IO.bRUN_DO.
- Conveyor 0..10V reference: ST-4424 slot11 AO Ch1 to GVL_MD200_IO.nAO1_Raw.

## Global PLC interlock in MAIN
bSystemEStopOK := GVL_MD200_IO.bEStopFeedback.
NOT bSystemEStopOK clears bMD200_Start, bServo_Enable and Jog,
bStartGlueCycle, bAutoDispatchEnable/Arm, bLegacyMotionPublisherAllow,
manual M1000/D100 writes, Echo, XY debug, camera FIFO simulator permissions.
VFD RUN/AO -> 0 via its FB; Servo/Halt normal MC logic is inhibited and STOP requested.
Publisher gated against new 4-corner commands while EStop not OK.
If a publisher operation is in progress, it sends BEST-EFFORT TCP abort
M105 then M1000=OFF; guard state 911 added to prevent abort re-entry.
TCP remains connected to attempt cancellation.
Camera FIFO is reset and old jobs discarded; new operator Start/Jog required
after EStop recovery. These are software inhibition functions, NOT safety-rated.

## Critical physical EStop limitation
ST-1228 DI0 is STANDARD DIGITAL INPUT, not a certified safety input.
The user-confirmed Online Byte0=1 indicates bit0 is high but DOES NOT prove
an emergency stop operates the VFD or Mitsubishi gantry drivers.
The same EStop hardwired safety chain must independently stop BOTH MD200
and each Gantry servo / QD75-controlled actuator as required by risk analysis.
Wire safety relay/controller AUX healthy feedback into ST-1228 DI0
(healthy 1; EStop pressed, broken wire, power loss => 0).
Software STOP over MC TCP (M105) CANNOT GUARANTEE physical Gantry stop when
PLC-Q is in independent mode or Ethernet fails; never treat it as EStop.
Do not use a Force TRUE on bEStopFeedback to test actual running.

## Mandatory commissioning checks before deployment
1. Verify 2-channel hardware EStop and independent safety outputs on BOTH
   systems; press-release test under supervised safe conditions.
2. Confirm current input is really slot6 DI0; when EStop pressed 1->0;
   confirm no unexpected power loss or self-start after reset.
3. EtherCAT in OP, Crevis field24V healthy, ST-2318 / ST-4424 mapping verified.
4. Confirm MD200 F0-12 limit 25Hz, motor parameters and AI voltage DIP/curve.
5. Inspect Mitsubishi PLC-Q response to M105 for software cancellation;
   verify PLC-Q HARDWIRED safe stop regardless of TwinCAT or network.
6. Operator must deploy at cabinet with motion disabled first, verify zero
   outputs, then review monitored commissioning; do NOT activate remotely.
7. If DI0=1 but MAIN.bSystemEStopOK=FALSE, the offline mapping has not been
   loaded into active TwinCAT configuration, or process image is invalid.

## Verified status before build/deploy
Original Runtime read: MAIN.bMD200_Start=FALSE, ConveyorSpeed=0,
bServo_Enable=FALSE, bGlueCycleBusy=FALSE, bLegacyMotionPublisherAllow=FALSE;
GVL_MD200_IO.bEStopFeedback=FALSE (old runtime without new DI link).
Three intended IO links found in offline DATN.tsproj. No output writes made.
