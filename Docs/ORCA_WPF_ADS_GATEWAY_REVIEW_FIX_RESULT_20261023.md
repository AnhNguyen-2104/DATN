# ORCA IMPLEMENTATION RESULT: WPF ADS GATEWAY REVIEW DEFECTS RESOLUTION
**Task ID:** DATN-WPF-ADS-GATEWAY-REVIEW-FIX-20261023  
**Date:** 2026-10-23  
**Authoritative Workspace:** `E:\Orca\DATN` (Only canonical workspace; Desktop and C: drives untouched)  
**Active Branch:** `backup/datn-full-source-20261021`  
**Subsystem Responsibility:** ORCA (Part 3 = WPF C# Code Review Defect Fixes & Manual Belt Architectural Decoupling); ChatGPT controls TwinCAT PLC runtime and physical hardware.

---

## Executive Summary
Orca has completed technical remediation of all defects identified in `Docs/ORCA_WPF_ADS_GATEWAY_REVIEW_DEFECTS_20261023.md`, incorporating the user's authoritative architectural confirmation regarding the Manual Conveyor subsystem.

1. **Manual Conveyor Hardware Decoupling (User Architecture Confirmation):**
   * **Inovance MD200 VFD** receives its frequency setpoint analog reference strictly through **Crevis ST-4424 Module 11 AO1 (0–10V)** and digital RUN commands strictly via **Crevis ST-2318 Module 10 DO0** wired to the drive DI terminal.
   * **MD200 is NOT an EtherCAT slave.** The `Ezi-SERVO2` on the EtherCAT bus is a separate, unused slave for current conveyor testing.
   * In `MAIN.TcPOU`, manual conveyor mailbox commands (`bHmiBeltSetReq`, `bHmiBeltStartReq`, `bHmiBeltStopReq`) do **NOT** depend on `bServo_Ready`.
   * In WPF, `RequestManualBeltStart` strictly does **NOT** block on `ServoReady`, `Camera1`, `SCARA`, or `Gantry`.
   * The status label `SERVO NOT READY` is explicitly scoped to `AUTO/GANTRY: SERVO NOT READY (Manual belt independent)` so operators are never misled into believing manual belt commissioning is blocked by the servo drive.
2. **Gateway Transport Lifecycle Connected:** Added `EnsureTransportConnected(851)` and `DisconnectTransport()` to `LineMailboxGateway`. When telemetry observes TwinCAT port 851 in RUN, the gateway's transport connects and monitors channel quality. On disconnect or error, the transport safely disconnects and flushes pending requests.
3. **Safe Bounded Pulse Hold & Async Handshake:** Replaced instantaneous synchronous pulse drops with a non-blocking asynchronous state machine (`SemaphoreSlim` single-writer). All command pulses (`bHmiBeltSetReq`, `bHmiBeltStartReq`, `bHmiBeltStopReq`, `bHmiLineStartReq`, `bHmiLineStopReq`, `bHmiLineResetReq`) are held for a minimum duration (`PulseHoldMs = 30 ms`, $\ge 2-3$ PLC scans of the nominal 10ms task), followed by active ACK polling within a deadline (`AckTimeoutMs = 600 ms`), clean pulse release to `FALSE`, and release verification.
4. **Stale Manual Result Elimination:** Prior to asserting any manual pulse, `MAIN.nBeltCommandResult` is explicitly cleared to `0`. Fresh results are verified, preventing stale `1` values from falsely acknowledging new commands.
5. **STOP Result Correctness (Fail-Closed):** `RequestManualBeltStop` no longer returns `Success = true` unconditionally. It rigorously checks that the PLC returned `nBeltCommandResult == 1` and that pulse release succeeded. On timeout or rejection, it fails closed with explicit error messages while emphasizing that emergency stop requires the physical hardwired E-Stop button.
6. **Dedicated 300 ms Heartbeat Timer:** Separated heartbeat scheduling from the 1000 ms telemetry poll into an independent `DispatcherTimer` running at 300 ms (matching the 250..500 ms requirement and comfortably under the TwinCAT 1.5 s `tonHmiHeartbeatStale` watchdog). Alternates locally when disarmed; writes to ADS only upon future operator commissioning.
7. **Explicit UI Differentiation:** SCADA now explicitly displays both channels:
   * **Telemetry:** `ADS LIVE (port 851 RUN)` vs `ADS OFFLINE / STALE`.
   * **Write Transport:** `WRITE TRANSPORT: CONNECTED (PORT 851) · DISARMED` vs `WRITE TRANSPORT: DISCONNECTED · DISARMED`.
   * **Preset Display:** `LOCAL PRESET: 100.0 mm/s (PLC SETPOINT NOT WRITTEN · DISARMED)` vs `TEST ONLY (SIMULATED ACCEPTANCE · NO PLC WRITE)`.
8. **Commissioning Gates & Safety Invariants Preserved:**
   * `LineMailboxProtocol.ProductionCommandSourceApproved => false` remains strictly hardcoded.
   * `LineAutoContractCommissioned = false` in `Dashboard.cs` and `cBeltMmSCommissioned = FALSE, cLineAutoCommissioned = FALSE` in PLC remain strictly untouched.
   * No physical motor motion, no live ADS writes, no PLC download.
9. **Test Suite & Staging:** 10 new targeted unit tests added covering pulse hold timing, stale ACK clearing, STOP truthfulness, transport lifecycle, and manual belt independence from ServoReady. All **300/300 test checks PASS** (`TOTAL_TEST_FAILURES=0`). Release binaries staged to `ReleaseCandidate\20261023_ReviewFixGateway\`. Active user Debug executable (PID 9036) and `START_DATN.cmd` were untouched.

---

## 1. Baseline Backup & Manifest

Prior to edits, a SHA-256 baseline manifest was saved to `Docs\WPF_BACKUP_20261023_REVIEW_FIX\SHA256_MANIFEST.txt`:
* `App\Vision\Vision\LineMailboxProtocol.cs`: `26c2916a8cf701f1cda058a0110713944091cb5b6bb6e79ad0faaf2feae52aea`
* `App\Vision\Dashboard.cs`: `613119b9d73f2aa6577c3895f45f2677dee1b8cf9f2c30d60f9fa539eaa64729`
* `App\Vision\MainWindow.xaml`: `e53b33dab93d65bf0e5a431c56588503a37474401309d2edc676554aa5fc7b09`
* `App\Vision\MainWindow.xaml.cs`: `f7927065d4fec4dbf4d5c83ef0a3bb7b5a6fdeaf81c94f25fcaaae8a442bde34`
* `App\Tests\Program.cs`: `c225845b88c56d2a03bd688cbcee591db52e704df3c3c436be86631670ae7063`

---

## 2. Technical Remediation Details

### 2.1 Manual Conveyor Architecture & Decoupling
* In accordance with user confirmation, manual conveyor operation is strictly governed by Crevis modules:
  * `GVL_MD200_IO.nAO1_Raw` (ST-4424 Module 11 AO1) $\to$ VFD AI frequency reference ($0..10\text{V} \leftrightarrow 0..50\text{Hz}$).
  * `GVL_MD200_IO.bRUN_DO` (ST-2318 Module 10 DO0) $\to$ VFD DI run command.
* In WPF, `RequestManualBeltStart` requires only:
  * ADS online (telemetry fresh).
  * Belt speed map ready (`bBeltSpeedMapReady` or `TEST ONLY` mode).
  * Safe speed setpoint ($1.0 \le v \le 150.0\text{ mm/s}$).
  * E-Stop closed (`bSystemEStopOK`).
* It strictly ignores `bServo_Ready`, Camera 1 acquisition, and Gantry state.
* The status banner in `LINE OVERVIEW` now clarifies: `AUTO/GANTRY: SERVO NOT READY (Manual belt independent)`.

### 2.2 Gateway Transport Lifecycle
* Added `EnsureTransportConnected(int port = 851)` and `DisconnectTransport()` to `LineMailboxGateway`.
* Connected transport in `RefreshDashboardAsync()` whenever `LineAds.Poll` successfully completes.
* In the catch block of `RefreshDashboardAsync()`, `DisconnectTransport()` is called, flushing pending requests and ensuring clean reconnection recovery.
* Registered `DisconnectTransport()` and `Dispose()` in window `Closed` event.
* In current development pass, `IsArmed` remains `false` (`ProductionCommandSourceApproved => false`), so physical writes are safely inhibited while the transport channel is live and monitored.

### 2.3 Pulse Timing & Async Handshake
* Replaced synchronous locks with `SemaphoreSlim _asyncGate = new(1, 1)` for single-writer concurrency across async delays.
* Implemented `PulseHoldMs = 30 ms`: holds pulse `TRUE` across $\ge 2-3$ PLC scans.
* Implemented `AckTimeoutMs = 600 ms` and `PollIntervalMs = 15 ms`: actively polls `udiLineLastAckSeq` (for AUTO) or `nBeltCommandResult` (for Manual).
* Released pulse `FALSE` and verified successful release before evaluating final command result.
* Provided both asynchronous (`Request...Async`) and synchronous (`Request...`) overloads for full API compatibility.

### 2.4 Stale Result Clearing & Manual Setpoint UI
* Gateway writes `MAIN.nBeltCommandResult := 0` immediately before asserting any manual pulse.
* When `nBeltCommandResult` becomes non-zero, it is guaranteed to be the fresh result of the current pulse.
* UI setpoint status updated:
  * In production disarmed mode: `LOCAL PRESET: 100.0 mm/s · central setpoint. (PLC SETPOINT NOT WRITTEN · DISARMED)`.
  * In `TEST ONLY` mode: `LOCAL PRESET: 100.0 mm/s · TEST ONLY. (SIMULATED ACCEPTANCE · NO PLC WRITE)`.

### 2.5 STOP Result Correctness
* Rewritten to evaluate `result == 1`.
* If `result == 0`: returns `Success = false, ResultCode = 12, "MANUAL STOP TIMEOUT: PLC did not acknowledge stop pulse within deadline. In danger use PHYSICAL EMERGENCY STOP."`.
* If `result > 1`: returns `Success = false, ResultCode = result, DescribeBeltCommandResult(result)`.
* If pulse release fails: returns warning.
* Software stop priority preserved over start requests.

### 2.6 Dedicated 300 ms Heartbeat Timer
* Added dedicated `DispatcherTimer _heartbeatTimer = new() { Interval = TimeSpan.FromMilliseconds(300) }`.
* Ticks at 300 ms independently of the 1000 ms telemetry refresh.
* Disposed cleanly on window close.

### 2.7 UI Differentiation
* Telemetry is labeled `ADS LIVE (port 851 RUN)` or `ADS OFFLINE / STALE`.
* Write transport is explicitly labeled `WRITE TRANSPORT: CONNECTED (PORT 851) · DISARMED` or `WRITE TRANSPORT: DISCONNECTED · DISARMED`.
* `COMMANDS DISARMED` remains prominent on the operator overview.

---

## 3. Verification & Evidence

### 3.1 Unit Test Results (`App/Tests/Program.cs`)
10 new targeted unit tests were added:
1. `stop truthfulness: manual STOP fails closed when PLC does not return 1 (timeout)` $\to$ **PASS**
2. `stop truthfulness: stop pulse was still safely released to false on timeout` $\to$ **PASS**
3. `stale result clearing: manual SET succeeds with verified fresh pulse` $\to$ **PASS**
4. `stale result clearing: nBeltCommandResult was explicitly cleared to 0 before pulse` $\to$ **PASS**
5. `transport lifecycle: initially disconnected` $\to$ **PASS**
6. `transport lifecycle: EnsureTransportConnected connects transport` $\to$ **PASS**
7. `transport lifecycle: DisconnectTransport disconnects transport` $\to$ **PASS**
8. `pulse timing: manual SET with PulseHoldMs=25 took >= 25ms` $\to$ **PASS**
9. `manual belt architecture: manual SET succeeds when ServoReady is FALSE` $\to$ **PASS**
10. `manual belt architecture: manual START succeeds when ServoReady and Gantry are FALSE` $\to$ **PASS**

**Full Test Suite Summary:** **300 / 300 checks PASS** (`TOTAL_TEST_FAILURES=0`).

### 3.2 Compilation & Staging
* `dotnet build App/Vision/PlateVision50.csproj -c Release` $\to$ **0 Errors, 0 Warnings**.
* `dotnet build App/Tests/Tests.csproj` $\to$ **0 Errors, 2 Warnings** (pre-existing obsolete preview helper annotations).
* Staged to `ReleaseCandidate\20261023_ReviewFixGateway\PlateVision50_V2.exe` and `ReleaseCandidate\20261023_LiveAdsGateway\`.
* Active user debug process (`PID 9036`) was left running and untouched; `Mutex Local\DATN_AML_LINE_CONTROL_UI` remains protected.

---

## 4. Handover Checklist for ChatGPT (TwinCAT Side)

1. **Manual Belt Commissioning:** Verify ST-4424 AO1 (0–10V) and ST-2318 DO0 (RUN) control Inovance MD200 independently of Ezi-SERVO2.
2. **Speed Factor Calibration:** `rBeltMeasuredMmPerHz` currently reads `0.0`. Calibration requires physical measurement at $\ge 3$ frequencies before setting `bBeltCalibrationVerified := TRUE` (nominal K $\approx 6.6667\text{ mm/s/Hz}$).
3. **Heartbeat Watchdog Reception:** With WPF heartbeat running at 300 ms, verify in TwinCAT online view that `bHmiHeartbeat` toggles and `bHmiLinkFresh` evaluates to `TRUE`.
4. **Physical E-Stop Verification:** Confirm hardware safety relay cuts motor power independently of software.
