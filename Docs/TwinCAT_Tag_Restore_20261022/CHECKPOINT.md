# DATN TwinCAT EtherCAT mapping restore — 2026-10-22

## Scope and authority
- Restored tag mappings **in the existing, active TcXaeShell solution**: `E:\Orca\DATN\PLC\TwinCAT\DATN.sln`.
- Changed project file: `E:\Orca\DATN\PLC\TwinCAT\DATN\DATN.tsproj`.
- No project clone was created. The other Desktop and C:\Users\aml\orca source copies were not edited.
- No TwinCAT Activate Configuration, PLC Download/Login, Run/Stop, ADS/MC writes, servo motion, physical I/O testing, EStop override or robot commands.

## Recovery sequence
1. Confirmed live TcXaeShell DTE solution and inspected in-memory `TIID`: Device 3 (EtherCAT), Crevis Box 1, Ezi-SERVO2 Drive 2.
2. Found on-disk tsproj unexpectedly contained Device 2 and lost physical I/O and mapping. Preserved pre-save backup.
3. Saved actual IDE state via DTE `File.SaveAll`, restoring Device 3 plus its configured slaves into the original tsproj; preserved second backup.
4. Called TwinCAT `ITcSysManager.LinkVariables` for all required PLC I/O symbols and saved in same open solution.
5. Offline validated XML and 23 exact PLC links; build succeeded (DTE `LastBuildInfo=0`, `BuildState=3`).

## Restored PLC process-image links (23)
- 16 input bytes: `GVL.aIn[0..15]` -> Device 3 / Crevis / Module 12 ST-5114 / Input(s) Byte 0..15.
- 4 output bytes: `GVL.aOut[0..3]` -> Device 3 / Crevis / Module 12 ST-5114 / Output(s) Byte 0..3.
- `GVL_MD200_IO.bEStopFeedback` -> Module 6 ST-1228 / Input(s) Byte 0 bit 0 (auxiliary status, NOT safety-rated).
- `GVL_MD200_IO.bRUN_DO` -> Module 10 ST-2318 / Output(s) Byte 0 bit 0.
- `GVL_MD200_IO.nAO1_Raw` -> Module 11 ST-4424 / Output(s) Analog Output Ch#1 Byte (16-bit AO1).
- Existing NC-to-servo and PLC-to-NC axis links were retained; total `<Mappings>` links = 43.

## Unresolved, not hidden
- PLC instance still contains **4 historic `UnrestoredVarLinks`** for `GVL.aEncOut[0..3]` pointing at deleted Device 2. These are `RestoreInfo=ANotFound`, are **not active mappings**, and the current `GVL.TcGVL` does not declare `aEncOut`. Preserved for forensic comparison; do not treat as a required output or re-link to hardware.
- Error List visually shows **3 runtime errors and 3 warnings** following offline build. EtherCAT master Device 3 selects network card `Ethernet 4` (TwinCAT-Intel PCI Ethernet Adapter), seen Up at 100 Mbps. Crevis Box 1 reported OP->SAFEOP Sync manager watchdog (AL 0x001b); Ezi-SERVO2 reported SAFEOP->OP failed, AL 0x0014 observed. Offline build success does not mean EtherCAT OP or machine-ready.
- No live hardware movement, analog output, physical emergency stop circuit or PLC-Q handshake has been verified. Proper independent hardware EStop is mandatory.

## Restorable backups
- `DATN.tsproj_BEFORE_IDE_SAVE.xml` — 2026-10-22 initial on-disk state (stripped Device 2).
- `DATN.tsproj_DEVICE3_before_links.xml` — Device 3 saved from IDE, before linking.
- `DATN.tsproj_BEFORE_FINAL_LINKS.xml` — after first validated encoder link, before remaining 22.
These backups are stored alongside this checkpoint under `Docs\TwinCAT_Tag_Restore_20261022`.

## Future action
Diagnose physical Crevis/servo EtherCAT state and SyncManager watchdog independently of restored mapping. Do not Activate/Download to physical equipment without explicit supervised commissioning permission.
