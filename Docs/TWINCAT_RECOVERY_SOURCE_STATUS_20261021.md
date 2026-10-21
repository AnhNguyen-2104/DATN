# DATN TwinCAT recovery and Git backup — 2026-10-21

## Original working tree (DO NOT create another TwinCAT project)
`C:\Users\aml\orca\DATN\PLC\TwinCAT\DATN.sln`
`DATN\DATN.tsproj` -> `PLC1\PLC1.plcproj` -> `POUs\MAIN.TcPOU`, 14 POU files, GVLs and DUTs.

## Confirmed facts
1. All 24 PLC project references in `PLC1.plcproj` resolve to existing files. PLC1 GUID matches the TwinCAT project reference.
2. The previous `DATN.tsproj` was unexpectedly rewritten during the earlier XAE reopen/build work, ballooning to approximately 691KB and losing the `PrjFilePath`/project GUID/TMC reference. That caused the PLC source project to vanish from the XAE Solution Explorer even though the source files remained on disk.
3. An initial surgical restoration of the missing `Project` XML attributes did not solve the loading problem.
4. The `DATN.tsproj` was restored from the existing Git HEAD snapshot after comparing and confirming the System, Motion, I/O and Mappings XML sections were identical between HEAD and the affected file; there are 47 I/O mapping links. The restored XML is approximately 116KB and references `PLC1\PLC1.plcproj`. It was validated and the original `MAIN.TcPOU` changes remained untouched.
5. On opening `DATN.sln`, XAE can load the TwinCAT project but its PLC source POU tree still does not appear. Later System Manager tree lookups returned COM RPC failure. Windows Application Event reported `TcXaeShell.exe` crash in `C:\TwinCAT\3.1\Components\Base\Build_4024.78\TwinCAT System Manager.dll` with exception `0xc0000005`. Therefore the UI recovery is STILL NOT VERIFIED, and build/online operation must not be attempted under the false assumption it has recovered.
6. Windows FIPS policy was inspected read-only and `Enabled=0`, so that known 4024 workaround is not applicable. No registry, installation, runtime or PLC configuration changes were made for that hypothesis.
7. Old live PLC firmware/runtime was not touched: NO download/activate/ADS writes/force, no motor motion, no machine test.
8. Source-only mm/s changes previously staged in `MAIN.TcPOU` remain uncommissioned and have NOT been verified as a successful new TwinCAT build. Keep compile-time gates FALSE and do not run this version on equipment.

## Restore backups (AML local)
- `D:\DATN_ARCHIVE_20261020\PLC_TreeRestore_before_20261021_171127.zip` (verified pre-repair archive with original affected 691KB tsproj, PLC1.plcproj, main source, TMC and manifest).
- `D:\DATN_ARCHIVE_20261020\DATN_tsproj_preGitBaselineRestore_20261021_173605.tsproj` (snapshot of affected tsproj before restoring Git baseline).
- Existing earlier `ChatGPT_LineMaster_before_20261021_161315.zip`, `ChatGPT_SpeedMmS_before_20261021_163555.zip` and `ChatGPT_TwinCAT_mmpsDirect_before_20261021_164925.zip` (with earlier source versions).
- The Git history itself stores the last known tracked project configuration.
- Keep all backups until a restored GUI and offline PLC compilation are independently verified.

## Recommended next XAE-only diagnosis
- Confirm matching TwinCAT 3.1 Build 4024.78 shell packages and PLC IDE integration; check system event dump for TwinCAT System Manager.dll 0xc0000005.
- Resolve XAE installation/loading issue under administrative review. Use only existing `PLC1.plcproj`, never empty template/new runtime project.
- When fixed, verify Solution Explorer shows `PLC1 Project` with all POU/GVL/DUT source objects, all 47 mappings still present, and perform offline compile. Resolve ST compiler errors if any.
- Do not Activate, Download, force or operate any machine until operator has approved safety calibration/commissioning.

## Git backup policy
Push a dated backup branch with tracked WPF/Mitsubishi/TwinCAT source and relevant newly added source/docs. Do not commit generated ReleaseCandidate, Runtime, binary libraries, historic backups, credentials or machine-local process logs.
