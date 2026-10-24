# DATN — K danh định trên giao diện SCADA (24/10/2026)
**Phạm vi:** Sửa phần HIỂN THỊ WPF, không thay đổi vận hành thực của PLC/MD200.
**Trạng thái:** Hoàn thành mã nguồn, Release build 0 lỗi, 307/307 bài test offline PASS, chưa triển khai đè cửa sổ đang chạy.

## K thực tế trong mã nguồn
- PLC `MAIN.cBeltFixedMmPerHz = 6.666666666666667` đã cố định từ trước.
- PLC `MAIN.rBeltMeasuredMmPerHz=0` đang là một trường riêng dành cho **K đo được đã nghiệm thu**, không tự copy hệ số danh định vào và không được coi là chứng nhận đo.
- PLC `MAIN.cBeltMmSCommissioned=FALSE`, `MAIN.bBeltCalibrationVerified=FALSE`, `MAIN.bBeltSpeedMapReady=FALSE`. Trạng thái SpeedMapReady được PLC tự tính trong MAIN.TcPOU, không tồn tại nút để bấm.
- WPF `LineMailboxProtocol.ProductionCommandSourceApproved => false`, nên không ghi SET, START, STOP hoặc HMI heartbeat lên PLC; HMI không thể tự "phê duyệt" qua MANUAL hay APPLY.
- `bSystemEStopOK=TRUE` chỉ là auxiliary signal, không phải xác nhận chức năng dừng an toàn cứng đã đạt.

## Đã sửa mã và kiểm thử trên AML-IPC
1. Sao lưu `Dashboard.cs`, `LineInterlock.cs`, `App/Tests/Program.cs` vào `Docs/WPF_NOMINAL_K_UI_FIX_20261024/Original/`, SHA trước sửa lưu bằng công cụ remote.
2. `App/Vision/Vision/LineInterlock.cs`: thêm `LineSpeedPreset.NominalMmPerHz=6.666666666666667` và `NominalPreviewHz(mmS)`. Preview 100 mm/s → ~15.00 Hz với nhãn **PREVIEW ONLY — calibration pending; NO PLC WRITE**. Không thay đổi hàm cũ `DerivedHzInfo` hay phần interlock.
3. `App/Vision/Dashboard.cs`: dùng preview danh định trong ô nhập tốc độ thay vì truyền false/0 khiến nó báo sai là "Hz NOT CALCULATED"; vẫn giữ NO PLC WRITE và DISARMED.
4. `App/Tests/Program.cs`: thêm 2 bài test preview 15Hz và chống nhầm với lệnh thật.
5. `dotnet build App/PlateVision50.sln -c Release`: 0 errors; warnings chỉ tồn tại ở thư viện camera vendor nullable.
6. `dotnet run --project App/Tests/Tests.csproj -c Debug --no-restore`: 307 PASS / 0 FAIL, TOTAL_TEST_FAILURES=0.
7. Staged compiled Release WPF in `E:\Orca\DATN\ReleaseCandidate\20261024_NOMINAL_K_PREVIEW_DISARMED` (SHA256_MANIFEST.txt). **Đây là bản DISARMED**, chưa được phát hành cho điều khiển motor. Tiến trình WPF Debug đang mở vẫn là bản trước sửa, không tự đóng hoặc restart.

## Quy trình MANUAL thật
Bắt buộc vận hành với người phụ trách kỹ thuật tại máy, xác nhận E-Stop cứng, MD200 DI/AI scaling, Crevis I/O OP, biên bản tốc độ thực từ encoder; ghi giá trị K đo đã xác nhận (không Force) và phê duyệt điều kiện MANUAL của bản PLC. Phát hành phiên bản WPF production kiểm tra heartbeat ACK và điều kiện STOP trước khi thử belt. Không kích hoạt gantry, AUTO, Ezi-SERVO2. Không copy/Force các khóa commissioning khi thiếu kết quả an toàn.
