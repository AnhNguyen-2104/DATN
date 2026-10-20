# DATN / LINE CONTROL — DU AN CHINH AML-IPC

## MOT THU MUC CHINH
Root: C:\Users\aml\orca\DATN
Double-click: START_DATN.cmd
C# source: App\Vision\PlateVision50.csproj
Third-party Camera wrappers: Dependencies\BaslerCamera, Dependencies\Devices
Runtime output: Release\PlateVision50_V2.dll
Embedded .NET 8 runtime: Runtime\DotNet8\dotnet.exe
TwinCAT stage copy: PLC\TwinCAT\DATN.sln
Operating contracts: Docs\TAG_MATRIX.md, Docs\build_single_instance.log

## UI / Automation architecture
1. MAIN / line status: TwinCAT port 851 ADS (read-only telemetry).
   One EStop overall: GVL_MD200_IO.bEStopFeedback, reflected into MAIN.bSystemEStopOK.
   This standard Crevis ST-1228 DI0 is NOT a safety input. Safety-rated hardware
   chain must independently stop hazardous energy at MD200 and Gantry drives.
2. Shared conveyor: START/STOP operator button, 0..25Hz MD200 reference.
   Controls OFF by default; manual operator unlock and confirmation required.
   Program only writes MAIN.bMD200_Start and MAIN.rMD200_SetFrequency_Hz.
   STOP button is normal STOP, NOT physical E-STOP.
3. GANTRY+Camera1: HIK/Basler/video detection, trigger 50% FOV, PID tracking,
   read live FIFO and Mitsubishi PLC-Q status. Robot start/move not enabled
   by dashboard; real M1000/M1001 handshake and calibration require commissioning.
4. SCARA+Camera2: RESERVED, disabled/no fake ONLINE state, future deployment.
   Share same conveyor encoder; maintain own camera, robot queue and acknowledgements.
   SCARA PLC commands must be separated from Gantry and never reuse M1000 blindly.

## CAMERA
Use existing 'Vận hành' and 'Camera / Nguồn ảnh' tabs to connect Hikrobot,
Basler or local video. A --demo startup opens C:\Users\aml\Desktop\DEMO\1.avi
for test; standard START_DATN.cmd opens Dashboard without simulation by default.
Video is IMAGE-only and does not synchronize product with physical Encoder.

## RUN / SAFETY
- Build 0 errors; 11 compiler warnings inherited from third-party Hik SDK wrappers.
- ADS status displayed is the PLC symbol value, not proof of field wiring safety.
- Dashboard currently recognizes these existing PLC tags only.
- PLC TwinCAT stage under DATN is COPY for maintenance; current running
  controller has not been changed or deployed in this C# dashboard task.
- System runs one DATN instance via Local named Mutex.
- When PLC ADS is unavailable, dashboard shows OFFLINE and disables commands.
- Default HMI writes are disarmed; START confirmation is required every time;
  STOP can be sent as a normal PLC stop, not a certified safety stop.

## CLEANUP / RECOVERY
Old PlateVision50 folders were moved (NOT PERMANENTLY DELETED) from Desktop to
D:\DATN_ARCHIVE_20261020\DATN_LEGACY_ARCHIVE_20261019
Manifest: Docs\legacy_folder_inventory.csv and Docs\legacy_folder_moves.csv.
Legacy directories PlateVision50_V2 and PlateVision50_V2_Tests were relocated after content verification to D:/DATN_ARCHIVE_20261020/Desktop_Legacy_Projects.
Do NOT delete WpfYolo sample project, original DATN_TwinCat3, live PLC project,
camera drivers, TwinCAT Boot, or original contracts without separate review.

## DASHBOARD PREVIEW — 2026-10-20
Bản Dashboard công nghiệp mới đang thử nghiệm tại `ReleaseCandidate\Dashboard_20261020`.
Mở bằng `START_DATN_PREVIEW.cmd`; bản `START_DATN.cmd` và `Release` gốc chưa đổi.
Gồm Tổng quan, Gantry/Camera1, SCARA/Camera2 (RESERVED), Băng tải chung, TAGS/ADS và các tab vận hành/camera/cài đặt cũ.
Bảng TAGS/ADS chỉ đọc, hiển thị chất lượng LIVE/READ_FAIL/RESERVED. Không có lệnh robot mới.
Xem `Docs\DASHBOARD_IMPL_CHECKPOINT_20261020.md` và `Docs\BUILD_DASHBOARD_FINAL_20261020.log` để xem xác minh, hạn chế và bước cần nghiệm thu trước release.

## STATION OVERVIEW / 2026-10-20 — BAN THU MOI NHAT
- Mo `START_DATN_STATION_PREVIEW.cmd` de xem Dashboard moi, mac dinh KHONG phat lenh robot va KHONG tu mo video DEMO.
- Tong quan co Camera1/live-video, Track ID, X/Y/theta, trigger log va sensor edge log; Tram2/SCARA van RESERVED.
- Tab Gantry co dieu khien START/STOP NHAN DANG ANH tren PC; tab Bang tai co HMI MD200 rieng (mac dinh khoa, thao tac vat ly can nguoi van hanh cho phep).
- START/STOP TONG tren overview CHUA CO HANDSHAKE PLC: click chi mo thong tin, KHONG gui lenh. E-STOP tren HMI chi la huong dan; dung nut EStop PHAN CUNG khi nguy hiem.
- Candidate: `ReleaseCandidate\StationOverview_SensorLog_20261020`. Test video chi dung voi `--demo`, khong dong bo encoder vat ly.
- Xem `Docs\STATION_OVERVIEW_CHECKPOINT_20261020.md` va `Docs\GLOBAL_COMMAND_CONTRACT_PENDING_20261020.md` de tiep tuc, khong lap lai thao tac da thanh cong.

## GIT SOURCE CONTROL
GitHub: https://github.com/AnhNguyen-2104/DATN
- App/Vision and App/PlateVision50.sln: one C# WPF/vision application (not the retired copies).
- Dependencies: camera integration code; Basler Pylon 8 SDK must be installed locally (vendor DLL is not committed).
- PLC/TwinCAT: TwinCAT 3 master PLC solution and IO mappings, source only.
- PLC/Mitsubishi/Sources_20261019/GXWorks2/Candidates: Mitsubishi Gantry candidates, not verified online against the live PLC-Q.
- Docs/TwinCAT_Stage_20261019: verified copied operating documents.
- Release, ReleaseCandidate, Runtime, PLC historical backups and local video are intentionally NOT in Git.
- Build with: dotnet build App/Vision/PlateVision50.csproj -c Release (Windows + .NET 8 SDK + Basler Pylon SDK + vendor camera drivers).
- Clone of repository is SOURCE ONLY: START_DATN*.cmd require locally built Release/ReleaseCandidate and .NET runtime.
- Do not download/activate TwinCAT PLC or write to physical devices merely to verify the repository.
- Old historical code and archives preserved on D: D:/DATN_ARCHIVE_20261020 after hash verification.

## DATN CONSOLIDATION / 2026-10-20
- Canonical source: App/Vision/PlateVision50.csproj and App/PlateVision50.sln.
- Regression test source is now App/Tests/Tests.csproj, linked to App/Vision sources; dotnet build was successful offline.
- Current TwinCAT master PLC source: PLC/TwinCAT/DATN.sln; 43 core project/source files matched the former Documents stage.
- Current Mitsubishi PLC-Q candidates: PLC/Mitsubishi/Sources_20261019/GXWorks2/Candidates.
- Historical C# releases and TwinCAT stage backups were moved with SHA-256 verification to D:/DATN_ARCHIVE_20261020.
- START_DATN_PREVIEW.cmd now forwards to START_DATN_STATION_PREVIEW.cmd (newest preserved SensorLog preview). START_DATN.cmd retains original release.
- The old Desktop/PlateVision50_V2 source was archived to D after verifying Visual Studio is currently using DATN/App/PlateVision50.sln; DATN/App remains the source of truth.
- PLC/TwinCAT_Original_20261015 and PLC/Backups were archived to D; do not blindly activate or deploy the Git version.

## ACTIVE SOURCE LOCATION / 2026-10-20
Use C:\Users\aml\orca\DATN for current source editing and Orca session. This Git checkout is source-only; build the camera/I-O English preview locally with installed .NET SDK, do not assume historical Desktop runtime paths exist here. Previous source location Desktop\DATN is archived context, not the active working tree.

## CAMERA-FIRST ENGLISH OVERVIEW / AML 2026-10-20
- Current source and Orca workspace: `C:\Users\aml\orca\DATN`.
- Run newest validated camera-first English preview with `START_DATN_OVERVIEW_EN.cmd`; this builds/runs separately from the older Release/START_DATN.cmd.
- Dual camera panels and I/O are read-only by default. SCARA/Cam2 remains NOT COMMISSIONED.
- Global START/STOP buttons are NOT connected to PLC; the E-STOP / Safety Info button is NOT a hardware emergency-stop control.
- See `Docs\CAMERA_IO_ENGLISH_OVERVIEW_CHECKPOINT_20261020.md` for safety limits, build and Orca audit state.
