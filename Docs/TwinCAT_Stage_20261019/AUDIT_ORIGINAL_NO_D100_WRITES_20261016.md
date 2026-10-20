# RÀ SOÁT MÃ GỐC TWINCAT 3 — KHÔNG GỬI D100–D114 (16/10/2026)

Phạm vi: đọc code tại C:\Users\aml\Desktop\DATN_TwinCat3 trên AML-IPC; đọc ADS runtime 851 và đọc MC 3E Binary tại port 4999. Không ghi PLC, không sửa cấu hình hoặc điều khiển trục. D5000=24680 giữ nguyên.

## Kết luận — nguyên nhân trực tiếp, chứng cứ mạnh
MAIN.TcPOU chỉ gọi fbPublisher(bStartCycle := bStartGlueCycle). Không có logic tự chuyển bCameraTrigger hoặc bProductValid -> bStartGlueCycle. bStartGlueCycle mặc định FALSE, fbPublisher có R_TRIG(CLK:=bStartCycle); do đó FSM không rời state 0 nếu không có cạnh lên Start độc lập.

FB_ConveyorTracking4Pt dùng R_TRIG bCameraResult. MAIN.bCameraTrigger default FALSE và trong source PLC không có camera bridge hoặc code tạo cạnh trigger. Các tham số camera X=50,Y=85,Angle=0 là giả lập mặc định, KHÔNG phải dữ liệu từ camera thực tế trong mã đã duyệt. Chỉ khi bCameraTrigger lên cạnh mới tính 4 điểm và bProductValid=TRUE. Sự kiện camera và yêu cầu phát lệnh là hai chuyện khác nhau.

ADS đo trực tiếp khi Servo chạy: bPlcQConnected=TRUE, rConveyorSpeed khoảng 88 mm/s, bCameraTrigger=FALSE, bProductValid=FALSE, bStartGlueCycle=FALSE, fbPublisher.nState=0, fbPublisher.bWriteReq=FALSE, fbWrite.bDone=FALSE, fbWrite.bError=FALSE, nErrorCode=0. D100/102/104/106/108/110/112/114 đọc trực tiếp từ Mitsubishi vẫn = 0. Đây là minh chứng hiện chưa HỀ phát lệnh ghi tọa độ.

## Các lỗi code thêm cần sửa
1. MAIN không có auto-dispatch cho mỗi sản phẩm. Bật Servo JogFwd chỉ sinh encoder speed, KHÔNG kích Camera và Start.
2. Publisher gốc thiếu bProductValid và thiếu interlock PLC-Q M102/M104 khi Start; khi tọa độ ngoài giới hạn X/Y, tự thay X=50 hoặc Y=85, có thể ghi tọa độ không thuộc sản phẩm thực tế. Không cho chạy robot theo mã này khi chưa xử lý.
3. Giao thức handshake M1000/M1001 chỉ được kiểm tra sau khi đã viết D100–D114; logic chỉ đợi M1001=0 ở state 10. Chưa đảm bảo M102=0, M104=0; còn liên động khác cần kiểm soát trong Mitsubishi.
4. FB_MelsecRead và FB_MelsecWrite gốc đều ép stSession.bBusy := FALSE ở state IDLE, sau đó kiểm tra stSession.bBusy để quyết định có nhận tác vụ; khóa dùng chung có thể bị xóa dù FB kia đang giao dịch. Nguy cơ socket conflict.
5. FB_MelsecRead/Write bản gốc giả định TCP response nhận đầy đủ trong một lần, coi nRecBytes < 11 là lỗi; TCP được phép trả từng mảnh. Bản sửa staging xử lý fragmentation.
6. FB_MelsecTcpConfig gốc đánh dấu bConnected=TRUE ở state 10 mà không có kiểm thử MC hợp lệ; khi connection fail có nhánh xóa hSocket.handle thay vì đóng socket theo vòng đời. Từng phát sinh timeout/kẹt socket; TF6310 ADS lỗi 6 đã sửa bằng đăng ký và RUN lại.
7. TON tonTimeout gốc trong Publisher không được gọi nhất quán mỗi scan ở trạng thái chờ ACK, nên các timeout 10s/5s không đáng tin; có thể kẹt ở trạng thái chờ.
8. FB_ConveyorTracking4Pt gốc tính travel theo UINT subtraction ép INT mà không xử lý modulo 16-bit có chủ đích; sau rollover hoặc khoảng di chuyển lớn có thể sai bù vị trí. Bản staging đã cải thiện.
9. Trong cấu hình DATN.tsproj, input GVL.aIn[0]/[1] có liên kết tới ST-5114 Byte 0/1; 4 liên kết output GVL.aEncOut[0..3] có RestoreInfo=ANotFound. Cần đối chiếu module mapping trước khi chạy đầy đủ.
10. Ảnh GX Works2 có 12 lỗi compile Tracking. Đây là vấn đề riêng cần đối chiếu PLC online; không giải thích vì sao trong TwinCAT Publisher state=0.

## Đã sửa nhưng CHƯA nạp Runtime
Bản staging trên AML-IPC: C:\Users\aml\Documents\DATN_ECHO_STAGE_20261016\DATN.sln
- MAIN có auto dispatch chỉ hoạt động nếu bAutoDispatchEnable=TRUE VÀ bAutoDispatchArm=TRUE (mặc định cả hai FALSE).
- Nhận cạnh Camera mới, kiểm tra product valid, tốc độ encoder >0, TCP, interlock, giới hạn chờ 750ms, sau đó mới yêu cầu Publisher thực hiện chu trình.
- Có FB_QD75CommandPublisher sửa logic kiểm tra M102/M104/M1001, đóng gói D100–D114, handshake M1000/M1001, chống timeout/abort.
- FB_MelsecRead/Write/Config đã sửa xử lý TCP và reconnect; FB_MelsecEchoVerify cho test độc lập D/M.
- Build XAE trên AML-IPC thành công LastBuildInfo=0 lúc 11:06:19 ngày 16/10/2026, TMC chứa bAutoDispatchEnable/Arm và Echo FB. Không chứng minh bản mới đã chạy trong Runtime.

## Điều kiện nghiệm thu còn thiếu
- Cần xác minh nguồn sự kiện camera thật: ứng dụng camera gửi X/Y/Angle và một cạnh bCameraTrigger vào TwinCAT, chưa tìm thấy trong code gốc.
- Cần kiểm tra và khôi phục chương trình Mitsubishi/QD75 online có handshake M1000/M1001 và PLC Ready trước khi cho robot chạy.
- Cần Activate bản staging theo quy trình thay cấu hình an toàn có rollback. Khối auto dispatch mặc định khóa, phải kiểm chứng camera và các interlock trước khi arm.
- Chỉ test gửi D100–D114/M1000 trong chu trình được kiểm soát vì đây là vùng nhớ điều khiển chuyển động, tuyệt đối không dùng như vùng test mạng.
- Không tự đổi bServo_Enable/JogFwd hoặc D5000 đang giữ giá trị 24680.

## Mã nguồn xác thực
C:\Users\aml\Desktop\DATN_TwinCat3\DATN\PLC1\POUs\MAIN.TcPOU
C:\Users\aml\Desktop\DATN_TwinCat3\DATN\PLC1\POUs\FB_ConveyorTracking4Pt.TcPOU
C:\Users\aml\Desktop\DATN_TwinCat3\DATN\PLC1\POUs\FB_QD75CommandPublisher.TcPOU
C:\Users\aml\Desktop\DATN_TwinCat3\DATN\PLC1\POUs\FB_MelsecRead.TcPOU
C:\Users\aml\Desktop\DATN_TwinCat3\DATN\PLC1\POUs\FB_MelsecWrite.TcPOU
C:\Users\aml\Desktop\DATN_TwinCat3\DATN\PLC1\POUs\FB_MelsecTcpConfig.TcPOU
