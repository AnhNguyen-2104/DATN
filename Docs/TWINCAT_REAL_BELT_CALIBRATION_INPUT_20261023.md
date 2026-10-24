# Thu thập số liệu hiệu chuẩn băng tải thật — 23/10/2026
**Trạng thái: CHƯA ĐO — KHÔNG phải kết quả hiệu chuẩn.**
Vị trí: AML-IPC / E:\Orca\DATN. Không tự chỉnh cLineAutoCommissioned, cBeltMmSCommissioned, bBeltCalibrationVerified; không ADS write, không Activate/Download/Start trong phiên kiểm tra này.

## Điều đã biết qua READ-ONLY ADS trực tiếp
TwinCAT port 10000 RUN, PLC 851 RUN, IO port 300 RUN (device state=3; **không chứng nhận mọi slave EtherCAT ở OP**).
MAIN.bSystemEStopOK TRUE, nhưng ST-1228 DI0 chỉ là tín hiệu phụ, không phải biên bản an toàn độc lập.
MAIN.bMD200_Start FALSE; GVL_MD200_IO.bRUN_DO FALSE; GVL_MD200_IO.nAO1_Raw=0.
MAIN.rBeltMeasuredMmPerHz=0.0; MAIN.bBeltCalibrationVerified=FALSE; MAIN.bBeltSpeedMapReady=FALSE; MAIN.nMD200_SpeedInputState=10.
MAIN.bHmiLinkFresh=FALSE; MAIN.bServo_Ready=FALSE; MAIN.bVisionCalibrationOK=FALSE; MAIN.bAutoDispatchArm=FALSE.
Người dùng thông báo hệ thống an toàn, **chưa có phiếu nghiệm thu/biên bản số đo đi kèm**. Không coi thông báo này là giá trị sensor.
## Phép đo do kỹ thuật viên vận hành tại chỗ thực hiện theo quy trình đã phê duyệt
Không dùng nút TEST ONLY; không dùng tín hiệu nguồn phát sinh từ WPF để hiệu chuẩn. Nếu phải thử chạy băng tải theo chế độ biến tần tại chỗ, kỹ thuật viên có thẩm quyền phải kiểm soát vùng nguy hiểm, cơ cấu dừng, quy trình khởi động/dừng và giới hạn thiết bị. Không điều khiển chuyển động từ xa.
Ghi ít nhất ba mức vận hành ổn định **được kỹ thuật viên chọn trong dải đã được nghiệm thu**, mức tần số thực tế phải đọc từ biến tần (không suy ra từ Hz setpoint); vận tốc phải đo qua quãng đường/thời gian độc lập hoặc encoder đã xác nhận xung/mm.
| Lần | Hz thực tại biến tần | Quãng đường thực (mm) | Thời gian đo (s) | Vận tốc mm/s | mm/s/Hz | Encoder counts + kênh | Ghi chú |
|---|---:|---:|---:|---:|---:|---|---|
| 1 | CHƯA ĐO | CHƯA ĐO | CHƯA ĐO | CHƯA TÍNH | CHƯA TÍNH | CHƯA XÁC MINH | |
| 2 | CHƯA ĐO | CHƯA ĐO | CHƯA ĐO | CHƯA TÍNH | CHƯA TÍNH | CHƯA XÁC MINH | |
| 3 | CHƯA ĐO | CHƯA ĐO | CHƯA ĐO | CHƯA TÍNH | CHƯA TÍNH | CHƯA XÁC MINH | |
Công thức từng lần: `v = quãng đường thực / thời gian (mm/s)`, `K = v / Hz thực (mm/s/Hz)`.
Sau khi có số liệu, kiểm tra tính tuyến tính và sai số, đối chiếu chiều quay, số xung/mm của ST-5114 (nguồn MAIN đang dùng channel 2 Byte8/9 với 0.476475 mm/count; các checkpoint trước ghi channel 0 x4 với 0.11911875 mm/count — PHẢI xác minh phần cứng).
**Không lấy 6.6667 mm/s/Hz theo giả định 15 Hz ≈ 100 mm/s làm hiệu chuẩn thật.**

## Các điều kiện vận hành AUTO chưa hoàn tất
- EtherCAT slave OP và không watchdog: kiểm tra thêm tại IDE/field theo đúng device/terminal.
- PLC: cLineAutoCommissioned FALSE, cBeltMmSCommissioned FALSE, cQueueMotionCommissioned FALSE.
- Vận hành Gantry: bServo_Ready FALSE, bVisionCalibrationOK FALSE, bVisionFeedVerified FALSE, bAutoDispatchArm FALSE và bLegacyMotionPublisherAllow FALSE.
- WPF: ProductionCommandSourceApproved=false, HMI heartbeat FALSE; mọi thao tác live-command phải được mở theo quy trình duyệt riêng.
- Trước triển khai: so sánh source vs runtime, offline build, backup khôi phục, nghiệm thu hệ thống an toàn độc lập và người vận hành xác nhận thông số.

Checkpoint: chỉ tạo tài liệu thu thập số liệu; không thực hiện bất kỳ lệnh có thể làm chuyển động máy.
