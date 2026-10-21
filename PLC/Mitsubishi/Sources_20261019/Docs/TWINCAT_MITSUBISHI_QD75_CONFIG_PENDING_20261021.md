# TwinCAT -> Mitsubishi Q06UDEH/QD75D4: cấu hình cần áp dụng sau (CHƯA NẠP)
Ngày rà soát: 2026-10-21 | Workspace: C:\Users\aml\orca\DATN

## Trạng thái và điều kiện tiên quyết
- Đây là **file bàn giao kỹ thuật / cấu hình dự kiến**, KHÔNG PHẢI dự án GX Works2 đã được nạp.
- TwinCAT đã được sửa offline: FIFO Camera 8 sản phẩm -> chọn head Product ID -> giữ đầu hàng đợi -> FB_QD75CommandPublisher -> chỉ giải phóng sản phẩm có ID khớp sau đủ 4 ACK.
- Trong mã TwinCAT, `cQueueMotionCommissioned : BOOL := FALSE` là **VAR CONSTANT**; không thể mở chế độ chạy thật bằng HMI/ADS. Không thay đổi cờ này trước khi nghiệm thu camera, encoder, PLC-Q và dừng an toàn.
- Không giả định `Test2_FROM_SESSION_20261017.gxw` trùng PLC thật. Trước khi chỉnh GX: **Read/Verify from PLC** và xác nhận bản dự án chuẩn. Có thay đổi đang tồn tại trong file GX ứng viên, không ghi đè.
- Không ghi/Force các bit điều khiển, không Activate/Download hoặc cấp lệnh chạy máy theo tài liệu này.

## A. Hợp đồng vùng nhớ (TwinCAT là bên ghi lệnh)
Các DINT dưới đây gồm WORD thấp trước, WORD cao sau. Một gói duy nhất gồm 15 WORD phải được viết trước M1000.
| Mitsubishi | Tên trường | Dạng | Đơn vị / chức năng |
|---|---|---|---|
| D100-D101 | Target_X | DINT | mm x 10000 (X đã đón đầu tại lúc bắt đầu điểm) |
| D102-D103 | Target_Y | DINT | mm x 10000 |
| D104-D105 | Target_Z_Down | DINT | mm x 10000; **CHƯA XÁC NHẬN** chiều Z |
| D106-D107 | Speed_Approach | DINT | mm/s x 6000 |
| D108-D109 | Speed_Sync | DINT | mm/s x 6000 |
| D110-D111 | DeltaX_Sync | DINT | mm x 10000 |
| D112-D113 | Target_Z_Safe | DINT | mm x 10000 |
| D114 | Vertex_Index | INT | 1..4, đọc/kiểm tra và đưa vào chẩn đoán |

| Mitsubishi | Chiều | Tên / ý nghĩa |
|---|---|---|
| M1000 | TwinCAT -> Q | Start 1 đỉnh, GIỮ ON đến khi PLC đưa ACK ON |
| M1001 | Q -> TwinCAT | ACK 1 đỉnh sau khi Point 3 / Z safe thực sự hoàn thành |
| M102 | Q -> TwinCAT | Busy |
| M103 | Q -> TwinCAT | Position ready/done (phải về 0 trước lần Start tiếp) |
| M104 | Q -> TwinCAT | Alarm |
| M105 | TwinCAT -> Q | Software cancel/reset request; KHÔNG phải safety E-Stop |
| D116-D117 | Q -> TwinCAT | Target_X_Sync (diagnostic) |
| D120-D121 | Q -> TwinCAT | Error_Code 32 bit |
| D122/D124/D126 | Q -> TwinCAT | Mã lỗi từng trục QD75 (kiểm tra loại word thực tế) |
| D200-D201 | Q -> TwinCAT | Trạng thái FSM 32 bit |

**Cấm dùng M100/M101 (đang dành cho Homing). Không ghi chéo D116, D120, D200, QD75 U0\\G... từ TwinCAT.**

## B. Ngõ ra vật lý theo xác nhận đấu dây người vận hành
| Device | Định nghĩa | Chủ sở hữu |
|---|---|---|
| Y20 | Servo 1 Enable | Chỉ ladder/logic enable duy nhất |
| Y21 | Servo 2 Enable | Chỉ ladder/logic enable duy nhất |
| Y22 | Servo 3 Enable | Chỉ ladder/logic enable duy nhất |
| Y23 | Brake Release | Chỉ logic nhả phanh có liên động |
| Y24 | Dự phòng | Không dùng |
| Y25 | Glue Pump Trigger | Chỉ một coil/điểm ghi trong MAIN, theo yêu cầu M200 nội bộ |

- File ST lịch sử viết Y20 như van keo là **sai với đấu dây hiện tại**. Không import đè ST lịch sử.
- Xác nhận coil Y25 duy nhất, trạng thái bình thường OFF; khi EStop/Alarm/mất Ready/kết thúc chu trình phải OFF. M200 chỉ là yêu cầu nội bộ; không thay thế liên động thật.
- Chưa biết driver bơm nhận xung ngắn hay mức duy trì, cần xác nhận độ rộng lệnh và phản hồi thực. Giá trị 30 ms trong mã cũ là GIẢ ĐỊNH, không được tự áp dụng.
- Cần đối chiếu QD75 Point 2 M-code AFTER/BEFORE: phải phát yêu cầu bơm đúng thời điểm chấm keo, không tự mở Y25 theo thời gian tùy ý.

## C. Chu trình Mitsubishi cần kiểm tra/sửa trong GX Works2 (chưa được thực hiện)
1. Xác nhận chương trình Tracking thuộc Scan Program / có được gọi thực sự, không chỉ Compile 0 errors. Kiểm tra đơn vị, trục X/Y/Z, QD75 ready, limit, homed, brake/servo và HARDWIRED E-Stop độc lập.
2. Trước START, xác nhận M102=0, M103=0, M104=0, M1001=0; dữ liệu D100..D114 nguyên gói, đọc D114 và từ chối số đỉnh ngoài 1..4.
3. Kiểm tra Target_X, Target_X + DeltaX_Sync trong [0,180] mm; Y [0,170] mm; Z [0,52] mm và điều kiện ZDown >= ZSafe **chỉ nếu trục Z tăng theo hướng hạ**. Cần xác minh cơ khí trước khi chạy.
4. Point 1: đến XYZ bằng Speed_Approach. Point 2: X đồng tốc (Speed_Sync), phát yêu cầu chấm keo M200 đúng pha đã xác thực. Point 3: Z rút về Safe.
5. Chỉ đưa M1001=TRUE sau khi QD75 báo Point3 xong, phanh/servo/motion feedback bình thường và Z đã về vị trí an toàn; dùng M102, M103 phản ánh trạng thái thực.
6. Khi TwinCAT hạ M1000, xóa M1001/M103 và về rảnh. Khi M104, lỗi limit, timeout, QD75 lỗi, hoặc M105: Y25/M200 OFF, không tự chạy lại; reset cần lệnh có chủ đích.
7. Kiểm tra lại logic xóa lỗi 501/502/503: các lỗi phần mềm phải có cơ chế reset độc lập với cờ lỗi phần cứng QD75. Không xóa lỗi chỉ vì mất kết nối.
8. Giữ Y0 SM400, Y10/M110 và các coil servo/phanh của MAIN đúng như dự án hiện có; xác minh bằng cross-reference trước khi sửa.

## D. Cấu hình Ethernet Mitsubishi
- CPU Q06UDEH built-in Ethernet, MC Protocol QnA 3E Binary, TCP 5010; địa chỉ đang dùng trong TwinCAT: 192.168.3.39.
- Chỉ kiểm tra xem cài đặt hiện tại có trùng cấu hình máy đang chạy; **không thay đổi Network Parameters hoặc reset CPU** khi chưa được cho phép.
- Không coi TCP Connected là PLC đã nhận/hiểu M1000, D100 hoặc trạng thái QD75.

## E. Bàn giao và các bước kiểm chứng trước khi mở khóa
- Xác nhận quãng đường encoder ST-5114: CountMode0=0xC / 360 PPR -> 1440 count/vòng; đang dùng 0.11911875 mm/count (D bánh 54.6mm, cần đo thật).
- C# cần cung cấp Frame ID, event exposure đồng bộ encoder, result pose (tâm X/Y mm, góc θ), độ tin cậy và kiểm tra trễ/stale. **C# hiện chỉ có BENCH SIM**, chưa cấp production trigger đáng tin cậy.
- FIFO TwinCAT được giữ chờ đúng Product ID; không được giải phóng bằng nút SimCompleteHead. Phải thử no-output theo trạng thái: trống, full, miss, lost frame, lỗi camera, lỗi PLC, bị ngắt kết nối, cạnh ACK sai, Reset khi busy.
- Verify/Read từ PLC-Q để xác nhận chương trình online và cross-reference toàn bộ Y20-Y25, M1000/M1001, QD75 buffer.
- Chỉ sau khi được người vận hành cho phép mới thiết kế test có giám sát, đo độ trễ thực tế, chọn thời gian/nhiệt độ keo và cấu hình Z. Không thay đổi `cQueueMotionCommissioned` hoặc nạp chương trình chỉ vì Build PASS.

## F. Checklist chờ quyết định của người dùng
- [ ] Xác nhận file GX Works2 chính thức sau PLC online Read/Verify.
- [ ] Xác nhận chiều/trục Z, ZSafe, ZDown và giới hạn thực tế.
- [ ] Xác nhận Y25 cần xung hay giữ ON và có feedback bơm hay không.
- [ ] Xác nhận camera Frame ID/exposure hardware hoặc phương án đồng bộ có bằng chứng.
- [ ] Xác nhận chương trình xử lý Stop/Reset/EStop độc lập trên PLC-Q.
- [ ] Chỉ định buổi nghiệm thu offline/simulation trước khi xem xét Apply vào PLC thật.
