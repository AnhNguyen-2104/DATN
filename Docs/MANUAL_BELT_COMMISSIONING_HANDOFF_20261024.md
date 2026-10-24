# DATN | Bàn giao nghiệm thu băng tải MANUAL — 24/10/2026

**Mục tiêu:** chỉ cho phép băng tải MD200 vận hành MANUAL bằng WPF sau khi nghiệm thu. Không cần Camera 1/2, Gantry, SCARA hay servo Ezi-SERVO2. MD200 nhận 0–10 V từ ST-4424 AO1 và lệnh RUN từ ST-2318 DO0/DI biến tần, không qua EtherCAT trực tiếp.

## A. Đã kiểm tra trực tiếp (đọc, không phát lệnh)
- AML-IPC ADS 851 = RUN; sequence tags `MAIN.udiHmiBeltCmdSeq`, `MAIN.udiBeltLastAckSeq` tồn tại, đều bằng 0.
- WPF chạy từ `E:\Orca\DATN\App\Vision\bin\Debug\net8.0-windows\PlateVision50_V2.exe` (tiến trình PID được ghi trong nhật ký riêng).
- SOURCE PLC `PLC\TwinCAT\DATN\PLC1\POUs\MAIN.TcPOU`: `cBeltMmSCommissioned=FALSE`; K mặc định 6.666666666666667; `cLineAutoCommissioned=FALSE` và `cQueueMotionCommissioned=FALSE` giữ nguyên.
- PLC runtime: `rBeltMeasuredMmPerHz=0`; `bBeltCalibrationVerified=FALSE`; `nBeltSpeedMapState=0`; `bBeltSpeedMapReady=FALSE`; `bHmiLinkFresh=FALSE`; `bSystemEStopOK=TRUE` (AUX, không phải chứng nhận E-Stop cứng); MD200 RUN=FALSE, AO=0.
- SOURCE WPF `App\Vision\Vision\LineMailboxProtocol.cs`: `ProductionCommandSourceApproved => false`. `TickHeartbeat` không phát ADS write khi disarmed, nên `bHmiLinkFresh=FALSE` là hệ quả dự kiến, không phải lỗi EtherCAT.
- WPF solution chính xác `E:\Orca\DATN\App\PlateVision50.sln`, build Debug/Release đạt; 305/305 offline unit checks, WPF mở và đáp ứng.
- Nhật ký điện tử: `Docs\MANUAL_BELT_PREFLIGHT_STATUS_20261024.txt`. Có thể đọc lại theo thời gian mới với `Docs\CHECK_MANUAL_BELT_READONLY_20261024.ps1`.

## B. Ghi nhận từ chủ máy
- Người dùng thông báo bước 1 (dây nguồn biến tần, E-Stop) đã kiểm tra; bước 2 `K=6.6667`; bước 3 đã hoàn tất. Các tuyên bố này là thông tin đầu vào, không tự động biến thành dữ liệu PLC, không đủ để suy ra bài thử dừng khẩn cấp và số đo tốc độ 3 điểm đã được ghi.
- Hồ sơ chứng minh cần điền/đính kèm thực tế tại máy: `[ ]` kiểm định mạch dừng khẩn cấp cứng; `[ ]` đối chiếu tham số MD200 nguồn DI RUN/AI/COM và thang analog; `[ ]` Crevis ST-4424 AO1 & ST-2318 DO0 vận hành ổn định (I/O slave OP/WKC); `[ ]` 3 điểm đo K thật; `[ ]` kết quả STOP/khởi động lại/rollback.

| Mẫu | Hz đo trên MD200 | Quãng đường băng tải (mm) | Thời gian (s) | Vận tốc (mm/s) | K = mm/s / Hz |
|---|---:|---:|---:|---:|---:|
| 1 | — | — | — | — | — |
| 2 | — | — | — | — | — |
| 3 | — | — | — | — | — |

## C. Công việc kỹ thuật viên/owner phát hành, theo thứ tự
1. Tại máy trong trạng thái an toàn, đối chiếu và ký nghiệm thu phần cứng độc lập, 3 điểm tốc độ K, tốc độ encoder, tình trạng máy không có người trong vùng chuyển động và không thể tự chạy lại sau reset/mất ADS.
2. Kỹ sư PLC phê duyệt bản chỉ chạy MANUAL; cấu hình và triển khai chính thức điều kiện commissioning và hiệu chuẩn **dựa trên dữ liệu đo**, không dùng ADS Force hay bypass hardwired EStop. Giữ AUTO/Gantry/Servo khóa; backup boot/source và test phục hồi.
3. Xác nhận runtime ADS 851 RUN; symbol và kiểu đúng; trạng thái `bBeltSpeedMapReady` READY **chỉ sau khi** nghiệm thu K thực. Ghi hash bản PLC đã nạp.
4. Kỹ sư WPF phê duyệt riêng production command source sau kết quả nghiệm thu, build, phát hành trên đường dẫn đúng `App\PlateVision50.sln`. Kiểm tra `bHmiLinkFresh` chỉ khi heartbeat WPF được cho phép và PLC nhận ổn định.
5. Tại máy với người giám sát, nghiệm thu SET → ACK seq mới → START → so Hz, điện áp tại MD200, tốc độ encoder; thử STOP, E-Stop cứng và mất kết nối, kiểm tra không tự khởi động lại. Ghi thành biên bản.

**Không được coi 305/305 unit tests offline là bằng chứng mô-tơ đã hoạt động hoặc phần cứng an toàn.**

## D. Tiêu chí xong (cần đủ)
- [ ] PLC: bộ MANUAL phát hành được kỹ sư ký duyệt; `bBeltSpeedMapReady=TRUE`, K và calibration true **đã chứng minh**.
- [ ] WPF: bản production được ký duyệt; heartbeat có ACK/đúng watchdog, SET/START/STOP và các kết quả từ PLC đều đúng.
- [ ] Phép thử thực tại máy thành công, STOP và E-Stop cứng đạt; không tự khởi động lại sau mất nguồn/mạng.

**Biên bản này KHÔNG cấp quyền chuyển động. Trạng thái khi tạo: BLOCKED_BY_COMMISSIONING.**
