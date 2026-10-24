# TwinCAT + MD200 — Checklist cho người vận hành tại AML-IPC, 23/10/2026
**Trạng thái: CẦN NGHIỆM THU. Không phải lệnh kích hoạt máy.**
**Phạm vi kỹ thuật:** ChatGPT: PLC TwinCAT, đối chiếu Crevis, mapping, logic đặt tốc độ. Orca: WPF real ADS mailbox, test offline, giữ lệnh ghi hardware DISARMED. **Người dùng / kỹ thuật viên có mặt tại máy**: kiểm tra dây, điện áp, an toàn, biến tần và đo tốc độ thực.

## 1. Đã xác minh tự động từ TwinCAT lúc 2026-10-23T14:02:44+07:00
- ADS port 10000 SYSTEM = RUN, port 300 I/O = RUN (device state 3), port 851 PLC = RUN.
- PLC Mitsubishi Q online: MAIN.bPlcQConnected = TRUE.
- AUX E-stop ST-1228 DI0: GVL_MD200_IO.bEStopFeedback = TRUE, MAIN.bSystemEStopOK=TRUE, nhưng đây KHÔNG phải chứng nhận mạch dừng khẩn phần cứng.
- Command MD200 START FALSE, ST-2318 bRUN_DO FALSE, ST-4424 nAO1_Raw=0, tốc độ đặt=0, Hz lệnh=0, tốc độ encoder=0 mm/s.
- MAIN.rBeltMeasuredMmPerHz=0; bBeltCalibrationVerified=FALSE; bBeltSpeedMapReady=FALSE; nBeltSpeedMapState=0; nMD200_SpeedInputState=10.
- HMI heartbeat fresh=FALSE; Servo Ready=FALSE; Vision Feed Verified=FALSE; Vision Calibration OK=FALSE; bAutoDispatchArm=FALSE; bLegacyMotionPublisherAllow=FALSE.
- RUN của ADS I/O port 300 **không tự chứng minh** mọi EtherCAT slave OP và working counter hợp lệ.
- Bản nguồn fixed K=100/15=6.6666667 mm/s/Hz trong MAIN.TcPOU đã build 0 errors, 26/26 test offline pass. Một lần boot project được ghi khoảng 13:40:48; chưa chứng minh đủ điều kiện vận hành.

## 2. Mapping dự án hiện có, cần xác nhận với module vật lý
- EtherCAT master Device 3 qua NIC 'Ethernet 4' — TwinCAT-Intel PCI Ethernet Adapter (Gigabit).
- Crevis ST-1228 Module 6 DI0 -> GVL_MD200_IO.bEStopFeedback (chỉ AUX).
- Crevis ST-2318 Module 10 DO0 bit0 -> GVL_MD200_IO.bRUN_DO -> tín hiệu RUN MD200 cần đo & kiểm tra cực tính/nguồn.
- Crevis ST-4424 Module 11 Analog Output Ch#1 (AO1) -> GVL_MD200_IO.nAO1_Raw -> AI/ACM biến tần MD200 cần xác nhận dây, đấu mass, nguồn module và scaling.
- Crevis ST-5114 Module 12 Ch2: MAIN hiện đọc GVL.aIn[8] / [9] và rK_mm=0.476475. Checkpoints trước từng chỉ Ch0 x4 =0.11911875 mm/xung: phải xác nhận thật kênh/độ phân giải trước khi tin rConveyorSpeed.
- Dự án .tsproj đã thấy các Link PLC / Module trên. **Không xác minh từ xa được điện áp AO1 đo bằng đồng hồ, thiết bị đạt EtherCAT OP, biến tần nhận đúng tham chiếu và cơ cấu EStop làm mất năng lượng nguy hiểm.**

## 3. Những gì người vận hành / kỹ thuật viên tại xưởng cần làm
A. **An toàn cơ khí / điện:** Chỉ người có thẩm quyền kiểm tra E-Stop hardwired, safety relay/contactors hoặc STO/isolator áp dụng cho MD200 và các trục, thử dừng độc lập với màn hình/ADS, bảo đảm không tự khởi động lại khi hồi EStop. ST-1228 DI0=TRUE chỉ là AUX. Ghi tên người nghiệm thu và kết quả; dừng nếu bất kỳ điểm nào không rõ.
B. **EtherCAT:** Tại TcXaeShell -> I/O -> Device 3 (EtherCAT) -> Online, đối chiếu Crevis Box 1, các module và Drive 2 Ezi-SERVO2: tất cả thiết bị liên quan ở OP, AL Status=0x0000, Working Counter ổn định; trước đây từng có Crevis watchdog AL 0x001b và servo AL 0x0014. Chụp ảnh cửa sổ Online / Diag. Không thử Force/Reset/Activate để che lỗi.
C. **VFD MD200:** Chụp model/nameplate và nhóm parameter điều khiển: (i) RUN source là terminal được nối từ ST-2318 DO0 qua tầng giao tiếp phù hợp; (ii) reference frequency source là analog AI input từ ST-4424 AO1; (iii) 0-10V tương ứng 0-50Hz như FB_InovanceMD200Conveyor.TcPOU đang giả định; (iv) limit/ramp/fault/terminal polarity. Kiểm tra điện áp nguồn module AO, COM chung và sơ đồ đấu nối. Kỹ thuật viên đo điện áp thực tại AI-GND theo quy trình commissioning đã phê duyệt, **không tự điều khiển máy chỉ để lấy số liệu**.
D. **Encoder ST-5114:** Xác minh thực tế có cắm vào Channel 2, chiều quay và số xung/quãng đường; đối chiếu đơn vị mm/s thực trên PLC. Trước khi chọn K, giải quyết sai khác Ch0/Ch2.
E. **Hiệu chuẩn (kỹ thuật viên giám sát local commissioning):** Ở ít nhất 3 tốc độ ổn định khác nhau được quy trình duyệt, ghi Hz THỰC trên biến tần + quãng đường mm đo THỰC và thời gian s hoặc encoder đã hiệu chuẩn; tính v_mm_s=quangduong_mm/thoigian_s; K=v_mm_s/Hz thực; ghi sai số so với K danh định 6.6666667. Nếu lệch quá 5%, phải xem lại K/drive scaling trước khi cho phép.
F. **WPF:** Sau khi Orca hoàn thành, kiểm tra build/test offline và checklist thực tế giao thức ADS mailbox, ACK, heartbeat, stop, no auto-replay. Bản đang chạy Debug hiện báo ADS READ-ONLY; không thể cho vận hành chỉ bằng bật checkbox MANUAL hay ấn APPLY.

## 4. Ghi lại số đo đã xác minh (điền bởi kỹ thuật viên)
| Lần | Hz THỰC biến tần | Quãng đường thực mm | Thời gian s | Tốc độ thực mm/s | K mm/s/Hz | Ghi chú |
|---|---:|---:|---:|---:|---:|---|
| 1 | | | | | | |
| 2 | | | | | | |
| 3 | | | | | | |

## 5. Sau khi tất cả dữ kiện đã có
- ChatGPT đối chiếu lại K danh định và PLC source/runtime, tính lại AO mapping nếu thông số MD200 không đúng 0..50 Hz, test logic fail-close & source/diff. Không tự thay đổi commissioning flags.
- Chỉ người có quyền nghiệm thu mới phê duyệt giải khóa các compilation gates và triển khai trên hệ thống đã cô lập/thử có giám sát, với bản backup có thể hoàn nguyên và phương án dừng.
- Chu trình thực sau nghiệm thu: kỹ thuật viên đứng máy theo quy trình vận hành -> WPF chọn MANUAL/Băng tải chung -> nhập mm/s -> gửi SET (PLC ACK thật) -> START thủ công -> kiểm tra drive actual Hz, AO physical, encoder mm/s, STOP & fault-response. Chế độ AUTO Gantry/Camera là quy trình nghiệm thu riêng.
- **KHÔNG tự bật TEST ONLY để chạy máy thật, không dùng nó làm bằng chứng máy đã sẵn sàng.**

## ADDENDUM 2026-10-23 — XAC NHAN KIEN TRUC TU NGUOI DUNG
Nguoi dung xac nhan: Inovance MD200 nhan tham chieu tan so qua 0-10V tu Crevis ST-4424 AO1 va lenh chay/dung qua tin hieu so tu ST-2318 DO0 (dau vao DI tren MD200). MD200 KHONG phai EtherCAT slave. Ezi-SERVO2 tren bus EtherCAT chua su dung de dieu khien chuyen dong; gantry/robot AUTO la pham vi nghiem thu rieng.
**HE QUA:** MANUAL BELT KHONG duoc bat buoc Servo Ready, homing Ezi, Camera1, SCARA hay tracking. Da doi chieu MAIN.TcPOU: nhom bHmiBeltSetReq/StartReq va bBeltSpeedMapReady khong tham chieu bServo_Ready. Dong bo WPF can giu phan tach nay. Tuy nhien, Ezi slave dang duoc cau hinh co the gay loi sync EtherCAT chung neu con tren bus: chi can xac minh master va Crevis I/O dang trao doi on dinh; khong tu y xoa Ezi khoi cau hinh hoac force OP.
**Nguoi dung can kiem tra:** I/O Crevis ST-1228 DI0, ST-4424 AO1, ST-2318 DO0, dau noi MD200 AI/COM va RUN/DI, tham so nguon lenh RUN va tan so tu analog. Xac minh 0-10V = 0-50Hz neu dung cau hinh FB dang co; do thuc te boi ky thuat vien theo quy trinh. So do/anh man hinh thong so MD200 khong bat buoc de chung minh no dung EtherCAT (vi no khong dung), chi de xac minh dai analog va nguon lenh.
**Chua du dieu kien de mo khoa:** rBeltMeasuredMmPerHz=0, bBeltCalibrationVerified=FALSE, cBeltMmSCommissioned=FALSE, HMI write gateway con DISARMED. Khong force bit, ADS write hay chay dong co de test trong buoc chinh sua source.
