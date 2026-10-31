# BÁO CÁO CHUẨN BỊ GÓI NẠP AN TOÀN — SAFE LOAD PACKAGE PREPARATION
**Ngày lập:** 2026-10-25  
**Workspace:** `E:\Orca\DATN`  
**Phiên bản:** 1.0 (Versioned Load Package Release)  
**Ranh giới an toàn tuyệt đối:** KHÔNG kích hoạt/download TwinCAT hoặc PLC từ xa, KHÔNG ghi ADS/MC, KHÔNG Run/Stop PLC, KHÔNG vận hành động cơ/robot gantry, KHÔNG mở các cổng an toàn chuyển động (motion commissioning gates), KHÔNG làm giả kết quả kiểm chứng phần cứng. Toàn bộ kiểm tra được thực hiện hoàn toàn offline.

---

## 1. TỔNG QUAN & BỐI CẢNH YÊU CẦU NGƯỜI DÙNG

### 1.1. Yêu cầu của người dùng
Người dùng đính kèm ảnh chụp màn hình máy trạm đang mở phần mềm **MELSOFT GX Works2** với chương trình `Tracking [PRG] Program [ST]` và đưa ra yêu cầu:
> *"vậy hãy sửa hoàn chỉnh cho tôi để tôi nạp và thực hiện các bước tiếp theo"*  
> *(Please fix completely for me so I can load and perform the next steps).*

### 1.2. Hiện trạng các ứng dụng trên máy trạm (Process Inspection)
Qua kiểm tra tiến trình đang chạy trên hệ điều hành, kỹ sư vận hành đã mở sẵn 3 môi trường phát triển chính quy:
1. **MELSOFT GX Works2** (`GD2.exe`, PID 6628): Đang mở dự án chính quy `E:\Orca\DATN\PLC\Mitsubishi\DATN_PLC.gxw` (Active Window: `[Tracking [PRG] Program [ST]]`).
2. **TwinCAT 3 XAE** (`TcXaeShell.exe`, PID 25148): Đang mở giải pháp chính quy `E:\Orca\DATN\PLC\TwinCAT\DATN_TwinCat.sln`.
3. **Microsoft Visual Studio Insiders** (`devenv.exe`, PID 22240): Đang mở giải pháp `E:\Orca\DATN\App\DATN_WPF.sln`.

Giai đoạn an toàn cơ điện (Phase 0) đã được người vận hành xác nhận hoàn tất: Mạch E-Stop phần cứng 2 kênh độc lập cho biến tần MD200 và servo gantry đã sẵn sàng; tiếp điểm phụ ST-1228 DI0 đã đấu nối; sơ đồ đấu dây ngõ ra module QY40P (Slot 1) đã xác định rõ:
- `Y20`: Servo 1 Enable (Trục X)
- `Y21`: Servo 2 Enable (Trục Y)
- `Y22`: Servo 3 Enable (Trục Z)
- `Y23`: Thắng từ (Brake Release)
- `Y24`: Dự phòng (Spare)
- `Y25`: Kích van bơm keo (Glue Pump Trigger)

---

## 2. CÁC ĐIỂM SỬA ĐỔI KỸ THUẬT CỤ THỂ ĐÃ THỰC HIỆN (CONCRETE SAFE FIXES)

### 2.1. Khắc phục xung đột ngõ ra nguy hiểm trong file nguồn ST Mitsubishi (`PLC_Q_Session2_Logic.st`)
* **Khuyết tật cũ nguy hiểm:** Trong file `PLC/Mitsubishi/Sources_20261019/Source/PLC_Q_Session2_Logic.st` (dòng 200), mã nguồn ST lịch sử ghi:
  ```pascal
  Y20 := bValve_Open AND NOT M105;            (* Van keo vat ly (Slot 1 QY40P) *)
  ```
  Nếu nạp đoạn mã này xuống CPU PLC-Q, khi chu trình nội suy Point 2 kích van keo, ngõ ra `Y20` sẽ bị kích/nhả, làm mất tín hiệu Servo Enable của Trục 1 (X) ngay khi robot đang chuyển động tải, gây sụt moment và nguy cơ va chạm cơ khí nghiêm trọng!
* **Hiệu chỉnh an toàn:**
  1. Đã sao lưu file gốc có kiểm tra hash SHA-256 vào `Docs/BACKUP_20261025_SAFE_LOAD/PLC_Q_Session2_Logic.st`.
  2. Sửa dòng 206 thành:
     ```pascal
     (* SAFETY FIX 2026-10-25: Reassigned from Y20 to Y25 to match confirmed physical wiring            *)
     (* Y20..Y22 are Servo 1..3 Enables; Y23 is Brake Release (all owned by MAIN ladder).                *)
     (* Y25 is the physical Glue Pump Trigger output on Slot 1 QY40P.                                     *)
     (* WARNING: Live PLC-Q still requires online Read/Verify in GX Works2 prior to authorized download.  *)
     Y25 := bValve_Open AND NOT M105;            (* Van keo vat ly (Slot 1 QY40P - Terminal Y25) *)
     ```
  3. Cập nhật header comment xác định rõ nguyên tắc **Single-Writer**: Các cuộn `Y20..Y22` (Servo Enables), `Y23` (Brake Release), `Y0` (QD75 Ready), và `Y10` (Positioning Start qua `M110`) thuộc quyền sở hữu độc quyền của chương trình `MAIN` Ladder.

### 2.2. Chuẩn hóa tài liệu hướng dẫn và Errata kỹ thuật
Đã bổ sung thông báo Errata / Reconciliation Notice và hiệu chỉnh các tài liệu lịch sử:
1. `PLC/Mitsubishi/Sources_20261019/Docs/Session2_Contract_For_Session1.md`:
   - Bổ sung cảnh báo Errata 2026-10-25 ở đầu tài liệu.
   - Sửa bảng phân bổ ngõ ra: `Y25` là van keo vật lý, `Y20..Y22` là Servo Enable, `Y23` là Thắng từ.
   - Sửa mô tả Point 2: QD75 phát M-code `X4`, PLC mở `Y25` trong 30 ms.
   - Sửa quy tắc cấm ghi: Cấm ghi `Y0`, `Y10`, `Y20..Y25`, `Y4..Y6`.
2. `PLC/Mitsubishi/Sources_20261019/Docs/Session2_Mitsubishi_Q_Guide.md`:
   - Thêm cảnh báo Errata nổi bật: `Y25` là van keo, `M1000/M1001` là bit bắt tay (không dùng `M100/M101` vì trùng Homing).
   - Hiệu chỉnh bảng địa chỉ thiết bị DMA và phần cứng Rack Q.
3. `PLC/Mitsubishi/Sources_20261019/Docs/Session2_BenchTest_Without_TwinCAT.md`:
   - Thêm cảnh báo Errata và đổi toàn bộ bước test van keo sang `Y25` (Terminal Y25 trên QY40P).

### 2.3. Bảo toàn nguyên tắc Fail-Closed trong TwinCAT 3 và WPF SCADA
* **TwinCAT 3:** Các cổng biên dịch an toàn giữ nguyên trạng thái khóa:
  - `cQueueMotionCommissioned : BOOL := FALSE;` (`MAIN.TcPOU:361`) $\to$ Khóa cứng `nAutoDispatchState := 100;`.
  - `cLineAutoCommissioned : BOOL := FALSE;` (`MAIN.TcPOU:363`) $\to$ Từ chối lệnh Auto HMI với mã 11.
  - `bLegacyMotionPublisherAllow : BOOL := FALSE;` (`MAIN.TcPOU:258`) $\to$ Khóa cứng trigger lệnh chạy robot.
  - Hệ số xung encoder băng tải `rK_mm := 0.476475` (ST-5114 Channel 2 1x count mode, đã nghiệm thu ngày 2026-10-24) được giữ nguyên vẹn.
* **WPF SCADA:**
  - Ứng dụng production duy trì chế độ **Read-Only** giám sát 10 biến telemetry Gantry (`MAIN.bPlcQError`, `MAIN.bStopCommandConfirmed`, `MAIN.nAutoDispatchState`...).
  - Tọa độ công cụ TCP không có trong PLC image được hiển thị rõ ràng `--` (`UNAVAILABLE`), tuyệt đối không bịa số.
  - Chế độ mô phỏng offline (`_offlineSimulationEnabled`) hoạt động thuần túy trên bộ nhớ RAM của PC, hoàn toàn ngắt quyền ghi ADS.

---

## 3. BẰNG CHỨNG KIỂM THỬ THEO PHƯƠNG PHÁP TDD (TEST-DRIVEN DEVELOPMENT)

Nhằm đảm bảo tính chính xác tuyệt đối và ngăn ngừa hồi quy, quá trình hiệu chỉnh mã nguồn ST đã tuân thủ nghiêm ngặt chu trình TDD:

### 3.1. Giai đoạn RED (Tạo kiểm thử thất bại trước khi sửa)
Đã bổ sung bộ kiểm tra tự động `Section 16: PLC-Q Offline Source Safety & Wiring Conflict Verification` trong `App/Tests/Program.cs`:
- Kiểm tra sự tồn tại của file `PLC_Q_Session2_Logic.st`.
- Kiểm tra ngõ ra van keo phải được gán vào `Y25`.
- Kiểm tra việc loại bỏ hoàn toàn lệnh gán `Y20 := bValve_Open`.
- Kiểm tra không có bất kỳ lệnh gán nào vào `Y20..Y23` trong khối Tracking.
- Kiểm tra bit bắt tay `M1000` (Start) và `M1001` (Ack).
- Kiểm tra bit hủy `M105`.
- Kiểm tra `Y0` và `Y10` tuân thủ quy tắc Single-Writer.

**Kết quả chạy thử nghiệm RED (trước khi sửa code):**
```
FAIL PLC-Q ST: Glue valve output mapped to Y25 (physical pump trigger)
FAIL PLC-Q ST: Stale Y20 glue valve assignment is REMOVED (prevent Servo 1 torque loss)
FAIL PLC-Q ST: Servo 1..3 Enables (Y20..Y22) and Brake (Y23) have zero writes in Tracking ST (owned by MAIN)
TOTAL_TEST_FAILURES = 3
Command exited with code 1
```
$\implies$ **Xác nhận pha RED thành công: Bắt đúng 3 lỗi xung đột phần cứng.**

### 3.2. Giai đoạn GREEN (Áp dụng sửa đổi và kiểm chứng đạt 100%)
Sau khi hiệu chỉnh file nguồn `PLC_Q_Session2_Logic.st` (dòng 206), toàn bộ test suite được chạy lại:
```
dotnet run --project App/Tests/Tests.csproj
```
**Kết quả kiểm thử tự động GREEN:**
```
PASS PLC-Q ST source exists for load package
PASS PLC-Q ST: Glue valve output mapped to Y25 (physical pump trigger)
PASS PLC-Q ST: Stale Y20 glue valve assignment is REMOVED (prevent Servo 1 torque loss)
PASS PLC-Q ST: Servo 1..3 Enables (Y20..Y22) and Brake (Y23) have zero writes in Tracking ST (owned by MAIN)
PASS PLC-Q ST: Handshake Start bit is M1000
PASS PLC-Q ST: Handshake Ack bit is M1001
PASS PLC-Q ST: Abort bit is M105
PASS PLC-Q ST: Single-writer Y0 is NOT written in Tracking (owned by MAIN via SM400)
PASS PLC-Q ST: Y10 is pulsed via M110, NOT directly written
TOTAL_TEST_FAILURES = 0
```
Tất cả các bài đo hồi quy (Tracking, Conveyor, Camera, Interlock, Offline Sim) đều đạt **100% PASS** (0 failures).

---

## 4. DANH MỤC GÓI NẠP CHÍNH QUY (CANONICAL LOAD PACKAGE MANIFEST)

Chi tiết toàn bộ 30 tệp tin cấu thành gói nạp an toàn đã được xuất ra tệp `Docs/SAFE_LOAD_PACKAGE_MANIFEST_20261025.csv`. Bảng dưới đây tóm tắt các tệp tin nạp chính:

| Phân hệ | Đường dẫn tệp tin | Kích thước | SHA256 Hash | Trạng thái / Vai trò |
|---|---|:---:|---|---|
| **Mitsubishi PLC-Q** | `PLC\Mitsubishi\DATN_PLC.gxw` | 2,476,032 B | `11cf8c99e1915919ecaf995a2457dd300030b299728a96c1287b490c22a29b1b` | Dự án GX Works2 chính quy (đang mở trong PID 6628) |
| **Mitsubishi PLC-Q** | `PLC\Mitsubishi\Sources_20261019\Source\PLC_Q_Session2_Logic.st` | 11,884 B | `da435f89cd45faee41e2e0e8650530d14ed7d75f8e3390faade0e62b77c37391` | Mã nguồn ST đã sửa an toàn (`Y25` van keo) |
| **TwinCAT 3** | `PLC\TwinCAT\DATN_TwinCat.sln` | 4,494 B | `dcd9f74605454c84c5ae3a9fd34ff0c59fbf87540f64d79b4503339ccda2728d` | Solution TwinCAT 3 chính quy (TcXaeShell PID 25148) |
| **TwinCAT 3** | `PLC\TwinCAT\DATN\DATN.tsproj` | 120,877 B | `6c1c704aebe15701dcb22dfaac1cd03edacc2cb8ca7ba88488a023d29684a1d3` | Cấu hình EtherCAT Crevis Coupler & Terminal I/O |
| **TwinCAT 3** | `PLC\TwinCAT\DATN\PLC1\POUs\MAIN.TcPOU` | 55,701 B | `343a52edaa0beb44094450ea9bc76e03dbfe967614112b37cef37069cb1fa8f0` | Điều phối chính, cổng an toàn biên dịch |
| **TwinCAT 3** | `PLC\TwinCAT\DATN\PLC1\POUs\FB_QD75CommandPublisher.TcPOU` | 21,982 B | `9baf846ade9edd304f7a75be56e8724569203dc771914567ac381d44eb807cee` | FSM điều khiển M1000/M1001 và nạp D100..D114 |
| **TwinCAT 3** | `PLC\TwinCAT\DATN\PLC1\DUTs\ST_QD75_Command.TcDUT` | 1,140 B | `ef0e465103aae8a642bdeb834600a35d5bc57d2b5fe8c3751b77d1be889e32e5` | Cấu trúc bộ nhớ 15 WORD chuẩn MC Protocol |
| **WPF SCADA** | `App\DATN_WPF.sln` | 3,361 B | `6e7f496fac86102c9ef18c741aa1dde771f0a38b5d0d7357d10a33d6e6fb5255` | Solution Visual Studio chính quy |
| **WPF SCADA** | `App\Vision\PlateVision50.csproj` | 1,471 B | `05bbda94b3694723a038bfbff09ee1cd3e47ba2bc4c5240716854787a2deb8fe` | Ứng dụng SCADA giao diện người-máy (0 errors) |
| **Test Suite** | `App\Tests\Tests.csproj` | 1,584 B | `44b4234158b3fa367e185cd4134ea870d888fd87e92901e656257098f5544a00` | Bộ kiểm thử tự động hồi quy và an toàn |

---

## 5. QUY TRÌNH NẠP DÀNH CHO KỸ SƯ VẬN HÀNH TẠI HIỆN TRƯỜNG (OPERATOR LOAD CHECKLIST)

> ⚠️ **CẢNH BÁO AN TOÀN:** Kỹ sư phải có mặt trực tiếp tại tủ điện. Luôn sẵn sàng tay ấn nút E-Stop cơ học. Tuyệt đối không đứng trong vùng quét của robot gantry.

### Bước 1: Kiểm tra an toàn trước khi nạp (Disarmed Pre-Load Check)
- [ ] **1.1.** Cắt nguồn động lực driver servo gantry (ngắt contactor nguồn 3 pha cấp cho servo).
- [ ] **1.2.** Xác nhận nút E-Stop cơ học đóng tiếp điểm an toàn: Kiểm tra biến `MAIN.bSystemEStopOK = TRUE` trong TwinCAT. Ấn E-Stop thử: xác nhận rơ-le an toàn nhả và `bSystemEStopOK = FALSE`. Xoay trả E-Stop.
- [ ] **1.3.** Xác nhận nguồn 24V cấp cho module Crevis ST-2318, ST-4424 và module Mitsubishi QY40P hoạt động bình thường, mass chung (COM) thông mạch.

### Bước 2: Đối chiếu & Nạp PLC Mitsubishi Q qua GX Works2 (Bắt buộc người tại máy)
- [ ] **2.1. Sao lưu trực tiếp từ PLC:** Trên GX Works2 (đang mở PID 6628), vào menu `Online` $\to$ `Read from PLC`. Tích chọn toàn bộ Program, Parameter, Device Data. Lưu file sao lưu: `DATN_PLC_LIVE_BACKUP_20261025.gxw`.
- [ ] **2.2. So sánh trực tuyến:** Vào menu `Online` $\to$ `Verify with PLC` để so sánh giữa project trên máy và chương trình đang chạy trong bộ nhớ CPU. Xác nhận không có sửa đổi bất thường chưa được tài liệu hóa.
- [ ] **2.3. Kiểm tra chương trình Scan:** Vào `Parameter` $\to$ `PLC Parameter` $\to$ `Program Setting`. Xác nhận POU `Tracking` nằm trong danh sách Scan Program của CPU (được quét chu kỳ).
- [ ] **2.4. Kiểm tra mã nguồn ST đã cập nhật:** Mở POU `Tracking [PRG] Program [ST]`. Xác nhận dòng lệnh kích van keo là:
  ```pascal
  Y25 := bValve_Open AND NOT M105;
  ```
  Xác nhận `Y20`, `Y21`, `Y22` và `Y23` không có bất kỳ lệnh gán coil nào trong POU này.
- [ ] **2.5. Biên dịch & Nạp:** Thực hiện `Compile` $\to$ `Rebuild All` (xác nhận 0 errors). Thực hiện `Online` $\to$ `Write to PLC` để ghi chương trình xuống CPU Q06UDEH. Chuyển switch CPU sang RUN.
- [ ] **2.6. Kiểm tra trạng thái Ready:** Quan sát cờ `X0` (`QD75 READY`) phải ON, cuộn `Y0` được nuôi ON bởi `SM400`, các cờ báo lỗi trục `X8, X9, XA = OFF`.

### Bước 3: Kiểm tra & Nạp TwinCAT 3 (TcXaeShell)
- [ ] **3.1.** Mở TcXaeShell (PID 25148), mở solution `PLC\TwinCAT\DATN_TwinCat.sln`.
- [ ] **3.2.** Xác nhận các cổng an toàn trong `MAIN.TcPOU`:
  - `cQueueMotionCommissioned = FALSE`
  - `cLineAutoCommissioned = FALSE`
  - `bLegacyMotionPublisherAllow = FALSE`
- [ ] **3.3.** Thực hiện `Build` $\to$ `Rebuild Solution` (xác nhận 0 compilation errors).
- [ ] **3.4.** Kiểm tra kết nối TCP MC Protocol: Quan sát `MAIN.bPlcQConnected = TRUE` trong cửa sổ Watch.

### Bước 4: Khởi động & Giám sát trên WPF SCADA
- [ ] **4.1.** Chạy ứng dụng SCADA (qua Visual Studio hoặc lệnh `OPEN_DATN_WPF.cmd`).
- [ ] **4.2.** Xác nhận thanh trạng thái ADS: Hiển thị `Port 851 Connected` và nhịp tim Heartbeat 300 ms giữ `MAIN.bHmiLinkFresh = TRUE`.
- [ ] **4.3.** Xác nhận bảng Telemetry Gantry hiển thị đầy đủ 10 biến giám sát (PLC-Q TCP ONLINE, Stop ACK OK, Auto Dispatch State 100, TCP Coordinates hiển thị `-- UNAVAILABLE`).
- [ ] **4.4.** Thử nghiệm chế độ mô phỏng offline (nếu cần diễn tập nhận diện và trượt 4 điểm mà không kích động lực).

### Bước 5: Thử nghiệm bắt tay nguội không tải (Motionless Cold Handshake)
- [ ] **5.1.** Giữ nguyên nguồn động lực servo bị ngắt.
- [ ] **5.2.** Trong TwinCAT Watch View, kích thử cờ `bTest_WriteM1000 := TRUE;`.
- [ ] **5.3.** Quan sát trên GX Works2 Device Monitor: Cờ `M1000` chuyển sang 1. PLC-Q nhận lệnh nhưng do servo chưa cấp nguồn (`NOT X0` hoặc trục bận), FSM trả về mã lỗi phần mềm `D120 = 901` (`QD75 not ready`) và bật `M104 = TRUE`, không kích động lực.
- [ ] **5.4.** Hạ `bTest_WriteM1000 := FALSE;`. Xác nhận FSM trở về trạng thái an toàn.

---

## 6. PHÂN ĐỊNH RANH GIỚI: VIỆC ĐÃ HOÀN THÀNH OFFLINE VS VIỆC TUYỆT ĐỐI CẦN NGƯỜI TẠI MÁY

Để đảm bảo minh bạch và tuyệt đối an toàn kỹ thuật, bảng sau phân định rõ ranh giới giữa những gì trợ lý phần mềm đã xử lý hoàn chỉnh so với những công việc **bắt buộc kỹ sư vận hành phải thực hiện trực tiếp tại hiện trường và KHÔNG THỂ LÀM TỪ XA**:

| Hạng mục công việc | Trợ lý thực hiện Offline | Bắt buộc Kỹ sư tại máy | Lý do kỹ thuật & Rủi ro an toàn |
|---|:---:|:---:|---|
| **Sửa lỗi mã nguồn ST (`Y20` $\to$ `Y25`)** | **ĐÃ HOÀN THÀNH** | Không | Xử lý triệt để xung đột logic trong file nguồn với kiểm thử TDD. |
| **Chuẩn hóa tài liệu hợp đồng & Errata** | **ĐÃ HOÀN THÀNH** | Không | Đã đồng bộ thông số `Y25`, `M1000/M1001`, `0.476475 mm/count`. |
| **Biên dịch & Kiểm thử tự động hồi quy** | **ĐÃ HOÀN THÀNH** | Không | C# WPF và Test Suite đạt 100% PASS (0 errors, 0 failures). |
| **Tạo Manifest và gói sao lưu SHA-256** | **ĐÃ HOÀN THÀNH** | Không | Xuất đầy đủ manifest CSV và thư mục lưu trữ có thể rollback. |
| **Thao tác `Read from PLC` & `Verify with PLC`** | **KHÔNG ĐƯỢC PHÉP** | **BẮT BUỘC** | Cần kết nối cáp USB/Ethernet vật lý tới CPU Q06UDEH để đối soát. |
| **Thao tác `Write to PLC` (Download chương trình)** | **KHÔNG ĐƯỢC PHÉP** | **BẮT BUỘC** | Nạp chương trình vào phần cứng có thể thay đổi trạng thái ngõ ra tức thời. |
| **Thao tác `Activate Configuration` TwinCAT** | **KHÔNG ĐƯỢC PHÉP** | **BẮT BUỘC** | Khởi động lại runtime EtherCAT Master có thể gián đoạn bus I/O trường. |
| **Ấn thử nghiệm mạch dừng khẩn cấp cơ học (E-Stop)** | **KHÔNG ĐƯỢC PHÉP** | **BẮT BUỘC** | Cần ấn nút nấm vật lý để đo tiếp điểm rơ-le ngắt nguồn động lực. |
| **Đo khoảng cách vật lý Camera ↔ Robot** | **KHÔNG ĐƯỢC PHÉP** | **BẮT BUỘC** | Cần dùng thước laser/thước thép đo chính xác khoảng cách $D_{phys}$ để nạp `rCameraToWorkStart_mm`. |
| **Kiểm tra chiều di chuyển trục Z (Polarity)** | **KHÔNG ĐƯỢC PHÉP** | **BẮT BUỘC** | Cần jog chậm trục Z tại tủ điện để xác nhận tọa độ dương là kim đi xuống hay đi lên. |
| **Mở các cổng an toàn (`cQueueMotionCommissioned`)** | **KHÔNG ĐƯỢC PHÉP** | **BẮT BUỘC** | Chỉ được mở sau khi đã hoàn thành 100% các bước thử nghiệm bắt tay nguội và dry-run. |

---

## 7. THÔNG TIN LƯU TRỮ VÀ SAO LƯU AN TOÀN (BACKUP & RECOVERY)

Mọi tệp tin trước khi sửa đổi đều được sao lưu kèm bảng mã băm SHA-256 nguyên vẹn:
- Thư mục sao lưu: `Docs/BACKUP_20261025_SAFE_LOAD/`
- Tệp bảng mã băm: `Docs/BACKUP_20261025_SAFE_LOAD/SHA256SUMS.txt`
  - `PLC_Q_Session2_Logic.st`: `de0c9b5e390780e5d97727d411cb0107c4b126deb7c7cad118fc949d17a0287a`
  - `Session2_Contract_For_Session1.md`: `669ad6fced0f1e6ab84efd9566de0545c977b4497f2fb609e105115e7639d82e`
  - `Session2_Mitsubishi_Q_Guide.md`: `648b35a2813c188c5cbaf16e41ec442c9d91b3950662017970671e6e09caa77e`
  - `Session2_BenchTest_Without_TwinCAT.md`: `d557bee32fb382319604d8bef31b799ab074a8a04c5f9bf3a73216271c516570`
  - `Program.cs`: `af627bddff4099e1637e01c970cfe18d348b4082a260babe17d70c34bbb950da`

Khi cần khôi phục lại trạng thái cũ, kỹ sư chỉ cần sao chép các tệp tin từ thư mục này ghi đè về vị trí tương ứng.

---

## 8. KẾT LUẬN & CHUYỂN GIAO TIẾP THEO

Toàn bộ gói mã nguồn của dự án DATN đã được rà soát, dọn dẹp các xung đột ngõ ra chí mạng, và xác thực thành công 100% ở cấp độ kiểm thử phần mềm offline. Kỹ sư vận hành tại hiện trường hiện đã có đầy đủ cơ sở kỹ thuật, tài liệu quy chuẩn và danh mục tệp tin để tiến hành **Bước 2 (Đối chiếu & Nạp PLC Mitsubishi Q trong GX Works2)** theo đúng quy trình an toàn đã nêu tại Mục 5.
