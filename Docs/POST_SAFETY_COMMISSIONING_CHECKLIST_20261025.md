# POST-SAFETY COMMISSIONING CHECKLIST & ARCHITECTURE AUDIT
**Date:** 2026-10-25  
**Workspace:** `E:\Orca\DATN`  
**Classification:** Technical Inspection, Contract Audit & Commissioning Checklist  
**Operating Boundary:** Zero live ADS/MC writes, zero PLC Activate/Download, zero TwinCAT Run/Stop, zero servo motion, zero robot start, zero E-stop manipulation, zero physical camera changes.

---

## 1. Executive Summary & Tailored Context

### 1.1. Context: Electrical & Mechanical Safety Phase Confirmed Complete
The operator has formally confirmed that the **Electrical & Mechanical Safety Phase (Phase 0)** is complete on the physical machine:
1. **Independent Dual-Channel Hardware E-Stop:** A physical safety relay / contactor circuit disconnects power to the QD75 servo amplifiers and the Inovance MD200 inverter independently of software or Ethernet commands.
2. **Auxiliary E-Stop Monitoring:** ST-1228 Module 6 DI0 (`GVL_MD200_IO.bEStopFeedback`) is wired and verified as an auxiliary status input to TwinCAT (`MAIN.bSystemEStopOK = TRUE`).
3. **Module Power & Common Grounds:** Crevis ST-4424 (AO1) and ST-2318 (DO0) are powered and share a common ground (COM) with the MD200 inverter.
4. **Physical Output Wiring (Slot 1 QY40P):** Physical field terminal assignments on the Mitsubishi Q-Series rack have been confirmed as:
   * **`Y20`**: Servo 1 Enable (X-Axis)
   * **`Y21`**: Servo 2 Enable (Y-Axis)
   * **`Y22`**: Servo 3 Enable (Z-Axis)
   * **`Y23`**: Brake Release
   * **`Y24`**: Spare
   * **`Y25`**: Glue Pump Trigger (Solenoid / Dispenser Controller)
5. **Shared Conveyor Subsystem Status:** The manual belt control subsystem was already fully commissioned on 2026-10-24 (`Docs/ORCA_COMMISSIONING_MANUAL_BELT_RUN_RESULT_20261024.md`). `MAIN.cBeltMmSCommissioned := TRUE;` is active in the PLC runtime, Crevis ST-5114 Module 12 Channel 2 reads actual belt speed ($85.8\text{ mm/s}$ at $15\text{ Hz}$), and the WPF manual belt interface is operational.

### 1.2. Purpose of this Document
With the electrical and mechanical safety baseline secured, this document audits the exact multi-tier software contract across **TwinCAT 3**, **WPF SCADA**, and the **Mitsubishi Q-Series PLC**, resolves existing documentation discrepancies, clearly demarcates what can be done **offline in source/docs** versus what **strictly requires an authorized operator at the physical machine**, and establishes a rigorous, phased commissioning checklist for the remaining test phases.

---

## 2. Multi-Tier Architecture & Contract Audit

The system comprises three collaborating subsystems:

```
+-----------------------------------------------------------------------------------+
|                                  WPF SCADA (C#)                                   |
| - Computer Vision: 50x50 mm plate detection, angle θ, 40x40 mm corner projection |
| - Midline Tracker: Crosses 50% FOV -> trigger event generation                    |
| - ADS Telemetry Client: Port 851 Read-Only polling (fail-closed, 0 motion writes) |
| - Mailbox Gateway: 300 ms heartbeat keeps MAIN.bHmiLinkFresh = TRUE               |
+-----------------------------------------------------------------------------------+
                                    | ADS Port 851 (TCP)
                                    v
+-----------------------------------------------------------------------------------+
|                                TwinCAT 3 PLC1 (TC3)                               |
| - Shared Conveyor: ST-5114 Ch2 encoder reading (rK_mm = 0.476475, rConveyorDist)  |
| - Product FIFO: FB_CameraProductQueue (8 slots, encoder delta tracking)           |
| - Interception Math: Target_X = X_corner + DeltaX_enc + V_conv * t_fly            |
| - Publisher Sequencer: FB_QD75CommandPublisher (D100..D114 + M1000/M1001 FSM)     |
| - Commissioning Gates: cQueueMotionCommissioned, bLegacyMotionPublisherAllow      |
+-----------------------------------------------------------------------------------+
                                    | MC Protocol QnA 3E Binary TCP (Port 5010)
                                    v
+-----------------------------------------------------------------------------------+
|                        Mitsubishi Q-Series (Q06UDEH + QD75D4)                     |
| - TCP Listener: 192.168.3.39:5010 (Host Station Port)                             |
| - Command Registers: D100..D114 (15 contiguous words, little-endian DINT)          |
| - Handshake: M1000 (Start, held by TC), M1001 (Ack, set by Q after Point 3)       |
| - Status Relays: M102 (Busy), M103 (In-Position), M104 (Alarm), M105 (Abort)      |
| - Motion Execution: QD75 Point 1 (Approach) -> Point 2 (Sync Glide) -> Point 3 (Z) |
| - Dispensing Actuation: M-Code X4 triggers Y25 glue pump pulse (30 ms)            |
+-----------------------------------------------------------------------------------+
```

### 2.1. WPF SCADA Contract
1. **Vision Tracking:**
   * Detects 50x50 mm square plates, determines center $(X_{px}, Y_{px})$ and orientation $\theta$.
   * Projects 4 dispensing corners (pitch 40x40 mm, offset $\pm 20\text{ mm}$ from center).
   * Generates a `TriggerEvent` when the workpiece centroid crosses the 50% FOV midline.
2. **ADS Communication & Posture:**
   * Connects to TwinCAT ADS Port 851 (`App/Vision/Dashboard.cs:85-87`).
   * Polls 10 Gantry telemetry symbols (`MAIN.bPlcQConnected`, `MAIN.bPlcQError`, `MAIN.nPlcQErrorID`, `MAIN.bGlueCycleBusy`, `MAIN.bGlueCycleDone`, `MAIN.bGlueCycleError`, `MAIN.nGlueCurVertex`, `MAIN.nGlueErrorCode`, `MAIN.nGlueCommErrorID`, `MAIN.bStopCommandConfirmed`).
   * **Write Isolation:** Production tracking operates strictly read-only (`MainWindow.xaml.cs:1007: AppendPipelineCsv(row, "MIDLINE_TRIGGER", "NO ADS WRITE")`). No ADS writes are made to motion registers.
3. **Explicit Display Boundaries (Zero Guesswork):**
   * Robot Tool Center Point (TCP): QD75 buffer memory `Md.20` is **not** transmitted over MC Protocol to TwinCAT. WPF UI fields `S1X`, `S1Y`, `S1Z`, `S1Rz` and `Ov1X`, `Ov1Y`, `Ov1Z`, `Ov1Rz` display `--` (`UNAVAILABLE`) with tooltip `TCP: NOT AVAILABLE IN PLC IMAGE`.
   * Homing status: Displays `UNMAPPED` (homing is handled by internal PLC-Q pulses `M100/M101`).
   * Drive Ready: `S1Ready` displays `UNMAPPED (QD75)`; `S1Servo` displays `ROTARY OK` (reflects Ezi-SERVO2, not QD75).

### 2.2. TwinCAT 3 PLC Contract
1. **Camera FIFO Queue (`FB_CameraProductQueue.TcPOU`):**
   * Capacity: 8 product identities.
   * Workpiece position tracking: $\Delta X = rConveyorDist - rEncoderAtExposure\_mm$.
   * Exposure latch: When `bCameraExposeTrigger` fires, `rEncoderAtExposure_mm := rConveyorDist;` and `bExposureEncoderLatched := TRUE;`.
   * Watchdog: `tonExposureTTL` (5-second timeout) invalidates the latch if downstream vision result is stale (`MAIN.TcPOU:1025-1027`).
2. **Interception Calculation (`FB_QD75CommandPublisher.TcPOU:225-256`):**
   * Dynamic drift compensation: $Target\_X = aCycleCorners[i].X + \Delta X_{enc} + V_{conv} \cdot t_{fly}$.
   * Validates mechanical boundaries before issuing MC Protocol write:
     * $X \in [0.0, 180.0\text{ mm}]$ AND $(Target\_X + DeltaX\_Sync) \le 180.0\text{ mm}$.
     * $Y \in [0.0, 170.0\text{ mm}]$.
     * $Z \in [0.0, 52.0\text{ mm}]$ AND $Target\_Z\_Safe \le Target\_Z\_Down \le 52.0\text{ mm}$.
3. **Publisher State Machine (`FB_QD75CommandPublisher.TcPOU`):**
   * Preflight (States 10–15): Verifies `M102=0`, `M103=0`, `M104=0`, `M1001=0`.
   * Write Block (States 20–21): Packs and writes 15 contiguous words to `D100..D114`.
   * Assert Start (States 211–22): Writes `M1000 = TRUE` and holds it high.
   * Await ACK (States 30–33): Starts 10-second watchdog `tonAck`, verifies `M104 = FALSE`, polls `M1001`.
   * Drop Start (State 32): Upon detecting `M1001 == TRUE`, writes `M1000 = FALSE`.
   * Await Release (States 40–45): Starts 5-second watchdog `tonRelease`, polls `M1001` until `FALSE`.
   * Vertex Advancement: Advances vertex index $1 \to 2 \to 3 \to 4$. Upon completing vertex 4, asserts `bDone := TRUE`.
   * Abort Handler (States 90–92): Writes `M105 = TRUE`, drops `M1000 = FALSE`, sets `bStopCommandConfirmed := TRUE`.
4. **Safety & Compilation Gates (`MAIN.TcPOU`):**
   * `cQueueMotionCommissioned : BOOL := FALSE;` (Constant; locks `nAutoDispatchState := 100`).
   * `cLineAutoCommissioned : BOOL := FALSE;` (Constant; blocks Line Master AUTO command).
   * `bLegacyMotionPublisherAllow : BOOL := FALSE;` (Variable; hard interlock on `fbPublisher.bStartCycle`).
   * `bVisionFeedVerified : BOOL := FALSE;` (Variable; gates camera latching and queue).
   * `bVisionCalibrationOK : BOOL := FALSE;` (Variable; gates auto dispatch).
   * `rCameraToWorkStart_mm : LREAL := 0.0;` (0.0 blocks queue tracking).

### 2.3. Mitsubishi Q-Series PLC Contract
1. **Rack Configuration:**
   * Base: Q35B / Q38B
   * CPU: Q06UDEH (Built-in Ethernet, IP `192.168.3.39`, TCP Port `5010`).
   * Slot 0: QD75D4 4-axis positioning module (`X00..X1F`, `Y00..Y1F`, buffer `U0\G...`).
   * Slot 1: QY40P 16-point transistor sink output module (`Y20..Y2F`).
2. **System Signal Inviolability:**
   * `Y0` is **QD75 PLC READY**. It must be kept ON continuously via `SM400` (`SM400 -> Y0`). It must **never** be used as a valve or interrupted on stop/fault.
   * `Y10` is **Positioning Start**. Tracking logic pulses `M110`, which triggers the single authorized `Y10` coil in the `MAIN` ladder.
3. **Motion Trajectory (QD75 Positioning Buffer):**
   * Point 1 (Approach, Offset `2000`): Method `16#1503` (ABS3 Linear Continuous). Target $(Target\_X, Target\_Y, Z\_Down)$ at `Speed_Approach` ($500\text{ mm/s}$).
   * Point 2 (Sync Glide, Offset `2010`): Method `16#1503` (ABS3 Linear Continuous). Target $(Target\_X + DeltaX\_Sync, Target\_Y, Z\_Down)$ at `Speed_Sync` ($V_{conv}$). QD75 asserts M-code `X4`. PLC-Q intercepts `X4`, fires `Y25` (glue valve) for 30 ms (`TC0`), and clears M-code (`Cd.7 = 1`).
   * Point 3 (Z Retract, Offset `2020`): Method `16#1500` (ABS3 Linear Complete). Target $(Target\_X + DeltaX\_Sync, Target\_Y, Z\_Safe)$ at `Speed_Approach`.
   * On axis completion (`X14 = ON`): Slave asserts `M1001 = TRUE`, `M103 = TRUE`, drops `M102 = FALSE`.

---

## 3. Reconciliation of Stale & Contradictory Documentation

An in-depth review of existing project documentation identified four critical inconsistencies that must be formally reconciled:

| Inconsistency | Stale Documentation | Authoritative Source / Reality | Technical Resolution & Impact |
|---|---|---|---|
| **1. Handshake Bits (`M100/M101` vs `M1000/M1001`)** | `Session2_Mitsubishi_Q_Guide.md:61-62` and `Session2_Contract_For_Session1.md:16` listed `M100` as Start and `M101` as ACK. | `TWINCAT_MITSUBISHI_QD75_CONFIG_PENDING_20261021.md:26-27`, `ST_QD75_Command.TcDUT:14`, `FB_QD75CommandPublisher.TcPOU:159, 171`, `PLC_Q_Session2_Logic.st:63, 191`. | **`M1000` is Start, `M1001` is ACK.**<br>`M100` and `M101` are **strictly reserved** for the Homing routine in the existing Mitsubishi `MAIN` ladder (`PLS M100`, `PLS M101`). Using M100/M101 would corrupt homing and cause unpredictable axis movements. |
| **2. Glue Dispenser Physical Output (`Y20` vs `Y25`)** | `PLC_Q_Session2_Logic.st:200` has `Y20 := bValve_Open AND NOT M105;`. `Session2_Mitsubishi_Q_Guide.md:25` referenced `Y20`. | `TWINCAT_MITSUBISHI_QD75_CONFIG_PENDING_20261021.md:40-49` confirmed physical terminal wiring: `Y20..Y22` are Servo Enables, `Y23` is Brake Release, and `Y25` is Glue Pump Trigger. | **FATAL SAFETY CONFLICT RESOLVED.**<br>If historical ST is downloaded as-is, firing the glue pump will toggle `Y20`, dropping Servo 1 torque during dispensing! In Section 4.1 below, the ST code fix is documented: `Y25 := bValve_Open AND NOT M105;`. `Y20..Y23` must remain under exclusive `MAIN` ladder ownership. |
| **3. ST-5114 Encoder Scaling Factor** | Older design notes cited `0.11911875 mm/count` (based on 4x quadrature count: 1440 counts/rev, roller diameter 54.6 mm). | `MAIN.TcPOU:659` configures `rK_mm := 0.476475` on ST-5114 Channel 2 (`GVL.aIn[8..9]`). Live belt run on 2026-10-24 confirmed $85.8\text{ mm/s}$ actual speed. | **`0.476475 mm/count` is the verified active scale.**<br>$0.476475 = 4 \times 0.11911875$, corresponding to 1x counter mode on Channel 2 (360 counts/rev). All tracking formulas must use `0.476475 mm/count`. |
| **4. Conveyor Commissioning Status** | `Docs/TWINCAT_OPERATOR_COMMISSIONING_CHECKLIST_20261023.md:10` listed belt as uncommissioned (`cBeltMmSCommissioned = FALSE`). | `Docs/ORCA_COMMISSIONING_MANUAL_BELT_RUN_RESULT_20261024.md` recorded successful live manual belt commissioning on 2026-10-24. | **Belt manual control is FULLY COMMISSIONED.**<br>`MAIN.cBeltMmSCommissioned := TRUE;` is active. Remaining work applies strictly to Gantry tracking, vision offset calibration, PLC-Q reconciliation, and auto-dispatch gates. |

---

## 4. Classification: Offline vs. Machine-Authorized Operator Actions

To guarantee equipment safety and eliminate assumptions, each commissioning item is strictly categorized:

### 4.1. Item 1: PLC-Q Read/Verify & Logic Reconciliation

#### Can Be Completed Offline in Source / Docs:
* **Source Logic Audit:** Reviewed `PLC/Mitsubishi/Sources_20261019/Source/PLC_Q_Session2_Logic.st`.
* **Mechanical Documentation Correction (Crucial Fix):**
  In `PLC_Q_Session2_Logic.st`, line 200 must be modified from:
  ```pascal
  // OLD DANGEROUS CODE:
  Y20 := bValve_Open AND NOT M105;            (* Van keo vat ly (Slot 1 QY40P) *)
  ```
  to:
  ```pascal
  // CORRECTED SAFE CODE:
  Y25 := bValve_Open AND NOT M105;            (* Van keo vat ly (Slot 1 QY40P - Terminal Y25) *)
  ```
  And verify that `Y20`, `Y21`, `Y22` (Servo 1..3 Enable) and `Y23` (Brake Release) have zero writes in `PLC_Q_Session2_Logic.st`, remaining under exclusive single-coil ladder rungs in `MAIN`.
* **Candidate Project Review:** Identified `Test2_D_DATN.gxw` and `Test2_FROM_SESSION_20261017.gxw` in `PLC/Mitsubishi/Sources_20261019/GXWorks2/Candidates/` as offline reference candidates.

#### Strictly Requires Authorized Operator at Physical Machine:
* [ ] **Physical Connection:** Connect programming PC to Q06UDEH CPU via USB mini-B cable or Ethernet switch.
* [ ] **Online Read from PLC:** In GX Works2, execute `Online` $\to$ `Read from PLC` (select Program, PLC Parameter, Network Parameter, Device Initial Value). Save uploaded project as `Live_Backup_PreCommissioning_YYYYMMDD.gxw`.
* [ ] **Online Verify:** In GX Works2, execute `Online` $\to$ `Verify with PLC` against candidate `Test2_D_DATN.gxw` to identify any undocumented rung modifications.
* [ ] **Scan Program List Check:** In GX Works2 `Parameter` $\to$ `PLC Parameter` $\to$ `Program Setting`, verify that the Tracking logic POU is registered in the CPU's scan execution sequence (not orphaned in Unregistered Programs).
* [ ] **Output Coil Cross-Reference:** Run `Find/Replace` $\to$ `Cross Reference` for devices `Y20`, `Y21`, `Y22`, `Y23`, `Y25`. Verify zero duplicate coil writes (`OUT` / `SET` / `RST`) exist anywhere in the project.
* [ ] **QD75 Basic Parameters Verification:** In GX Works2 `Intelligent Function Module` $\to$ Slot 0 `QD75D4` $\to$ `Parameter`:
  * `Pr.1 Unit setting`: `0: mm`
  * `Pr.2 Pulses per rotation (Ap)`: `10000 pulse`
  * `Pr.3 Movement per rotation (Al)`: `1000.0 um`
  * `Pr.4 Unit magnification (Am)`: `10: x10 Times`
  * Confirm resolution: $1.0\ \mu\text{m/pulse} \to 0.1\ \mu\text{m/unit}$.
* [ ] **QD75 Drive Readiness Check:** With control power on, verify `X0 = ON` (QD75 Ready), `X8..XA = OFF` (no axis alarm), and `Y0` is driven ON by `SM400`.

---

### 4.2. Item 2: Coordinate D100-D114 Contract & Boundary Rules

#### Can Be Completed Offline in Source / Docs:
* **Memory Structure Verification:** Verified `ST_QD75_Command.TcDUT` matches the exact 15-word contiguous memory map required by `FB_MelsecWrite`:
  * $D100..D101$: $Target\_X$ (DINT, $0.1\ \mu\text{m}$, $1\text{ mm} = 10,000$)
  * $D102..D103$: $Target\_Y$ (DINT, $0.1\ \mu\text{m}$, $1\text{ mm} = 10,000$)
  * $D104..D105$: $Target\_Z\_Down$ (DINT, $0.1\ \mu\text{m}$, $4.0\text{ mm} = 40,000$)
  * $D106..D107$: $Speed\_Approach$ (DINT, $0.01\text{ mm/min}$, $500\text{ mm/s} = 3,000,000$)
  * $D108..D109$: $Speed\_Sync$ (DINT, $0.01\text{ mm/min}$, $V_{conv}\text{ mm/s} \times 6,000$)
  * $D110..D111$: $DeltaX\_Sync$ (DINT, $0.1\ \mu\text{m}$, $V_{conv} \cdot t_{sync} \cdot 10,000$)
  * $D112..D113$: $Target\_Z\_Safe$ (DINT, $0.1\ \mu\text{m}$, $0.0\text{ mm} = 0$)
  * $D114$: $Vertex\_Index$ (INT, $1..4$)
* **Endianness Contract:** Confirmed Mitsubishi Q-Series standard little-endian word layout (Low Word at $D_n$, High Word at $D_{n+1}$).
* **Diagnostic Feedback Registers:**
  * $D116..D117$: $Target\_X\_Sync$ ($0.1\ \mu\text{m}$)
  * $D120..D121$: Software error code (`501`: X bound, `502`: Y bound, `503`: Z bound, `901`: QD75 not ready)
  * $D122, D124, D126$: QD75 Axis 1, 2, 3 error codes (`Md.23`)
  * $D200..D201$: Sequencer state ($0..50$)
* **Unit Tests Passing:** C# test suite verifies coordinate conversion, yaw transformation, and corner projection with zero failures.

#### Strictly Requires Authorized Operator at Physical Machine:
* [ ] **Z-Axis Travel Polarity & Safe Retract Verification (HIGH HAZARD):**
  * Manually jog Axis 3 (Z) at crawl speed ($< 5\text{ mm/s}$) in GX Works2 Positioning Test.
  * **Verify direction:** Does increasing coordinate value move the dispenser needle **DOWN** toward the belt or **UP** away from the belt?
  * If positive is DOWN: $Target\_Z\_Down$ ($4.0\text{ mm}$) must be $> Target\_Z\_Safe$ ($0.0\text{ mm}$).
  * If positive is UP: $Target\_Z\_Down$ must be $< Target\_Z\_Safe$.
  * Verify software stroke limit switches ($Pr.12$ Upper Limit, $Pr.13$ Lower Limit) match physical mechanical stops.
* [ ] **Gantry Physical Mechanical Envelopes:**
  * Confirm physical stroke clearance: $X \ge 180.0\text{ mm}$, $Y \ge 170.0\text{ mm}$, $Z \ge 52.0\text{ mm}$.
  * Confirm that needle cannot collide with conveyor side rails or camera bracket anywhere in the envelope.
* [ ] **Non-Motion Data Register Echo Test:**
  * Keep servo drives disabled.
  * In TwinCAT, set `bEnableManualWrite := TRUE;` and `bTest_WriteD100 := TRUE;` with test value `nD100_ManualVal := 770000;`.
  * In GX Works2 Device Batch Monitor, read `D100..D101`. Confirm value is exactly `770000` ($77.0\text{ mm}$) with correct word arrangement.

---

### 4.3. Item 3: M1000/M1001 Handshake & Abort Protocol

#### Can Be Completed Offline in Source / Docs:
* **FSM Specification Audit:** Verified 4-phase sequence in `FB_QD75CommandPublisher.TcPOU:124-396` and `PLC_Q_Session2_Logic.st:63-173`.
* **Watchdog Timeouts:** Verified `tonAck` (10-second timeout on waiting for `M1001 = TRUE`) and `tonRelease` (5-second timeout on waiting for `M1001 = FALSE`).
* **Abort Path:** Verified that setting `bAbortCycle = TRUE` executes States 90–92: sends `M105 = TRUE` and drops `M1000 = FALSE`.

#### Strictly Requires Authorized Operator at Physical Machine:
* [ ] **Cold Handshake Test (Servos Disabled / Motionless):**
  * Disarm servo amplifiers (open hardware contactor or keep servo enables OFF).
  * In TwinCAT, toggle `bTest_WriteM1000 := TRUE;` (`bM1000_ManualVal := TRUE`).
  * In GX Works2 Device Monitor, observe `M1000` transition to 1.
  * Observe PLC-Q FSM: Since servos are not ready, verify PLC-Q transitions to State 0 $\to$ detects `NOT X0` or axis busy $\to$ asserts alarm `M104 = TRUE` and sets error `dErrCode := 901` (`D120 = 901`), without executing motion.
* [ ] **Single-Vertex Controlled Motion Test (Slow Dry Run):**
  * With servos enabled, gantry homed to $(0, 0, 0)$, and glue valve disconnected:
  * Send single test coordinate vector via GX Works2 Device Test or TwinCAT manual write:
    * $D100 = 500000$ ($50.0\text{ mm}$), $D102 = 500000$ ($50.0\text{ mm}$), $D104 = 100000$ ($10.0\text{ mm}$), $D106 = 600000$ ($100\text{ mm/s}$ approach), $D108 = 300000$ ($50\text{ mm/s}$ sync), $D110 = 20000$ ($2.0\text{ mm}$ glide), $D112 = 0$ ($0.0\text{ mm}$ safe), $D114 = 1$.
  * Assert `M1000 = TRUE` and hold it.
  * Verify Point 1: Gantry moves to $(50, 50, 10)$.
  * Verify Point 2: Gantry glides along X to $52\text{ mm}$ at $50\text{ mm/s}$. M-code $X4$ fires.
  * Verify Point 3: Z retracts to $0.0\text{ mm}$.
  * Verify `M1001` rises to 1 and `M103` rises to 1.
  * Lower `M1000 = FALSE`. Verify `M1001` drops to 0 within 50 ms and FSM returns to 0.
* [ ] **Emergency Abort (`M105`) Network Test:**
  * Start motion, and midway through Point 1 trigger `bAbortCycle := TRUE` in TwinCAT.
  * Verify `M105 = TRUE` is written to PLC-Q.
  * Verify PLC-Q immediately fires `Y4` (Axis 1 Stop), `Y5` (Axis 2 Stop), `Y6` (Axis 3 Stop), shuts off `Y25` (valve), and latches FSM in safe stop.

---

### 4.4. Item 4: Camera Offset (`rCameraToWorkStart_mm` & Extrinsics)

#### Can Be Completed Offline in Source / Docs:
* **Extrinsics Math & Yaw Transform Audit:**
  * Verified coordinate frame transformations in `App/Vision/Vision/BeltCoordinates.cs:14-38` and `PlateDetector.cs:30-109`.
  * Verified that image $Y+$ maps to robot $X+$ along the belt travel direction, and image $X$ maps to robot $Y$ across belt width.
* **Unit Tests Passing:** `App/Tests/Program.cs` verified rotation math for $0^\circ, 15^\circ, -30^\circ, 35.5^\circ, 44^\circ$ and square 40 mm pitch with zero failures.

#### Strictly Requires Authorized Operator at Physical Machine:
* [ ] **Physical Measuring Tape Calibration (`rCameraToWorkStart_mm`):**
  * Use a calibrated steel scale or laser measure to measure the exact linear distance from the Camera 50% FOV optical centerline to the Gantry physical work entrance boundary ($X = 0.0\text{ mm}$).
  * Nominal design estimate is $\approx 300.0\text{ mm}$.
  * Record exact measured value: $D_{phys} = \_\_\_\_\_\_.\_\_\text{ mm}$.
* [ ] **Pixel-to-Millimeter Optical Calibration ($K_{px\_to\_mm}$):**
  * Place a precision 50.0x50.0 mm calibration target under Camera 1 at focal height.
  * In WPF Vision Diagnostic View, measure detected plate dimension in pixels ($W_{px}$).
  * Calculate scaling: $K_{px\_to\_mm} = 50.0 / W_{px}\text{ mm/pixel}$.
  * Confirm distortion-free FOV and verify camera mounting height and focus are locked mechanically.
* [ ] **Camera Yaw Alignment Verification:**
  * Place plate with edge aligned against conveyor guide rail.
  * Verify detected angle $\theta$ in WPF reads $0.0^\circ \pm 0.5^\circ$.
  * If camera bracket has physical angular tilt, measure tilt angle and enter into `MAIN.rCamToRobotYaw_deg`.
* [ ] **Parameter Entry in TwinCAT:**
  * Enter measured distance into `MAIN.rCameraToWorkStart_mm`.
  * Set `MAIN.bVisionCalibrationOK := TRUE;`.

---

### 4.5. Item 5: Encoder / Exposure Latch Synchronization

#### Can Be Completed Offline in Source / Docs:
* **Latch Logic Audit:** Verified in `MAIN.TcPOU:1021-1028`:
  * Exposure event triggers: `rEncoderAtExposure_mm := rConveyorDist;` and `bExposureEncoderLatched := TRUE;`.
  * Stale frame rejection: `tonExposureTTL` (5-second timeout) clears `bExposureEncoderLatched` if camera processing stalls.
* **FIFO Displacement Math:** Verified in `FB_CameraProductQueue.TcPOU`:
  * Product tracked position: $X(t) = X_{at\_trigger} + (rConveyorDist(t) - rEncoderAtExposure\_mm)$.

#### Strictly Requires Authorized Operator at Physical Machine:
* [ ] **Trigger Interface Selection & Verification:**
  * **Option A (Hardware Strobe Trigger - Recommended):** Camera strobe output pin is wired to Crevis ST-5114 hardware latch input. When camera exposes, hardware freezes encoder counter instantly with zero software latency.
  * **Option B (Software ADS Trigger):** Camera frame acquisition in WPF sends ADS pulse `MAIN.bCameraTrigger` on midline crossing. (Requires measuring Ethernet + ADS jitter; typical jitter $5..15\text{ ms} \leftrightarrow 0.5..1.5\text{ mm}$ drift at $100\text{ mm/s}$).
  * Confirm which interface is installed on the electrical cabinet.
* [ ] **Encoder Direction Verification:**
  * Start conveyor running in forward direction.
  * In TwinCAT, monitor `MAIN.rConveyorDist` and `MAIN.nEncoderDelta`.
  * **Verify sign:** `nEncoderDelta` must be positive, and `rConveyorDist` must increase monotonically. If negative, invert encoder count direction in ST-5114 parameter or swap A/B channels.
* [ ] **Encoder Physical Distance Verification (Slip Calibration):**
  * Place a physical mark on the belt.
  * Jog conveyor forward by exactly $1000.0\text{ mm}$ measured with physical scale.
  * Read `MAIN.rConveyorDist` delta: Confirm reading is $1000.0 \pm 2.0\text{ mm}$ (error $< 0.2\%$).

---

### 4.6. Item 6: Commissioning Gates & Controlled Auto Unlock

#### Can Be Completed Offline in Source / Docs:
* **Safety Lock Analysis:**
  * `cQueueMotionCommissioned : BOOL := FALSE;` (`MAIN.TcPOU:361`) is a compile-time `VAR CONSTANT`.
  * `cLineAutoCommissioned : BOOL := FALSE;` (`MAIN.TcPOU:363`) is a compile-time `VAR CONSTANT`.
  * Cannot be forced or altered via ADS or HMI.
  * Require deliberate engineering source edit and **Online Change** in TwinCAT TcXaeShell.

#### Strictly Requires Authorized Operator at Physical Machine:
* [ ] **Controlled Gate Unlock Sequence (Must be executed in exact order):**
  1. Complete Gates 1 through 5 with 100% PASS.
  2. In TwinCAT TcXaeShell, perform Online Change to set runtime flags:
     * `bVisionFeedVerified := TRUE;`
     * `bVisionCalibrationOK := TRUE;`
  3. Perform single-vertex dry-run test with `bLegacyMotionPublisherAllow := TRUE;`.
  4. Perform Online Change to enable compile-time constants:
     * `cQueueMotionCommissioned := TRUE;`
     * `cLineAutoCommissioned := TRUE;`
  5. Test single-product dispatch in disarmed dry-run mode (valve unpowered).

---

## 5. Master Commissioning Checklist: Tailored to Confirmed Safety Phase

> **OPERATING DISCIPLINE:** Execute strictly in numerical order. Do not advance to the next phase if any preceding checkpoint fails. Ensure one authorized engineer stands at the cabinet with the E-Stop button held accessible at all times.

### Phase 1: Machine Power-On & Static Verification (Motionless / Disarmed)
- [ ] **1.1. Safety Chain Verification:** Confirm dual-channel E-Stop relay is closed. Verify ST-1228 DI0 reads 1 (`MAIN.bSystemEStopOK = TRUE`). Press E-Stop: verify servo contactors drop out instantly and DI0 reads 0. Reset E-Stop.
- [ ] **1.2. TwinCAT System State:** Open TcXaeShell. Verify ADS Port 10000 (System) is RUN, Port 300 (I/O) is RUN, Port 851 (PLC) is RUN.
- [ ] **1.3. EtherCAT Bus Health:** Verify Crevis Coupler (Box 1) and Modules 1..12 are in `OP` state (AL Status `0x0000`, Working Counter valid, zero CRC errors).
- [ ] **1.4. PLC-Q TCP Communication:** Verify `MAIN.bPlcQConnected = TRUE` in TwinCAT. Verify ping to `192.168.3.39` $< 1\text{ ms}$.
- [ ] **1.5. GX Works2 Online Read & Backup:** Connect USB to Q06UDEH. Read full project from PLC. Save timestamped backup `PLC_Q_Live_Backup_YYYYMMDD.gxw`.
- [ ] **1.6. Wiring & Coil Conflict Audit in PLC-Q:** Cross-reference `Y20..Y25`. Confirm `Y25` is the sole coil for glue dispenser, and `Y20..Y23` are dedicated to servo enables and brake release. Confirm corrected ST logic (`Y25 := bValve_Open AND NOT M105;`) is applied.
- [ ] **1.7. QD75 Ready State:** Verify QD75 buffer memory: `X0 = ON` (QD75 Ready), `Y0` driven ON continuously by `SM400`, all axis alarm bits `X8, X9, XA = OFF`.

### Phase 2: Axis Polarity, Stroke Envelopes & Motionless MC Test
- [ ] **2.1. Z-Axis Polarity Confirmation:** Jog Axis 3 (Z) at crawl speed ($< 5\text{ mm/s}$). Confirm whether positive direction moves needle DOWN. Confirm $Target\_Z\_Down \ge Target\_Z\_Safe$.
- [ ] **2.2. Axis Homing:** Execute homing routine on PLC-Q cabinet controls. Verify axes return cleanly to physical mechanical origins $(X=0, Y=0, Z=0)$ and QD75 buffer indicates axes homed.
- [ ] **2.3. D100..D114 Register Echo Test:** Using TwinCAT manual test mode (`bEnableManualWrite = TRUE`, `bTest_WriteD100 = TRUE`), write test values to `D100..D114`. In GX Works2 Device Monitor, verify all 15 words match exactly with correct 32-bit little-endian layout.
- [ ] **2.4. M1000 / M1001 Motionless Handshake Test:** With servo power isolated, toggle `M1000 = TRUE` in TwinCAT. Observe PLC-Q FSM transition. Re-enable servos, perform single slow dry vertex ($50\text{ mm/s}$), verify `M1001` rises upon completion of Point 3. Drop `M1000 = FALSE`, verify `M1001` drops to 0.
- [ ] **2.5. Network Abort (`M105`) Test:** Trigger `bAbortCycle := TRUE` during dry motion. Verify PLC-Q asserts `Y4..Y6` stop coils, drops valve trigger `Y25`, and latches in fault state.

### Phase 3: Vision Extrinsics, Camera Offset & Dynamic Tracking Calibration
- [ ] **3.1. Optical Field Calibration:** Place 50x50 mm physical calibration gauge under Camera 1. Verify detected width in WPF. Calibrate $K_{px\_to\_mm}$ scaling factor. Lock camera focus and aperture.
- [ ] **3.2. Physical Distance Measurement:** Measure physical distance from Camera 50% FOV trigger line to Gantry entrance boundary ($X=0.0\text{ mm}$). Enter measured value into `MAIN.rCameraToWorkStart_mm`. Set `MAIN.bVisionCalibrationOK := TRUE;`.
- [ ] **3.3. Conveyor Encoder Slip & Direction Test:** Run belt at $100\text{ mm/s}$ via WPF Manual Belt tab. Verify `MAIN.rConveyorDist` increases monotonically. Measure physical belt displacement over $1000\text{ mm}$; confirm encoder reading within $\pm 2.0\text{ mm}$.
- [ ] **3.4. Vision Crossing & FIFO Queue Test (Disarmed Dry Run):** Run belt at $100\text{ mm/s}$. Place sample 50x50 mm plate on conveyor.
  * WPF detects plate, tracks continuous ID, fires trigger at 50% FOV midline.
  * TwinCAT receives coordinate record in `fbCameraQueue` (`nQueueCount` increments to 1).
  * Monitored product coordinates update dynamically: $X_{now} = X_{trigger} + \Delta X_{enc}$.
  * Verify predicted corner coordinates remain within physical gantry envelope ($X \in [0, 180]$, $Y \in [0, 170]$).

### Phase 4: Staged Gate Unlocking & Single-Workpiece Integrated Test
- [ ] **4.1. Gate Unlocking via Online Change (TcXaeShell):**
  * Update `cQueueMotionCommissioned := TRUE;`
  * Update `cLineAutoCommissioned := TRUE;`
  * Set `bLegacyMotionPublisherAllow := TRUE;`
  * Set `bVisionFeedVerified := TRUE;`
  * Download via **Online Change** with 0 errors.
- [ ] **4.2. Single Workpiece Dry Run (Dispenser Unpowered):**
  * Keep glue valve air supply / electrical trigger disconnected.
  * Start conveyor at $100\text{ mm/s}$.
  * Feed one 50x50 mm test plate onto the conveyor.
  * Observe sequence:
    1. Plate passes Camera 1 $\to$ Trigger fires at 50% FOV.
    2. FIFO tracks plate moving down conveyor toward Gantry.
    3. Auto Dispatch emits `bAutoStartPulse` as plate reaches interception threshold.
    4. Gantry intercepts Vertex 1: Point 1 (rapid approach) $\to$ Point 2 (glide at $100\text{ mm/s}$) $\to$ Point 3 (Z safe retract) $\to$ `M1001` ACK 1.
    5. Gantry repeats for Vertices 2, 3, 4 $\to$ ACKs 2, 3, 4 confirmed.
    6. FIFO completes product $\to$ `nQueueRealCompleted` increments by 1.
- [ ] **4.3. Position Accuracy & Timing Inspection:**
  * Stop conveyor. Inspect needle tip trajectory alignment relative to the 4 corners of the moving workpiece.
  * Verify needle maintained safe height during transit and descended accurately at each dispensing vertex.

### Phase 5: Live Dispensing Trial & Fault Injection Verification
- [ ] **5.1. Live Dispensing Trial:**
  * Connect glue valve actuator / pneumatic pressure supply to terminal `Y25`.
  * Run single plate through system. Confirm 4 distinct, clean glue dots placed at the 4 corners ($40\times 40\text{ mm}$ pitch).
- [ ] **5.2. E-Stop Fault Injection Test:**
  * During an active dispensing cycle (between Vertex 2 and Vertex 3), press physical E-Stop.
  * **Pass Criteria:**
    1. QD75 servo drives immediately lose power and gantry motion freezes.
    2. MD200 belt inverter stops immediately.
    3. Glue pump output `Y25` de-energizes instantly (no glue dribble).
    4. Releasing E-Stop does **not** cause automatic motion resumption. Operator reset is mandatory.
- [ ] **5.3. Conveyor Speed Variation Test:**
  * Run test plates at $50\text{ mm/s}$, $100\text{ mm/s}$, and $120\text{ mm/s}$.
  * Confirm dynamic tracking glide speed `Speed_Sync` ($D108$) correctly adapts to conveyor speed without needle lag.

---

## 6. Offline Verification Evidence & Traceability

Prior to handing over to the machine operator, the following offline verification checks were executed in the `E:\Orca\DATN` workspace:

1. **Unit Test Suite Execution (`App/Tests/Tests.csproj`):**
   * Command: `dotnet run --project App/Tests/Tests.csproj`
   * Result: **`TOTAL_TEST_FAILURES = 0`** (Wall time: 77.37 s).
   * Verified Areas:
     * Fixed nominal K UI preview and read-only constraints.
     * Plate detector corner extraction and yaw angle transformations.
     * Conveyor coordinate mapping (image $Y+$ to robot $X+$).
     * Camera-to-FIFO contract simulation without live ADS writes.
     * Line interlock and auto fail-closed safety constraints.
     * Gantry telemetry tag read-only parsing and `--` unmapped TCP display.
2. **TwinCAT Source Contract Traceability:**
   * `PLC/TwinCAT/DATN/PLC1/DUTs/ST_QD75_Command.TcDUT`: Verified contiguous 15-word D100..D114 layout.
   * `PLC/TwinCAT/DATN/PLC1/DUTs/ST_QD75_Feedback.TcDUT`: Verified M1001, M102, M103, M104 status structure.
   * `PLC/TwinCAT/DATN/PLC1/POUs/MAIN.TcPOU`: Verified `rK_mm := 0.476475`, `tonExposureTTL := T#5S`, compile-time gates `cQueueMotionCommissioned`, `cLineAutoCommissioned`.
   * `PLC/TwinCAT/DATN/PLC1/POUs/FB_QD75CommandPublisher.TcPOU`: Verified complete 4-phase handshake and boundary check logic.
3. **Mitsubishi Source Contract Traceability:**
   * `PLC/Mitsubishi/Sources_20261019/Source/PLC_Q_Session2_Logic.st`: Identified and documented mechanical documentation fix for Line 200 (`Y25` vs `Y20`).
   * `PLC/Mitsubishi/Sources_20261019/Docs/TWINCAT_MITSUBISHI_QD75_CONFIG_PENDING_20261021.md`: Reconciled physical terminal assignments and handshake bit assignments.

---

## 7. Safety Declaration & Sign-Off Block

This checklist and architectural audit was compiled strictly using offline source-code inspection, architectural analysis, and non-intrusive unit test verification. **It does not authorize unmonitored machine operation.** Live activation of any mechanical or electrical motion must be performed exclusively by authorized technical personnel physically present at the equipment cabinet with appropriate safety equipment and emergency stop oversight.

| Role | Name | Signature | Date |
|---|---|---|---|
| **Audit & Documentation Specialist** | AI Subagent (Orca DATN) | *Verified Offline* | 2026-10-25 |
| **Lead Automation Engineer** | | | |
| **Electrical Safety Officer** | | | |
| **Machine Commissioning Operator** | | | |
