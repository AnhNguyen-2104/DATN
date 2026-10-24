# DATN — Real ADS command path hardening — 2026-10-24
**Objective:** user requests a genuinely operable MD200 conveyor from WPF, not mock. This is *the real ADS command implementation*, but its machine authorization remains uncommissioned. No motor motion commanded, no PLC/download/ADS write performed.

**Actual device observations:** ADS 851 RUN; manual sequence and ACK symbols exist; MAIN.bBeltSpeedMapReady=FALSE; MAIN.nBeltSpeedMapState=0; MAIN.rBeltMeasuredMmPerHz=0; MAIN.bBeltCalibrationVerified=FALSE; MAIN.bHmiLinkFresh=FALSE; MAIN.bSystemEStopOK=TRUE (auxiliary only); GVL_MD200_IO.bRUN_DO=FALSE and nAO1_Raw=0. WPF source LineMailboxProtocol.ProductionCommandSourceApproved=false. PLC MAIN.TcPOU source cBeltMmSCommissioned=FALSE and nominal K=6.666666666666667. The user has previously asserted K=6.6667 and hardware checks OK; runtime does not contain verified measured K.
**Underlying cause:** real-motor commands are disarmed in WPF, PLC manual calibration commission source gate false, PLC speed map false, heartbeat absent by design while WPF disarmed. Zero RUN output cannot establish physical safety.

**Changes to actual WPF command gateway** `App/Vision/Vision/LineMailboxProtocol.cs`:
- On each manual command, avoid allocating the PLC's last ACK sequence after a WPF restart, reducing risk of stale ACK acceptance.
- MANUAL SET: validate ADS writes of sequence and speed setpoint *before* pulsing SET; invalidate prior-set state on new failed SET.
- MANUAL START: validate ADS sequence write *before* pulsing START.
- MANUAL STOP: attempt to issue stop pulse even if command metadata write fails, but never report a confirmed STOP if sequence write or START-deassert write failed; still distinguish controlled STOP from hardware E-stop.
- No change to physical command gate `ProductionCommandSourceApproved=false`, no live ADS writes, no test of real outputs, no physical safety bypass.

**Verification**:
- Source backups: `Docs/REAL_GATEWAY_HARDENING_20261024/Original/`, including SHA256 previously recorded.
- `dotnet build App/PlateVision50.sln -c Release`: **0 errors**, 12 nonblocking vendor/nullable warnings. `Docs/REAL_GATEWAY_HARDENING_20261024/BUILD.log`.
- `dotnet run --project App/Tests/Tests.csproj -c Debug --no-restore`: **310 PASS, 0 FAIL**, `TOTAL_TEST_FAILURES=0`; includes new test cases for stale PLC ACK on WPF restart and ADS speed-write failure never pulsing SET. `Docs/REAL_GATEWAY_HARDENING_20261024/TEST.log`.
- Versioned candidate `E:\Orca\DATN\ReleaseCandidate\20261024_WPF_ADS_REAL_PATH_HARDENED_DISARMED`, 64 runtime files and SHA256 manifest. Existing running Debug instance and active Release untouched.

**What production means, not yet accomplished:** qualified technician at machine must verify independent hardwired E-stop and guarded drive controls, correct Crevis output mapping, MD200 scaling, documented physically measured steady speed/Hz and encoder (three points), then produce and approve a MANUAL-belt-only PLC commissioning configuration (no AUTO/Gantry/Ezi) and deploy through TwinCAT engineering workflow. New PLC runtime must report speedMapReady based on actual verified calibration. Separately approve, version and deploy WPF live-command capability; verify 300ms heartbeat fresh, valid SET/ACK, START/STOP and physical readbacks, no auto-restart after disconnect under onsite supervision. Current code and config are explicitly NOT a certified motion-control release. No operating button or ADS Force substitutes for these field steps.
