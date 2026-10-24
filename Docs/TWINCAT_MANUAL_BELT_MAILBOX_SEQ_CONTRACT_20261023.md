# Manual belt HMI-PLC sequence/ACK contract — TwinCAT OFFLINE STAGE 2026-10-23
Owner TwinCAT (ChatGPT): PLC source E:\Orca\DATN\PLC\TwinCAT\DATN\PLC1\POUs\MAIN.TcPOU. Separate owner Orca handles WPF. This is SOURCE ONLY, NOT RUNNING PLC until approved deployment. Do not use to claim controls are armed.

## Symbol ownership / type
- MAIN.udiHmiBeltCmdSeq : UDINT / WPF WRITES ONLY. Nonzero monotonically increasing for every SET, START, STOP. Sequence 0 is invalid; avoid reuse of latest PLC ACK after restart/reconnect.
- MAIN.udiBeltLastAckSeq : UDINT / PLC WRITES, WPF READS ONLY. Assigned to received nonzero/new request seq after START/SET processed, regardless of accepted/rejected result. STOP is processed as normal stop even if seq invalid/replayed; ACK only new valid seq.
- MAIN.nBeltCommandResult : UINT / PLC WRITES, WPF READS ONLY. Result 1=accepted, 10=zero/duplicate seq, 11=not commissioned, 12=interlock/stale, 13=invalid input, 14=outside drive limit, 18=run revoked.
- MAIN.rHmiBeltSetSpeed_mm_s : LREAL / WPF WRITES; MAIN.bHmiBeltSetReq, MAIN.bHmiBeltStartReq, MAIN.bHmiBeltStopReq : BOOL / WPF WRITES pulse. Exact PLC feedback includes MAIN.rMD200_SetSpeed_mm_s and MAIN.rBeltAppliedSpeed_mm_s, bMD200_Start, GVL_MD200_IO.bRUN_DO. ALL feedback is PLC-owned read-only.
- Heartbeat MAIN.bHmiHeartbeat : BOOL HMI writes every ~300ms when separately commissioned, MAIN.bHmiLinkFresh : BOOL PLC reports watchdog.

## Command flow (preflight design, not deployment authorization)
For each command, first verify ADS RUN, correct target NetId/port851, fresh telemetry, EStop and mode ready. Reconcile last PLC ACK seq after reconnect, choose NEW nonzero seq that differs from udiBeltLastAckSeq (no replay). Serialize all writes in single gateway. Write command setpoint (if SET), write udiHmiBeltCmdSeq, create clean rising edge of correct Req bit, hold pulse through multiple PLC task cycles, release it reliably even if timeout or cancellation, and independently poll udiBeltLastAckSeq == sent seq. Read nBeltCommandResult only when matching ACK seq seen, with bounded timeout. Reject stale ACK, read failure, bad result, bad setpoint readback. STOP should not be delayed behind unrelated camera/Gantry conditions or queued START; actual physical stop confirmation still requires independent output/feedback.
Do not directly write MAIN.nBeltCommandResult for clearing stale ACK; doing so races PLC and violates result ownership. After stale link, flush queue, restart only following an explicit NEW operator action. Debug and TEST ONLY do not give production authorization.

## PLC source changes in this offline stage
- Added udiHmiBeltCmdSeq, udiBeltLastAckSeq UDINT in MAIN VAR.
- STOP priority remains unconditional bHmiBeltStopReq logic, while STOP ACK requires new valid seq.
- SET/START edge arms only after fresh nonzero sequence; rejected commands update same ACK seq, WPF sees matched rejection.
- Retained bBeltSpeedMapReady and cBeltMmSCommissioned=FALSE, calibration flags FALSE, AUTO/Gantry gates FALSE, no run command issued from this stage.
- Need offline build/tests and code review, then independent on-site integrated acceptance, operator sign-off. PLC boot/runtime NOT updated by editing source; WPF cannot assume new symbols exist in running runtime.

## Release hold conditions
At the time of stage, user has asserted physical safety checked and K=6.6667, but no logged 3-point measured dataset or field acceptance certificate was available to independently verify it. Do not force bBeltCalibrationVerified TRUE or enable ProductCommandSourceApproved. PLC owner approves staged source only, not production motion.
