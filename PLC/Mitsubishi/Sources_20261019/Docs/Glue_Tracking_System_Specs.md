# TÀI LIỆU KỸ THUẬT & ĐẶC TẢ HỆ THỐNG ĐIỂM KEO 4 ĐỈNH BÁM BĂNG TẢI
## (Conveyor Tracking Dispensing System Specs)

---

## 1. TỔNG QUAN DỰ ÁN & MỤC TIÊU KỸ THUẬT

* **Bài toán**: Chấm keo tại đúng 4 góc/đỉnh của sản phẩm hình vuông kích thước $40 \times 40\text{ mm}$ ngẫu nhiên đang di chuyển liên tục trên băng tải (không dừng băng tải).
* **Cơ cấu chấp hành**: Robot Gantry 3 trục Cartesian ($X, Y, Z$).
* **Vùng làm việc cơ khí**: $180\text{ mm} \times 170\text{ mm} \times 52\text{ mm}$ (Trục $X$ dọc theo hướng dòng chảy băng tải).
* **Đầu công tác (Tool/TCP)**: Van xả keo tốc độ cao gắn trực tiếp trên trục $Z$ của Gantry.
* **Thời gian chu trình yêu cầu**: Hoàn thành toàn bộ 4 đỉnh trong vòng $\le 1.2\text{ giây}$ (khi phôi còn nằm trong vùng hành trình $180\text{ mm}$).

---

## 2. DANH MỤC PHẦN CỨNG & THÔNG SỐ CÀI ĐẶT

### 2.1. Cụm điều khiển Robot Gantry (Mitsubishi Electric)
* **Bộ điều khiển (CPU)**: Mitsubishi Q6UDEH (tích hợp cổng Ethernet nội tại).
* **Module phát xung điều khiển chuyển động**: Mitsubishi QD75D4 (4 trục Differential Line Driver).
* **Cài đặt thông số QD75D4 (Basic Parameters)**:
  * `Unit setting`: `0: mm`
  * `No. of pulses per rotation`: `10000 pulse`
  * `Movement amount per rotation`: `1000.0 um`
  * `Unit magnification`: `10: x10 Times` $\rightarrow$ Độ dịch chuyển 1 vòng: $10.0\text{ mm/rev}$.
  * `Độ phân giải vị trí phát xung`: $\frac{10.0\text{ mm}}{10000\text{ xung}} = 0.001\text{ mm/xung} = 1.0\ \mu\text{m/xung}$.
  * `Đơn vị vị trí trong thanh ghi QD75`: **$0.1\ \mu\text{m}$** ($1\text{ mm} = 10000\text{ đơn vị}$).
  * `Vận tốc giới hạn (Speed limit value)`: `60000.00 mm/min` = **$1000.0\text{ mm/s}$**.
  * `Đơn vị vận tốc trong thanh ghi QD75`: **$0.01\text{ mm/min}$** ($1\text{ mm/s} = 6000\text{ đơn vị}$).
  * `Thời gian tăng tốc 0 (Acceleration time 0)`: **$100\text{ ms}$** $\rightarrow$ Gia tốc $a = 10000\text{ mm/s}^2$ ($~1.0g$).
  * `Thời gian giảm tốc 0 (Deceleration time 0)`: **$100\text{ ms}$** $\rightarrow$ Gia tốc $d = 10000\text{ mm/s}^2$.
  * `Hành trình giới hạn mềm (Stroke Limit)`:
    * Trục $X$ (Axis 1): `0.0 um` đến `180000.0 um` ($0 \sim 180\text{ mm}$).
    * Trục $Y$ (Axis 2): `0.0 um` đến `170000.0 um` ($0 \sim 170\text{ mm}$).
    * Trục $Z$ (Axis 3): `0.0 um` đến `52000.0 um` ($0 \sim 52\text{ mm}$).

### 2.2. Trạm Master & Xử lý ảnh (Beckhoff TwinCAT 3 PC)
* **Phần mềm**: TwinCAT 3 eXtended Automation (XAE / XAR), chu kỳ task `PlcTask` = **$10\text{ ms}$**.
* **Camera Vision**: Kết nối trực tiếp vào máy tính TwinCAT (GigE / USB3 Vision), máy tính chạy đồng thời thuật toán xử lý ảnh nhận diện góc xoay $\theta$ và tâm $(X_c, Y_c)$.
* **Trạm thu thập dữ liệu EtherCAT (Crevis FnIO S-Series)**:
  * Coupler: NA-9286.
  * Slot 12 (ST-5114): High-speed counter 24V sink, cấu hình **2-Input Encoder Mode (x4)** đọc xung encoder băng tải.
  * Slot 7 (ST-2318): 8 DO Sink 24V (kích chạy/dừng biến tần MD200).
  * Slot 11 (ST-4424): 4 AO 0~10V (xuất áp đặt tốc độ băng tải cho MD200).
* **Encoder đo băng tải**:
  * Mã: LPD3806-360BM-G5-24C (NPN Open-collector, 360 xung/vòng).
  * Đường kính bánh xe tì: $D = 54.6\text{ mm}$.
  * Chu vi bánh xe: $C = \pi \times 54.6 \approx 171.531\text{ mm}$.
  * Hệ số quy đổi cơ khí: $rK = \frac{171.531}{360} \approx \mathbf{0.476475\text{ mm/xung}}$.
  * Trở kéo lên (Pull-up): 2 điện trở $2.2\text{ k}\Omega$ nối từ $+24\text{V}$ vào chân Ch0 (Pha A) và Ch1 (Pha B).
* **Biến tần kéo băng tải (Inovance MD200)**:
  * Cài đặt tốc độ chạy thực tế: $V_{conv} \approx \mathbf{100\text{ mm/s}}$ (tần số khoảng $28\text{ Hz}$).

---

## 3. KIẾN TRÚC PHÂN CHIA NHIỆM VỤ HỆ THỐNG

```text
  [Camera Vision (GigE)]
           │ Chụp ảnh phôi
           ▼
┌─────────────────────────────────────────────────────────────┐
│ TWINCAT 3 (BỘ NÃO TRUNG TÂM - MASTER)                       │
│ 1. Xử lý ảnh: Trích xuất tọa độ tâm (Xc, Yc) và góc lệch θ. │
│ 2. Đọc Encoder ST-5114: Tích phân tọa độ trôi theo mm.      │
│ 3. Bộ lọc vận tốc: Lọc phẳng dao động (100ms moving avg).   │
│ 4. Tính toán hình học: Xoay ma trận 2D ra 4 đỉnh tĩnh.      │
│ 5. Đón đầu chuyển động: Cộng độ trôi thời gian thực.        │
│ 6. Quy đổi đơn vị: Sang 0.1 um và 0.01 mm/min.             │
│ 7. Giao tiếp Ethernet: Điều khiển tuần tự từng đỉnh.       │
└─────────────────────────────────────────────────────────────┘
           │ Gửi Tọa độ & Lệnh (D100..D112, M100) qua Ethernet
           ▼
┌─────────────────────────────────────────────────────────────┐
│ MITSUBISHI PLC Q6UDEH + QD75D4 (CƠ CẤU CHẤP HÀNH - SLAVE)  │
│ 1. Nhận tọa độ đã dọn sẵn (không cần tính toán lượng giác).│
│ 2. Nạp số vào 3 dòng Positioning Data nội suy 3 trục.       │
│ 3. Phát xung điều khiển Gantry XYZ chạy tiếp cận đỉnh.      │
│ 4. Hạ Z, trượt đồng tốc X trong 60ms, xả keo 30ms.          │
│ 5. Nhấc Z lên an toàn, bật M101 báo xong để nhận đỉnh kế.   │
└─────────────────────────────────────────────────────────────┘
```

---

## 4. BẢNG PHÂN BỔ THANH GHI TRUYỀN THÔNG ETHERNET
*(Giao thức: MC Protocol Q-na 3E Frame - Giao tiếp nhị phân Direct Memory Access)*

### 4.1. Dữ liệu từ TwinCAT 3 gửi sang PLC-Q (Lệnh & Tham số)

| Vùng nhớ | Tên biến | Kiểu dữ liệu | Đơn vị quy đổi | Mô tả chức năng |
| :--- | :--- | :---: | :---: | :--- |
| **D100 - D101** | `Target_X` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Tọa độ $X$ điểm đón đầu của đỉnh hiện tại ($1\text{ mm} = 10000$) |
| **D102 - D103** | `Target_Y` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Tọa độ $Y$ của đỉnh hiện tại ($1\text{ mm} = 10000$) |
| **D104 - D105** | `Target_Z_Down` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Độ cao hạ kim để chấm keo (ví dụ $4.0\text{ mm} = 40000$) |
| **D106 - D107** | `Speed_Approach`| DINT (32-bit) | $0.01\text{ mm/min}$ | Vận tốc bay nội suy 3 trục ($1\text{ mm/s} = 6000$) |
| **D108 - D109** | `Speed_Sync` | DINT (32-bit) | $0.01\text{ mm/min}$ | Vận tốc đồng tốc trục $X$ khi xả keo ($= V_{conv} \times 6000$) |
| **D110 - D111** | `DeltaX_Sync` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Quãng đường trượt đồng tốc ($= V_{conv} \times 0.06\text{s} \times 10000$) |
| **D112 - D113** | `Target_Z_Safe` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Độ cao an toàn khi bay (ví dụ $0.0\text{ mm} = 0$) |
| **D114** | `Vertex_Index` | INT (16-bit) | Số nguyên | Thứ tự đỉnh đang thi công ($1, 2, 3, 4$) |
| **M100** | `Cmd_Start_Vertex`| BIT | Boolean | Cạnh lên kích hoạt PLC-Q chạy đỉnh đang nạp |
| **M105** | `Cmd_Cycle_Reset` | BIT | Boolean | Lệnh hủy chu trình khẩn cấp / đưa Robot về gốc |

### 4.2. Dữ liệu từ PLC-Q phản hồi về TwinCAT 3 (Trạng thái)

| Vùng nhớ | Tên biến | Kiểu dữ liệu | Ý nghĩa kỹ thuật |
| :--- | :--- | :---: | :--- |
| **M101** | `Ack_Vertex_Done` | BIT | PLC-Q bật ON = Đã hạ, đồng tốc bắn keo và nhấc Z xong |
| **M102** | `Robot_Busy` | BIT | Gantry đang chuyển động |
| **M103** | `Robot_In_Position`| BIT | Trục đã vào vùng đích (In-position) |
| **M104** | `Robot_Alarm` | BIT | Module QD75D4 hoặc Driver Servo báo lỗi |

---

## 5. THUẬT TOÁN ĐIỀU KHIỂN & TOÁN HỌC TRÊN TWINCAT 3

### 5.1. Ma trận xoay tính 4 đỉnh tĩnh tại thời điểm chụp
* Đầu vào: Kích thước phôi $L = 40.0\text{ mm}, W = 40.0\text{ mm}$; Tâm phôi $(X_c, Y_c)$; Góc nghiêng $\theta$ (độ).
* Nửa cạnh: $dx = 20.0\text{ mm}, dy = 20.0\text{ mm}$.
* Góc radian: $rad = \theta \times \frac{\pi}{180.0}$.
* Tọa độ 4 đỉnh tương đối:
  $$\begin{cases} 
  X_{1\_trig} = X_c + (dx\cos\theta - dy\sin\theta), & Y_{1\_trig} = Y_c + (dx\sin\theta + dy\cos\theta) \\
  X_{2\_trig} = X_c + (-dx\cos\theta - dy\sin\theta), & Y_{2\_trig} = Y_c + (-dx\sin\theta + dy\cos\theta) \\
  X_{3\_trig} = X_c + (-dx\cos\theta + dy\sin\theta), & Y_{3\_trig} = Y_c + (-dx\sin\theta - dy\cos\theta) \\
  X_{4\_trig} = X_c + (dx\cos\theta + dy\sin\theta), & Y_{4\_trig} = Y_c + (dx\sin\theta - dy\cos\theta)
  \end{cases}$$

### 5.2. Tính toán đón đầu & bù trôi thời gian thực (Interception Math)
* Mốc xung encoder lúc chụp: $N_{trig} = nChA$.
* Tại thời điểm chuẩn bị gửi lệnh đỉnh $k$:
  * Đọc xung hiện tại: $nChA_{current}$.
  * Quãng đường vật đã trôi từ lúc chụp:
    $$\Delta X_{drift} = (nChA_{current} - N_{trig}) \times rK_{mm}$$
  * Bù trước khoảng trôi trong thời gian Robot bay tiếp cận ($t_{fly} \approx 0.12\text{ s}$):
    $$\Delta X_{fly} = rSpeed\_Filtered \times 0.12$$
  * Tọa độ đón đầu thực tế gửi sang PLC-Q:
    $$X_{target\_k} = X_{k\_trig} + \Delta X_{drift} + \Delta X_{fly}$$
    $$Y_{target\_k} = Y_{k\_trig}$$

### 5.3. Quy đổi hệ đơn vị sang Mitsubishi QD75
```iec
// Vị trí (0.1 um)
D100_Target_X := LREAL_TO_DINT(X_target_k * 10000.0);
D102_Target_Y := LREAL_TO_DINT(Y_target_k * 10000.0);
D104_Target_Z := LREAL_TO_DINT(4.0 * 10000.0); // Độ cao chấm 4.0 mm

// Vận tốc (0.01 mm/min)
D106_Speed_App := LREAL_TO_DINT(500.0 * 6000.0); // Vận tốc bay 500 mm/s
D108_Speed_Sync := LREAL_TO_DINT(rSpeed_Filtered * 6000.0); // Đồng tốc bằng tốc độ băng tải

// Khoảng trượt đồng tốc 60ms (0.1 um)
D110_DeltaX_Sync := LREAL_TO_DINT((rSpeed_Filtered * 0.060) * 10000.0);
```

---

## 6. CẤU HÌNH ĐỊNH VỊ NỘI SUY 3 TRỤC TRÊN QD75D4 (GX WORKS2)

Cài đặt cố định 3 dòng **Positioning Data** chạy theo chuỗi liên hoàn cho mỗi đỉnh:

| Positioning Data | `Operation Pattern` | `Control System` | Trục X | Trục Y | Trục Z | `Command Speed` | Hành vi chuyển động |
| :---: | :---: | :---: | :---: | :---: | :---: | :---: | :--- |
| **Point 1** | **`Continuous (Cont)`** | **`3-axis Linear (ABS)`** | `D100` | `D102` | `D104` ($Z_{hạ}$) | `D106` ($V_{bay}$) | Bay nội suy 3 trục từ trên cao đáp thẳng xuống góc phôi. |
| **Point 2** | **`Continuous (Cont)`** | **`3-axis Linear (ABS)`** | `D100 + D110` | `D102` (giữ nguyên) | `D104` (giữ nguyên) | `D108` ($V_{sync}$) | Trục $X$ trượt theo băng tải; trục $Y, Z$ đứng im. Van keo mở $30\text{ ms}$. |
| **Point 3** | **`End (Locate)`** | **`3-axis Linear (ABS)`** | `D100 + D110` | `D102` (giữ nguyên) | `D112` ($Z_{safe}$) | `D106` ($V_{bay}$) | Trục $Z$ rút lên vị trí an toàn; trục $X, Y$ đứng im. Bật $M101$ báo xong. |

---

## 7. KẾ HOẠCH TRIỂN KHAI TỪNG BƯỚC (STEP-BY-STEP ROADMAP)

```text
[Giai đoạn 1] Cấu hình & Tinh chỉnh phần cứng độc lập
   ├── Bước 1.1: Cài đặt QD75D4 (Gia tốc 100ms, Stroke limit 180mm).
   ├── Bước 1.2: Đấu nối & xác thực Encoder 2 kênh ST-5114 trên TwinCAT 3.
   └── Bước 1.3: Cài đặt biến tần MD200 chạy ổn định ở tốc độ 100 mm/s.

[Giai đoạn 2] Lập trình & Kiểm thử trên TwinCAT 3
   ├── Bước 2.1: Viết Function Block tính ma trận xoay 4 đỉnh tĩnh.
   ├── Bước 2.2: Tích hợp bộ lọc vận tốc băng tải (Moving Average 100ms).
   └── Bước 2.3: Viết thuật toán cộng độ trôi thời gian thực & quy đổi sang đơn vị 0.1 um.

[Giai đoạn 3] Lập trình PLC-Q (GX Works2) & Truyền thông Ethernet
   ├── Bước 3.1: Thiết lập cấu hình truyền thông Ethernet giữa PC và Q6UDEH.
   ├── Bước 3.2: Viết chương trình Ladder nạp dữ liệu D100..D112 vào Buffer QD75D4.
   └── Bước 3.3: Viết chuỗi điều khiển FSM kích chạy Point 1->2->3 và bật van keo 30ms.

[Giai đoạn 4] Tích hợp hệ thống & Chạy thử nghiệm thực tế
   ├── Bước 4.1: Bắt tay tín hiệu M100/M101 từng đỉnh độc lập (Single Step).
   ├── Bước 4.2: Chạy liên hoàn 4 đỉnh trên sản phẩm đặt tĩnh (không chạy băng tải).
   └── Bước 4.3: Chạy liên hoàn 4 đỉnh khi băng tải đang chuyển động 100 mm/s.
```

---

## 8. HƯỚNG DẪN PHÂN TÁCH 2 SESSION ĐỘC LẬP (PARALLEL WORKFLOW)

Để triển khai song song trên 2 cửa sổ/phiên làm việc (sessions) mà không bị xung đột hay lẫn lộn code, dự án được chia đôi thành 2 nhánh hoàn toàn độc lập, giao tiếp với nhau duy nhất qua một **Hợp đồng giao diện (Interface Contract)**:

```text
┌────────────────────────────────────────────────────────┐
│                   HỢP ĐỒNG GIAO DIỆN CHUNG             │
│                 (SHARED INTERFACE CONTRACT)            │
│  - D100..D101: Target_X (0.1 um)                       │
│  - D102..D103: Target_Y (0.1 um)                       │
│  - D104..D105: Target_Z_Down (0.1 um)                  │
│  - D106..D107: Speed_Approach (0.01 mm/min)            │
│  - D108..D109: Speed_Sync (0.01 mm/min)                │
│  - D110..D111: DeltaX_Sync (0.1 um)                    │
│  - D112..D113: Target_Z_Safe (0.1 um)                  │
│  - D114      : Vertex_Index (1..4)                     │
│  - M100      : Cmd_Start_Vertex (TwinCAT -> Q)         │
│  - M101      : Ack_Vertex_Done  (Q -> TwinCAT)         │
└────────────────────────────────────────────────────────┘
            ▲                                  ▲
            │ (Ghi dữ liệu / Bật M100)         │ (Đọc dữ liệu / Bật M101)
            │                                  │
┌───────────────────────────────┐  ┌───────────────────────────────────┐
│     SESSION 1: TWINCAT 3      │  │      SESSION 2: MITSUBISHI Q      │
│  (MASTER / VISION & TRACKING) │  │    (SLAVE GANTRY XYZ & DISPENSE)  │
│  - Cài đặt / IDE: TwinCAT 3   │  │  - Cài đặt / IDE: GX Works2       │
│  - File làm việc: ST POUs     │  │  - File làm việc: Ladder / SFC    │
│  - Không đụng vào code PLC-Q  │  │  - Không đụng vào code TwinCAT 3  │
└───────────────────────────────┘  └───────────────────────────────────┘
```

---

### 8.1. PHẠM VI SESSION 1: TWINCAT 3 (MASTER / VISION & COMPUTATION)

* **Trách nhiệm**:
  1. **Khối Vision**: Kết nối Camera, trích xuất tâm $(X_c, Y_c)$ và góc xoay $\theta$ khi có trigger.
  2. **Khối Encoder**: Đọc 2 kênh A/B module ST-5114, lọc vận tốc trung bình trượt 100ms ($rSpeed\_Filtered$).
  3. **Khối Tính toán Hình học**: Tính 4 đỉnh phôi $40 \times 40\text{ mm}$ theo ma trận xoay 2D.
  4. **Khối Bám tọa độ & Đón đầu**: Đọc độ trôi tức thời, cộng bù vận tốc bay $t_{fly} \approx 120\text{ ms}$, quy đổi toàn bộ sang đơn vị $0.1\ \mu\text{m}$ và $0.01\text{ mm/min}$.
  5. **Khối Truyền thông Ethernet Client**: Viết hàm kết nối Socket/MC Protocol ghi dữ liệu vào `D100..D114` của Q6UDEH và thực hiện bắt tay $M100 \rightarrow M101$.
* **Cách Test độc lập (Không cần Robot Gantry)**:
  - Dùng phần mềm giả lập PLC Mitsubishi (GX Simulator 2) hoặc viết một biến nội bộ mô phỏng $M101$ tự động bật lên sau $300\text{ ms}$ để kiểm chứng thuật toán bám 4 đỉnh và tính toán bù trôi trên màn hình Watch.
* **Checklist hoàn thành Session 1**:
  - [ ] FB tính toán ma trận xoay 4 đỉnh chạy đúng với góc $\theta$ bất kỳ.
  - [ ] Encoder đọc êm, vận tốc không nhảy cóc quanh $100\text{ mm/s}$.
  - [ ] Dữ liệu đóng gói sang DINT ($0.1\ \mu\text{m}$) chuẩn xác.
  - [ ] Vòng lặp tuần tự gửi Đỉnh 1 $\rightarrow$ chờ $M101$ $\rightarrow$ gửi Đỉnh 2 $\rightarrow$ 3 $\rightarrow$ 4 hoạt động mượt mà.

---

### 8.2. PHẠM VI SESSION 2: MITSUBISHI PLC-Q & QD75D4 (MOTION & DISPENSING FSM)

* **Trách nhiệm**:
  1. **Cấu hình QD75D4 (GX Configurator-QP / GX Works2)**:
     - Basic Parameters: $1000.0\ \mu\text{m} \times 10 / 10000\text{ pulses}$, Acc/Dec = $100\text{ ms}$, Speed limit = $60000.00\text{ mm/min}$.
     - Stroke limit: $X = 180\text{ mm}, Y = 170\text{ mm}, Z = 52\text{ mm}$.
  2. **Cài đặt 3 Point nội suy 3 trục trên QD75D4**:
     - Point 1: 3-axis Linear (ABS, Cont) bay đến $(Target\_X, Target\_Y, Target\_Z\_Down)$.
     - Point 2: 3-axis Linear (ABS, Cont) trượt $X$ thêm $DeltaX\_Sync$, giữ $Y, Z$. Đồng thời mở van keo $Y0$ trong $30\text{ ms}$ (Timer $T0$).
     - Point 3: 3-axis Linear (ABS, End) nhấc $Z$ lên $Target\_Z\_Safe$, giữ $X, Y$.
  3. **Lập trình Ladder FSM (Q6UDEH)**:
     - Bắt cạnh lên $M100$ từ TwinCAT $\rightarrow$ Nạp `D100..D113` vào Buffer QD75D4 $\rightarrow$ Kích lệnh phát xung `ZP.PSTRT`.
     - Chờ Point 3 kết thúc (`Md.31` Axis Status In-position) $\rightarrow$ Kích $M101 = \text{ON}$ báo TwinCAT.
     - Xóa $M101$ khi $M100$ tắt.
* **Cách Test độc lập (Không cần TwinCAT 3 & Camera)**:
  - Trên GX Works2, người lập trình tự gõ số mẫu vào `D100..D114` (ví dụ: $X=50\text{ mm} \rightarrow 500000$, $Y=40\text{ mm} \rightarrow 400000$, $V=500\text{ mm/s} \rightarrow 3000000$) rồi ép bit $M100 = \text{ON}$ để kiểm tra Robot Gantry có bay đúng tọa độ, hạ Z, trượt X và xả van keo $30\text{ ms}$ hay không.
* **Checklist hoàn thành Session 2**:
  - [ ] QD75D4 phát xung 3 trục nội suy mượt mà, không giật khựng, không dính lỗi `Error 517`.
  - [ ] Van keo đóng mở chuẩn xác $30\text{ ms}$ ngay trong lúc trục X đang trượt đồng tốc.
  - [ ] Trục Z rút lên an toàn sau khi bơm keo xong.
  - [ ] Bit $M101$ bật đúng lúc và tự tắt khi $M100$ ngắt.
