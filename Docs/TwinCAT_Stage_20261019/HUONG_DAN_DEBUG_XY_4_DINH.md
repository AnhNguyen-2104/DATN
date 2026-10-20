# DATN — CHẾ ĐỘ DEBUG X/Y 4 ĐỈNH TRỰC TIẾP BẰNG TWINCAT

Bản thử: C:\Users\aml\Documents\DATN_XY_DEBUG_STAGE_20261016\DATN.sln
FB mới: DATN\PLC1\POUs\FB_MelsecDebugXY4Pt.TcPOU
Bản đang chạy trên máy xưởng C:\Users\aml\Desktop\DATN_TwinCat3 KHÔNG bị sửa.
Không tự Activate/Download. Không thay đổi I/O, thông số QD75, Servo hay Ladder GX Works2.

## Khả năng của test
- Chỉ ghi D100..D103 (X DINT, Y DINT) và D114 (chỉ số đỉnh UINT).
- KHÔNG ghi D104..D113, M1000, M105, output Y, hay buffer QD75.
- Camera giả lập nội bộ tâm (50,85) mm, 40x40 mm, góc 0; tính bốn đỉnh độc lập không cần camera/encoder chạy.
- Trước MỖI đỉnh, TwinCAT đọc M1000/M1001=0, M102/M103/M104/M105=0, D200..201=0.
- Sau ghi X/Y/index, TwinCAT đọc lại cả hai DINT và index, kiểm tra khớp rồi giữ nguyên 10 giây cho GX Works2 Monitor.
- Sau giữ đủ 4 đỉnh, kiểm tra lại các trạng thái và ghi trả D100..D103 + D114 bằng dữ liệu gốc; đọc lại xác nhận, rồi mới đánh dấu bDebugXYDone=TRUE.
- bDebugXYDone=TRUE chỉ là xác nhận TRUYỀN DỮ LIỆU và KHÔI PHỤC. Robot chưa chạy, M1001 không được giả lập, bGlueCycleDone không thay đổi.

## Điều kiện trước khi nạp và chạy
1. Máy tại chế độ bảo trì, kỹ sư xác minh cơ khí/van/gantry thực sự ở trạng thái an toàn.
2. Dừng Jog theo quy trình máy: MAIN.bServo_JogFwd=FALSE và bServo_JogBwd=FALSE; đợi rServo_ActVel_Deg_s gần 0 và rConveyorSpeed <0.5 mm/s. Không đổi Enable tuỳ tiện.
3. PLC-Q M1000/M1001/M102/M103/M104/M105 phải OFF, D200..D201=0. Không cho phép bất kỳ HMI hay tác vụ khác phát Start trong khi debug.
4. Sao lưu TwinCAT Boot, trạng thái dự án và cấu hình trước khi kích hoạt. Một bản sao backup đã có dưới C:\Users\aml\Documents\DATN_BEFORE_ECHO_ACTIVATION_20261016.
5. Trên TcXaeShell AML-IPC mở đúng DATN.sln trong DATN_XY_DEBUG_STAGE_20261016, kiểm tra Target=<Local> và các mappings trước thay đổi.
6. Kỹ sư chịu trách nhiệm vận hành thực hiện Activate Configuration + đăng nhập PLC/download chỉ khi có cửa sổ bảo trì phù hợp. Remote agent CHƯA thực hiện bước này và không được coi build thành công là runtime đã nạp.

## Cách thao tác SAU KHI người vận hành đã nạp đúng bản mới
Tại MAIN [Online], kiểm tra:
- bDebugXYPermit=FALSE (mặc định); bDebugXYExecute=FALSE (mặc định).
- bDebugXYMachineStopped phải là TRUE; nếu FALSE, PHẢI dừng trục/băng tải và đợi vận tốc về 0.
- bAutoDispatchEnable=FALSE, bAutoDispatchArm=FALSE, bStartGlueCycle=FALSE, bEchoPermit=FALSE.
- rDebugCenterX=50, rDebugCenterY=85, rDebugAngleDeg=0, rDebugLength=40, rDebugWidth=40.

Mở GX Works2 Device/Buffer Memory Batch Monitor ở D100 (DEC; dạng 32-bit DINT nếu có).
Trong MAIN [Online]:
1. Đặt bDebugXYPermit=TRUE.
2. Tạo một cạnh lên ở bDebugXYExecute: FALSE -> TRUE -> FALSE.
3. Xem nDebugXYState, nDebugXYVertex, nDebugXYPassed, nDebugXYWriteX/Y, nDebugXYReadX/Y, nDebugXYReadVertex, bDebugXYReadbackOK.
4. Sau đỉnh 1/2/3/4, chờ 10 giây/đỉnh; nDebugXYPassed tăng 1 khi đã đọc lại thành công.
5. Khi xong: bDebugXYDone=TRUE, nDebugXYPassed=4 và bDebugXYRestored=TRUE; D100..D103 và D114 đã trở về giá trị gốc. Vùng Z không thay đổi.
6. Hạ bDebugXYPermit=FALSE; bDebugXYExecute=FALSE trước khi trở lại chế độ vận hành.

Giá trị mong đợi (định vị tĩnh, không có bù trôi/đón đầu vì Servo dừng):
- Đỉnh 1: X=70mm -> D100-D101 DINT=700000; Y=105mm -> D102-D103 DINT=1050000; D114=1.
- Đỉnh 2: X=30mm -> 300000; Y=105mm -> 1050000; D114=2.
- Đỉnh 3: X=30mm -> 300000; Y=65mm -> 650000; D114=3.
- Đỉnh 4: X=70mm -> 700000; Y=65mm -> 650000; D114=4.
- ZDown D104-D105 và ZSafe D112-D113 KHÔNG bị ghi.

Trạng thái lỗi: bDebugXYError=TRUE, nDebugXYErrorCode phản ánh:
901 interlock/machine/socket; 902 thông số hoặc tHold không hợp lệ;
903 M1000/M1001 vẫn ON; 904 M102..M105 không rảnh; 905 D200 PLC FSM không rảnh;
906 tọa độ XY vượt vùng; 910..919 lỗi đọc/ghi hoặc đọc lại dữ liệu;
920..924 lỗi phục hồi các thanh ghi gốc.
Bất kỳ lỗi nào cũng không phát M1000. Nếu lỗi xảy ra giữa khi ghi X/Y, có thể còn dữ liệu thử tại PLC: PHẢI xác minh và khôi phục dưới điều kiện an toàn trước khi vận hành gantry.

Lưu ý: Đây là DATA-ONLY test chứ KHÔNG phải test robot hoàn thành chấm keo. Chu trình thực phải dùng M1001 ACK và liên động QD75, KHÔNG dùng 10 giây làm Done chuyển động.
