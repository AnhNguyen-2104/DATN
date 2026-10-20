# HƯỚNG DẪN KIỂM THỬ CAMERA FIFO THEO MM

Phiên bản: 2026-10-16. Bản riêng biệt, đã Build 0 lỗi, CHƯA NẠP lên Runtime.
Solution: C:\Users\aml\Documents\DATN_CAMERA_FIFO_STAGE_20261016\DATN.sln
Khối mới: PLC1 > POUs > FB_CameraProductQueue
DUT mới: PLC1 > DUTs > ST_QueuedCameraProduct

## 1. Đặc tả Camera đã xác nhận
Camera trả về:
- CenterX (mm)
- CenterY (mm)
- AngleDeg (độ)
Không có chuyển pixel->mm trong TwinCAT.
Khi Camera thực tế được nối vào PLC phải có sự kiện Image Exposure, sự kiện Result và bảo đảm đúng ảnh;
hiện chưa có Camera bridge thật. Do Camera chưa cung cấp FrameID, chỉ cho 1 ảnh đang chờ kết quả;
nếu có exposure chồng nhau sẽ chặn và yêu cầu reset bQueueReset sau khi giải quyết.
Không được giả bVisionFeedVerified/ bVisionCalibrationOK là TRUE để chạy thử mô phỏng.

## 2. Reset và tham số
nEncoderPulse: counter thô, không về 0 khi Restart XAR.
nEncoderPulseSinceStart và rConveyorDist: ban đầu về 0 trong runtime reset, sau đó cộng theo Encoder.
fbCameraQueue, nQueueCount, counters, aQueueHeadCornerNow: về 0 trên scan đầu tiên.
rCameraToWorkStart_mm=300 mm hiện là giá trị người dùng nhập trong Runtime. Giá trị này có thể không
tự lưu qua Restart/Download; cần nhập lại sau khi nạp, cho đến khi bổ sung PERSISTENT/recipe đã kiểm thử.
rCamToRobotX_mm là offset hiệu chỉnh, KHÔNG nhập -300 vào biến này lần thứ hai.
Các thông số rProductLength=40, rProductWidth=40, rCamToRobotYaw_deg theo hiệu chuẩn thực tế.

## 3. Watch biến để thử mà KHÔNG KÍCH GANTRY
Trước khi nạp bản mới, dừng Ezi-Servo, xác nhận QD75 an toàn, và kiểm tra cấu hình/map.
Đây là thay đổi Runtime cần người vận hành giám sát, không Activate/Download tự động.
Bản FIFO đã khóa:
  MAIN.bLegacyMotionPublisherAllow = FALSE (mặc định; không bật)
  MAIN.bStartGlueCycle=FALSE; MAIN.bAutoDispatchEnable=FALSE; MAIN.bAutoDispatchArm=FALSE
  MAIN.bXYManualPermit=FALSE; MAIN.bDebugXYPermit=FALSE; MAIN.bEnableManualWrite=FALSE

Theo dõi:
  MAIN.bQueueSimMode / bQueueSimExposure / bQueueSimResult / bQueueSimCompleteHead
  MAIN.nQueueCount, nQueueHeadProductID, nQueueHeadState
  MAIN.rQueueHeadTravel_mm, MAIN.rQueueToWorkStart_mm
  MAIN.rQueueHeadX_mm, MAIN.rQueueHeadY_mm
  MAIN.bQueueHeadAllInside, MAIN.bQueueHeadForecastOK
  MAIN.aQueueHeadCornerNow[1..4], MAIN.fbCameraQueue.aQueue[1..8]
  MAIN.nQueueAccepted, nQueueRejected, nQueueMissed, nQueueSimCompleted, nQueueErrorCode
  MAIN.rConveyorSpeed, MAIN.nEncoderPulseSinceStart

## 4. Test CƠ BẢN BENCH (sau khi nạp & kiểm tra an toàn)
Không bật bVisionFeedVerified hoặc bVisionCalibrationOK cho mô phỏng.
- rCameraToWorkStart_mm := 300.0
- rCameraCenterX:=0.0, rCameraCenterY:=85.0, rCameraAngleDeg:=0.0.
- rProductLength:=40.0; rProductWidth:=40.0.
- bQueueSimMode := TRUE (Write Values, KHÔNG Force).
- Để Encoder đứng yên, tạo xung bQueueSimExposure: FALSE -> TRUE -> FALSE.
- Tạo xung bQueueSimResult: FALSE -> TRUE -> FALSE (trong 5 giây).
- Kết quả ngay khi camera result: nQueueCount=1, ProductID=1, HeadX=-300mm, HeadY=85mm,
  các đỉnh X = [-280,-320,-320,-280], Y=[105,105,65,65].
- Cho Encoder quay thử độc lập ở tốc độ đo được. Tất cả head corners X tăng theo cùng quãng đường.
- Sau Encoder đi thêm 300mm: HeadX=0mm; 4 góc X=[20,-20,-20,20];
  bQueueHeadAllInside phải FALSE.
- Sau Encoder đi thêm 320mm: HeadX=20mm; 4 góc X=[40,0,0,40];
  bQueueHeadAllInside TRUE. bQueueHeadForecastOK còn phụ thuộc V/chu trình dự báo.
- Khi cả 4 góc ra ngoài X>180mm: nQueueMissed tăng và nQueueCount về 0.
- Thử thêm 2 sản phẩm với mỗi cặp xung Exposure->Result hoàn chỉnh trước lúc phôi đầu ra vùng.
  nQueueCount có thể =2, mỗi ProductID và mốc Encoder riêng.
- bQueueSimCompleteHead chỉ dùng đánh dấu xong giả lập khi 4 góc đang nằm trong vùng.
  Nó KHÔNG phải M1001 và KHÔNG chứng minh robot đã chấm keo.

## 5. Những gì chưa được nối
FB_CameraProductQueue là monitor/dry-run theo Encoder, KHÔNG nối lệnh gantry thật.
fbPublisher của chương trình cũ không được đưa trực tiếp từ queue;
đường start của nó mặc định KHÓA bằng bLegacyMotionPublisherAllow=FALSE trong stage này.
Khi có yêu cầu sản xuất thực tế, cần:
- Camera bridge thật theo giao diện X mm, Y mm, AngleDeg, ImageExposure, Result;
- dữ liệu hiệu chuẩn camera->robot thực tế và lưu persistent có kiểm tra version;
- kiểm thử đủ vùng thao tác, tổng thời gian dự báo toàn bộ 4 đỉnh và giới hạn QD75;
- FSM chọn Head của FIFO, đọc trạng thái robot, chỉ Start khi READY & an toàn;
- gửi D100..D114 rồi M1000, chờ M1001 thật từ PLC-Q sau Point3, chu kỳ từng đỉnh;
- timeout, miss, overflow, khôi phục trạng thái sau Restart, xử lý stop chức năng an toàn;
- sau các bước kiểm tra mới cho phép chuyển động robot thật.

## 6. Biên kiểm chứng
XAE Build 0 Errors chỉ chứng minh cấu trúc biên dịch, không chứng minh Camera/PLC handshakes ngoài thực tế.
Các giới hạn: cần đồng bộ Exposure và Result; khi Camera có FrameID phải cập nhật để
có thể chụp song song nhiều frame đang xử lý. Hiện chỉ 1 frame pending tại một thời điểm,
nhưng có thể giữ đến 8 sản phẩm đã có kết quả trong hàng đợi.
