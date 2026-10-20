# MD200 — SƠ ĐỒ ĐẤU NỐI & THÔNG SỐ ĐÃ ĐỐI CHIẾU (19/10/2026)

Nguồn chính hãng: Inovance MD200 User Guide (EN C02)
https://www.inovance.eu/fileadmin/downloads/Manuals/EN/MD200/MD200_User_Guide_EN_C02_19012113.PDF
Crevis ST-4424 (4AO, 0..10V, 12-bit): https://www.beijerelectronics.com/en/Products/Distributed___IO/S___series___IOs/analog-output-modules/Analog___output___modules/ST-4424

## 1. Kết luận bảng tham số người dùng đưa
F0-02=1: terminal RUN/STOP — ĐÚNG.
F0-03=2: frequency from AI1 — ĐÚNG.
F4-11=0: two-wire mode 1 — ĐÚNG.
F4-00=1: DI1=Forward Run — ĐÚNG.
F4-01=2: DI2=Reverse Run — ĐÚNG nếu muốn giữ chức năng đảo chiều.
Nếu chỉ chạy thuận, DI2 PHẢI để không kích; có thể đặt F4-01=0 (no function)
CHỈ sau khi đối chiếu máy thật, không có mạch hiện hữu phụ thuộc DI2.
Trong two-wire mode 1, DI1=ON/DI2=OFF chạy thuận; DI1=OFF/DI2=ON chạy ngược;
DI1=DI2=ON hoặc OFF thì không chạy.

## 2. Sơ đồ SỬ DỤNG CHIẾT ÁP NGOÀI (ảnh người dùng)
Chiết áp (khuyến nghị loại 1..5kOhm theo hướng dẫn đúng model):
  MD200 +10V ---- [chân ngoài 1 của chiết áp]
  MD200 AI1  ---- [chân giữa / con chạy (wiper)]
  MD200 GND  ---- [chân ngoài 3 của chiết áp]
  Chỉnh chiết áp = chỉnh AI1 0..10V -> tần số MD200.
Lệnh RUN/STOP vẫn độc lập: công tắc NO DI1--COM; DI2 không kích (nếu chỉ chạy thuận).
Đấu shield đúng tiêu chuẩn, không dùng COM digital thay GND analog.

## 3. Sơ đồ DÙNG TWINCAT/CERVIS AO thay chiết áp — CHỌN PHƯƠNG ÁN NÀY
Crevis ST-4424 slot 11 (module 4AO):
  ST-4424 terminal 1 = Analog Output Channel 1 (AO1) ---> MD200 AI1
  ST-4424 terminal 5 = Analog Ground AGND/Common -------> MD200 GND
  MD200 +10V: KHÔNG ĐẤU vào AO Crevis.
  MD200 AO: là OUTPUT CỦA BIẾN TẦN, không đấu nhầm vào AO của Crevis.
  Kênh AO chưa được map trong cấu hình EtherCAT, cần chọn kênh thật đang trống.
  ST-4424 0..10V, 12-bit data trên 16-bit int (0..4095).
  Phải có field 24V và kiểm tra điện áp bằng DMM trước khi ghép AI1.
  Không sử dụng chiết áp và AO song song vào cùng AI1.

DO RUN:
  ST-2318 (slot 7,8,9 hoặc 10) kênh trống, transistor NPN sinking.
  Nguồn +24Vdc đã kiểm tra --> cuộn relay trung gian 24Vdc --> ngõ DO ST-2318 (sinking).
  Có bảo vệ cuộn relay bằng diode/module phù hợp và kiểm tra dòng cuộn.
  Tiếp điểm NO relay cách ly --> MD200 DI1 và MD200 COM.
  DI2 không kích hoặc vô hiệu hóa sau kiểm tra. KHÔNG nối COM digital vào GND analog.
  Xác nhận kiểu terminal MD200XXX vs MD200XXX-NC và DIP input mode; không phỏng đoán chân
  DO trống hoặc điện nguồn; cần chụp sơ đồ terminal tủ điện trước khi đấu.
  Mạch emergency-stop/STO/contactor độc lập là bắt buộc, FB ST không phải safety logic.

## 4. Bảng cấu hình MD200 đề nghị CHO CHẾ ĐỘ TWINCAT
F0-02 = 1           Command from terminal DI
F0-03 = 2           Main frequency from AI1
F0-07 = 00          Main source only (no auxiliary frequency combining)
F0-10 = 50.00 Hz    Max frequency reference (manual: min 50Hz)
F0-11 = 0           Source of upper limit = F0-12
F0-12 = 25.00 Hz    Running upper limit = 25Hz
F0-14 = 0.00 Hz     Lower limit = 0Hz
F4-00 = 1           DI1 = Forward RUN
F4-01 = 2           DI2 = Reverse RUN, must remain NOT activated
   Optional F4-01 = 0 when DI2 genuinely unused, after cabinet review.
F4-11 = 0           Two-wire mode 1
AI DIP = VOLTAGE (0..10V), not CURRENT (0..20mA).
F4-33              Select AI1 curve 1 (ones digit=1; verify actual setting)
F4-13 = 0.00V      AI curve 1 minimum input
F4-14 = 0.0%       Percent at min input
F4-15 = 10.00V     AI curve 1 maximum input
F4-16 = 100.0%     Percent at max input
F0-17/F0-18         Acceleration/deceleration times appropriate to mechanics.
F1 group            Motor nameplate voltage/current/frequency/rpm match actual motor.
  Không thay đổi F0/F4 trên VFD khi chưa xác nhận motor dừng và phương án an toàn.

## 5. AO volts vs 25 Hz (F0-10=50Hz, F0-12 upper limit=25Hz)
For standard linear AI curve 0V->0%, 10V->100% and F0-10=50Hz:
Frequency set  | AO analog expected | Crevis AO raw /4095
0 Hz           | 0.00 V             | 0
5 Hz           | 1.00 V             | ~410
10 Hz          | 2.00 V             | ~819
12.5 Hz        | 2.50 V             | ~1024
20 Hz          | 4.00 V             | ~1638
25 Hz          | 5.00 V             | ~2048
Above 5 V will be clipped to <=25 Hz by F0-12, if upper-limit source F0-11=0.
Do not set F0-10=25 (manual requires >=50Hz).
TwinCAT FB uses rMD200_HzAt10V=50.0, hard-clamps setpoint to 25Hz,
nMD200_AO_RawVirtual=ROUND(rRamped_Hz/50.0*4095) after ramp.
VFD panel real frequency is the acceptance test, not the virtual LREAL only.

## 6. TwinCAT thực trạng theo ADS + cấu hình offline
Solution: C:\Users\aml\Documents\DATN_MD200_VFD_STAGE_20261019\DATN.sln
FB: FB_InovanceMD200Conveyor. Build thành công LAST_BUILD_INFO=0.
Các ngõ bMD200_DO_RunVirtual, nMD200_AO_RawVirtual CHƯA LINK tới ST-2318/ST-4424.
Chỉ bật yêu cầu RUN khi:
  bMD200_CommissioningEnable
  bMD200_SafetyChainOK
  bMD200_IoHealthy
  bMD200_DriveReady
  bMD200_MappingVerified
  bMD200_VfdLimitVerified
  không có bMD200_DriveFault.
Không được set TRUE giả các trạng thái interlock này.
Tại lần kiểm tra trước output virtual = 0, RUN false. Chưa xác nhận DI/AI VFD live.
Không thay đổi runtime/PLC code, không Activate/download hoặc xuất tín hiệu vật lý qua máy từ xa.

## 7. Biên giới nghiệm thu
1. LOTO + xả DC bus theo MD200, đối chiếu nhãn biến tần chính xác (MD200XXX hay MD200XXX-NC).
2. Xác minh cable terminal AI1/GND và DI1/COM, DI2 không được nối nhầm.
3. Xác minh channel Crevis DO/AO chưa được sử dụng và mapping PDO,
   IO EtherCAT OP/WcState; thao tác map cần phê duyệt.
4. Cài giới hạn F0-12=25Hz tại VFD, AI input voltage và đường cong.
5. Thử RUN relay và điện áp AO với drive/tải ở trạng thái cô lập an toàn, đo bằng DMM.
6. Chạy thử tốc độ thấp khi người vận hành có mặt, xác nhận motor, chiều quay,
   tần số hiển thị, fault feedback, fail safe khi mất PLC và emergency stop.
7. Không kết nối automatic băng tải/gantry/tracking trước khi nghiệm thu toàn bộ.

Ghi chú: Với hệ thống Camera + Conveyor + Gantry, TwinCAT phải sử dụng
vận tốc băng tải đo bằng Encoder (mm/s) cho Tracking. Tần số motor Hz chỉ đặt
tốc độ; không tự suy ra tốc độ tuyến tính nếu chưa hiệu chuẩn rulô và tỉ số truyền.

## 8. Rà soát FB ngày 19/10/2026 — sửa chống tự khởi động lại
- FB_InovanceMD200Conveyor đã được sửa trên bản sao và biên dịch lại, LAST_BUILD_INFO=0.
- Thêm fbRunRise, bRunLowSeen, bRunGranted, nStatus=7.
- Nếu RUN đang TRUE từ trước khi PLC khởi động, FB không tự chấp nhận lệnh; cần nhìn thấy RUN=FALSE rồi mới nhận xung FALSE->TRUE sau khi đủ mọi điều kiện.
- Nếu mất safety chain, trạng thái I/O, DriveReady, có DriveFault, thiếu điều kiện mapping/giới hạn hoặc tham số không hợp lệ: bRunGranted bị xóa; sau khi phục hồi phải nhả RUN và yêu cầu mới.
- Nếu đặt tần số thấp hơn 0.05Hz, FB hủy quyền RUN; tránh sau đó tăng setpoint mà động cơ tự khởi động.
- nStatus=7 có nghĩa WAIT NEW RUN EDGE; nStatus=1 mới là RUN logic (virtual).
- Chưa nghiệm thu kiểm thử động lực hoặc IO vật lý. Không có đường %Q nối với DO/AO và chưa xác nhận cặp DI1/AI1 trên biến tần thật.
- Không được đặt các cờ interlock TRUE giả. Chỉ bàn giao để review và chạy dry-run khi người vận hành có giám sát.
