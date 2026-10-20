# TwinCAT 3 — MD200 điều khiển đơn giản (19/10/2026)

## Mục tiêu
FB chỉ có 3 input: EStop_OK (tín hiệu giám sát chuỗi an toàn), START, tần số (Hz).
2 output vật lý: RUN=DO0 ST-2318 slot 10; AO1 ST-4424 slot 11.
Bản sao giữ nguyên dự án đang chạy, KHÔNG kích chạy băng tải, KHÔNG Activate/Download.
Solution: C:\Users\aml\Documents\DATN_MD200_SIMPLE_STAGE_20261019\DATN.sln
POU: DATN\PLC1\POUs\FB_InovanceMD200Conveyor.TcPOU
GVL: DATN\PLC1\GVLs\GVL_MD200_IO.TcGVL
Build log: build_fifo_checked_result.txt -> LAST_BUILD_INFO=0, biên dịch OK.

## Ba biến trong MAIN [Online], sau khi được người vận hành nạp và nghiệm thu
1. bMD200_EStopOK — READ ONLY derived from GVL_MD200_IO.bEStopFeedback.
   FALSE khi mất tín hiệu giám sát dừng khẩn hoặc đầu vào chưa map.
2. bMD200_Start — BOOL: TRUE yêu cầu chạy thuận, FALSE yêu cầu dừng.
3. rMD200_SetFrequency_Hz — LREAL: 0..25Hz (clamped). Không tự vượt 25Hz.

Outputs:
bMD200_DO_RunVirtual / GVL_MD200_IO.bRUN_DO: output RUN to ST-2318 DO0.
nMD200_AO_RawVirtual / GVL_MD200_IO.nAO1_Raw: analog 0..4095.
rMD200_AO_VoltsVirtual, rMD200_Ramped_Hz, nMD200_Status: diagnostic.
nStatus: 0 STOP; 1 RUN; 2 STOPPED BY ESTOP / FEEDBACK MISSING;
         3 REQUIRES NEW START EDGE; 4 INVALID SETPOINT.

## Logic bảo vệ
- Khi bEStopFeedback FALSE, cả RUN lẫn AO bằng 0 ngay trong logic PLC.
- PLC khởi động không tự RUN nếu bStart đã TRUE trước đó.
- Khi dừng khẩn, mất EStopOK hoặc đặt 0Hz, START phải được hạ FALSE
  rồi lên TRUE sau khi điều kiện đã phục hồi để tạo lần chạy mới.
- Nguồn tốc độ MD200 0-10V ứng 0-50Hz: 15Hz sau ramp -> ~3V, raw ~1229.
- Reference ramp 2Hz/s; task period tính theo 10ms, xác nhận với PLC Task thực.
- Tối đa 25Hz trong FB; vẫn phải giới hạn F0-12 trên biến tần MD200 25.00Hz.

## Mapping OFFLINE đã kiểm tra
MAIN → GVL_MD200_IO.bRUN_DO → Module 10 (ST-2318) Output(s) Byte 0,
AutoLink=true, Size=1: bit 0 (DO0) / physical pin 0.
MAIN → GVL_MD200_IO.nAO1_Raw → Module 11 (ST-4424) Output(s)
Analog Output Ch#1 Byte (AO1) / physical pin1, common analog pin5.
Không thêm/ghi đè các bit DO khác.

## Chưa hoàn thành: PHẢN HỒI EMERGENCY STOP
GVL_MD200_IO.bEStopFeedback AT %I* : BOOL := FALSE
chưa được link đến kênh Digital Input cụ thể. Không giả định kênh trống
hoặc tên kênh khi chưa có số terminal và sơ đồ đấu dây.
Cần đọc contact phụ của relay an toàn đã kiểm chứng vào DI và dùng feedback
healthy=TRUE, EStop pressed/lost power/open wire=FALSE.
KHÔNG dùng DI PLC làm đường dừng khẩn duy nhất. Mạch dừng khẩn phải trực tiếp
ngắt năng lượng chạy qua safety relay/STO/contactor đạt yêu cầu; FB chỉ giám sát.

## Trước khi Activate / Download, bắt buộc
- Có người vận hành tại máy, cơ cấu dừng khẩn đã test, vùng băng tải an toàn.
- Xác minh ST-2318 pin0 đúng DI1 MD200 và common NPN phù hợp.
- Xác minh ST-4424 AO1→AI1 và pin5→GND analog (không nối +10V).
- Crevis nguồn field 24V và EtherCAT OP/WcState; watchdog fail-safe.
- MD200 tham số terminal RUN, AI1 Voltage, F0-12 <=25Hz, motor parameters đúng tem.
- Xác minh DI phản hồi EStop có dây thật và có tính đúng khi mất nguồn/đứt dây.
- Bản mới đang CHƯA được nạp. Runtime cũ vẫn đang dùng bMD200_RunRequest,
  bMD200_CommissioningEnable v.v., các biến mới chưa có trong Runtime.
- Kết quả Build offline 0 lỗi KHÔNG phải bài test điện áp thực hay vận hành 15Hz.
