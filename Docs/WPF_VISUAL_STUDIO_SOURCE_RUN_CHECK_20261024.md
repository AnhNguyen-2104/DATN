# DATN WPF — kiểm tra đúng Visual Studio solution / 2026-10-24

## Dự án đúng
- Visual Studio Insiders trong ảnh người dùng mở **E:\Orca\DATN\App\PlateVision50.sln**.
- Startup project `PlateVision50` tương ứng `E:\Orca\DATN\App\Vision\PlateVision50.csproj`.
- Target: Windows WPF .NET 8, AssemblyName `PlateVision50_V2`, x64.
- TwinCAT source chính nằm riêng tại `E:\Orca\DATN\PLC\TwinCAT\DATN.sln`. Hai project không phải là một solution.
- Code WPF chứa hai tag `MAIN.udiHmiBeltCmdSeq` và `MAIN.udiBeltLastAckSeq`, đã thấy các tag trên ADS 851 sau khi người dùng nạp PLC.

## Thao tác đã xác minh trên AML-IPC
- Thực hiện `dotnet build App\PlateVision50.sln -c Debug -p:Platform='Any CPU' --nologo`: `Build succeeded`, **0 errors, 12 nullable warnings** trong mã camera vendor.
- Test `dotnet run --project App\Tests\Tests.csproj -c Debug --no-restore`: **305 PASS, 0 FAIL**, `TOTAL_TEST_FAILURES=0`, `TEST_EXIT=0`. Log: `Docs\WPF_SOLUTION_TEST_20261024.log`.
- Đã trực tiếp mở `E:\Orca\DATN\App\Vision\bin\Debug\net8.0-windows\PlateVision50_V2.exe`. Tiến trình PID **19140** lúc 10:15 ngày 2026-10-24, `Responding=True`, cửa sổ `DATN | Line Control – TwinCAT Master`.
- Không tải/nạp lại PLC, không gửi ADS write, không test chuyển động. Không chạm chế độ Servo Ezi hay Gantry.
- PLC ADS 851 `RUN`, hai seq/ACK tag đọc được và bằng 0, EStop AUX TRUE, MD200 DO RUN FALSE, AO raw 0.

## Lý do chức năng băng tải vẫn không chạy thật
- Source WPF: `LineMailboxProtocol.ProductionCommandSourceApproved => false`, **DISARMED** cho lệnh ghi PLC theo quy định an toàn dự án AGENTS.md.
- PLC runtime: `MAIN.bBeltSpeedMapReady = FALSE`, `MAIN.bBeltCalibrationVerified=FALSE`, `MAIN.rBeltMeasuredMmPerHz=0`, `MAIN.bHmiLinkFresh=FALSE`.
- Đồng bộ symbol thành công **không đồng nghĩa** với PLC điều kiện commissioning được nghiệm thu hoặc lệnh PLC đã được chấp thuận.
- Chuỗi khởi động WPF đang chạy thực chỉ dành cho quan sát an toàn và thử giao thức OFFLINE/mock. STOP phần mềm khác EStop phần cứng.
- Người dùng khai K=6.6667 và đã kiểm tra phần cứng, nhưng runtime chưa chứa K measured + bản ghi kiểm định; chưa có tài liệu kiểm định tốc độ 3 điểm để đối chiếu. Không tự force biến hoặc bật gate gây chuyển động từ xa.

## Những việc cần cho nghiệm thu vận hành
1. Kỹ thuật viên tại máy đối chiếu mạch EStop phần cứng, Crevis IO OP, nguồn lệnh MD200 DI RUN / AI scaling, kiểm định encoder và ít nhất 3 điểm thực Hz-mm/s.
2. Theo quy trình TwinCAT đúng, phát hành một build MANUAL-only đã được xét duyệt với điều kiện commissioned/K calibrated; giữ AUTO/Gantry/servo khóa.
3. Phát hành WPF operator-approved, heartbeat có xác thực và giới hạn stale; thực nghiệm PLC đáp ứng số seq đúng, SET/ACK/START/STOP, ngắt kết nối và không tự restart dưới giám sát.
4. Kết thúc test ghi biên bản K thực tế, phiên bản nguồn, trạng thái kết nối, tốc độ và phương án rollback.

Không xác nhận đã vận hành cơ cấu thật trong bước build/run WPF này.
