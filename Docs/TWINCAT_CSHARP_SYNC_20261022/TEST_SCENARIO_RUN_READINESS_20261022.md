# DATN — KỊCH BẢN TEST VÀ CỔNG ĐIỀU KIỆN CHẠY THẬT
Ngày đánh giá: 2026-10-22, 19:33–19:35 (Asia/Bangkok). Máy: AML-IPC.
Workspace nguồn: E:\Orca\DATN. Tài liệu này CHỈ lập kịch bản; KHÔNG cho phép nạp PLC hay tác động thiết bị.

## 1. Kết luận GO / NO-GO tại thời điểm kiểm tra
**GO cho kiểm tra offline và đọc tín hiệu. NO-GO cho START băng tải, Enable/Jog servo, tự động Gantry và SCARA.**
- TwinCAT System ADS 10000 = Run; PLC1 ADS 851 = Run. Đây là PLC đang xử lý, KHÔNG có nghĩa tất cả EtherCAT slave OP hoặc máy sẵn sàng.
- E-Stop phản hồi phụ GVL_MD200_IO.bEStopFeedback = TRUE; MAIN.bSystemEStopOK = TRUE; MAIN.bSystemStopRequested = FALSE. CHƯA có bằng chứng mạch E-Stop hai kênh phần cứng bảo vệ MD200 và Gantry.
- Băng tải: MAIN.bMD200_Start = FALSE; MAIN.rMD200_SetSpeed_mm_s = 10; MAIN.rMD200_SetFrequency_Hz = 0; MAIN.nMD200_SpeedInputState = 10; MAIN.bBeltSpeedMapReady = FALSE; nBeltSpeedMapState = 0; bBeltCalibrationVerified = FALSE; rBeltMeasuredMmPerHz = 0. RUN DO = FALSE, AO raw = 0.
- Mitsubishi PLC-Q: MAIN.bPlcQConnected = FALSE, MAIN.nPlcQErrorID = 0x8007274C (Winsock 10060 timeout). PLC-Q target nguồn 192.168.3.39:5010, local Ethernet 3 IP 192.168.3.40/24 nhưng NIC Ethernet 3 DISCONNECTED (0 bps), không có TCP session đến target và không có route hoạt động 192.168.3.0/24.
- EtherCAT: Ethernet 4 (TwinCAT-Intel PCI Ethernet Adapter) UP 100 Mbps; **trạng thái OP của từng slave chưa được xác nhận trực tiếp**. Lịch sử đã có Crevis watchdog AL 0x001b / servo SAFEOP->OP 0x0014; không được tuyên bố OP chỉ vì link Up.
- Servo: MAIN.bServo_Ready = FALSE và MAIN.bServo_Enable = FALSE. File .tsproj hiện nối MAIN.Axis1 -> NC Axis 1, nhưng Ezi-SERVO2 vật lý -> NC Axis 2. Cần xác nhận trục bằng sơ đồ/drive, tuyệt đối không đoán hoặc bật jog.
- HMI: MAIN.bHmiLinkFresh = FALSE; bHmiCamera1Ready = FALSE; bHmiTracking1Ready = FALSE; bVisionFeedVerified = FALSE; bLineAutoAccepted = FALSE; các lệnh bHmiLineStartReq, bHmiBeltStartReq = FALSE.
- PLC source cLineAutoCommissioned = FALSE, cQueueMotionCommissioned = FALSE, cBeltMmSCommissioned = FALSE. C# ProductionCommandSourceApproved = FALSE. Đây là khóa cố ý; không tự sửa TRUE để ép máy chạy.
- Encoder nguồn MAIN.fbConveyor sử dụng GVL.aIn[8] với rK_mm = 0.476475; hợp đồng ST-5114 channel 0 x4 ghi 0.11911875 mm/count (chênh 4 lần). Chưa xác nhận đơn vị mm/s thực.
- Mapping trong DATN.tsproj là XML hợp lệ, 43 active links (23 liên kết PLC I/O được kiểm tra), đã xóa link trùng; TwinCAT XAE BUILD LẠI SAU KHI XÓA TRÙNG **chưa được xác nhận thành công**.
- C# build Release 0 errors, 228 unit tests PASS/0 FAIL. Candidate ở ReleaseCandidate/20261022_TwinCAT_CSharp_Sync_SourceOnly, chưa qua visual QA; dotnet publish còn lỗi Basler.Pylon duplicate NETSDK1152. START_DATN.cmd vẫn trỏ Release cũ, không tự thay.

## 2. Ma trận kịch bản test theo thứ tự ưu tiên
Mỗi test ghi: người thực hiện, thời điểm, ảnh/log, PASS/FAIL/BLOCKED, kết luận. Bất kỳ lỗi an toàn/OP/feedback => DỪNG GIAI ĐOẠN, không thử tiếp bằng cách Force.

| ID | Mức | Thao tác / bằng chứng | Điều kiện PASS | Hiện tại |
| --- | --- | --- | --- | --- |
| T01 | OFFLINE | Mở .sln C# + TwinCAT đúng E:\Orca\DATN; kiểm tra Git diff/backup/checkpoint | Đúng bản nguồn; không mất thay đổi Orca | PASS ở lần kiểm trước |
| T02 | OFFLINE | Xác minh XML .tsproj, link EStop/RUN/AO/encoder + NC | 43 active links; 23 I/O links duy nhất, không Device 2; source/NC đúng | PARTIAL: mapping có, NC còn lệch |
| T03 | OFFLINE | XAE Build/Rebuild cấu hình cuối cùng không Activate | 0 lỗi; không tạo trùng links; 43/23 giữ nguyên sau build | BLOCKED: chưa xác minh Build cuối |
| T04 | OFFLINE | C# Build/Test và xem UI candidate ở 1366x768/desktop | 0 lỗi build, mọi test PASS, UI không clip, phân biệt RUN/ACK/physical | PARTIAL: 228 PASS, visual QA chưa chạy |
| T05 | CHỈ ĐỌC | Đọc ADS 10000, 851 và dấu thời gian dữ liệu | Cả hai RUN, ADS dữ liệu mới, không symbol mismatch | PASS trạng thái ADS, cần giám sát liên tục |
| T06 | CHỈ ĐỌC | EtherCAT Device 3: slave Box Crevis, Ezi-SERVO2, WcState/AL codes, 24V field, DC sync | Master + mọi slave cần thiết OP, WcState valid, không watchdog trong thời gian giám sát | UNKNOWN / lịch sử lỗi |
| T07 | CHỈ ĐỌC | PLC-Q: cáp Ethernet 3, đèn link, NIC Up, đúng 192.168.3.40/24, PLC 192.168.3.39, route, TCP 5010 | Cáp/switch ổn định, local NIC Up, không timeout, bPlcQConnected TRUE | FAIL: NIC Disconnected / TCP timeout |
| T08 | AN TOÀN | Kỹ thuật viên trực tiếp xác minh mạch E-Stop hardwired hai kênh + rơle an toàn cho MD200 và Gantry, guard & nguồn 24V | Đạt thử chức năng theo quy trình an toàn máy; fail-safe khi đứt dây/mất nguồn | NOT VERIFIED; DI phụ TRUE không đủ |
| T09 | CHỈ ĐỌC | Xác minh NC Axis thực với số drive/cáp/encoder/sơ đồ, so mapping MAIN.Axis1, NC Axis1/2 | Axis PLC->NC->servo vật lý khớp; trạng thái ready/fault được hiểu đúng | FAIL/UNCONFIRMED: Axis mismatch |
| T10 | OFFLINE/CHỈ ĐỌC | Đối chiếu ST-5114 count mode, Channel, byte input, scaling và chiều encoder | Một mm/count được xác nhận bằng sơ đồ + số xung và phép đo cơ khí an toàn | FAIL: scale hiện chênh x4 |
| T11 | OFFLINE/CAMERA | Camera1 IP/USB Scan, Connect, nhận frame/Track ID, không gửi tọa độ điều khiển; video chỉ QA nhận diện | Real frame + tracking fresh, sản phẩm nào thông số đi theo sản phẩm đó; không ADS write | CHƯA TEST TRỰC TIẾP lần này |
| T12 | OFFLINE/HMI | Mock RUN/STOP/RESET, lost ADS, heartbeat stale, thiếu symbol, replay seq, AUTO/MANUAL conflict, speed NaN/zero | Tất cả fail-closed, no auto resume, không ghi đầu ra, không nhầm ACK là chuyển động | 228 tests PASS; UI mock QA chưa kiểm chứng lại |
| T13 | NGHIỆM THU | Kỹ thuật viên xác nhận AO ST-4424/DO ST-2318 wiring, tham số MD200, tần số giới hạn, tín hiệu dừng | Đúng terminal/biến tần, dải AO và cực tính; NO RUN trong trạng thái lỗi | CHƯA KIỂM CHỨNG HIỆN TRƯỜNG |
| T14 | NGHIỆM THU | Đo tốc độ băng tải bằng ít nhất 3 điểm vận hành được phép, đo encoder count/chiều/distance | rK_mm đúng thực tế, hệ số mm/s/Hz từ dữ liệu đo thật; không dùng 15Hz~100mm/s như số đo | BLOCKED đến sau T06–T10,T13 |
| T15 | NGHIỆM THU | Kiểm thử riêng mailbox HMI qua bộ giả lập ADS trước khi dùng PLC thật | Start/Stop/Set/Reset, seq/ACK, heartbeat TTL 1.5s, unknown timeout; interlock độc quyền | Chưa có gateway ghi thực |
| T16 | CHẠY CÓ GIÁM SÁT | Chạy thử băng tải thật theo SOP được phê duyệt, vùng an toàn và nguồn chuyển động robot cô lập | DO/AO thực đo phù hợp, encoder đối chiếu mm/s, STOP làm dừng, mất comm fail-safe, không tự restart | NO-GO |
| T17 | CHẠY CÓ GIÁM SÁT | Chạy riêng Gantry, xác minh PLC-Q handshake/QD75 homing/giới hạn/trục trước hành trình có tải | Không lỗi NC/Ezi-SERVO, phản hồi PLC-Q đúng, hardwired stop hiệu quả | NO-GO |
| T18 | CHẠY CÓ GIÁM SÁT | Chuỗi camera1 -> Product ID -> FIFO -> băng tải -> 4 đỉnh -> ACK hoàn tất; SCARA không tham gia | Không trộn tọa độ sản phẩm, không chạy trùng lệnh, track & encoder có hiệu chuẩn; STOP ở mọi giai đoạn | NO-GO |

## 3. Kịch bản chi tiết và thứ tự thực hiện
### S0. Chuẩn bị không chuyển động — có thể thực hiện ngay
- Ghi ảnh toàn bộ tủ điện, wiring thực tế/thiết bị, drive firmware/parameter, route NIC; chụp trạng thái TwinCAT online và log trước khi chỉnh.
- Kiểm tra E-Stop phần cứng bởi người có chuyên môn khi hệ thống ở điều kiện an toàn; không Force EStop DI; không dựa vào software STOP.
- Mở đúng file candidate C# chỉ để xem; ứng dụng phải hiển thị trạng thái khóa START/STOP, lý do NOT COMMISSIONED. Không dùng bản Release cũ để suy ra tính năng mới.

### S1. Kiểm tra hiện trường ở trạng thái chỉ đọc — có thể thực hiện ngay
- Kiểm tra Ethernet 3 link tại tủ/switch/kết nối PLC-Q, KHÔNG đổi địa chỉ IP, NIC binding EtherCAT hay route khi chưa sao lưu cấu hình và xác nhận chủ sở hữu.
- Theo dõi trong TwinCAT: PLC1 ADS RUN, EtherCAT per-slave OP và trạng thái WcState, không dùng chỉ số 'Ethernet4 Up' thay cho OP.
- Đọc tần số đặt/analog/raw/DO/encoder, đảm bảo khi chưa Start: rMD200_SetFrequency_Hz=0, nAO1_Raw=0, bRUN_DO=FALSE.
- Rà NC Axis mismatch qua tài liệu drive/sơ đồ, không thử jog để đoán trục.

### S2. Kiểm thử phần mềm không chuyển động — có thể thực hiện ngay
- Chạy 228 tests + mock interlock (không ADS writes), video/camera không phát lệnh PLC, tình huống stale ADS/mất PLC-Q/camera absent/missing symbol.
- Build offline TwinCAT cần hoàn thành, kiểm tra số links trước và sau; nếu tự phát sinh duplicate, ngừng để chẩn đoán.
- Kiểm thử UI candidate màn hình thật, phân biệt SETPOINT mm/s / Hz nội bộ / ENCODER actual / RUN DO.

### S3. Nghiệm thu điện/động cơ — **chỉ có kỹ thuật viên tại máy và phép thử riêng**
- Chỉ chuyển sang khi S0-S2 PASS, EStop hardwired đã được nghiệm thu và EtherCAT OP ổn định.
- Kiểm tra tủ điện, tài liệu đấu AO/DO, đấu nối MD200, tham số giới hạn và hướng dừng; xác nhận đồng bộ servo/PLC-Q/NC; ký biên bản người giám sát.
- Với thiết bị robot được cách ly và vùng băng tải trống/guard đóng, vận hành băng tải theo SOP/phạm vi tốc độ thấp đã được chuyên gia phê duyệt để lấy ít nhất 3 mẫu tốc độ, xác nhận dừng; không thay source gate tùy tiện.

### S4. Test chuyển động robot / tích hợp — **NO-GO đến khi S3 và từng cơ cấu đủ điều kiện**
- Cấm chạy đồng thời tất cả trước khi kiểm tra từng cơ cấu. Xác minh lệnh/ACK/actual/fault riêng cho từng trục dưới giám sát, biên cơ khí an toàn và phản ứng lỗi.
- Sau khi robot, băng tải, encoder, camera đều được nghiệm thu riêng mới kiểm tra Camera1/Gantry end-to-end.
- SCARA/Camera2 vẫn RESERVED, không coi camera connected là robot READY.

## 4. Điều kiện dừng test ngay
Dừng kế hoạch chạy máy nếu: EStop phần cứng chưa nghiệm thu, thiếu người giám sát, EtherCAT slave không OP, servo axis chưa xác nhận, mất link PLC-Q, lỗi watchdog, encoder scale chưa đo, nguồn lệnh HMI cạnh tranh, AI/vision nhận sai sản phẩm, đầu ra chạy khi không có lệnh, tốc độ thực và tốc độ yêu cầu sai bất thường hoặc STOP chưa có phản hồi.
Nếu có nguy hiểm: sử dụng phương tiện dừng an toàn phần cứng theo quy trình của nhà máy; STOP trên C#/TCP không phải EStop được chứng nhận.

## 5. Cổng quyết định tổng
- **Hiện tại cho phép:** T01/T02/T04/T05/T07 đọc trạng thái/T09 kiểm tra sơ đồ/T10 đối chiếu nguồn/T11 camera QA/T12 mock; chỉ hoạt động không chuyển động, không ADS write.
- **Hiện tại chưa xác minh:** T03 build XAE cuối, T06 slave EtherCAT OP, T08 EStop certified, T13 drive IO, T14 calibration.
- **Hiện tại không cho phép:** T16–T18 chạy chuyển động thực, tải Gantry, mở source commissioning constants, nạp/Activate PLC từ xa.
- Kết quả nghiệm thu phải có: người thực hiện, thời gian, thiết bị, ảnh màn hình/log, số đo, PASS/FAIL, chữ ký hoặc xác nhận của người giám sát.

## 6. Các lệnh kiểm tra hỗ trợ
- Chỉ đọc trạng thái: `powershell -NoProfile -File "E:\Orca\DATN\Docs\TWINCAT_CSHARP_SYNC_20261022\READ_ONLY_RUN_CHECK.ps1"`.
- Lệnh này không gửi ADS write, không kích hoạt EtherCAT, không chạy PLC, không điều khiển camera; chỉ tạo log báo cáo trên đĩa.
- Đối chiếu với checkpoint nguồn: `Docs\TWINCAT_CSHARP_SYNC_20261022\CHECKPOINT.md`.
