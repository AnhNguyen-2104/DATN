# QUY TRINH CHUAN: CAMERA UPSTREAM -> FIFO -> ENCODER -> QD75 GANTRY 4 DINH

Ngay kiem tra: 2026-10-16. Day la THIET KE QUY TRINH va ke hoach sua, CHUA PHAI CHUONG TRINH ROBOT DA NGHIEM THU.
Tai lieu tham chieu: D:\VC\Session2_Contract_For_Session1.md (uu tien dia chi M1000/M1001) va D:\VC\Glue_Tracking_System_Specs.md.
Pham vi: DATN TwinCAT 3 AML-IPC -> Mitsubishi Q06UDEH + QD75D4.
D:/VC/Glue_Tracking_System_Specs.md con nhieu cho ghi M100/M101 lich su, KHONG su dung: M100/M101 thuoc Homing.

## 1. CAU TRUC DUNG
Camera cua nguoi dung chi tra ve (CenterX, CenterY, AngleDeg). KHONG tra ve tung dinh.
Camera dat TRUOC vung Robot theo chieu bang tai. rCameraToWorkStart_mm = 300 mm hien la gia tri nguoi dung nhap, CHUA xac thuc hieu chuan thuc te.
Khi vat den duoc xu ly:
1. Tai thoi diem exposure, luu encoder lien tuc, gan ma san pham.
2. Khi ket qua Camera ve (Xc, Yc, theta), ghep dung ID voi moc exposure. Kiem tra phan mem Camera da quy pixel->mm.
3. Bien doi toa do Camera sang toa do Robot, tinh 4 dinh cua phoi 40x40 theo theta.
4. Them san pham vao FIFO co gioi han suc chua (queue capacity cau hinh theo throughput), moi item doc lap:
  ProductID, valid, encoderAtExposure, XcenterRobotAtExposure, YcenterRobotAtExposure, thetaRobot,
  aBaseCorner[1..4], state, vertexIndex, queueArrivalTime, errorCode.
5. PLC cycle 10 ms cap nhat TOA DO cua moi san pham dang cho dua theo:
  deltaMM = encoderIntegratedNow - encoderAtExposure
  X_i_now = X_i_atExposure + deltaMM
  Y_i_now = Y_i_atExposure
  Khong dung nEncoderPulse UINT 16-bit truc tiep cho quang duong dai vi wrap 65535.
6. Khi vat chua vao robot: queue state WAIT_ROBOT. Khong gui robot START.
7. Du doan toa do khi dau phun thuc su tiep can: XiTarget=XiNow+Vconveyor*tFly; tinh lai cho tung dinh.
8. Xac minh toi thieu duong di 3 point cua TOAN BO 4 dinh nam trong X=0..180, Y=0..170, Z=0..52, tinh den DeltaXSync, quang duong trượt va thoi gian chu trinh co bien an toan. Khong chi dung 1 bit bInRobotWorkspace (co it nhat 1 goc vao); no chua duoc dung de start.
9. Neu robot ready (home, all safety, QD75 idle, no alarm), ngan sach thoi gian con du, san pham valid:
   lay phan tu FIFO dau tien lam active, cac san pham khac van tiep tuc cap nhat theo encoder.
10. Cho moi dinh 1..4: kiem tra bien X/Y/Z/path va trang thai PLC; tinh lai XiTarget;
   gui D100..D114, doc lai neu co the; SAU DO moi kich M1000.
   M1000 duoc giu ON den khi PLC-Q tu dat M1001=ON sau Point3 va Z da rut.
   TwinCAT ha M1000=OFF, cho M1001=OFF & M103=OFF va M102=OFF roi tinh/gửi dinh tiep theo.
   Khong cap nhat D100..D114 khi M1000 ON va dang cho M1001.
11. Hoan tat 4 lan ACK THAT: danh dau ProductID DONE, luu ket qua, loai khoi queue, nhan ProductID tiep theo.
12. Neu vat da qua khoi vung truoc khi hoan tat du bao: MISSED (khong tu dong kick robot), canh bao va xu ly reject.
13. Khi mat encoder/Camera/TCP/QD75 alarm, queue full, mat dong bo capture: STOP DISPATCH, khong tu khoi dong lai; yeu cau nguoi van hanh danh gia.
14. Emergency stop phai dung lien dong phan cung/safety PLC; M105/MC TCP KHONG PHAI emergency stop safety-rated.

## 2. TRANG THAI FIFO DE HIEN THI
IDLE -> CAPTURED -> WAIT_ROBOT -> READY -> PROCESSING_1 -> PROCESSING_2 ->
PROCESSING_3 -> PROCESSING_4 -> DONE -> POP
Phan nhanh: INVALID_CAPTURE, QUEUE_FULL, MISSED, FAULT, ABORTED.
HMI/Watch: nQueueCount, nCurrentProductID, aQueue[...].state, rToRobot_mm, activeVertex,
aActiveCorners[1..4], nProcessed, nMissed, nRejected, bAutoReady, bAutoPermit, bRobotSafe.
Quan trong: aCornerBase va aCornerNow CU (FB_ConveyorTracking4Pt) la he bien legacy.
Khong dung chung de xem queue/robot thuc te. Khi khong co active product, hien thi active corners=0
hoac -- tren HMI; danh sach historical LastProductCorners hien thi rieng de debug.

## 3. HIEN TRANG DUOC DOC TU SOURCE & RUNTIME, CHUA DUNG QUY TRINH
- MAIN van goi fbTracking4Pt legacy nhan bCameraTrigger, xuat bProductValid va aCornerNow.
- fbPublisher vao bProductValid := bProductValid CU, aCornerNow := aCornerNow CU.
- MAIN bAutoPendingProduct chi la mot flag, tonAutoProductTTL = 750ms, khong co FIFO nhieu vat.
- Camera upstream fbUpstreamTrack moi co bCaptured (duoc TOI DA 1 phoi), chua phai queue.
- bVisionFeedVerified=false, bVisionCalibrationOK=false; chua noi bridge Camera thuc.
- bAutoDispatchEnable=false, bAutoDispatchArm=false, bStartGlueCycle=false;
  khong co lenh START robot tu dong hien tai.
- fbPublisher dang co logic M1000/M1001 chuyen dong that. Khong thu bang cach Force M1000/M1001.
- Sau khi legacy bProductValid false o X>180, aCornerNow giu toa do cu thay vi ve 0; day la stale display.

## 4. KIEM TRA THOI GIAN
D=300mm, V=90.53mm/s -> thoi gian trung binh den robot 300/90.53=3.31s
(ket qua thuc te con phu thuoc Xcam, chieu san pham va vi tri quan sat).
750ms TTL hien tai chi cho phep di ~67.9mm, nen auto flag mat truoc khi den robot.
Voi phoi 40mm (theta=0), de TOAN BO 4 dinh nam trong X=0..180, tam can trong [20,160].
Tai V=90.53mm/s, khoang thoi gian hinh hoc nay la 140/90.53=1.55s.
Yeu cau chu trinh <=1.2s la CHAT, can du tru fly time, sync 60ms, TCP latency, homing,
safe-Z, gia toc, so dinh can hoan thanh. Khong mac dinh du thoi gian chi vi 1 dinh vua vao.
Gia su Camera Xc=0mm, Yc=85mm, theta=0, D=300mm, offset=0, yaw=0:
tai exposure XcenterRobot=-300, 4 dinh X=[-280,-320,-320,-280];
Encoder +300mm -> XcenterRobot=0, 4 dinh X=[20,-20,-20,20] (CHUA du 4 dinh vao vung);
Encoder +320mm -> XcenterRobot=20, 4 dinh X=[40,0,0,40];
do la dieu kien hinh hoc co ban, KHONG tu dong cho phep chay robot.

## 5. GIAO TIEP PLC: HOP DONG HIEN HANH
TwinCAT -> PLC: D100-D101 X, D102-D103 Y, D104-D105 Zdown,
D106-D109 speeds, D110-D111 DeltaXSync, D112-D113 Zsafe, D114 vertex.
DINT x10000 mm -> 0.1um. Speed mm/s x6000 -> 0.01 mm/min.
M1000 Start (THAT): TwinCAT ghi, co the lam QD75 xuat chuyen dong / keo Y20.
M1001 Ack (THAT): PLC-Q tu tao sau khi da ket thuc Point3 va Z rut len Safe.
M102 RobotBusy, M103 InPosition, M104 Alarm, M105 Reset.
Khong dung M100/M101 (dang thuoc Homing).
KHONG phep gom M2100 ACK THU voi M1001. M2100 la de xuat chua audit.
Khi test bang ban tay chi doc bit ACK thu, tuyet doi khong chay QD75.

## 6. KE HOACH SUA CODE TACH RO TUNG BƯOC
A. Chot giao dien Camera Result: ProductID/FrameID, CenterX/Y physical mm, ThetaDeg,
   ExposureEncoderTimestamp+EncoderAtExposure, trigger edge; doi chieu dau vao.
B. Them ST_TrackedProduct va FB_ProductQueue/Tracker FIFO N item; co overflow handling;
   doc lap moi product va cap nhat theo encoder tich phan.
C. Them FB_WorkspaceAdmission: xac minh dieu kien du doan TOAN BO chu trinh,
   mo hinh do tre thuc te va gioi han hanh trinh.
D. Tich hop fbPublisher voi ACTIVE FIFO item, thay bProductValid/aCornerNow CU.
   Bao ve exclusive owner socket, qua tat ca interlock; start mac dinh DISABLED.
E. Trang thai DONE/FAULT la per-ProductID; giai phong item, dung queue tiep theo.
F. Tach variables GUI: aActiveCornerNow (0 khi khong active) va last/history debug.
G. Tinh thong so calib LREAL mm (D=300 to user), luu persistent co version/valid flag,
   khong cho phep nhap 0 ma tu coi calibration OK.

## 7. THU TU TEST
1. TEST OFFLINE ST co camera gia lap + Encoder gia lap, bo dem 65535 wrap,
   1..N phoi lien tiep, queuing FIFO, ROI enter/exit, timeout/miss, reverse/stop/too fast.
2. TEST REAL ENCODER READ-ONLY tren AML-IPC; khong gan vao Robot, kiem tra sai so mm/pulse.
3. TEST REAL CAMERA/OFFLINE ROBOT: exposure timestamp, center angle, calibration,
   queue nQueueCount va active corner updates trong watch, KHONG M1000.
4. TEST MC D100-D103/D114 chi khi gantry co lap an toan va duoc phep,
   readback tren GX Works2, khong M1000, ACK thu dung M bit auditted khong trung.
5. TEST QD75 static/dry run duoi su giam sat, valv e ra an toan,
   xac thuc M1001 D200 D120, gioi han hanh trinh, stop.
6. TEST băng tai dong + 1 phoi, roi moi N phoi, đo latency va tinh ty le MISSED.
   Khong tu dong enable robot truoc khi qua cac gate.

## 8. CHO PHE DUYET DÀNH CHO NGUOI DUNG
- Mau tin Camera: X/Y la mm hay pixel; co ProductID/FrameID khong? how trigger/exposure signal?
- D=300mm da do va hieu chuan co khi hay gia lap?
- Robot homing, Zdown, Zsafe, QD75 PLC program va M1001 co nghiem thu chua?
- Phoi co thuc su nhieu chiec lien tiep hay test don phoi truoc?
- M bit rieng de test ACK duoc Cross Reference chua?
