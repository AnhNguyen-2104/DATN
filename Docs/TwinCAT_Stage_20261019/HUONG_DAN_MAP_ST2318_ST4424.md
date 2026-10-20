# MD200 — DO RUN & AO speed mapping staged on AML-IPC, 2026-10-19

## Project and build
Project: C:\Users\aml\Documents\DATN_MD200_RUN_AO_STAGE_20261019\DATN.sln
Source preserved: C:\Users\aml\Documents\DATN_MD200_IO_TAG_STAGE_20261019
TwinCAT offline Build: LAST_BUILD_INFO=0.
GVL: PLC1\GVLs\GVL_MD200_IO.TcGVL
MAIN: PLC1\POUs\MAIN.TcPOU
FB: PLC1\POUs\FB_InovanceMD200Conveyor.TcPOU

## Verified OFFLINE links in .tsproj
1) PLC PlcTask Outputs^GVL_MD200_IO.bRUN_DO -> 
   Module 10 (ST-2318)^Output(s)^Byte 0
   AutoLink=true, Size=1 (one bit, source bit offset 0, target offset 0).
   Physical intended RUN: ST-2318 in slot 10 directly before ST-4424, DO0 terminal 0.
2) PLC PlcTask Outputs^GVL_MD200_IO.nAO1_Raw ->
   Module 11 (ST-4424)^Output(s)^Analog Output Ch#1 Byte
   AutoLink=true, TYPE UINT.
   Physical intended AO: ST-4424 slot 11, AO1 pin 1; COM analog pin 5.
Exactly two links; no other channel of those modules mapped in this project.
No Activate, Download, online FORCE or physical output change performed.

## Current live ADS read results
PLC RUN, MAIN.bMD200_RunRequest=FALSE; MAIN.rMD200_SetFrequency_Hz=15 Hz.
MAIN.nMD200_Status=2, bMD200_CommissioningEnable=FALSE,
bMD200_SafetyChainOK=FALSE, bMD200_IoHealthy=FALSE,
bMD200_DriveReady=FALSE, bMD200_MappingVerified=FALSE,
bMD200_VfdLimitVerified=FALSE.
GVL_MD200_IO.bRUN_DO=FALSE, GVL_MD200_IO.nAO1_Raw=0.
Thus no digital output LED expected and 0V analog expected even when frequency setpoint=15Hz.
User's current online PLC GVL is from a previous deployment: it is NOT proof
that the newly built DO0 bit link has been Activated into live I/O.

## What must be verified physically before any activation
- Confirm DO0 terminal 0, slot10 is actually empty/unwired or wired to MD200 DI1,
  no cross-coupled loads; NPN mode and MD200 COM reference correct.
- Field 24V / 0V supply healthy, Crevis EtherCAT OP, WcState healthy.
- Analog pin 1 and pin 5 used with load impedance meeting ST-4424 specification,
  analog MD200 AI1 DIP voltage mode, correct GND. Do not connect MD200 +10V
  to PLC AO. Use DMM for verification after qualified worker locks out VFD.
- DriveReady/Fault, IOHealthy, SafetyChainOK all must come from real signals.
  Never flip these variables TRUE just to produce voltage or illuminate a DO LED.
- Verify inverter F0-12 upper limit 25Hz; AI reference 0-10V -> 0-50Hz if
  expecting 15Hz -> ~3V (raw 1229), after slew ramp completes.
- The application STOP=FALSE RUN logic requires new RUN edge on safe recovery.
- Secure machine, confirm hardware emergency stop; solve communication warning
  'Drive 2 (Ezi-SERVO EtherCAT) (1002)' separately; do not assume it means Crevis OP.
- Only an on-site operator can authorize staged commissioning, map activation
  and controlled speed tests with drive power and risks assessed.

## Practical operator check
1. Keep RUN request FALSE and isolate motor power during I/O validation.
2. Open project, review IO tree I/O -> Device 2 -> Box1 (Crevis):
   module 10 -> Output(s) -> Byte 0 (bit 0), module11 -> Output(s) -> AO Ch#1.
3. Check PLC1 Instance -> PlcTask Outputs for exactly two blue-linked variables:
   GVL_MD200_IO.bRUN_DO, GVL_MD200_IO.nAO1_Raw.
4. Build Solution. Never assume 0 Errors automatically activates physical links.
5. Stop, field-wiring, protective conditions, field power and motor nameplate
   checked before supervised activation. No remote PLC motion start.
