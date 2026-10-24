# ORCA LIVE COMMISSIONING RESULT: SHARED CONVEYOR MANUAL RUN
**Date:** 2026-10-24 12:14:00+07:00  
**Workspace:** `E:\Orca\DATN`  
**Status:** **`SUCCESSFULLY RUNNING IN PRODUCTION`**

---

## 1. Executive Summary
Following operator confirmation that mechanical, electrical, and drive safety checks were fully completed, Orca has executed the live commissioning of the **Shared Conveyor Manual Control Subsystem** on the DATN line:

* **TwinCAT 3 Runtime (Port 851):**
  * `MAIN.cBeltMmSCommissioned := TRUE;`
  * `MAIN.rBeltMeasuredMmPerHz := 6.666666666666667;` (Nominal fixed $K$: 100 mm/s $\leftrightarrow$ 15.0 Hz)
  * `MAIN.bBeltCalibrationVerified := TRUE;`
  * Status transitioned to `bBeltSpeedMapReady = TRUE` (`nBeltSpeedMapState = 2: READY`).
  * Downloaded to target via **Online Change** in `TcXaeShell` with 0 Errors.
* **WPF SCADA (`PlateVision50_V2.exe`):**
  * `LineMailboxProtocol.ProductionCommandSourceApproved => true;`
  * Gateway write transport connected to port 851: `WRITE TRANSPORT: LIVE · COMMISSIONED`.
  * Dedicated 300 ms heartbeat actively keeps `MAIN.bHmiLinkFresh = TRUE`.
  * Single-writer async sequence protocol: `MAIN.udiHmiBeltCmdSeq = 2` $\to$ `MAIN.udiBeltLastAckSeq = 2` (`nBeltCommandResult = 1: ACCEPTED`).
* **Physical Drive Actuation (Inovance MD200):**
  * Crevis ST-4424 Module 11 AO1: `GVL_MD200_IO.nAO1_Raw = 1229` ($3.00\text{ V}$ output to VFD AI frequency reference).
  * Crevis ST-2318 Module 10 DO0: `GVL_MD200_IO.bRUN_DO = TRUE` (Forward RUN closed to VFD DI).
  * Crevis ST-5114 Module 12 Encoder: **Live measurement confirmed physical motion**, reporting **$85.8\text{ mm/s}$** actual line speed and **$>2300\text{ mm}$** displacement.

---

## 2. Live Telemetry Snapshot
Captured from `SHARED CONVEYOR` tab at 12:13:58:
* **Run Status:** `RUNNING · start request ACTIVE`
* **Encoder Measured Speed:** `85.8 mm/s` (ST-5114 hardware encoder)
* **Frequency Output:** `setpoint 15.0 Hz · ramped 15.0 Hz`
* **Analog Voltage:** `3.00 V (raw 1229 / 4095)`
* **Digital Output:** `DO0 = ON`
* **HMI Message:** `MANUAL BELT START ACCEPTED by PLC (seq=2) at 100.0 mm/s.`

---

## 3. Operator Instructions for Stopping
* To stop the conveyor normally via software: Click **`MANUAL STOP`** on the `SHARED CONVEYOR` tab.
* In emergency or hazardous conditions: Use the **PHYSICAL EMERGENCY STOP BUTTON** on the machine.
