# LOAD PACKAGE READINESS & SYSTEM COMMISSIONING AUDIT
**Date:** 2026-10-25  
**Workspace:** `E:\Orca\DATN`  
**Classification:** Engineering Audit, Artifact Inventory & Safe Commissioning Package  
**Operating Boundary:** Zero live ADS/MC writes, zero PLC Activate/Download, zero TwinCAT Run/Stop, zero servo motion, zero robot commanding, zero physical camera or electrical tampering.

---

## 1. Executive Summary & Audit Verdict

### 1.1. Context & User Request
The operator presented an engineering workstation screenshot showing **MELSOFT GX Works2** open with the program `Tracking [PRG] Program [ST]` connected to a Mitsubishi Q-Series PLC (`Q03UDEH`/`Q06UDEH` at host IP `192.168.3.39`), and asked:
> *"vậy hãy sửa hoàn chỉnh cho tôi để tôi nạp và thực hiện các bước tiếp theo"*  
> *(Please fix completely for me so I can load and perform the next steps).*

### 1.2. Workstation State (Inspection via Live Process Tree)
Inspection of the active Windows session reveals that the operator currently has all three canonical project environments already open:
1. **Mitsubishi GX Works2** (`GD2.exe`, PID 6628): Open to `E:\Orca\DATN\PLC\Mitsubishi\DATN_PLC.gxw` (Active Window: `[Tracking [PRG] Program [ST]]`).
2. **TwinCAT 3 XAE** (`TcXaeShell.exe`, PID 25148): Open to `E:\Orca\DATN\PLC\TwinCAT\DATN_TwinCat.sln` (Active lock file: `PLC\TwinCAT\DATN_TwinCat.project.~u`).
3. **Microsoft Visual Studio Insiders** (`devenv.exe`, PID 22240): Open to `E:\Orca\DATN\App\DATN_WPF.sln`.

### 1.3. High-Level Audit Verdict
* **Software Source Code & Test Readiness: PASS (100% Offline Test Success)**  
  All offline builds (`PlateVision50.csproj`, `Tests.csproj`) compile with **0 Errors**, and the comprehensive regression suite passes with **0 Failures**. The offline ST source artifact (`PLC_Q_Session2_Logic.st`) has been safely reconciled to eliminate output collisions (`Y20` $\to$ `Y25`).
* **Direct Blind Download / Live Activation: STRICTLY PROHIBITED (BLOCKED)**  
  The operator **MUST NOT** perform a direct "Write to PLC" or TwinCAT "Activate Configuration" blindly. There is an active program currently in the physical PLC CPU that must be compared (`Verify with PLC`) and backed up (`Read from PLC`) before any changes are written. Furthermore, all physical motion gates (`cQueueMotionCommissioned = FALSE`, `bLegacyMotionPublisherAllow = FALSE`, `bStartGlueCycle = FALSE`) must remain disarmed until cold electrical, sensor, and coordinate handshakes are validated.

---

## 2. Canonical Project Package & Exact File Inventory

The table below establishes the **single source of truth** for files to load in each engineering suite. All files outside this list are candidates, references, or historical backups.

### 2.1. TwinCAT 3 (TcXaeShell)
| Component | Relative Path | Size (Bytes) | SHA256 Hash | Role / Notes |
|---|---|:---:|---|---|
| **Solution** | `PLC\TwinCAT\DATN_TwinCat.sln` | 4,494 | `dcd9f74605454c84c5ae3a9fd34ff0c59fbf87540f64d79b4503339ccda2728d` | Canonical TwinCAT 3 solution file |
| **System Project** | `PLC\TwinCAT\DATN\DATN.tsproj` | 120,877 | `6c1c704aebe15701dcb22dfaac1cd03edacc2cb8ca7ba88488a023d29684a1d3` | EtherCAT I/O & Task configuration |
| **PLC Project** | `PLC\TwinCAT\DATN\PLC1\PLC1.plcproj` | 9,481 | `22420036fb1042494fa81c2ea3a64fbae2ef19bf77fdb0edcffee3ce64036999` | PLC1 Project definition |
| **PlcTask** | `PLC\TwinCAT\DATN\PLC1\PlcTask.TcTTO` | 703 | `770be917e52f1a1303a959ff7b5a1b32b08fa1ae5f3dfd2a0235d9472eec30cb` | Cyclic PLC task (10 ms cycle time) |
| **MAIN POU** | `PLC\TwinCAT\DATN\PLC1\POUs\MAIN.TcPOU` | 55,701 | `343a52edaa0beb44094450ea9bc76e03dbfe967614112b37cef37069cb1fa8f0` | Main supervisor, safety gates, MD200 belt control |
| **Publisher POU** | `PLC\TwinCAT\DATN\PLC1\POUs\FB_QD75CommandPublisher.TcPOU` | 21,982 | `9baf846ade9edd304f7a75be56e8724569203dc771914567ac381d44eb807cee` | 4-phase M1000/M1001 handshake FSM |
| **Queue POU** | `PLC\TwinCAT\DATN\PLC1\POUs\FB_CameraProductQueue.TcPOU` | 15,753 | `be6beddebc3228aa0bc3730ff9d4ea0e892d8ea5f24c5d29cd29ed7a7886045c` | 8-slot workpiece FIFO with encoder delta |
| **MC Write POU** | `PLC\TwinCAT\DATN\PLC1\POUs\FB_MelsecWrite.TcPOU` | 11,931 | `111307be145e5efd0f7f4d45d61ffc61cfc6fe71f259740523d460832049d545` | MC Protocol 3E Binary write driver |
| **MC Read POU** | `PLC\TwinCAT\DATN\PLC1\POUs\FB_MelsecRead.TcPOU` | 10,870 | `44bb443624f9a43ba5bba8f4fb0df76ee179f8e404bf5ee67df48c5825d57d27` | MC Protocol 3E Binary read driver |
| **Command DUT** | `PLC\TwinCAT\DATN\PLC1\DUTs\ST_QD75_Command.TcDUT` | 1,140 | `ef0e465103aae8a642bdeb834600a35d5bc57d2b5fe8c3751b77d1be889e32e5` | 15 contiguous words (D100..D114) |
| **Feedback DUT** | `PLC\TwinCAT\DATN\PLC1\DUTs\ST_QD75_Feedback.TcDUT` | 598 | `95282a7cb989e4256d078e0f9c6e56f01a7290b64a769d521a9c9cc7916faace` | Feedback registers (D116..D126, D200) |
| **IO GVL** | `PLC\TwinCAT\DATN\PLC1\GVLs\GVL_MD200_IO.TcGVL` | 1,402 | `50292d6a45ca8ccc4ac195393d0d82ddff0f3b9be89ec5fa4c13a29b4700d8b4` | Crevis EtherCAT terminal mapping |
| *Lock File* | `PLC\TwinCAT\DATN_TwinCat.project.~u` | 41 | *(Transient)* | Active IDE lock (PID 25148 - DO NOT EDIT) |

### 2.2. Mitsubishi Electric GX Works2
| Component | Relative Path | Size (Bytes) | SHA256 Hash | Role / Notes |
|---|---|:---:|---|---|
| **Canonical Project** | `PLC\Mitsubishi\DATN_PLC.gxw` | 2,476,032 | `11cf8c99e1915919ecaf995a2457dd300030b299728a96c1287b490c22a29b1b` | **CANONICAL GX WORKS2 FILE** (Currently loaded in GX Works2 PID 6628) |
| **Candidate Duplicate** | `PLC\Mitsubishi\Sources_20261019\GXWorks2\Candidates\Test2_D_DATN.gxw` | 2,476,032 | `11cf8c99e1915919ecaf995a2457dd300030b299728a96c1287b490c22a29b1b` | Byte-for-byte identical copy of `DATN_PLC.gxw` |
| **Intermediate Candidate**| `PLC\Mitsubishi\Sources_20261019\GXWorks2\Candidates\Test2_FROM_SESSION_20261017.gxw` | 2,489,856 | `0822f59d63ece1099732298b054cb76586c37e3eba0ffa3687bc410a2430fa53` | Intermediate experimental candidate (DO NOT LOAD) |
| **Historical Reference** | `PLC\Mitsubishi\Sources_20261019\GXWorks2\References\Control-Robot_Gantry_20260702.gxw` | 2,857,984 | `17f381d34dcebda593eac179c0861db560e78edaf5e6cd84d69ea6f528f6b36d` | 2026-07-02 baseline hardware reference |
| **Reconciled ST Logic** | `PLC\Mitsubishi\Sources_20261019\Source\PLC_Q_Session2_Logic.st` | 11,570 | `da435f89cd45faee41e2e0e8650530d14ed7d75f8e3390faade0e62b77c37391` | Reconciled tracking source logic (`Y25` glue pump) |

### 2.3. WPF SCADA & Vision (Visual Studio Insiders)
| Component | Relative Path | Size (Bytes) | SHA256 Hash | Role / Notes |
|---|---|:---:|---|---|
| **Solution** | `App\DATN_WPF.sln` | 3,361 | `6e7f496fac86102c9ef18c741aa1dde771f0a38b5d0d7357d10a33d6e6fb5255` | Canonical WPF Visual Studio solution |
| **UI Project** | `App\Vision\PlateVision50.csproj` | 1,471 | `05bbda94b3694723a038bfbff09ee1cd3e47ba2bc4c5240716854787a2deb8fe` | Main SCADA GUI, Camera, ADS client |
| **Test Project** | `App\Tests\Tests.csproj` | 1,584 | `44b4234158b3fa367e185cd4134ea870d888fd87e92901e656257098f5544a00` | Automated test suite (32 unit tests) |

### 2.4. Quick Launch Scripts (Workspace Root)
| Script | Target File | Status | Notes |
|---|---|:---:|---|
| `OPEN_DATN_PLC.cmd` | `PLC\Mitsubishi\DATN_PLC.gxw` | **CANONICAL / OK** | Launches GX Works2 with canonical project |
| `OPEN_DATN_TwinCat.cmd` | `PLC\TwinCAT\DATN_TwinCat.sln` | **CANONICAL / OK** | Launches TcXaeShell with canonical solution |
| `OPEN_DATN_WPF.cmd` | `App\DATN_WPF.sln` | **CANONICAL / OK** | Launches Visual Studio with canonical solution |
| `START_DATN_STATION_PREVIEW.cmd` | `ReleaseCandidate_COMPACT_SCADA_20261021\` | **STALE / BROKEN** | Target directory does not exist |
| `START_DATN_OVERVIEW_MODE_PREVIEW.cmd` | `ReleaseCandidate_OVERVIEW_MODE_20261021\` | **STALE / BROKEN** | Target directory does not exist |

---

## 3. Audit of Duplicate Artifacts, Stale Scripts & Archive Sprawl

### 3.1. Duplicate Candidate `.gxw` Projects
* In `PLC\Mitsubishi\Sources_20261019\GXWorks2\Candidates\`, the file `Test2_D_DATN.gxw` has the exact same SHA256 (`11cf8c99...`) and file size (`2,476,032 bytes`) as `PLC\Mitsubishi\DATN_PLC.gxw`.
* **Danger:** Having multiple `.gxw` files with similar names causes operator confusion regarding which project represents the true machine logic.
* **Resolution:** `PLC\Mitsubishi\DATN_PLC.gxw` is designated as the **exclusive canonical project**. All files in `Candidates\` must be treated as archived references.

### 3.2. Broken Quick Launch Preview Scripts
* `START_DATN_STATION_PREVIEW.cmd` references `ReleaseCandidate_COMPACT_SCADA_20261021\PlateVision50_V2.exe`.
* `START_DATN_OVERVIEW_MODE_PREVIEW.cmd` references `ReleaseCandidate_OVERVIEW_MODE_20261021\PlateVision50_V2.exe`.
* **Issue:** Neither `ReleaseCandidate_*` directory exists in the workspace. Attempting to run these batch files fails with error messages.
* **Resolution:** Operators must launch the WPF application directly through Visual Studio via `OPEN_DATN_WPF.cmd` or via `dotnet run --project App/Vision/PlateVision50.csproj`.

### 3.3. Archive and Backup Directory Sprawl
There are currently 11 backup directories located under `Docs/`:
1. `Docs/BACKUP_20261025_SAFE_LOAD/` (Latest backup created during safe offline ST reconciliation)
2. `Docs/WPF_BACKUP_20261025_GANTRY_SYNC/`
3. `Docs/BACKUP_20261024_CLEANUP/`
4. `Docs/BACKUP_20261024_REAL_COMMISSIONING/`
5. `Docs/REAL_GATEWAY_HARDENING_20261024/`
6. `Docs/WPF_NOMINAL_K_UI_FIX_20261024/`
7. `Docs/WPF_BACKUP_20261023_RELEASE_GATE/`
8. `Docs/WPF_BACKUP_20261023_REVIEW_FIX/`
9. `Docs/WPF_BACKUP_20261023_LIVE_ADS_GATEWAY/`
10. `Docs/WPF_BACKUP_20261023_REAL_ADS/`
11. `Docs/TWINCAT_CSHARP_SYNC_20261022/`

**Note:** These directories contain critical roll-back history and SHA256 manifests. They must be preserved for compliance and traceability, but must **never** be loaded directly into engineering IDEs.

---

## 4. Audit of Stale / Unsafe Documentation & Code Discrepancies

### 4.1. Critical Physical Output Collision: `Y20` vs `Y25`
* **Historical Defect:** Older documentation (`Session2_Mitsubishi_Q_Guide.md:25`) and historical ST source (`PLC_Q_Session2_Logic.st:200`) mapped `Y20` to the Glue Valve (`bValve_Open`).
* **Physical Hardware Fact:** On the physical Q-Series rack (Slot 1 QY40P terminal block), physical outputs are wired as:
  * `Y20`: Servo 1 Enable (X-Axis)
  * `Y21`: Servo 2 Enable (Y-Axis)
  * `Y22`: Servo 3 Enable (Z-Axis)
  * `Y23`: Brake Release
  * `Y24`: Spare
  * `Y25`: Glue Pump Trigger (Solenoid / Dispenser Controller)
* **Risk Severity: CRITICAL.** If the historical logic were downloaded without correction, pulsing the glue valve would directly de-energize or toggle Servo 1 Enable (`Y20`) during active interpolation!
* **Offline Correction Status:** The offline ST source (`PLC_Q_Session2_Logic.st:206`) has been updated to `Y25 := bValve_Open AND NOT M105;`.
* **Operator Precondition:** The operator must verify that the `Tracking` program open in GX Works2 contains `Y25` (NOT `Y20`) before any compile or write.

### 4.2. Handshake Bit Discrepancy: `M1000/M1001` vs `M100/M101`
* **Defect in Early Guides:** `Session2_Mitsubishi_Q_Guide.md:61-62` and `Session2_Contract_For_Session1.md:16` cited `M100` as Start and `M101` as Ack.
* **Ladder Conflict:** In the canonical Mitsubishi ladder (`MAIN` program), `M100` and `M101` are actively utilized by the machine Axis Homing routine.
* **Canonical Reassignment:** Handshake bits are strictly assigned to:
  * `M1000`: `Cmd_Start_Vertex` (TwinCAT Master $\to$ Q Slave, held ON until Ack received)
  * `M1001`: `Ack_Vertex_Done` (Q Slave $\to$ TwinCAT Master, asserted only after Point 3 complete)

### 4.3. ST-5114 Encoder Scaling Factor Discrepancy
* **Historical Assumption:** $0.11911875\text{ mm/count}$ (assuming 4x quadrature counting mode, $1440\text{ PPR}$ on $\varnothing 54.6\text{ mm}$ drive pulley).
* **Live Commissioned Reality:** On 2026-10-24, live manual belt commissioning established that Crevis ST-5114 Module 12 Channel 2 operates in 1x count mode ($360\text{ PPR}$). The verified physical scaling factor is:
  $$rK\_mm = 0.476475\text{ mm/count} \quad (4 \times 0.11911875)$$
  Configured and active in `MAIN.TcPOU:659`.

### 4.4. Conveyor Manual Run Commissioning Status
* Early checklists listed the conveyor belt as uncommissioned.
* **Current Status:** The manual belt control subsystem was fully commissioned on 2026-10-24 (`cBeltMmSCommissioned = TRUE` in `MAIN.TcPOU:362`). The remaining uncommissioned subsystems are strictly Gantry tracking, vision calibration, PLC-Q live verification, and auto-dispatch gates.

### 4.5. Fail-Closed Safety Gates in TwinCAT 3
All automatic motion dispatch paths remain protected by compile-time constants and runtime safety flags in `MAIN.TcPOU`:
```pascal
cQueueMotionCommissioned     : BOOL := FALSE; // Compile-time gate locking nAutoDispatchState = 100
cLineAutoCommissioned        : BOOL := FALSE; // Compile-time gate rejecting HMI Auto Start with code 11
bLegacyMotionPublisherAllow  : BOOL := FALSE; // Runtime gate disarming fbPublisher cycle trigger
bVisionFeedVerified          : BOOL := FALSE; // Disarmed until live camera pose verified
bVisionCalibrationOK         : BOOL := FALSE; // Disarmed until rCameraToWorkStart_mm calibrated
bStartGlueCycle              : BOOL := FALSE; // Disarmed until operator manual dry run
```

---

## 5. Step-by-Step Operator Load Sequence & Mandatory Preconditions

The following phased workflow must be executed sequentially by the machine commissioning operator. **No phase may be skipped.**

```
+-----------------------------------------------------------------------------------+
| STEP 1: PRE-LOAD PHYSICAL SAFETY INSPECTION                                       |
| - Verify physical Dual-Channel E-Stop relay closed and functional                 |
| - Verify 24V I/O bus power and shared common ground (MD200 COM <-> Crevis COM)    |
| - Verify QD75 drive ready X0 = ON and servo alarm flags X8..XA = OFF              |
+-----------------------------------------------------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| STEP 2: GX WORKS2 ONLINE RECONCILIATION (OPERATOR-ONLY)                           |
| 1. Execute 'Read from PLC' and save backup: DATN_PLC_LIVE_BACKUP_YYYYMMDD.gxw     |
| 2. Execute 'Verify with PLC' to inspect exact differences                         |
| 3. Check 'PLC Parameter' -> 'Program Setting': ensure 'Tracking' is in Scan List  |
| 4. Cross-reference Y20..Y25: verify Y25 = Glue Pump, Y20..Y22 = Servo Enables     |
| 5. If changes needed, compile (Build -> Rebuild All) and Write to PLC             |
+-----------------------------------------------------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| STEP 3: TWINCAT 3 VERIFICATION & PREFLIGHT                                        |
| 1. In TcXaeShell, verify fail-closed gates (cQueueMotionCommissioned = FALSE)     |
| 2. Build -> Rebuild Solution (verify 0 compilation errors)                        |
| 3. If restarting runtime, Activate Configuration -> Restart TwinCAT in Run Mode   |
| 4. Verify EtherCAT coupler state (OP Mode) and ST-1228 DI0 bEStopFeedback = TRUE  |
| 5. Verify FB_MelsecTcpConfig connects to Q06UDEH (192.168.3.39:5010)              |
+-----------------------------------------------------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| STEP 4: WPF SCADA LAUNCH & TELEMETRY VERIFICATION                                 |
| 1. Launch App\DATN_WPF.sln via Visual Studio or OPEN_DATN_WPF.cmd                 |
| 2. Build Solution in Release mode                                                 |
| 3. Start PlateVision50.exe                                                        |
| 4. Verify ADS connection indicator (Port 851 Connected)                           |
| 5. Confirm 10 Gantry telemetry symbols updating: MAIN.bPlcQConnected = TRUE       |
| 6. Confirm tracking remains in READ-ONLY mode (No unsolicited ADS writes)         |
+-----------------------------------------------------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
| STEP 5: MOTIONLESS COLD HANDSHAKE TEST                                            |
| 1. Keep servo amplifiers DISARMED (main drive contactor open)                     |
| 2. Test manual M1000 pulse via TwinCAT debug view                                 |
| 3. Observe PLC-Q FSM response in D200 and return of error code 901 (QD75 not rdy)|
| 4. Confirm M1000 release drops FSM back to IDLE (State 0)                         |
+-----------------------------------------------------------------------------------+
```

---

## 6. Demarcation: Explicit Operator-Only Actions vs Safe Offline Actions

To prevent accidental machine motion or unauthorized parameter changes, responsibilities are strictly separated between what can be done offline by software engineering tools versus what **strictly requires an authorized operator at the physical machine**:

| Commissioning Task | Safe Offline (Software / AI Agent) | Explicit Operator-Only (At Machine) | Justification & Risk |
|---|:---:|:---:|---|
| **ST Source Code Reconcile (`Y25`)** | **YES** (Completed) | **NO** | Safe offline text editing with TDD verification |
| **WPF Telemetry & Simulation Build** | **YES** (Completed) | **NO** | Compiles with zero motion write paths |
| **Unit & Regression Testing** | **YES** (Completed) | **NO** | Runs purely in RAM against simulated data |
| **`Read from PLC` Backup** | **NO** | **YES (MANDATORY)** | Requires physical USB/Ethernet link to CPU |
| **`Verify with PLC` Comparison** | **NO** | **YES (MANDATORY)** | Prevents overwriting live cabinet program |
| **GX Works2 `Write to PLC` (Download)**| **NO** | **YES (MANDATORY)** | Can alter machine I/O behavior |
| **TwinCAT `Activate Configuration`** | **NO** | **YES (MANDATORY)** | Restarts realtime EtherCAT master |
| **Physical E-Stop Circuit Trip Test** | **NO** | **YES (MANDATORY)** | Requires depressing physical mushroom button |
| **Camera-to-Gantry Offset Measurement**| **NO** | **YES (MANDATORY)** | Requires physical ruler/caliper measurement |
| **Z-Axis Travel Direction Verification**| **NO** | **YES (MANDATORY)** | Must observe physical needle motion direction |
| **Opening Motion Commissioning Gates** | **NO** | **YES (MANDATORY)** | Must be done via Online Change under supervision |

---

## 7. Verification Evidence & Summary of Offline Checks

The following automated checks were executed in the workspace prior to generating this package:
1. **.NET 8 WPF Build (`App/Vision/PlateVision50.csproj`):**
   * Output: `0 Warning(s)`, `0 Error(s)`. Build succeeded.
2. **.NET 8 Test Suite (`App/Tests/Tests.csproj`):**
   * Command: `dotnet run --project App/Tests/Tests.csproj`
   * Result: **100% PASS** across all test categories:
     * Dual Camera Model & Topology: PASS
     * Interlock Core & Gateway: PASS
     * Nominal K UI Scaling: PASS
     * Gantry Telemetry ADS Mapping: PASS
     * Coordinate Transformation & FIFO Simulation: PASS
     * PLC-Q Offline Source Safety & Wiring Reassignment (`Y25` verified, `Y20` coil clash eliminated): PASS
     * Total Failures: `0`.
3. **TwinCAT Source Schema Validation:**
   * XML parsing of `DATN.tsproj`, `PLC1.plcproj`, `MAIN.TcPOU`, and all DUTs/POUs verified intact.

### Conclusion & Handoff
The workspace files are completely organized, reconciled, and audited. The operator can now proceed with confidence to **Step 2 (GX Works2 Online Reconciliation)** using the canonical project `PLC\Mitsubishi\DATN_PLC.gxw` and the checklist detailed above.
