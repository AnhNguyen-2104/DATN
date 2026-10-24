# TwinCAT 3 DATN — Fixed K / Offline source checkpoint (2026-10-23)

## Mục tiêu / quyền hạn
- Yêu cầu: hoàn thiện TwinCAT cho nhập tốc độ tuyến tính mm/s, tự đổi sang Hz và analog MD200, sử dụng K cố định danh định 100/15 = 6.666666666666667 mm/s/Hz.
- Dự án đang sửa: `E:\Orca\DATN\PLC\TwinCAT\DATN.sln`, POU `DATN\PLC1\POUs\MAIN.TcPOU`. Không sửa WPF, PLC-Q, drive hoặc các liên động ngoài phạm vi. AGENTS.md/README/PROJECT_INDEX/PLC README đã đọc.
- Không hề gửi ADS write, force biến, tải PLC, Activate Configuration, ra lệnh START/STOP/JOG; build XAE offline only. Mọi trạng thái runtime là quan sát đọc, không có yêu cầu sửa runtime nào từ phiên này.

## Trước khi thay đổi
- Nguồn `MAIN.TcPOU` SHA256: `86E61DB3527B95500766E413CB1FDAEE1D502762241000AE9988178967135BCC`; lưu file `MAIN.TcPOU.before` ở thư mục này.
- Git worktree có nhiều thay đổi WPF và mapping Crevis từ các phiên trước, được giữ nguyên (không reset/clean/commit).
- Lược đồ trước: WPF manual SET / TwinCAT engineering input dùng `rBeltMeasuredMmPerHz` (đang 0), K 6.66667 chỉ dùng preview, nguồn bị khóa.
- Hardware hiện hữu chưa có biên bản kiểm tra analog, EtherCAT slave OP, kênh encoder và hệ số đo thực. Không tự giả định 15 Hz luôn đạt 100 mm/s.

## Những thay đổi ĐÃ XONG trong MAIN.TcPOU
1. Thêm hằng số cấu hình `cBeltFixedMmPerHz : LREAL := 6.666666666666667;`.
2. Thêm điều kiện sai số cho K đo thực `cBeltMaxKDriftRatio : LREAL := 0.05;` (5%). Nếu sai lệch quá giới hạn hoặc chưa có phép đo, `bBeltSpeedMapReady` vẫn FALSE.
3. Cả nhánh `rHmiBeltSetSpeed_mm_s` và `rMD200_SetSpeed_mm_s` thống nhất tính Hz bằng `speed / cBeltFixedMmPerHz`, giữ giới hạn ứng dụng 25 Hz, ZERO command khi chưa READY.
4. Preview vẫn mang nhãn UNVERIFIED; `bBeltCalibrationVerified` KHÔNG được set TRUE, `rBeltMeasuredMmPerHz` KHÔNG được ép thành danh định. Đúng thiết kế fixed-K nhưng kiểm chứng phép đo vẫn độc lập.
5. Giữ nguyên source compile gates `cLineAutoCommissioned=FALSE`, `cBeltMmSCommissioned=FALSE`, `cQueueMotionCommissioned=FALSE`, đảm bảo không có lệnh robot hoặc MD200 phát sinh.
6. Sửa chú thích cũ 'unmapped virtual only' vì mapping ST-2318/ST-4424 đã xuất hiện trong tsproj; không thay IO mapping.
7. `FB_InovanceMD200Conveyor.TcPOU` không thay đổi: dùng 0..50 Hz ↔ 0..10V, 0..4095 raw, ramp 2Hz/s, RUN riêng ST-2318. Cấu hình biến tần vật lý chưa được xác nhận.

## Kiểm thử
- DTE: `TcXaeShell.DTE.15.0`, solution `E:\Orca\DATN\PLC\TwinCAT\DATN.sln`, configuration `Release`; gọi `SolutionBuild.Build(true)` (không Activate).
- Lần build sau cùng: `LastBuildInfo=0`, `BuildState=3`, không có compile errors.
- `FIXED_K_REGRESSION.ps1` chạy lại sau sửa, kết quả `REGRESSION_RESULTS.txt`: 26 PASS / 0 FAIL.
- Điểm 100 mm/s => 15 Hz => expected 3.0 V => raw khoảng 1228–1229 (chỉ là lệnh analog ước tính; chưa đo vật lý).
- File MAIN hiện tại SHA256: `6DEB8EB75E5EC8B8FAF74D7D3AE5122DEC20D22106C63A3C8E2D146CA3F37187`.
- `git diff --check` không phát hiện lỗi whitespace (có cảnh báo Git LF/CRLF với file C# trước đó).

## Vấn đề runtime mới / KHÔNG ĐƯỢC TỰ KHỞI ĐỘNG
- Preflight trước đó đọc ADS 10000 RUN và PLC 851 RUN. Kiểm tra cuối phiên này thấy ADS 10000 Config, ADS I/O 300 Config, PLC 851 không tồn tại (error 0x6).
- Boot PLC `C:\TwinCAT\3.1\Boot\Plc\Port_851.app` vẫn ghi mốc 11:21:12 cùng ngày, chưa có bằng chứng tải bản mới.
- Nguyên nhân chuyển CONFIG **CHƯA XÁC ĐỊNH**; không gán nguyên nhân cho lượt build khi thiếu sự kiện tương ứng. Nhật ký Application lấy được không chỉ rõ sự kiện config tại thời điểm chuyển. Kiểm tra thao tác tại IDE/người vận hành trước khi thay đổi chế độ.
- Tuyệt đối không tự Start/Activate khi chưa giải quyết dấu hỏi trạng thái phần cứng.

## Để nghiệm thu chạy thật (cần người vận hành tại máy)
1. Xác minh nguyên nhân runtime CONFIG, kiểm tra lý do/tình trạng EtherCAT slaves (Crevis watchdog 0x001b, Ezi-SERVO SAFEOP/OP 0x0014 ở checkpoint cũ), kiểm tra relay E-Stop phần cứng độc lập.
2. Xác nhận MD200 được cấu hình AI analog 0-10V = 0-50Hz, RUN DO0 ST-2318, AO1 ST-4424, nguồn và điện áp đo thực; xác minh encoder ST-5114 đúng kênh và mm/count.
3. Đo độc lập mm/s ở >= 3 Hz thực; bảo đảm K thực trong dung sai đã được kỹ sư chấp nhận với K cố định; cập nhật phiếu nghiệm thu và bBeltCalibrationVerified. Nếu K lệch, phải điều chỉnh K cố định theo đo được và build lại, không force cờ.
4. Xác nhận WPF giao thức ADS thực đang có khóa `ProductionCommandSourceApproved=false`: phải được chủ WPF rà soát/phê duyệt; không tự mở cùng lượt này.
5. Sau biên bản nghiệm thu, sao lưu/đối chiếu runtime, phê duyệt compiler gates, triển khai trực tiếp có người giám sát và thử tín hiệu từng bước theo quy trình an toàn. Build thành công không có nghĩa đã được phép vận hành.

## Rollback / Tiếp tục
- Nếu chỉ muốn phục hồi mã nguồn (không ảnh hưởng runtime), có thể phục hồi file `MAIN.TcPOU.before` sau khi đã đối chiếu diff và phiên khác; KHÔNG chạy lệnh phục hồi tự động. Chỉ file MAIN được chỉnh.
- Lần tiếp tục phải đọc file này, xác minh log/PID/TcXaeShell/git status, không lặp lại backup/build thành công trừ khi source hoặc thư viện thay đổi.
- Trạng thái: `OFFLINE_SOURCE_COMPLETE | HARDWARE_COMMISSIONING_BLOCKED | RUNTIME_CONFIG_UNRESOLVED`.
