# DATN / Hợp đồng lệnh tổng — CHƯA TRIỂN KHAI PLC

Mục tiêu: HMI C# có màn hình START TỔNG / STOP TỔNG và trạng thái E-Stop chung. Hiện các nút HMI Tổng quan chỉ mở thông báo, KHÔNG gọi ADS Write, không thay đổi PLC hay thiết bị.

## Hiện có (đã đối chiếu Docs/TAG_MATRIX.md)
- MAIN.bSystemEStopOK — chỉ phản hồi AUX từ ST-1228 DI0, KHÔNG là an toàn phần cứng.
- GVL_MD200_IO.bEStopFeedback — ảnh trạng thái DI thường, không đạt chuẩn safety.
- MAIN.bSystemStopRequested — đọc trạng thái dừng phần mềm từ TwinCAT, CHƯA được phép ghi.
- MAIN.bMD200_Start và MAIN.rMD200_SetFrequency_Hz — điều khiển MD200/băng tải theo module C# cũ, mặc định khóa và có xác nhận vận hành, chỉ có trong trang BĂNG TẢI CHUNG.
- MAIN.bPlcQConnected, MAIN.bGlueCycleBusy / Done / Error, MAIN.nQueueCount — giám sát Gantry hiện có.
- SCARA: chưa có symbol PLC được xác minh, không cấp lệnh.

## Đề xuất hợp đồng tương lai (KHÔNG PHẢI tag hiện hành)
- CmdSystemStartReq / CmdSystemStopReq + Ack, SeqNo, Origin, Timeout, FaultReason (tên thực tế phải được thống nhất với PLC).
- START tổng chỉ khi mạch EStop safety-rated đã nghiệm thu, conveyor/gantry/scara tất cả Ready, recipe hợp lệ, mode AUTO hợp lệ, vùng làm việc an toàn, queue rõ ràng.
- STOP tổng là lệnh dừng có điều khiển/đã nghiệm thu, tự xác nhận băng tải và mỗi robot đã dừng, bảo toàn hoặc hủy FIFO theo thiết kế.
- Mất ADS hoặc timeout Ack phải đánh dấu lệnh UNKNOWN và chống replay tự động; không suy diễn Stop hoàn thành chỉ từ việc gửi lệnh.
- E-STOP bắt buộc là mạch dừng vật lý được thiết kế/kiểm định độc lập cho MD200, Gantry và SCARA. UI chỉ hiển thị hồi tiếp và hướng dẫn; không là nút an toàn được chứng nhận.
- PLC/QD75 hiện có handshake M1000/M1001 chỉ cho Gantry; tuyệt đối không dùng lại cho SCARA.
- Robot manual Jog/Homing/Van keo/Gripper/Tray phải có quyền operator, interlock và điểm chuyển trạng thái riêng, chưa được kích hoạt từ HMI hiện tại.

## Nghiệm thu trước kết nối nút HMI vào thiết bị
1. Xác thực chính xác chương trình TwinCAT đang chạy và chương trình PLC-Q hiện hành; kiểm tra tag/ADS handle, hướng ghi và ACK, tránh ghi nhầm biến do logic PLC quản lý.
2. Nghiệm thu EStop hai kênh/mạch cứng theo đánh giá rủi ro với đầy đủ drive/VFD/robot, độc lập với C#/TwinCAT network.
3. Simulation offline fault injection; sau đó bench có giám sát theo thủ tục an toàn có phê duyệt.
4. Người vận hành cho phép rõ ràng trước khi thực thi bất kỳ ghi ADS/MC, servo enable hoặc tác động hiện trường.

## Offline source-stage addendum — 2026-10-21 (NOT RUNTIME)
The original document's NO VERIFIED LIVE GLOBAL CONTRACT statement still applies to the currently running controller. However, NEW MAIN.TcPOU source symbols have been staged and compiled (LAST_BUILD_INFO=0), with cLineAutoCommissioned=FALSE and cBeltMmSCommissioned=FALSE. NORMAL STOP latch and HMI watchdog are prepared, but fault RESET remains a separate uncommissioned robot contract. No PLC Activate/Download. See Docs/TWINCAT_LINE_MASTER_HMI_CONTRACT_20261021.md. WPF must not assume newly added source tags are available on ADS 851.
