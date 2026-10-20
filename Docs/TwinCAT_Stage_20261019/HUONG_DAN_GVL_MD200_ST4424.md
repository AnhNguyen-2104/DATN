# ST-4424 AO1 → GVL_MD200_IO.nAO1_Raw — đã AutoLink OFFLINE
Ngày 2026-10-19, máy AML-IPC.

## Trạng thái xác minh
Dự án độc lập: C:\Users\aml\Documents\DATN_MD200_IO_TAG_STAGE_20261019\DATN.sln
Backup/bản nguồn trước: C:\Users\aml\Documents\DATN_MD200_VFD_STAGE_20261019
Tập tin: DATN\PLC1\GVLs\GVL_MD200_IO.TcGVL
PLC1.plcproj đã đăng ký GVL mới.
MAIN.TcPOU đã nối kết quả FB → tag process-image GVL:
  GVL_MD200_IO.nAO1_Raw := INT_TO_UINT(nMD200_AO_RawVirtual); // giới hạn 0..4095
  GVL_MD200_IO.bRUN_DO := bMD200_DO_RunVirtual;
Hai lệnh assignment nằm SAU lời gọi fbMD200_VFD.

AO1 đã gán AutoLink offline thành công: file DATN.tsproj lưu:
  <Link VarA="PlcTask Outputs^GVL_MD200_IO.nAO1_Raw"
        VarB="Module 11 (ST-4424)^Output(s)^Analog Output Ch#1 Byte"
        AutoLink="true"/>
Đây là module Crevis NA-9286 ở Device 2 EtherCAT, Slot 11 ST-4424 AO1.
TAG: GVL_MD200_IO.nAO1_Raw AT %Q* : UINT (raw 16-bit chứa code 12bit 0..4095).
HARDWARE PIN: terminal 1 AO1 → MD200 AI1; terminal 5 COM analog → MD200 GND.
CẤM nối MD200 +10V vào ST-4424 AO.
Build 13:18:47: LAST_BUILD_INFO=0, GVL tags đã có trong PLC1.tmc.

## Tín hiệu RUN digital
Đã tạo GVL_MD200_IO.bRUN_DO AT %Q* : BOOL và assignment từ FB,
NHƯNG CHƯA GÁN DO vật lý vào ST-2318 vì chưa biết chính xác module slot/kênh
trống và loại NPN input của MD200 trong tủ. Mapping nhầm có thể chạy tải.
KHÔNG có Link VarA=...bRUN_DO trong tsproj.

## Trạng thái runtime
Bản chương trình đang RUN trên AML-IPC là bản CŨ, chưa có GVL_MD200_IO.nAO1_Raw.
Đọc ADS ngày 19/10/2026 13:xx: MAIN.bMD200_RunRequest=TRUE;
MAIN.rMD200_SetFrequency_Hz=15; MAIN.bMD200_CommissioningEnable=FALSE;
MAIN.nMD200_AO_RawVirtual=0; MAIN.bMD200_DO_RunVirtual=FALSE.
Trước khi nghiệm thu, vận hành PHẢI Write Values RUN request về FALSE trong MAIN;
KHÔNG Force; xác minh biến tần thật đã dừng và có biện pháp cách ly nguồn.
Bản này KHÔNG được ChatGPT Activate / Download từ xa.

## Cách kiểm tra đường đi trong TwinCAT XAE (offline)
1. Mở solution DATN.sln ở đường dẫn STAGE mới, không ghi đè stage cũ.
2. PLC1 → GVLs → GVL_MD200_IO; thấy nAO1_Raw AT %Q* : UINT; bRUN_DO AT %Q* : BOOL.
3. Build → Build Solution, mã nguồn phải không lỗi.
4. PLC1 Instance → PlcTask Outputs → GVL_MD200_IO.nAO1_Raw.
5. Link To / Variable / I/O: liên kết AUTO đến
   I/O → Devices → Device 2 (EtherCAT) → Box 1 (NA-9286,Crevis) →
   Module 11 (ST-4424) → Output(s) → Analog Output Ch#1 Byte.
6. Kiểm tra danh sách IO không có kênh Analog nào đã dùng bởi chức năng khác.
7. Trước khi xét nghiệm thu phải xử lý warning EziServo EtherCAT 1002 và
   xác minh Crevis NA-9286 trạng thái OP/WcState+nguồn Field 24V; không suy luận
   từ "PLC ADS Run" rằng EtherCAT output đã hoạt động.

## Nguyên lý hệ thống
MAIN.rMD200_SetFrequency_Hz (0..25Hz)
  → MAIN.fbMD200_VFD
  → MAIN.nMD200_AO_RawVirtual (0..2048 nếu scale 10V=50Hz)
  → GVL_MD200_IO.nAO1_Raw (UINT)
  → PDO ST-4424 AO1 (module 11)
  → (nếu đã tải/đấu/OP và liên động READY) AI1 MD200.
Những bước nạp cấu hình, kết nối RUN thật, cấp điện áp ra AO và cho VFD
chạy phải được giám sát tại tủ với đo kiểm/hardwired E-stop.
Bộ mã ST chỉ tạo logic vận hành, không phải chức năng an toàn độc lập.
