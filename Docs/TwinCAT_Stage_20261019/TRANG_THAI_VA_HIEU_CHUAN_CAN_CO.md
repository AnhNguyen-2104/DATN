# CAMERA UPSTREAM -> TWINCAT -> MITSUBISHI: BẢN STAGED, CHƯA NẠP / CHƯA GỬI PLC
Thời điểm build: 2026-10-16 15:36:34 +07:00, LAST_BUILD_INFO=0.
Project: C:\Users\aml\Documents\DATN_CAMERA_TRACK_MANUAL_ACK_STAGE_20261016\DATN.sln
Source mới: PLC1\POUs\FB_UpstreamCameraTracking4Pt.TcPOU và FB_XYTrackingManualAck.TcPOU.
Không sửa live repo C:\Users\aml\Desktop\DATN_TwinCat3.
Không thay đổi chương trình PLC-Q, không Download/Activate vào IPC.

## Mục tiêu dữ liệu
Camera cung cấp CenterX, CenterY, Theta_deg (THÔNG SỐ TỌA ĐỘ PHẢI TÍNH THEO mm).
Đồng bộ rEncoderAtExposure_mm tại thời điểm sensor/camera phát xung exposure.
Khi Camera trả kết quả, yêu cầu đã có pulse exposure tương ứng trước đó (<5 s).
Nếu bVisionFeedVerified=FALSE hay bVisionCalibrationOK=FALSE, KHÔNG ghi D100.
Dùng chuyển hệ rigid 2D:
  X0=TX+cos(phi)*Xcam - sin(phi)*Ycam;
  Y0=TY+sin(phi)*Xcam + cos(phi)*Ycam;
  thetaRobot = thetaCam + phi.
Tính bốn đỉnh +/-20mm theo góc thetaRobot, rồi:
  Xi(t) = Xi(camera exposure) + [encoder_total(t)-encoder_at_exposure]
  Yi(t) = Yi(camera exposure)
  Xi_target = Xi(t) + v_encoder_filtered(t) * 0.120s
Chỉ gửi khi Xi_target thuộc [0,180]mm, Yi thuộc [0,170]mm.
Cổng TCP 5010, word D100..D103 X/Y DINT*10000, D114 index UINT.

## Chế độ xác nhận THỦ CÔNG: chỉ thử truyền dữ liệu
- Cần chọn một bit ACK riêng trong GX Works2 mà PLC/HMI chưa dùng. Đề xuất M2100 CHỈ LÀ GIẢ THUYẾT chưa qua Cross Reference; phải xác minh rồi mới đặt bXYManualAckBitAudited=TRUE.
- KHÔNG ghi M1000 (robot start), KHÔNG Force M1001 (true gantry-complete bit PLC-Q tự sinh).
- TwinCAT tự đọc M1000/1001, M102..105 và D200 trước mỗi đỉnh, tất cả phải không hoạt động.
- Gửi X/Y/index, đọc lại chính 5 word, chờ M2100 ON do người vận hành tạo trong PLC, sau đó chờ M2100 OFF mới cho phép đỉnh tiếp theo.
- Khi băng tải đứng yên, sau ACK giữ hiển thị tối thiểu 2s. Khi băng tải di chuyển, không được áp đặt giữ 2s vì vật ra khỏi vùng X quá nhanh.
- Sau 4 ACK, khôi phục X/Y/index trước bài test; chỉ báo bXYManualDone khi đọc lại khớp.
- Trong mọi lỗi, KHÔNG Start robot; nếu lỗi giữa chu kỳ, phải xác minh D100..D103/D114 và xử lý tọa độ thử còn tồn trước khi vận hành QD75.
- M2100 là bit thử nghiệm, tuyệt đối không xem là bằng chứng Z đã rút hoặc robot đã hoàn tất.
- Không chạy hai FB debug cùng lúc; trước khi chạy chế độ mới cần bDebugXYPermit=FALSE (thực tế IPC hiện đang TRUE).

## Những dữ liệu vật lý CHƯA CÓ để nghiệm thu và phát lệnh:
1. Camera trả tâm X,Y theo mm hay pixel? Nếu pixel, phải dùng hiệu chuẩn homography/pixel-mm trước FB.
2. Ba thông số transform đã đo: rCamToRobotX_mm, rCamToRobotY_mm, rCamToRobotYaw_deg. Không tự cho giá trị 0.
3. Cách đồng bộ Encoder tại thời điểm image exposure (hardware trigger/photocell/software strobe).
4. Camera bridge thực tế ghi CenterX/CenterY/Theta mới vào MAIN, tạo bCameraExposeTrigger và bCameraTrigger. Hiện mới có biến giả lập, CHƯA CÓ CAMERA BRIDGE.
5. Cross Reference bit M2100 (hay M bit khác): phải chưa được Ladder/HMI/QD75 sử dụng.
6. Đồng thời phải xác minh Servo, robot, interlock và vùng làm việc cho giai đoạn chạy băng tải.

## Phép tính tham khảo CHỈ LÀ VÍ DỤ
Camera Center=(50,85)mm, theta=0, Camera->Robot TX=-600mm, TY=0, phi=0.
Khi Encoder đi 600mm sau Camera chụp, vận tốc=88mm/s, t_fly=120ms:
Đỉnh 1: X_target=80.56mm, Y=105mm -> D100=805600, D102=1050000, D114=1.
Đỉnh 2: X_target=40.56mm, Y=105mm -> D100=405600, D102=1050000, D114=2.
Đỉnh 3: X_target=40.56mm, Y=65mm -> D100=405600, D102=650000, D114=3.
Đỉnh 4: X_target=80.56mm, Y=65mm -> D100=805600, D102=650000, D114=4.
Thực tế phải tính lại Xi_target ngay trước MỖI đỉnh; KHÔNG dùng cùng snapshot khi băng tải chạy.
Giá trị -600mm ví dụ không phải đo đạc máy thực.

## Phân biệt DONE
bXYManualDone: 4 x X/Y readback + 4 lần người vận hành xác nhận bit thử + khôi phục dữ liệu. KHÔNG có chuyển động robot.
bGlueCycleDone: chỉ từ bộ điều khiển chuyển động thực M1001 của PLC-Q sau khi QD75 hoàn thành và rút Z an toàn.
M1001 thuộc PLC-Q; không Force M1001 để giả vờ hoàn thành robot.

## Điểm kiểm tra hiện tại
Runtime đang Run với bản XY Debug cũ: bDebugXYDone=TRUE, nDebugXYPassed=4,
bDebugXYPermit=TRUE, Servo Jog=FALSE, ConveyorSpeed=0, bStartGlueCycle=FALSE,
bPlcQConnected=TRUE. Các symbol mới bVisionFeedVerified chưa xuất hiện trên Runtime vì KHÔNG NẠP.

## Cách nghiệm thu tiếp theo
Chỉ sau khi đáp ứng các dữ liệu hiệu chuẩn, bit M audit và camera bridge:
- Test toán học với góc 0 / 30 / -45 và quãng trôi 0 / 100 / 600, kiểm tra output thủ công.
- Test MC Protocol XYZ READ-ONLY trước khi gửi dữ liệu; verify no M1000.
- Chỉ tải chương trình từ TcXaeShell trong cửa sổ bảo trì có giám sát.
- Theo dõi FB_XYTrackingManualAck.nVertex, nAckedVertices, bReadbackOK, bRestored,
  và GX Works2 D100, D102, D114; người vận hành bật/tắt M bit đã được duyệt.
