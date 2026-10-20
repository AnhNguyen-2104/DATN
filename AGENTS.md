# DATN AML-IPC — Agent operating rules

Scope: C:\Users\aml\Desktop\DATN is the canonical workspace. App\Vision\PlateVision50.csproj is the only actively maintained C# app; START_DATN.cmd uses Release\PlateVision50_V2.dll. TwinCAT is the lower control layer; the physical line consists of one shared conveyor, Gantry glue dispenser + Camera 1, future SCARA tray picker + Camera 2.

Read README.md, PROJECT_INDEX.md, Docs\TAG_MATRIX.md and Docs\PLC_README_ESTOP.md BEFORE changing code. When inspecting PLC\TwinCAT_Original_20261015, read its own AGENTS.md first. Its historic D:\VC contract paths might be absent on AML-IPC; do not claim to have read missing files.

Safety: no ADS/MC writes, PLC activation/download, TwinCAT Run/Stop, servo motion, robot start, E-stop manipulation, IO tests, physical camera reconfiguration, or real equipment actuation as part of development/testing. UI code for an operator-confirmed shared conveyor is permitted but MUST remain disarmed, fail closed on ADS errors/stale data, and never treat software feedback as certified safety.

Never permanently delete or force-move other projects or live TwinCAT directories. Legacy archive relocation is already completed and documented; do not repeat. If cleanup targets are access-denied or in use, record them and defer. No fake SCARA connected state, PLC tags or camera measurements. Preserve existing camera/track-ID overlays and video-only/no-encoder behavior.

Before each write make a restorable backup; record checkpoint/log and changed paths under Docs. Build/test C# offline, output to a versioned release-candidate directory, do not overwrite active Release. Summarize verified changes, test results and pending commissioning prerequisites.
