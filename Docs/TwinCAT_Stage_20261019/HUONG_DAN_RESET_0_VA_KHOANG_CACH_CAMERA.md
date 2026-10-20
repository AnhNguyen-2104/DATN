# DATN — Reset Tracking về 0 khi PLC Runtime khởi động và chỉnh Camera → Robot bằng mm

**Trạng thái:** Mã đã biên dịch trong bản sao, KHÔNG nạp vào TwinCAT Runtime đang điều khiển máy.
**Dự án mới:** C:\Users\aml\Documents\DATN_CAMERA_DISTANCE_ZERO_STAGE_20261016\DATN.sln

## Phạm vi Reset
- Sau khởi động PLC Runtime/reset chương trình, các biến xử lý Tracking, cờ sản phẩm, đếm sản phẩm, tọa độ góc, tâm, quãng drift trở về 0 theo khối FB_UpstreamCameraTracking4Pt.
- MAIN.rCameraCenterX / rCameraCenterY / rCameraAngleDeg mặc định 0 khi chưa có ảnh mới.
- MAIN.nEncoderPulseSinceStart := 0 ban đầu; sau đó tích phân nEncoderDelta để hiển thị xung tương đối từ thời điểm PLC chạy.
- MAIN.rConveyorDist := 0 ban đầu; sau đó tiếp tục cộng dồn theo Encoder.
- MAIN.nEncoderPulse (counter thô ST-5114), MAIN.rServo_ActPos_Deg (tọa độ vị trí NC thực tế), MAIN.rServo_ActVel_Deg_s (vận tốc thực tế) KHÔNG sửa, không ẩn các giá trị vật lý. Có thể không bằng 0 lúc Runtime khởi động.
- Các thông số cài đặt như rProductLength=40, rProductWidth=40, tốc độ Jog, giới hạn Robot KHÔNG bị đặt bằng 0 vì là cấu hình an toàn.
- Mở TcXaeShell (phần mềm IDE) KHÔNG có nghĩa PLC Runtime đã khởi động lại.
- Runtime biến nEncoderPulseSinceStart không phải hardware homing; không có chuyển động robot hay ghi vào QD75.

## Tham số mới chỉnh sau calib
Trong MAIN [Online], tham số mới:
  rCameraToWorkStart_mm : LREAL := 0.0

Đây là khoảng cách dương đo DỌC THEO chiều chuyển động băng tải, từ vị trí chụp/trigger Camera (gốc hệ Camera sau calib) đến BIÊN TRÁI vùng làm việc Robot X=0. Ví dụ 600.0 mm.
Khi để bằng 0 hoặc lớn hơn 100000, khối từ chối dữ liệu Camera; không tự giả định đã hiệu chuẩn.

Để tránh double offset, các giá trị:
  rCamToRobotX_mm : LREAL := 0.0  // phần HIỆU CHỈNH NHỎ thêm sau khi đã nhập khoảng cách, KHÔNG điền -600 lần nữa
  rCamToRobotY_mm : LREAL := 0.0  // dịch Y theo phép căn chỉnh Camera/Robot
  rCamToRobotYaw_deg : LREAL := 0.0  // góc lệch trục Camera và Robot

Công thức:
  X_robot_at_capture = -rCameraToWorkStart_mm + rCamToRobotX_mm
                       + Xcam*cos(yaw) - Ycam*sin(yaw)
  Y_robot_at_capture = rCamToRobotY_mm
                       + Xcam*sin(yaw) + Ycam*cos(yaw)
  Theta_robot = Theta_cam + yaw
  X_vertex_i(t) = X_vertex_i_at_capture + rConveyorDist(t) - rEncoderAtExposure_mm
  Y_vertex_i(t) = Y_vertex_i_at_capture

Tâm hiện tại:
  MAIN.rUpstreamNowX
  MAIN.rUpstreamNowY
Khoảng còn lại đến đầu vùng Robot:
  MAIN.rToRobotWorkStart_mm = MAX(0, -MAIN.rUpstreamNowX)

Encoder được chốt ở XUNG CHỤP ẢNH thực sự MAIN.bCameraExposeTrigger;
sau khi camera trả tâm/góc và kích MAIN.bCameraTrigger mới đóng dấu sản phẩm.
Không lấy Encoder lúc nhận kết quả xử lý ảnh vì độ trễ xử lý làm sai X.

Camera cần gửi tọa độ vật lý (mm), không phải pixel. Với pixel, phải có khối calib chuyển pixel sang mm trước khi ghi MAIN.rCameraCenterX/Y.
MAIN.bVisionFeedVerified và MAIN.bVisionCalibrationOK mặc định FALSE; chỉ được xác nhận khi kết nối Camera, calib và timestamp đã được nghiệm thu.
Hiện chưa có bridge Camera thật; nhập TRUE để giả làm kết nối Camera không chứng minh chạy thật.

## Test toán học OFFLINE, không tải PLC
Ví dụ: rCameraToWorkStart_mm = 600, rCamToRobotX_mm = 0,
camera center=(50,85)mm, yaw=0, theta=0, kích thước 40x40mm.
Ở exposure (encoder travel=0):
 center X=-550; corners X=[-530,-570,-570,-530]; Y=[105,105,65,65].
Khi travel=500 mm:
 center X=-50; corners X=[-30,-70,-70,-30].
Khi travel=600 mm:
 center X=50; corners X=[70,30,30,70].
Khi travel=800 mm:
 center X=250; mọi điểm đã ra vùng X<=180.

Không được ghi các tọa độ giả định này xuống D100-D103/D114 khi PLC-Q còn có khả năng thực hiện robot start.
M2100 chỉ là địa chỉ đề xuất ACK riêng trong FB_XYTrackingManualAck, chưa được GX Works2 Cross Reference xác nhận.
M1000 có thể làm QD75 chạy thật; M1001 là xác nhận robot từ PLC-Q, không bật bằng tay làm ACK thử.

## Cần trước khi test LIVE
1. Máy trong chế độ bảo trì, Servo/Gantry được cách ly an toàn và không có lệnh QD75 đang thực hiện.
2. Khai báo cách Camera cấp X,Y,theta mm & tín hiệu exposure, timestamp. Nếu chưa có, chỉ test offline.
3. Xác nhận khoảng cách Camera → Robot trên cơ khí và góc lệch hệ tọa độ.
4. Dùng bản sao, không ghi đè project gốc. Xem mapping EtherCAT và chỉ Activate Configuration/Login thủ công có giám sát.
5. Xác nhận bDebugXYPermit và bXYManualPermit đều FALSE khi chưa test MC. Không kích M1000.
6. Để lưu tham số calib qua khởi động lại: sau khi đo đạc cần cấu hình PERSISTENT/RETAIN trong PLC hoặc lưu trong cấu hình HMI/recipe. Write Values đơn thuần KHÔNG bảo đảm lưu vĩnh viễn.
