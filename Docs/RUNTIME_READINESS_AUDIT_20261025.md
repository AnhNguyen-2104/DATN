# RUNTIME READINESS AUDIT — KIỂM TOÁN TÌNH TRẠNG SẴN SÀNG HỆ THỐNG
**Ngày thực hiện:** 2026-10-25  
**Workspace:** `E:\Orca\DATN`  
**Phương pháp:** Evidence-first audit (chỉ đọc và đối soát mã nguồn, cấu hình, kiểm thử offline; không sửa mã, không nạp/kích hoạt TwinCAT, không ghi ADS, không vận hành phần cứng).

---

## 1. TỔNG QUAN & TRẢ LỜI 5 CÂU HỎI TRỌNG TÂM

| Câu hỏi của người dùng | Kết luận kỹ thuật | Tóm tắt bằng chứng |
|---|:---:|---|
| **1. Hệ thống có kết nối được không?** | **VERIFIED (Một phần)** | • **WPF ↔ TwinCAT 3 (ADS Port 851):** KẾT NỐI TỐT (`App/Vision/Dashboard.cs:86-87`, `LineMailboxGateway.cs:555`).<br>• **TwinCAT 3 ↔ Crevis EtherCAT Coupler:** KẾT NỐI TỐT (`ST-1228, ST-2318, ST-4424, ST-5114`). Băng tải đã chạy thực nghiệm 2026-10-24.<br>• **TwinCAT 3 ↔ Mitsubishi Q06UDEH:** SOCKET TCP KẾT NỐI ĐƯỢC (`MAIN.bPlcQConnected = TRUE`, IP `192.168.3.39:5010`).<br>• **WPF ↔ Camera 1:** KẾT NỐI ĐƯỢC (Hikrobot GigE/USB, Basler USB3, demo video).<br>• **WPF ↔ Camera 2 (SCARA):** RESERVED / CHƯA TRIỂN KHAI. |
| **2. Có track được vật không?** | **VERIFIED (Trong WPF)**<br>*(Chưa đồng bộ PLC thực)* | • **Xử lý ảnh & Tracking trên PC:** HOẠT ĐỘNG TỐT (`PlateDetector.cs:30-109`, `MidlineTracker.cs:13-90`). Phôi 50x50 mm được nhận diện, tính tâm $(X_{px}, Y_{px})$, góc $\theta$, quy đổi sang tọa độ băng tải $(X_{mm}, Y_{mm})$ và phát sự kiện Trigger khi qua vạch 50% FOV.<br>• **Đồng bộ phần cứng:** CHƯA. Chưa có phần cứng chốt xung Encoder ST-5114 tại thời điểm phơi sáng (Hardware Exposure Latch); khoảng cách Camera đến Robot (`rCameraToWorkStart_mm`) chưa hiệu chuẩn thực tế. |
| **3. PLC có nhận được tọa độ từ TwinCAT 3 không?** | **BLOCKED / NOT VERIFIED**<br>*(Trong vận hành tự động)* | • **Vận hành tự động (Auto Dispatch):** KHÔNG NHẬN ĐƯỢC. Logic bị khóa cứng ở cấp biên dịch bởi `cQueueMotionCommissioned = FALSE` (`MAIN.TcPOU:361`), `cLineAutoCommissioned = FALSE` (`MAIN.TcPOU:363`), `bLegacyMotionPublisherAllow = FALSE` (`MAIN.TcPOU:258`), `nAutoDispatchState = 100` (`MAIN.TcPOU:802`).<br>• **Giao diện C# WPF:** Khi camera trigger trong chế độ live, WPF ghi `NO ADS WRITE` (`MainWindow.xaml.cs:1007`), không ghi tọa độ xuống ADS.<br>• Tọa độ chỉ ghi xuống PLC-Q nếu kỹ thuật viên kích hoạt thủ công chế độ service debug (`bTest_WriteD100`). |
| **4. Có chạy điểm được không?** | **BLOCKED** | • **Robot Gantry (QD75D4):** BỊ KHÓA TUYỆT ĐỐI. Cờ kích hoạt `bStartGlueCycle = FALSE` (`MAIN.TcPOU:312`), gate chạy lệnh `fbPublisher` bị khóa (`MAIN.TcPOU:847`).<br>• **Chương trình PLC-Q:** Chưa được đối chiếu/đọc trực tiếp từ PLC thực (Read/Verify). File ST cũ có xung đột ngõ ra (nhầm Y20 van keo với Servo Enable). Chiều trục Z và giới hạn hành trình ($Z_{down}$, $Z_{safe}$) chưa được nghiệm thu tại máy.<br>• **Phản hồi vị trí:** PLC-Q không truyền tọa độ thực tế (QD75 Md.20 TCP) về TwinCAT; WPF chỉ hiển thị `--` (`UNAVAILABLE`). |
| **5. Còn thiếu những gì?** | **XEM MỤC 4 & 5** | • Cầu nối truyền tọa độ Camera thực từ C# xuống TwinCAT ADS trong chế độ chạy tự động kèm sequence/handshake.<br>• Hiệu chuẩn khoảng cách vật lý Camera ↔ Gantry (`rCameraToWorkStart_mm`).<br>• Đọc và chuẩn hóa dự án GX Works2 trực tiếp từ PLC-Q thực; phân định rõ Y20..Y22 (Servo), Y23 (Brake), Y25 (Van keo).<br>• Mở khóa các cổng an toàn biên dịch trong TwinCAT sau khi nghiệm thu cơ điện.<br>• Mạch E-Stop phần cứng 2 kênh độc lập cho biến tần và servo. |

---

## 2. PHÂN LOẠI TRẠNG THÁI KIỂM TOÁN (VERIFIED / NOT VERIFIED / BLOCKED)

### 2.1. VERIFIED (Đã kiểm chứng bằng mã nguồn, bài đo hoặc unit test)
1. **WPF ADS Client & Polling (Port 851):**
   - File: `App/Vision/Dashboard.cs:71-128, 193-289`.
   - Kết nối thành công đến TwinCAT 3 Port 851. Đọc chu kỳ an toàn với cơ chế bảo vệ tái nhập (`_dashPolling`), timeout và fallback fail-closed.
   - Nhận diện đầy đủ 10 biến telemetry Gantry mới thêm ngày 2026-10-25 (`MAIN.bPlcQError`, `MAIN.nPlcQErrorID`, `MAIN.bStopCommandConfirmed`, `MAIN.nAutoDispatchState`, `MAIN.rQueueHeadX_mm`, `MAIN.fbPublisher.rTargetX_mm`...).
2. **HMI Heartbeat & Async Mailbox:**
   - File: `App/Vision/Vision/LineMailboxProtocol.cs:523-740`, `App/Vision/Dashboard.cs:152`.
   - Heartbeat 300 ms giữ `MAIN.bHmiLinkFresh = TRUE`.
   - Giao thức Mailbox ghi lệnh theo số thứ tự (`SequenceCounter`), chống phát lại lệnh cũ (`PriorAck`), giải phóng bit xung trong khối `try/finally`.
3. **Băng tải chung (Inovance MD200 via Crevis I/O):**
   - File: `PLC/TwinCAT/DATN/PLC1/POUs/MAIN.TcPOU:501-570`, `Docs/ORCA_COMMISSIONING_MANUAL_BELT_RUN_RESULT_20261024.md`.
   - Đã nghiệm thu chạy thực tế ngày 2026-10-24: `MAIN.cBeltMmSCommissioned = TRUE`, Crevis ST-4424 AO1 xuất 3.00 V (Raw 1229), Crevis ST-2318 DO0 kích RUN, Encoder ST-5114 đọc phản hồi tốc độ thực 85.8 mm/s.
   - Hỗ trợ đổi tốc độ tức thời khi đang chạy (On-the-fly retuning) đạt kết quả mượt mà.
4. **Thuật toán nhận diện phôi & Tracking (C# Computer Vision):**
   - File: `App/Vision/Vision/PlateDetector.cs:30-109`, `App/Vision/Trigger/MidlineTracker.cs:13-90`, `App/Vision/Vision/BeltCoordinates.cs:14-38`.
   - Định vị phôi 50x50 mm, góc xoay $\theta$, trích xuất 4 điểm góc 40x40 mm.
   - Bộ lọc Hungarian theo vết liên tục (`Track.Id`), bắt vạch qua tâm 50% FOV phát sự kiện `TriggerEvent`.
   - Kiểm thử tự động: `App/Tests/Program.cs` đạt **100% PASS** (0 failures).

### 2.2. NOT VERIFIED (Chưa kiểm chứng trên phần cứng / Chưa có luồng chạy tự động)
1. **Đồng bộ phần cứng Camera ↔ Encoder ST-5114:**
   - Hiện tại C# chỉ có chế độ mô phỏng qua bàn thử (`AdsFifoBenchBridge.cs:95-152`), chỉ cho phép ghi khi tốc độ băng tải $< 0.5\text{ mm/s}$ và ở chế độ `bQueueSimMode = TRUE`.
   - Trong quá trình băng tải chạy thật, chưa có cơ chế chụp ảnh đồng bộ bằng chân kích phần cứng (Hardware Trigger) liên kết trực tiếp với thanh ghi chốt xung encoder của Crevis ST-5114.
2. **Hiệu chuẩn khoảng cách vật lý Camera ↔ Trạm Robot:**
   - Biến `MAIN.rCameraToWorkStart_mm` trong TwinCAT đang khởi tạo bằng `0.0` (`MAIN.TcPOU:236`). Tài liệu quy trình ước tính 300 mm nhưng chưa được đo và nghiệm thu cơ khí.
   - Cờ `MAIN.bVisionCalibrationOK` vẫn đang là `FALSE` (`MAIN.TcPOU:235`).
3. **Chương trình PLC Mitsubishi Q06UDEH thực tế:**
   - Thư mục `PLC/Mitsubishi/Sources_20261019/GXWorks2/Candidates` có 2 file dự án (`Test2_D_DATN.gxw` và `Test2_FROM_SESSION_20261017.gxw`), chưa được kết nối online để so sánh (`Read/Verify`) với bộ nhớ CPU PLC-Q thực tế trên tủ điện.
4. **Quy trình bắt tay M1000 / M1001 trên phần cứng:**
   - Khối `FB_QD75CommandPublisher` chứa logic gửi D100..D114 và chờ M1001, nhưng chưa có phiên chạy thử nghiệm thu (supervised bench test) trên bàn máy thật.

### 2.3. BLOCKED (Bị khóa bởi logic an toàn / Biên dịch / Phần cứng)
1. **Cổng an toàn biên dịch (TwinCAT Compile-Time Constant Gates):**
   - `cQueueMotionCommissioned : BOOL := FALSE;` (`PLC/TwinCAT/DATN/PLC1/POUs/MAIN.TcPOU:361`).
     - Khóa cứng `nAutoDispatchState := 100;` (`MAIN.TcPOU:802`). Không một phôi nào trong FIFO được cấp phát sang Publisher.
   - `cLineAutoCommissioned : BOOL := FALSE;` (`PLC/TwinCAT/DATN/PLC1/POUs/MAIN.TcPOU:363`).
     - Khóa lệnh chạy tự động toàn chuyền từ HMI, trả về mã từ chối `11` (`MAIN.TcPOU:451`).
2. **Cờ cấp phép chuyển động (TwinCAT Safety Interlock Flags):**
   - `bLegacyMotionPublisherAllow : BOOL := FALSE;` (`MAIN.TcPOU:258`).
   - `bStartGlueCycle : BOOL := FALSE;` (`MAIN.TcPOU:312`).
   - `bVisionFeedVerified : BOOL := FALSE;` (`MAIN.TcPOU:234`).
   - Khối `fbPublisher` tại dòng 847 yêu cầu `bLegacyMotionPublisherAllow = TRUE` mới cho phép kích chu trình. Hiện cờ này luôn `FALSE`.
3. **Cơ chế phát lệnh từ WPF SCADA:**
   - Trong `App/Vision/MainWindow.xaml.cs:1001-1007`, khi phát hiện sản phẩm qua vạch trigger, trạng thái phát lệnh được đặt là `Delivery = "TRIGGER → NOT SENT"` và `Plc = "ADS READ ONLY"`.
   - Không có tiến trình chạy ngầm nào đẩy tọa độ trực tiếp vào hàng đợi TwinCAT khi chạy sản xuất thật.
4. **Xung đột ngõ ra vật lý Mitsubishi PLC-Q (Physical I/O Risk):**
   - Tài liệu `TWINCAT_MITSUBISHI_QD75_CONFIG_PENDING_20261021.md:40-52` chỉ rõ: File nguồn ST lịch sử coi `Y20` là van keo, nhưng thực tế phần cứng tủ điện đã đấu: `Y20` (Servo 1 Enable), `Y21` (Servo 2 Enable), `Y22` (Servo 3 Enable), `Y23` (Brake Release), `Y25` (Glue Pump Trigger).
   - Nếu chạy chương trình cũ mà không kiểm tra, có nguy cơ bật nhầm servo hoặc mở nhầm van keo.
5. **Giới hạn hành trình trục Z và Homing:**
   - Chiều di chuyển trục Z (tăng là hạ xuống hay rút lên), các giá trị an toàn $Z_{down}$, $Z_{safe}$ và giới hạn hành trình vật lý chưa được kiểm chứng.
   - Tọa độ công cụ TCP thực tế (`Md.20`) không được PLC-Q đưa lên mạng MC Protocol; TwinCAT và WPF hoàn toàn không có phản hồi vị trí thực của robot.
6. **Mạch Dừng khẩn cấp (E-Stop Safety Chain):**
   - Ngõ vào ST-1228 DI0 (`GVL_MD200_IO.bEStopFeedback`) chỉ là tiếp điểm phụ (Auxiliary Feedback), KHÔNG PHẢI mạch an toàn tiêu chuẩn.
   - Lệnh hủy M105 qua mạng Ethernet chỉ là phần mềm "best-effort", không thể thay thế rơ-le an toàn ngắt nguồn động lực cơ khí.

---

## 3. ĐỐI SOÁT KỸ THUẬT CHI TIẾT THEO MÃ NGUỒN (EXACT SYMBOLS & LINES)

### Phân hệ 1: Kết nối WPF ADS & Giám sát Telemetry
- **Mã kết nối ADS:** `App/Vision/Dashboard.cs:86-87`
  ```csharp
  using var ads = new AdsClient();
  ads.Connect(851);
  if (ads.ReadState().AdsState != AdsState.Run)
      throw new InvalidOperationException("TwinCAT ADS 851 is not in RUN state.");
  ```
- **Khai báo các biến Gantry đọc an toàn:** `App/Vision/Dashboard.cs:99-109`
  - `MAIN.bPlcQConnected` (Line 99)
  - `MAIN.bPlcQError` (Line 100)
  - `MAIN.nPlcQErrorID` (Line 101)
  - `MAIN.bGlueCycleBusy` (Line 102)
  - `MAIN.bGlueCycleDone` (Line 103)
  - `MAIN.bGlueCycleError` (Line 104)
  - `MAIN.nGlueCurVertex` (Line 105)
  - `MAIN.nGlueErrorCode` (Line 106)
  - `MAIN.nGlueCommErrorID` (Line 107)
  - `MAIN.bStopCommandConfirmed` (Line 108)
- **Đọc dữ liệu hàng đợi FIFO & Tọa độ đón đầu:** `App/Vision/Dashboard.cs:112-118`
  - `MAIN.nQueueCount`, `MAIN.nQueueHeadProductID`, `MAIN.nQueueRealCompleted`
  - `MAIN.rQueueHeadX_mm`, `MAIN.rQueueHeadY_mm`
  - `MAIN.fbPublisher.rTargetX_mm`, `MAIN.fbPublisher.rTargetY_mm`
  - `MAIN.nAutoDispatchState`

### Phân hệ 2: Đường truyền Camera & Tracking
- **Giao diện bắt ảnh:** `App/Vision/Vision/StationCameraController.cs:22-132` (Hỗ trợ Hikrobot và Basler).
- **Phát hiện phôi:** `App/Vision/Vision/PlateDetector.cs:30-109`
  - Tìm Contour, kiểm tra tỉ lệ khung hình (Aspect Ratio ~ 1.0) và tỉ lệ lấp đầy (Fill ~ 1.0).
  - Trích xuất tâm $(X, Y)$, góc $\theta$ và 4 lỗ keo cách tâm 20 mm (bước 40x40 mm).
- **Theo vết & Trigger 50% FOV:** `App/Vision/Trigger/MidlineTracker.cs:43-75`
  - Gán nhãn `Track.Id` bền vững qua từng frame bằng khoảng cách Euclid tối thiểu.
  - Khi tọa độ tâm cắt qua vạch `_lineFraction = 0.5`, tạo `TriggerEvent`.
- **Rào cản truyền tọa độ live:** `App/Vision/MainWindow.xaml.cs:1001-1007`
  ```csharp
  Delivery = _benchArmed ? "TRIGGER → FIFO BENCH" : "TRIGGER → NOT SENT",
  Plc = _benchArmed ? "WAITING FOR SAFETY / ACK" : "ADS READ ONLY"
  // ...
  AppendPipelineCsv(row, "MIDLINE_TRIGGER", _benchArmed ? "BENCH REQUESTED" : "NO ADS WRITE");
  ```
  *Nhận xét:* Khi không bật cờ Bench thủ công, sự kiện Trigger hoàn toàn KHÔNG phát lệnh ghi ADS.

### Phân hệ 3: Handoff Tọa độ trong TwinCAT 3 (FIFO → Publisher)
- **Khai báo hàng đợi 8 sản phẩm:** `PLC/TwinCAT/DATN/PLC1/POUs/MAIN.TcPOU:1077-1110`
  - Khối `fbCameraQueue: FB_CameraProductQueue` nhận tín hiệu `bCameraTrigger`, tọa độ `rCameraCenterX`, `rCameraCenterY`, `rCameraAngleDeg`.
  - Cập nhật vị trí phôi theo encoder: $\Delta X = rConveyorDist - rEncoderAtExposure\_mm$.
- **Khóa tự động điều phối (Auto Dispatch Hard-Lock):** `MAIN.TcPOU:801-802`
  ```pascal
  ELSIF NOT cQueueMotionCommissioned THEN
      nAutoDispatchState := 100; // Hard-coded offline commissioning lock
  ```
- **Điều kiện kích chạy Robot Publisher:** `MAIN.TcPOU:847`
  ```pascal
  fbPublisher(
      stSession := stMelsec,
      bStartCycle := (bStartGlueCycle OR bAutoStartPulse)
                     AND bSystemEStopOK
                     AND bLegacyMotionPublisherAllow
                     AND NOT bQueueSimMode ...,
  ```
  *Nhận xét:* `bStartCycle` phụ thuộc vào `bLegacyMotionPublisherAllow` (đang `FALSE`) và `bAutoStartPulse` (không bao giờ sinh ra do `cQueueMotionCommissioned = FALSE`).

### Phân hệ 4: Giao thức MC Protocol TwinCAT ↔ Mitsubishi PLC-Q
- **Cấu hình TCP Client:** `MAIN.TcPOU:375-385`
  - Host: `sPlcQ_IP = '192.168.3.39'`, Port: `nPlcQ_Port = 5010`.
  - Biến trạng thái: `MAIN.bPlcQConnected`, `MAIN.bPlcQError`, `MAIN.nPlcQErrorID`.
- **Ánh xạ thanh ghi ghi tọa độ (TwinCAT → PLC-Q):** `PLC/TwinCAT/DATN/PLC1/POUs/FB_QD75CommandPublisher.TcPOU:225-256`
  - Vùng đệm 15 WORD từ $D100$ đến $D114$:
    - $D100..D101$: $Target\_X$ ($0.1\ \mu\text{m} = \text{mm} \times 10000$)
    - $D102..D103$: $Target\_Y$ ($0.1\ \mu\text{m} = \text{mm} \times 10000$)
    - $D104..D105$: $Target\_Z\_Down$ ($0.1\ \mu\text{m}$)
    - $D106..D107$: $Speed\_Approach$ ($0.01\ \text{mm/min} = \text{mm/s} \times 6000$)
    - $D108..D109$: $Speed\_Sync$ ($0.01\ \text{mm/min} = \text{mm/s} \times 6000$)
    - $D110..D111$: $DeltaX\_Sync$ ($0.1\ \mu\text{m}$)
    - $D112..D113$: $Target\_Z\_Safe$ ($0.1\ \mu\text{m}$)
    - $D114$: $Vertex\_Index$ ($1..4$)
- **Chu trình bắt tay 4 đỉnh:** `FB_QD75CommandPublisher.TcPOU:124-396`
  - Bước 10: Đọc bit trạng thái robot $M102$ (Busy), $M103$ (InPosition), $M104$ (Alarm), $M1001$ (Ack). Đảm bảo đều bằng 0.
  - Bước 20: Kiểm tra giới hạn tọa độ ($X \in [0, 180]$, $Y \in [0, 170]$, $Z \in [0, 52]$) $\to$ Ghi $D100..D114$.
  - Bước 211 & 22: Ghi $M1000 = TRUE$ (Start lệnh).
  - Bước 30 & 31: Đợi PLC-Q kéo $M1001 = TRUE$ (xác nhận hoàn thành Point 3 / Z rút về Safe) $\to$ Ghi $M1000 = FALSE$.
  - Bước 40 & 41: Đợi PLC-Q hạ $M1001 = FALSE$ $\to$ Chuyển sang đỉnh tiếp theo ($1 \to 2 \to 3 \to 4$).
  - Hoàn thành đỉnh 4: Đặt `bDone := TRUE`, `bBusy := FALSE`.

---

## 4. PHÂN TÍCH KHOẢNG CÁCH (GAP ANALYSIS) — CÒN THIẾU NHỮNG GÌ?

Để hệ thống chuyển từ trạng thái **kiểm toán tĩnh** sang **chạy tự động thực tế**, cần giải quyết 5 khoảng trống kỹ thuật bắt buộc sau:

```
[Camera C#] ---> (1) Live Trigger ADS Bridge ---> [TwinCAT FIFO]
                                                          |
                                           (2) Commissioning Gates
                                           (cQueueMotion = TRUE)
                                                          |
                                                          v
[PLC-Q / QD75] <--- (3) GX Works2 & Coils Audit <--- [FB_Publisher]
      |
(4) Md.20 TCP Telemetry stream
      |
      v
[WPF SCADA Dashboard]
```

1. **Khoảng trống 1: Luồng đẩy tọa độ từ Camera vào TwinCAT khi chạy thật (Live Bridge):**
   - *Hiện trạng:* C# chỉ có `EnqueueBenchAsync` dùng cho test tĩnh (băng tải dừng). Khi băng tải chạy thật, `RecordTrigger` không ghi tọa độ xuống ADS.
   - *Cần làm:* Bổ sung cơ chế ghi có sequence/handshake từ C# xuống TwinCAT (`MAIN.rCameraCenterX`, `MAIN.rCameraCenterY`, `MAIN.rCameraAngleDeg` và kích `MAIN.bCameraTrigger`) hoặc triển khai ngõ vào phần cứng chốt xung chụp ảnh (Hardware Camera Trigger Output $\to$ ST-5114 Latch).
2. **Khoảng trống 2: Hiệu chuẩn cơ khí Camera ↔ Vùng làm việc Robot:**
   - *Hiện trạng:* `MAIN.rCameraToWorkStart_mm = 0.0`; `MAIN.bVisionCalibrationOK = FALSE`.
   - *Cần làm:* Đo đạc cơ khí thực tế khoảng cách từ vạch 50% FOV của Camera đến mép vào của vùng làm việc Gantry (ước tính sơ bộ 300 mm). Cập nhật thông số vào TwinCAT và xác nhận phôi đi đúng thời gian đón đầu.
3. **Khoảng trống 3: Rà soát và chuẩn hóa chương trình PLC Mitsubishi Q (GX Works2):**
   - *Hiện trạng:* Chưa Read/Verify online giữa phần mềm và PLC thực. Nguy cơ nhầm lẫn coil $Y20..Y25$ (Servo Enable vs Glue Valve).
   - *Cần làm:* Dùng GX Works2 đọc trực tiếp chương trình từ CPU Q06UDEH, đối chiếu sơ đồ đấu dây thực tế:
     - $Y20, Y21, Y22$: Servo Enable trục 1, 2, 3.
     - $Y23$: Thắng từ (Brake Release).
     - $Y25$: Kích van bơm keo (Glue Dispenser Trigger).
     - Kiểm tra chu trình nhận $D100..D114$, logic trả $M1001$ sau khi Z rút lên an toàn và điều kiện xóa $M1001$ khi $M1000$ hạ.
4. **Khoảng trống 4: Phản hồi vị trí thực (TCP Md.20) và Trạng thái Homing:**
   - *Hiện trạng:* QD75 buffer memory $Md.20$ không được đưa vào bảng dữ liệu truyền thông MC; trạng thái Homing $M100/M101$ bị cô lập.
   - *Cần làm:* Bổ sung khối đọc buffer QD75 trong PLC-Q hoặc TwinCAT để đưa tọa độ thực của đầu phun lên WPF, tránh hiển thị `--`.
5. **Khoảng trống 5: Mở khóa các cổng an toàn (Commissioning Gates) có giám sát:**
   - *Hiện trạng:* Các hằng số `cQueueMotionCommissioned`, `cLineAutoCommissioned` trong `MAIN.TcPOU` bị khóa cứng `FALSE`.
   - *Cần làm:* Sau khi đã hoàn tất các bước nghiệm thu tĩnh và kiểm tra E-Stop phần cứng, thực hiện Online Change để mở khóa từng cổng theo trình tự có kiểm soát.

---

## 5. KẾ HOẠCH NGHIỆM THU AN TOÀN THEO TỪNG GIAI ĐOẠN (STAGED COMMISSIONING CHECKLIST)

> **CẢNH BÁO AN TOÀN:** Tuyệt đối không mở cờ chuyển động robot khi chưa hoàn thành các bước kiểm tra phần cứng độc lập. Người thực hiện phải túc trực tại tủ điện và nút dừng khẩn cấp.

### Giai đoạn 1: Nghiệm thu Cơ điện & Mạch dừng khẩn cấp (Hardware & E-Stop Stage)
- [ ] **1.1.** Kiểm tra nút dừng khẩn cấp (E-Stop) phần cứng độc lập: Khi ấn nút, toàn bộ nguồn động lực cấp cho biến tần Inovance MD200 và driver servo Gantry phải bị ngắt vật lý bằng contactor/rơ-le an toàn.
- [ ] **1.2.** Xác minh tiếp điểm phụ ST-1228 DI0: Khi E-Stop đóng mạch, DI0 = 1; khi E-Stop bị ấn, DI0 = 0 và phần mềm TwinCAT lập tức hủy mọi yêu cầu RUN.
- [ ] **1.3.** Xác minh nguồn cấp module ra tương tự Crevis ST-4424 (AO1) và ST-2318 (DO0), đảm bảo mass chung (COM) với biến tần.

### Giai đoạn 2: Đọc & Đối chiếu PLC Mitsubishi Q (GX Works2 Online Verification)
- [ ] **2.1.** Mở GX Works2, kết nối cáp USB hoặc Ethernet tới Q06UDEH, thực hiện **Read from PLC** và so sánh với file ứng viên `Test2_D_DATN.gxw`.
- [ ] **2.2.** Cross-reference toàn bộ các ngõ ra $Y20, Y21, Y22, Y23, Y25$, đảm bảo $Y25$ là cuộn hút duy nhất cho van keo.
- [ ] **2.3.** Kiểm tra logic Homing thủ công (bằng nút nhấn tủ điện hoặc lệnh kiểm soát riêng, không tự động chạy).
- [ ] **2.4.** Kiểm tra giới hạn hành trình trục Z: Xác định rõ hướng di chuyển và giá trị $Z_{safe}$ (vị trí nâng cao nhất an toàn) và $Z_{down}$ (vị trí hạ chấm keo).

### Giai đoạn 3: Kiểm thử Truyền thông MC Protocol & Dry Run không tải (Cold Motion Test)
- [ ] **3.1.** Giữ van keo ngắt nguồn điện (không xuất keo).
- [ ] **3.2.** Cho Gantry thực hiện Homing về gốc tọa độ cơ khí ($X=0, Y=0, Z=0$).
- [ ] **3.3.** Kích hoạt chế độ test thủ công trong TwinCAT (`bEnableManualWrite = TRUE`, `bTest_WriteD100 = TRUE`) để kiểm tra ghi 1 bộ tọa độ thử nghiệm vào $D100..D114$. Đọc lại trên GX Works2 xác nhận dữ liệu khớp 100%.
- [ ] **3.4.** Kiểm tra thử nhịp bắt tay $M1000 \to M1001$: Kích thử 1 điểm ở vận tốc chậm, quan sát đầu gantry di chuyển tới điểm, hạ Z, nhấc Z và PLC-Q bật $M1001$. Xác nhận TwinCAT tự hạ $M1000$ và chu trình dừng an toàn.

### Giai đoạn 4: Hiệu chuẩn Camera & Thử nghiệm Hàng đợi FIFO (Vision & Queue Dry Run)
- [ ] **4.1.** Đo khoảng cách thực tế từ vị trí chụp ảnh đến mép làm việc của Gantry, nạp giá trị vào `rCameraToWorkStart_mm`.
- [ ] **4.2.** Bật băng tải chạy ở tốc độ ổn định ($100\text{ mm/s}$). Đặt 1 phôi mẫu 50x50 mm lên băng tải.
- [ ] **4.3.** Quan sát trên WPF: Phôi được nhận diện, vạch 50% FOV trigger, tọa độ được đưa vào hàng đợi FIFO của TwinCAT (`MAIN.nQueueCount = 1`).
- [ ] **4.4.** Giám sát biến `aQueueHeadCornerNow` trong TwinCAT: Tọa độ $X$ của phôi phải tăng tịnh tiến theo đúng số xung encoder ST-5114 đọc về.

### Giai đoạn 5: Mở khóa Tự động toàn chuyền có giám sát (Supervised Full-Auto Commissioning)
- [ ] **5.1.** Chỉ khi 4 giai đoạn trên đều đạt 100%, kỹ sư trưởng mới thực hiện Online Change trong TwinCAT:
  - `cQueueMotionCommissioned := TRUE;`
  - `cLineAutoCommissioned := TRUE;`
  - `bLegacyMotionPublisherAllow := TRUE;`
  - `bVisionFeedVerified := TRUE;`
  - `bVisionCalibrationOK := TRUE;`
- [ ] **5.2.** Chạy thử nghiệm 1 phôi đơn lẻ: Băng tải kéo phôi $\to$ Camera nhận diện $\to$ TwinCAT đón đầu $\to$ Robot chạy 4 điểm $\to$ Xác nhận 4 lần ACK $\to$ Hàng đợi giải phóng phôi (`nQueueRealCompleted = 1`).
- [ ] **5.3.** Thử nghiệm kịch bản bất thường: Nhấn E-Stop giữa chu trình, kiểm tra robot dừng ngay lập tức, van keo đóng tức thời và hệ thống không tự khởi động lại khi hồi vị nút E-Stop.

---

## 6. TUYÊN BỐ AN TOÀN KỸ THUẬT (SAFETY DISCLAIMER)

Báo cáo kiểm toán này được lập hoàn toàn dựa trên đối soát tĩnh mã nguồn, dữ liệu cấu hình và kết quả kiểm thử phần mềm. **Tài liệu này KHÔNG PHẢI là chứng nhận sẵn sàng phần cứng (Physical Readiness Certification)** và **KHÔNG THAY THẾ** quy trình nghiệm thu an toàn cơ điện tại hiện trường. Việc kích hoạt chuyển động cơ khí hoặc nạp chương trình PLC bắt buộc phải do kỹ sư có thẩm quyền thực hiện trực tiếp tại tủ điều khiển với đầy đủ trang bị bảo hộ và phương án xử lý sự cố.
