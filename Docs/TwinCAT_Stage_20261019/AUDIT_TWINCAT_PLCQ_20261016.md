# Báo cáo kiểm tra toàn bộ đường dữ liệu TwinCAT3 -> Mitsubishi PLC-Q
Thời điểm: 2026-10-16 (AML-IPC). Phạm vi: kiểm tra đọc mã dự án đang chạy, snapshot ADS và MC read-only; chỉnh sửa/build bản sao staging, không kích robot.

## Kết luận chính (xác nhận bằng ADS và mã)
- Giao tiếp qua 192.168.3.39:5010 OK: MAIN.bPlcQConnected=True; khối FB_MelsecRead đã đọc M1001 không lỗi trong lượt kiểm tra trước.
- Không có lệnh phát tọa độ hiện tại: MAIN.bCameraTrigger=False, MAIN.bProductValid=False, MAIN.bStartGlueCycle=False; fbPublisher.nState=0, fbPublisher.bBusy=False; stCommand.Target_X=0, Target_Y=0, Vertex_Index=0.
- MC 3E Binary đọc-only qua cổng 4999: D100,D102,D104,D106,D108,D110,D112,D114 = 0; M1000, M1001, M102, M103, M104 đều 0; D5000 vẫn 24680 theo yêu cầu người dùng.
- Không có khối giao tiếp camera thật trong MAIN. Tâm 50,85 và góc 0 là giá trị giả lập. bCameraTrigger phải có cạnh lên thì FB_ConveyorTracking4Pt mới tính tọa độ. Không có bridge Camera -> bCameraTrigger đang xác nhận từ mã.
- MAIN hiện tại chỉ gọi fbPublisher(bStartCycle := bStartGlueCycle). Không có logic nối bProductValid hoặc sự kiện Camera tới bStartGlueCycle; do đó giá trị tính toán KHÔNG tự động gửi sang D100..D114.
- nEncoderPulse gần nhất 38043 (liên tục không đổi trong lấy mẫu), nEncoderDelta=0, rConveyorSpeed=0. Khi băng tải đứng yên đó là trạng thái hợp lý, không tự động coi là encoder hỏng; khi chạy cần chứng minh tốc độ >0.
- PLC-Q D100..D114 sẽ chỉ đổi khi Publisher nhận Start và đã đi qua trạng thái chuẩn bị (nState 20,21); nếu không Start thì không có gói WRITE.

## Các lỗi/rủi ro phụ phát hiện từ source đang chạy
1. FB_QD75CommandPublisher cũ không kiểm tra bProductValid, M102/M104 trước WRITE và dùng fallback rTargetX:=50, rTargetY:=85 khi tọa độ sai. Hành vi này có thể gây chuyển động không mong muốn, cần dùng bản đã sửa có interlock.
2. FB_MelsecRead/FB_MelsecWrite cũ cùng đặt stSession.bBusy:=FALSE khi ở state idle, làm mất mutex giữa giao dịch socket đồng thời. Logic cũ không xử lý phân mảnh TCP chặt chẽ.
3. FB_MelsecTcpConfig cũ tự đặt stSession.bConnected:=TRUE ở state 10, reconnect timeout 3s và có trường hợp xóa handle socket trước khi FB_SocketClose.
4. Cấu hình EtherCAT hiện có link PlcTask Inputs GVL.aIn[0] và [1] với Module 12 ST-5114 Byte0/Byte1: link INPUT có. Tuy nhiên 4 link GVL.aEncOut[0..3] tới output Module 12 có RestoreInfo="ANotFound"; GVL hiện không khai báo aEncOut. Cần kiểm tra chức năng output/cấu hình counter, không sửa bừa khi chưa biết tác động thiết bị.
5. PLC Task CycleTime=100000 = 10ms (100ns tick). FB_ConveyorEncoder cũ dùng bộ lọc 20 scan = 200ms (so với yêu cầu 100ms). Bản sửa staging lọc 10 scan.
6. Tài liệu mô tả quadrature encoder x4 nhưng hệ số 0.476475 mm/pulse dựa trên 360 xung/vòng. Nếu counter thực báo 1440 cạnh/vòng thì mm/count thực phải khoảng 0.11912; PHẢI hiệu chuẩn số đếm/vòng thực tế, chưa khẳng định sai phần cứng.
7. Ảnh GX Works2 từ người dùng hiển thị 12 lỗi compile Program Tracking. Không suy từ đó ra firmware Q06UDEH hiện chạy sai: cần đọc lỗi cụ thể trong Output, sửa và compile, xác minh chương trình nào đang download xuống PLC. Hợp đồng Session2_Contract_For_Session1.md là nguồn chính; một số đoạn cũ của Glue_Tracking_System_Specs.md còn nhầm M100/M101 thay vì M1000/M1001.
8. Trong phiên audit, không có bằng chứng Camera GigE/USB3 đã kết nối tới biến MAIN.rCameraCenterX/Y/Angle. Cần xác định ứng dụng camera và bridge ADS.

## Bản sửa staging ĐÃ BIÊN DỊCH trên AML-IPC, CHƯA ACTIVATE
Đường dẫn: C:\Users\aml\Documents\DATN_ECHO_STAGE_20261016\DATN.sln
- Bản staging chứa FB_MelsecTcpConfig/Read/Write, FB_QD75CommandPublisher với kiểm tra điều kiện, cập nhật Encoder/Tracking, và FB_MelsecEchoVerify.
- Đã thêm Auto Dispatch trong MAIN theo camera edge: bAutoDispatchEnable=FALSE, bAutoDispatchArm=FALSE mặc định. Khi 2 cờ được bật có kiểm soát, có event mới và bProductValid, thử gửi nếu rConveyorSpeed>0, mạng OK, không có hoạt động xung đột; hủy yêu cầu quá 750ms.
- Debug: nAutoDispatchState=0/1/2/3/4/5/6/10 (idle/queued/speed invalid/TCP busy/motion busy/expired/invalid/start requested); nAutoStartRequests.
- Không tự hoạt động khi khởi động; không tự kích M1000 hoặc van keo với cờ mặc định FALSE.
- Build TwinCAT XAE trên AML-IPC lúc 11:06:17: LastBuildInfo=0; PLC1.tmc mới chứa bAutoDispatchEnable, bAutoDispatchArm, nAutoDispatchState, nAutoStartRequests, FB_MelsecEchoVerify.
- Chưa chạy bản này trong TwinCAT Runtime do yêu cầu bảo toàn ứng dụng điều khiển đang chạy; thao tác ActivateConfiguration trước đây bị remote safety filter chặn.

## Điều kiện nghiệm thu hoàn chỉnh
1. Giữ D5000=24680 cho tới khi người dùng yêu cầu trả lại giá trị 0. Không dùng D100..D114 để test mạng nếu có nguy cơ robot chưa sẵn sàng.
2. Xác định nguồn Camera: phát mỗi sản phẩm thành event bCameraTrigger FALSE->TRUE, cập nhật tâm X,Y, góc trước trigger (chưa có camera bridge).
3. Khi băng tải được chạy trong điều kiện an toàn, theo dõi nEncoderPulse và rConveyorSpeed, hiệu chuẩn counter ST-5114 (x1/x2/x4).
4. Kiểm tra và sửa compile errors của Tracking trong GX Works2; xác minh Q06UDEH ladder/session2 handshake M1000/M1001 thật sự online, QD75 Ready.
5. Trong bảo trì được duyệt: Activate Configuration của bản staging, xác nhận giữ I/O mapping, TwinCAT RUN, ADS error 0, không lỗi NC; xác minh nAutoDispatchState trước khi arming.
6. Chỉ khi cấp phép chạy robot và PLC-Q Ready, kích một product event rồi xem fbPublisher.nState: 0->10->11->14->15->20->21, verify D100..D114, sau đó handshake M1000/M1001; theo dõi M102/M103/M104/D120; đảm bảo rollback.
7. Không dùng M100 hoặc M101 (thuộc Homing trong Q06UDEH). Không dùng abort M105 làm E-stop safety-rated.

## Bằng chứng tại thời điểm audit
ADS: Runtime RUN; bPlcQConnected TRUE; bCameraTrigger FALSE; bProductValid FALSE; bStartGlueCycle FALSE; fbPublisher.nState 0; stCommand.Target_X/Y=0, Vertex_Index=0; rConveyorSpeed=0.
Direct MC only-read: D100/D102/D104/D106/D108/D110/D112/D114=0, M1000/1001/102/103/104=0, D5000=24680.
