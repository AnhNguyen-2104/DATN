# DATN / Kế hoạch đồng bộ TwinCAT 3 ↔ C# WPF — GỬI ORCA CHỈ ĐỂ DUYỆT
Ngày 22/10/2026 | Trạng thái: PLAN ONLY — CHỜ PHÊ DUYỆT, KHÔNG TRIỂN KHAI

## 0. Quy tắc giao việc Orca
- Orca CHỈ đọc, kiểm tra, phản biện kế hoạch. CHƯA chỉnh C#/TwinCAT cho tới khi người dùng duyệt.
- Workspace đang hoạt động: E:\Orca\DATN; TwinCAT solution trong cửa sổ TcXaeShell: E:\Orca\DATN\PLC\TwinCAT\DATN.sln; C# trong App\Vision\PlateVision50.csproj. KHÔNG tự ghi đè từ C:\Users\aml\orca\DATN hoặc Desktop bản cũ.
- Đọc AGENTS.md, README.md, PROJECT_INDEX.md, Docs\TAG_MATRIX.md, Docs\PLC_README_ESTOP.md, Docs\TWINCAT_LINE_MASTER_HMI_CONTRACT_20261021.md, Docs\TwinCAT_Tag_Restore_20261022\CHECKPOINT.md.
- Kiểm tra Git diff, tiến trình/log, PID/checkpoint trước, không chạy lại bước hoàn thành; trước khi sửa thật phải sao lưu khôi phục được, ghi hash và checkpoint dưới Docs.
- KHÔNG ADS/MC writes, Force, Activate Configuration, Download, TwinCAT Run/Stop, servo jog/motion, thử analog/DO, bypass EStop/interlock, sửa camera phần cứng hay điều khiển PLC-Q.

## 1. Mục tiêu chung
- C# WPF là giao diện điều khiển duy nhất: START/STOP/RESET tổng và đặt tốc độ băng tải mm/s. TwinCAT là chủ quyết định interlock, quy đổi tốc độ và phân quyền, giao tiếp với Mitsubishi Gantry.
- MANUAL vận hành riêng theo vai trò, AUTO chỉ một nguồn phát lệnh; các tag ở trang Diagnostics phải read-only mặc định, không cần bấm RUN ở nhiều nơi.
- SCARA/Camera 2 vẫn NOT COMMISSIONED, không tạo connected state giả hoặc mượn M1000/M1001 của Gantry; giữ nguyên camera IP/USB, overlay/Track ID và logic Vision đã được Orca sửa.
- Tách hiển thị PLC ADS RUN, EtherCAT OP, Command Accepted, Conveyor DO RUN, Encoder measured speed; không coi ACK hoặc feedback EStop phụ là chứng nhận chuyển động/an toàn.

## 2. Chênh lệch đã xác nhận trong nguồn
- TwinCAT PLC 851 và System 10000 đều RUN lúc kiểm tra; bộ mapping Device 3 phục hồi 23 link, build offline 0 lỗi. Không lặp lại mapping đã đúng.
- C# Dashboard.cs còn LineAds.StartBelt(double hz) ghi MAIN.rMD200_SetFrequency_Hz và MAIN.bMD200_Start trực tiếp; SysLineStart_Click dùng InterlockModel nhưng chưa có lệnh AUTO ADS thực tế (mock/test-only).
- TwinCAT MAIN.TcPOU lấy input tốc độ MAIN.rMD200_SetSpeed_mm_s; xuất Hz nội bộ. Compile gate cBeltMmSCommissioned=FALSE, bBeltSpeedMapReady=FALSE, bBeltCalibrationVerified=FALSE, rBeltMeasuredMmPerHz=0; do đó nMD200_SpeedInputState=10, START=FALSE, AO=0. KHÔNG tự mở gate.
- Các symbol mailbox nguồn: bHmiLineStartReq/StopReq/ResetReq, udiHmiLineCmdSeq, bHmiHeartbeat, bHmiBeltSetReq/StartReq/StopReq, rHmiBeltSetSpeed_mm_s và ACK/status, chưa được mặc định coi là sẵn có trên runtime nếu chưa ADS xác nhận.
- GVL_MD200_IO.TcGVL có 3 thuộc tính TcLinkTo/TcLinkToOSO trỏ Device 2 cũ; mapping .tsproj hoạt động Device 3.
- MAIN.Axis1 đi NC Axis 1 nhưng Ezi-SERVO2 được nối NC Axis 2; cần xác minh đúng trục vật lý TRƯỚC khi thay liên kết.
- Crevis từng báo SyncManager watchdog 0x001b; Ezi-SERVO báo SAFEOP/OP 0x0014; Mitsubishi bPlcQConnected=FALSE và bHmiLinkFresh=FALSE lúc chẩn đoán; đây là các blockers riêng.
- Encoder ST-5114 cần rà lại byte/counter mode x4, giá trị 0.476475 vs 0.11911875 mm/count. 15Hz ~ 100mm/s chỉ là tham chiếu lịch sử, KHÔNG hiệu chuẩn.

## Pha A — kiểm kê hợp đồng TAG, CHỈ ĐỌC
1. Xác minh checkout thật, phiên Orca WPF, PID/log/checkpoint, Git changes, asset/version; không đè phiên đang chạy.
2. So sánh C# ADS reads/writes với TwinCAT MAIN/TMC và ReadOnly ADS 851. Ma trận mỗi tag: name/type/direction/owner/unit/scope, nguồn phản hồi, timeout/quality, AVAILABLE_IN_RUNTIME vs SOURCE_ONLY/MISSING/RESERVED.
3. Phân tách START tổng và băng tải manual, camera/tracking, Gantry PLC-Q và EziServo. Báo cáo chênh lệch tag, cờ commission và tương tranh nguồn lệnh.
Deliverable A: ma trận đầy đủ + issue list, chưa sửa source.

## Pha B — chuẩn hóa TwinCAT offline SAU KHI DUYỆT
1. Đối chiếu và sửa đúng 3 attribute TcLinkTo từ Device 2 sang Device 3, khớp offset bit và AO UINT; giữ 23 mapping và 43 active links đã khôi phục.
2. Xác minh pairing PLC Axis1 ↔ NC ↔ EziServo và ST-5114 channel/count/mm-per-pulse. Nếu không có bằng chứng trục vật lý thì chỉ báo lỗi, KHÔNG tự đổi.
3. Xác nhận contract mailbox: command sequence không lặp, xung cạnh lên, heartbeat đổi bit 250–500ms với 1.5s watchdog, ACK=processed KHÔNG phải motor running. Không phát ADS write ở bước kiểm tra.
4. Giữ cLineAutoCommissioned/cBeltMmSCommissioned/cQueueMotionCommissioned=FALSE. Chỉ build offline và kiểm tra XML + tag mapping khi có duyệt code.
Deliverable B: diff tối thiểu, backup SHA256, XAE build/report, không Activate.

## Pha C — đồng bộ C# WPF SAU KHI DUYỆT
1. Tạo một gateway điều khiển dựa trên mailbox ADS; không dùng đường direct Hz cũ cho Overview/Shared Conveyor; cô lập START legacy không cạnh tranh nhưng không xóa trang hoặc logic đã ổn.
2. SET tốc độ theo mm/s bằng rHmiBeltSetSpeed_mm_s + bHmiBeltSetReq; TwinCAT mới tính Hz từ hiệu chuẩn đo thực, giới hạn 0<Hz<=25. Hiển thị riêng requested mm/s, applied mm/s, measured encoder mm/s, commanded Hz và AO raw.
3. Manual START/STOP dùng bHmiBeltStartReq/StopReq; AUTO START/STOP/RESET dùng bHmiLineStartReq/StopReq/ResetReq + udiHmiLineCmdSeq và ACK. Dùng rising edge, unique sequence, timeout, không replay sau reconnect.
4. Chỉ cho chạy khi PLC xác nhận các trạng thái cần thiết: camera 1 có frame thật/fresh, tracking/calibration thật, PLC-Q/gantry không lỗi, interlocks + heartbeat và speed map verified. Không cho C# tự viết PLC-Q D100..D114 hoặc M1000/M1001.
5. Khi mất ADS/symbol/stale dữ liệu thì UNKNOWN/OFFLINE và khóa START. Normal STOP không thay E-Stop phần cứng; cảnh báo nếu STOP qua ADS chưa xác nhận.
Deliverable C: thay đổi nhỏ trong App/Vision, không thay Release đang chạy, giữ camera IP/USB + overlay, SCARA chưa commissioned.

## Pha D — kiểm thử offline SAU KHI DUYỆT
1. C# Debug/Release, unit tests và XAE offline build; tất cả test hiện có phải tiếp tục PASS.
2. Test fake/mocked ADS: PLC RUN nhưng belt bị khóa, symbol thiếu, heartbeat stale, ACK timeout, reconnect không tự khởi động, manual↔auto conflict, STOP latch, invalid/NaN speed, STOP khi mất ADS báo UNKNOWN.
3. Orca/Computer Use kiểm tra WPF ở 1366x768 và màn hình thật: nút START/STOP, lỗi liên động, mm/s vs Hz, Station01/02, video/image Track ID; screenshot và checkpoint.
4. Báo cáo PASS/FAIL, Git diff, danh sách chưa thể xác minh offline; phát hành riêng ReleaseCandidate, không ghi đè production.

## Pha E — thử thật/commissioning TÁCH RIÊNG, cần người dùng cho phép rõ ràng
- Cần kiểm tra E-Stop hardwired 2 kênh cho MD200 và servo, EtherCAT OP không watchdog, danh tính trục NC/EziServo, đồng bộ PLC-Q/QD75, encoder distance trên thực tế.
- Đo >=3 mức mm/s và Hz xác lập hệ số; sau đó mới trình duyệt mở compile gate, Activate/Download dưới giám sát tại tủ máy; tuyệt đối không tự thực hiện bởi Orca.

## Tiêu chí bàn giao
- C# là nơi START/STOP và nhập tốc độ duy nhất; TwinCAT sở hữu quyết định, unit chuẩn; không còn hai đường ghi PLC xung đột.
- Trạng thái RUNNING có tín hiệu xác nhận thật, không gán theo PLC RUN/ACK; mất link hoặc thiếu symbol thì BLOCKED/UNKNOWN.
- Backups + diff + test logs + screenshots + checkpoint; giữ project hiện tại; không làm máy chuyển động; báo rõ việc phải nghiệm thu sau.
- Orca chỉ ghi nhận/review tài liệu này, KHÔNG bắt đầu Pha B–E cho đến khi người dùng phê duyệt.
