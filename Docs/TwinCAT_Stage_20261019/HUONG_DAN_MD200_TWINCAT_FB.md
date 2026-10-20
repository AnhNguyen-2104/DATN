# FB MD200 TwinCAT 3 — băng tải 0…25 Hz (chưa nối chân thật)
Kiểm tra 2026-10-19 trên AML-IPC. BUILD offline: LAST_BUILD_INFO=0.

## Nơi lưu
Solution độc lập: C:\Users\aml\Documents\DATN_MD200_VFD_STAGE_20261019\DATN.sln
POU: PLC1/POUs/FB_InovanceMD200Conveyor.TcPOU
MAIN[STAGE] có instance fbMD200_VFD gọi mỗi PLC scan.
Mọi bMD200_DO_RunVirtual, nMD200_AO_RawVirtual ở MAIN hiện CHƯA được liên kết %Q thật.
KHÔNG phải chương trình đang nạp/runtime; không Activate Configuration / Login/Download từ xa.

## Phần cứng EtherCAT phát hiện từ DATN.tsproj
Crevis NA-9286:
Module 7-10: ST-2318 DO negative logic / NPN sink, 8 kênh/module.
Module 11: ST-4424 AO 0…10 V, bốn kênh, 12-bit, mã 0x0000..0x0FFF (0..4095).
Các kênh vật lý mới vẫn CHƯA xác định trống/đang sử dụng; chỉ chọn sau khi đo và xác nhận sơ đồ điện tủ.
KHÔNG tự nối vào GVL.aOut[] vì GVL.aOut hiện là dữ liệu riêng ST-5114 Encoder.

## Cách đấu tham khảo, cần kỹ thuật viên điện kiểm tra và làm khi cách ly năng lượng
MD200 DI1 là ngõ vào số lệnh chạy thuận, DI1/COM active LOW.
TwinCAT PLC không tự có chân dây; dùng một channel DO của ST-2318.
ST-2318 là transistor sink NPN, KHÔNG phải dry contact. Phương án khuyến nghị:
DO NPN (chọn kênh trống) -> module relay giao tiếp có diode dập ngược / đầu vào phù hợp,
tiếp điểm relay NO cách ly -> MD200 DI1--COM.
Không đấu DO source/sink tùy tiện vào DI1 nếu chưa xác minh cực tính, nguồn COM, cách ly.
MD200 AI voltage + GND: ST-4424 AO CHx -> AI; ST-4424 AGND -> MD200 GND analog.
MD200 +10V là nguồn tham chiếu cấp biến trở, KHÔNG nối tới AO PLC.
Đi dây analog shield, cách đường động lực, nối mass/chống nhiễu đúng hướng dẫn.
Khi mất điện/PLC crash, cần thiết kế phần cứng bảo đảm RUN OFF, ngõ analog về 0
và cơ cấu dừng đạt yêu cầu; E-stop/STO hoặc contactor an toàn là hệ riêng, FB KHÔNG thay thế.

## Tham số MD200 cần kỹ thuật viên xác nhận trên màn hình biến tần
F0-02 = 1: lệnh RUN/STOP từ terminal I/O.
F0-03 = 2: nguồn tần số từ AI analog.
F4-00 = 1: DI1 là lệnh chạy thuận (xác nhận với đúng MD200 submodel).
F4-11 = 0: điều khiển hai dây mode 1; DI1-COM đóng = chạy; mở = dừng.
F0-10 thường tối thiểu 50 Hz, KHÔNG giả định có thể chỉnh xuống 25 Hz.
F0-12 = 25.00 Hz: giới hạn tần số tham chiếu phía VFD.
F0-14 = 0.00 Hz: giới hạn dưới tham khảo.
F0-17/18: thời gian tăng/giảm tốc của VFD phù hợp cơ khí và tải.
Xác nhận DIP AI là Voltage 0-10V (không phải 0-20mA).
Xác nhận F4-13..F4-18 / AI curve: 0-10V tương ứng tần số Hz bao nhiêu.
Xác nhận bộ thông số định mức motor và giới hạn dòng, các lỗi VFD trước khi chạy.
Chỉ bật biến bMD200_VfdLimitVerified khi đo/ghi nhận thông số F0-12 thật và AI reference.

## Hệ số mặc định / ví dụ khi MD200 0-10V -> 0-50Hz
rMD200_HzAt10V = 50.0; rMD200_SetFrequency_Hz = 0..25;
nMD200_AO_RawVirtual = ROUND((rMD200_Ramped_Hz / 50.0) * 4095).
Lệnh (Hz) | AO Volt | Raw 12-bit (sau khi ramp ổn định)
0         | 0.00    | 0
5         | 1.00    | 410
10        | 2.00    | 819
12.5      | 2.50    | 1024
20        | 4.00    | 1638
25        | 5.00    | 2048
Nếu đã hiệu chuẩn 0-10V tương đương 0-25 Hz, rMD200_HzAt10V=25 và 25Hz->10V->4095.
Giữ F0-12 giới hạn tối đa 25.00Hz tại biến tần trong mọi trường hợp.
Kiểm tra điện áp ngõ ra thực bằng đồng hồ khi ngắt kết nối AI MD200, theo quy trình an toàn.

## Biến Watch MAIN (chỉ sau khi người dùng tự nạp stage có giám sát)
bMD200_CommissioningEnable = FALSE mặc định.
bMD200_SafetyChainOK / bMD200_IoHealthy / bMD200_DriveReady / bMD200_MappingVerified /
bMD200_VfdLimitVerified = FALSE mặc định.
bMD200_DriveFault = FALSE (khi VFD lỗi đặt TRUE).
bMD200_RunRequest = FALSE; rMD200_SetFrequency_Hz = 0 mặc định.
rMD200_HzAt10V = 50; rMD200_RampUp_Hz_s = 2; rMD200_RampDown_Hz_s = 4.
bMD200_DO_RunVirtual = FALSE; nMD200_AO_RawVirtual = 0 lúc khởi động.
rMD200_Ramped_Hz, rMD200_AO_VoltsVirtual là số hiển thị nội bộ.

Status:
0 stopped, 1 running, 2 interlock missing, 3 VFD fault, 4 mapping/drive-frequency limit not verified,
5 invalid parameters, 6 drive not ready.
FB sẽ giảm tần số tham chiếu theo rate khi thay đổi setpoint trong khi RUN.
Khi STOP hoặc lỗi, RUN FALSE, AO 0; giảm tốc cơ thực theo MD200 F0-18.
Bộ điều khiển phải có independent hardware safety chain, test interlock/dừng khẩn theo quy trình.

## Thử nghiệm giai đoạn
1. Kiểm tra code và Build offline: DONE (0 compile errors). Không tác động field I/O.
2. Giữ không dây kết nối biến tần, test offline MAIN logic và hiển thị outputs virtual.
3. Kiểm tra kênh DO/AO đang trống, đối chiếu process map và sơ đồ terminal đã nối thực tế.
4. Thử ngõ AO và relay với động lực cô lập, đo 0/1/2/4/5 V tương ứng 0/5/10/20/25 Hz nếu 50Hz/10V.
5. Cấu hình và xác nhận MD200 theo sách đúng submodel, motor cách ly tải.
6. Chỉ lúc nghiệm thu tại tủ mới map logic RUN/AO vào PLC %Q đúng channel; fail safe 0-output,
   có watchdog mất PLC/fieldbus và hardwired E-stop/feedback.
7. Lần chạy thử dưới sự giám sát người vận hành; xác nhận max thực <=25Hz và băng tải an toàn.

## Phân biệt hệ thống hiện tại
- EziServo trục và tracking Camera→FIFO chưa đổi. Encoder Crevis ST-5114 vẫn chạy theo cấu hình cũ.
- Không có kết nối VFD mới với QD75/Robot; không đổi M1000, M1001, D100..D114, Y20.
- Encoder rConveyorSpeed là vận tốc đo mm/s; Hz biến tần không chuyển thành mm/s tuyến tính nếu chưa biết
  tỷ số truyền, đường kính rulô và trượt, cần hiệu chuẩn từ Encoder khi thử.
