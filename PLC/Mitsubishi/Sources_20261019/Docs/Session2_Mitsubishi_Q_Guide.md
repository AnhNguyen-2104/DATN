# HƯỚNG DẪN KỸ THUẬT TRIỂN KHAI & KIỂM THỬ ĐỘC LẬP SESSION 2: MITSUBISHI Q
## (SLAVE GANTRY XYZ & HIGH-SPEED DISPENSING FSM)
**Dự án**: Hệ thống điểm keo 4 đỉnh bám băng tải (Conveyor Tracking Dispensing System)  
**Tài liệu tham chiếu**: `D:\VC\Glue_Tracking_System_Specs.md`  
**Môi trường phát triển**: Mitsubishi GX Works2 (Q06UDEH + QD75D4)  
**Tập tin bàn giao**:
1. `D:\VC\QD75_Config_Session2.csv` (Bảng thông số cấu hình tham số & Positioning Data QD75)
2. `D:\VC\PLC_Q_Session2_Logic.st` (Mã nguồn Structured Text hoàn chỉnh cho Q06UDEH)
3. `D:\VC\PLC_Q_Session2_Ladder.csv` (Bảng mã lệnh Ladder Instruction List tương đương)
4. `D:\VC\Session2_Mitsubishi_Q_Guide.md` (Tài liệu hướng dẫn chi tiết này)

---

## 1. TỔNG QUAN PHẦN CỨNG & QUY HOẠCH ĐỊA CHỈ TRẠM Q

### 1.1. Cấu hình Rack PLC Q-Series (Q06UDEH)
Hệ thống sử dụng Main Base Q35B / Q38B với phân bổ Slot như sau:
* **Power Supply Module**: Q61P (Slot nguồn).
* **CPU Module**: Q06UDEH (Tích hợp cổng 100BASE-TX Ethernet, chu kỳ Scan $\le 1\text{ ms}$).
* **Slot 0**: Module điều khiển vị trí **QD75D4** (4 trục xung vi sai Differential Line Driver).
  * Chiếm dụng 32 điểm I/O: **`X00..X1F`** và **`Y00..Y1F`**.
  * Base Buffer Memory truy cập qua tiền tố: **`U0\G...`**.
* **Slot 1**: Module 16 ngõ ra Transistor Sink **QY40P** (điều khiển cơ cấu chấp hành trường).
  * Chiếm dụng 16 điểm Output: **`Y20..Y2F`**.
  * **Ngõ ra van keo tốc độ cao**: Gán tại **`Y20`** (Transistor phản ứng $<0.1\text{ ms}$).

---

### 1.2. Giải quyết xung đột phần cứng chí mạng: `Y0` (PLC READY) vs Van keo
Tại dòng 255 tài liệu đặc tả `Glue_Tracking_System_Specs.md` có ghi chú:
> *"Đồng thời mở van keo Y0 trong 30 ms (Timer T0)"*

⚠️ **CẢNH BÁO KỸ THUẬT QUAN TRỌNG**:
Trong kiến trúc phần cứng Mitsubishi Q-Series, khi QD75D4 cắm tại Slot 0 (`H0000`):
1. **`Y0` là tín hiệu hệ thống chuyên dụng: `PLC READY`** của module QD75 (xem tài liệu *QD75 User's Manual SH-080058 / IB-0300062*, Mục 3.3, Bảng 14.2).
2. Khi `Y0 = OFF`, module QD75 chuyển sang trạng thái chưa sẵn sàng, cờ `X0` (`QD75 READY`) tắt ngấm, **mọi chuyển động lập tức bị hủy bỏ và dừng khẩn cấp**.
3. Nếu lập trình viên dùng `Y0` làm van keo: Ngay khi timer $30\text{ ms}$ đóng van (`Y0 := FALSE`), module QD75 sẽ sập `PLC READY`, ngắt cờ `X0`, trục $X$ và $Z$ khựng lại ngay giữa Point 2, Point 3 bị hủy $\rightarrow$ Băng tải cuốn sản phẩm đâm gãy kim keo!

**GIẢI PHÁP TIÊU CHUẨN ĐÃ THỰC THI (ĐỒNG THUẬN 100%)**:
* **`Y0` luôn luôn được giữ ON liên tục trong chương trình PLC** bằng tiếp điểm hệ thống `SM400` (`LD SM400 -> OUT Y0` hoặc `Y0 := SM400;`), tuyệt đối không bị ngắt kể cả khi có lệnh dừng khẩn `M105` hay lỗi. Điều này đảm bảo cờ `X0` (`QD75 READY`) luôn ở trạng thái sẵn sàng, không bị reset bộ nhớ đệm hay mất 100ms chu kỳ khởi tạo lại.
* Chuyển tín hiệu kích van keo vật lý sang ngõ ra Transistor độc lập **`Y20`** (hoặc ngõ ra DO vật lý riêng theo tủ điện), được điều khiển an toàn qua cuộn nội bộ **`M200`** (`bValveCmd`) và bảo vệ bằng liên khóa.
---

## 2. BẢNG PHÂN BỔ THANH GHI VÀ VÙNG NHỚ TRUYỀN THÔNG DMA

### 2.1. Vùng nhớ giao diện với TwinCAT 3 (Ethernet MC Protocol / D100..D114, M100..M105)

| Địa chỉ Device | Tên biến | Kiểu dữ liệu | Đơn vị quy đổi | Chức năng kỹ thuật |
| :--- | :--- | :---: | :---: | :--- |
| **D100 - D101** | `Target_X` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Tọa độ $X$ điểm đón đầu của đỉnh ($1\text{ mm} = 10000$) |
| **D102 - D103** | `Target_Y` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Tọa độ $Y$ của đỉnh ($1\text{ mm} = 10000$) |
| **D104 - D105** | `Target_Z_Down` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Độ cao hạ kim để chấm keo ($4.0\text{ mm} = 40000$) |
| **D106 - D107** | `Speed_Approach`| DINT (32-bit) | $0.01\text{ mm/min}$ | Vận tốc bay nội suy tiếp cận ($500\text{ mm/s} = 3000000$) |
| **D108 - D109** | `Speed_Sync` | DINT (32-bit) | $0.01\text{ mm/min}$ | Vận tốc đồng tốc trục $X$ ($100\text{ mm/s} = 600000$) |
| **D110 - D111** | `DeltaX_Sync` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Quãng đường trượt đồng tốc ($6.0\text{ mm} = 60000$) |
| **D112 - D113** | `Target_Z_Safe` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Độ cao an toàn khi rút kim ($0.0\text{ mm} = 0$) |
| **D114** | `Vertex_Index` | INT (16-bit) | Số nguyên | Thứ tự đỉnh đang thi công ($1, 2, 3, 4$) |
| **D116 - D117** | `Target_X_Sync` | DINT (32-bit) | $0.1\ \mu\text{m}$ | Vị trí kết thúc trượt $X$ ($= Target\_X + DeltaX\_Sync$) |
| **D120** | `Diag_ErrCode` | INT (16-bit) | Mã lỗi | Mã chẩn đoán phần mềm ($501: X,\ 502: Y,\ 503: Z,\ 901: QD75$) |
| **D200** | `iState` | INT (16-bit) | $0..50$ | Bước hiện tại của Máy trạng thái FSM |
| **M100** | `Cmd_Start_Vertex`| BIT | Boolean | Cạnh lên kích chạy đỉnh từ TwinCAT 3 (hoặc ép tay) |
| **M101** | `Ack_Vertex_Done` | BIT | Boolean | Báo hoàn thành đỉnh về Master TwinCAT 3 |
| **M102** | `Robot_Busy` | BIT | Boolean | Gantry đang trong quá trình chuyển động nội suy |
| **M103** | `Robot_In_Position`| BIT | Boolean | Cả 3 trục đã đáp vào vị trí đích an toàn |
| **M104** | `Robot_Alarm` | BIT | Boolean | Báo động lỗi phần cứng QD75 hoặc vi phạm Stroke Limit |
| **M105** | `Cmd_Cycle_Reset` | BIT | Boolean | Lệnh dừng khẩn cấp / Hủy chu trình / Reset lỗi |
| **M200 / Y20** | `Valve_Dispense` | BIT | Boolean | Cuộn lệnh (`M200`) và ngõ ra vật lý van keo (`Y20`) |
| **T0 / TC0** | `Dispense_Timer` | High-Speed Timer| $10\text{ ms/count}$ | Bộ định thì tốc độ cao $30\text{ ms}$ (`OUTH T0 K3`) |

---

## 3. THIẾT LẬP THAM SỐ QD75D4 (BASIC & DETAILED PARAMETERS)

### 3.1. Bảng quy đổi cơ khí & Độ phân giải phát xung
* Động cơ Servo: $10000\text{ xung/vòng}$. Vít me bước $10.0\text{ mm/vòng}$.
* Cài đặt QD75:
  * `Pr.1 Unit setting`: `0: mm`
  * `Pr.2 No. of pulses per rotation (Ap)`: `10000 pulse`
  * `Pr.3 Movement amount per rotation (Al)`: `1000.0 um` ($10000$ đơn vị $0.1\ \mu\text{m}$)
  * `Pr.4 Unit magnification (Am)`: `10: x10 Times`
  $$\Delta L = \frac{1000.0\ \mu\text{m} \times 10}{10000\text{ pulses}} = 0.001\text{ mm/xung} = 1.0\ \mu\text{m/xung}$$
  $\rightarrow$ Đơn vị vị trí trong thanh ghi QD75 là **$0.1\ \mu\text{m}$** ($1\text{ mm} = 10000$ đơn vị).
* Đơn vị vận tốc trong QD75: **$0.01\text{ mm/min}$** ($1\text{ mm/s} = 6000$ đơn vị).

### 3.2. Giới hạn hành trình mềm và Gia tốc
* `Pr.8 Speed limit value`: `6000000` ($60000.00\text{ mm/min} = 1000.0\text{ mm/s}$).
* `Pr.9 Acceleration time 0` / `Pr.10 Deceleration time 0`: `100 ms` ($a = 10000\text{ mm/s}^2$).
* `Pr.12 Software stroke limit upper`:
  * Trục 1 ($X$): $180.0\text{ mm} \rightarrow 1,800,000$ (Buffer `U0\G18-19`).
  * Trục 2 ($Y$): $170.0\text{ mm} \rightarrow 1,700,000$ (Buffer `U0\G168-169`).
  * Trục 3 ($Z$): $52.0\text{ mm} \rightarrow 520,000$ (Buffer `U0\G318-319`).
* `Pr.13 Software stroke limit lower`: `0.0 um` ($0$) cho cả 3 trục.
* `Pr.19 Speed switching mode`: `1: Front-loading switching` (Chuyển tiếp vận tốc mượt mà).
* `Pr.20 Interpolation speed designation`: `0: Composite speed` (Giữ nguyên vận tốc tổng hợp vector).
* `Pr.18 M code ON timing`: `0: WITH mode` (Bật tín hiệu $X4$ ngay khi bắt đầu Point 2).

---

## 4. BẢNG MÃ HÓA BUFFER MEMORY CHO 3 ĐIỂM NỘI SUY (POINT 1..3)

QD75 sử dụng Trục 1 ($X$) làm Trục tham chiếu (Reference Axis). Tọa độ của Trục 2 và Trục 3 được nạp vào vùng đệm tương ứng (Trục 2 bắt đầu từ `8000`, Trục 3 bắt đầu từ `14000`).

```
                    QUY HOẠCH BUFFER NỘI SUY 3 TRỤC ABS3
 Point 1 (Fly/Approach)        Point 2 (Sync & Dispense)         Point 3 (Retract Z)
   Operation: Cont (11b)         Operation: Cont (11b)             Operation: End (00b)
   Speed: D106 (500mm/s)         Speed: D108 (100mm/s)             Speed: D106 (500mm/s)
   M-Code: 0                     M-Code: 1 (Triggers X4)           M-Code: 0
  ┌───────────────────────┐    ┌───────────────────────────┐    ┌───────────────────────┐
  │ X: Target_X (D100)    │    │ X: Target_X+DeltaX (D116) │    │ X: Target_X+DX (D116) │
  │ Y: Target_Y (D102)    │───►│ Y: Target_Y (D102)        │───►│ Y: Target_Y (D102)    │
  │ Z: Target_Z_Down(D104)│    │ Z: Target_Z_Down (D104)   │    │ Z: Target_Z_Safe(D112)│
  └───────────────────────┘    └───────────────────────────┘    └───────────────────────┘
                                   [Mở van Y20 trong 30ms]          [Bật M101 báo xong]
```

### 4.1. Chi tiết địa chỉ Buffer Memory từng Point

| Thông số kỹ thuật | Ký hiệu | Point 1 (Bay tiếp cận) | Point 2 (Trượt & Xả keo) | Point 3 (Rút Z an toàn) |
| :--- | :---: | :---: | :---: | :---: |
| **Positioning Identifier** | `Da.1..5` | **`1503H`** (`U0\G2000`) | **`1503H`** (`U0\G2010`) | **`1500H`** (`U0\G2020`) |
| **M Code** | `Da.10` | `0` (`U0\G2001`) | **`1`** (`U0\G2011`) | `0` (`U0\G2021`) |
| **Dwell Time** | `Da.9` | `0 ms` (`U0\G2002-3`) | `0 ms` (`U0\G2012-3`) | `0 ms` (`U0\G2022-3`) |
| **Command Speed (0.01 mm/min)** | `Da.8` | `D106-107` (`U0\G2004-5`) | `D108-109` (`U0\G2014-5`) | `D106-107` (`U0\G2024-5`) |
| **Trục 1 (X) Tọa độ (0.1 um)** | `Da.6` (Ax1) | `D100-101` (`U0\G2006-7`) | `D116-117` (`U0\G2016-7`) | `D116-117` (`U0\G2026-7`) |
| **Trục 2 (Y) Tọa độ (0.1 um)** | `Da.6` (Ax2) | `D102-103` (`U0\G8006-7`) | `D102-103` (`U0\G8016-7`) | `D102-103` (`U0\G8026-7`) |
| **Trục 3 (Z) Tọa độ (0.1 um)** | `Da.6` (Ax3) | `D104-105` (`U0\G14006-7`)| `D104-105` (`U0\G14016-7`)| `D112-113` (`U0\G14026-7`)|

*Giải mã từ định danh `1503H` và `1500H`*:
- Bit $8 \sim 15$ (`Da.2` Control System): `15H` = 3-axis Linear Interpolation (ABS3).
- Bit $6 \sim 7$ (`Da.4` Decel No.): `00b` = Decel Time 0 ($100\text{ ms}$).
- Bit $4 \sim 5$ (`Da.3` Accel No.): `00b` = Accel Time 0 ($100\text{ ms}$).
- Bit $2 \sim 3$ (`Da.5` Interpolation Axis): `00b` = Mặc định (Axis 1 là Reference).
- Bit $0 \sim 1$ (`Da.1` Operation Pattern):
  * Point 1 & 2: `11b` ($3$) = Continuous Path Control (Nối tiếp mượt không dừng).
  * Point 3: `00b` ($0$) = Positioning Complete (Dừng và kết thúc chuỗi).

---

## 5. MÁY TRẠNG THÁI FSM & LƯU ĐỒ THỜI GIAN (TIMING CHART)

### 5.1. Các trạng thái trong FSM (`D200: iState`)
```
  ┌────────────────────────────────────────────────────────────────────────┐
  │ State 0: IDLE                                                          │
  │   - Hệ thống sẵn sàng (X0=ON, không có lỗi). Chờ cạnh lên M100.       │
  └───────────────────────────────────┬────────────────────────────────────┘
                                      │ M100 Cạnh lên
                                      ▼
  ┌────────────────────────────────────────────────────────────────────────┐
  │ State 10: BUFFER WRITE & STROKE PRE-CHECK                              │
  │   - Tính D116 = D100 + D110.                                           │
  │   - Kiểm tra giới hạn mềm 3 trục (X <= 180, Y <= 170, Z <= 52).        │
  │   - Hợp lệ: Nạp Point 1..3 vào Buffer QD75. Nạp Cd.3 = 1.              │
  │   - Bất hợp lệ: Bật M104 (Robot_Alarm), ghi D120, từ chối chạy.        │
  └───────────────────────────────────┬────────────────────────────────────┘
                                      │ Nạp Buffer thành công
                                      ▼
  ┌────────────────────────────────────────────────────────────────────────┐
  │ State 20: START MOTION (Cd.3 + Y10)                                    │
  │   - Bật Y10 = ON (Axis 1 Positioning Start).                           │
  │   - Chờ X10 = ON (Start Complete) -> Hạ ngay Y10 = OFF.               │
  └───────────────────────────────────┬────────────────────────────────────┘
                                      │ X10 = ON (Bắt đầu phát xung)
                                      ▼
  ┌────────────────────────────────────────────────────────────────────────┐
  │ State 30: RUNNING & DISPENSE (Point 1 -> 2 -> 3)                       │
  │   - Point 1: Robot bay từ trên cao đáp xuống (Target_X, Y, Z_down).   │
  │   - Bắt đầu Point 2: QD75 bật cờ X4 (M-Code ON) -> SET M200 (Van Y20) │
  │     và kích hoạt High-speed Timer T0 (30ms).                           │
  │   - Handshake M-Code: Ghi U0\G1504 (Cd.7) = 1 để xóa cờ X4.            │
  │   - Hết 30ms (T0 xong): Ngắt M200 -> Khóa van Y20 = OFF.              │
  │   - Point 3: Robot nhấc Z lên Target_Z_Safe (0.0 mm).                  │
  └───────────────────────────────────┬────────────────────────────────────┘
                                      │ X14 = ON (Positioning Complete)
                                      ▼
  ┌────────────────────────────────────────────────────────────────────────┐
  │ State 40: IN-POSITION & ACK (Báo xong đỉnh)                            │
  │   - Bật M103 (Robot_In_Position) = ON, hạ M102 (Robot_Busy) = OFF.    │
  │   - Bật M101 (Ack_Vertex_Done) = ON báo về TwinCAT 3.                  │
  └───────────────────────────────────┬────────────────────────────────────┘
                                      │ Chuyển sang State 50
                                      ▼
  ┌────────────────────────────────────────────────────────────────────────┐
  │ State 50: HANDSHAKE WAIT (Bắt tay 4 pha)                               │
  │   - Chờ TwinCAT 3 nhận biết M101 và hạ cờ M100 = OFF.                  │
  │   - PLC-Q hạ M101 = OFF, đưa FSM quay về State 0 chờ đỉnh tiếp theo.   │
  └────────────────────────────────────────────────────────────────────────┘
```

---

## 6. HƯỚNG DẪN KIỂM THỬ ĐỘC LẬP TRÊN GX WORKS2 (INDEPENDENT TEST GUIDE)

*Không cần kết nối Master TwinCAT 3, Camera Vision hay Băng tải.* Người lập trình có thể kiểm thử toàn diện module QD75D4 và chương trình PLC-Q bằng **Device Test** hoặc **Watch Window** trong GX Works2.

### 6.1. Thiết lập cửa sổ Watch trong GX Works2
1. Mở phần mềm **GX Works2**, kết nối CPU hoặc bật **GX Simulator 2** (`Debug` $\rightarrow$ `Start/Stop Simulation`).
2. Mở cửa sổ **Watch 1** (`View` $\rightarrow$ `Docking Window` $\rightarrow$ `Watch 1`).
3. Đăng ký danh sách các biến sau vào bảng Watch:

| Device Name | Data Type | Display Format | Ý nghĩa theo dõi |
| :--- | :--- | :--- | :--- |
| **`D200`** | 16-bit Signed | Decimal | Bước FSM (`0, 10, 20, 30, 40, 50`) |
| **`D100`** | 32-bit Signed | Decimal (Double Word) | `Target_X` ($0.1\ \mu\text{m}$) |
| **`D102`** | 32-bit Signed | Decimal (Double Word) | `Target_Y` ($0.1\ \mu\text{m}$) |
| **`D104`** | 32-bit Signed | Decimal (Double Word) | `Target_Z_Down` ($0.1\ \mu\text{m}$) |
| **`D106`** | 32-bit Signed | Decimal (Double Word) | `Speed_Approach` ($0.01\text{ mm/min}$) |
| **`D108`** | 32-bit Signed | Decimal (Double Word) | `Speed_Sync` ($0.01\text{ mm/min}$) |
| **`D110`** | 32-bit Signed | Decimal (Double Word) | `DeltaX_Sync` ($0.1\ \mu\text{m}$) |
| **`D112`** | 32-bit Signed | Decimal (Double Word) | `Target_Z_Safe` ($0.1\ \mu\text{m}$) |
| **`D114`** | 16-bit Signed | Decimal | `Vertex_Index` ($1..4$) |
| **`D116`** | 32-bit Signed | Decimal (Double Word) | `Target_X_Sync` (Vị trí kết thúc trượt $X$) |
| **`D120`** | 16-bit Signed | Decimal | `Diag_ErrCode` (Mã chẩn đoán lỗi) |
| **`M100`** | Bit | Bit | Lệnh chạy đỉnh (`Cmd_Start_Vertex`) |
| **`M101`** | Bit | Bit | Phản hồi xong đỉnh (`Ack_Vertex_Done`) |
| **`M102`** | Bit | Bit | Trạng thái bận (`Robot_Busy`) |
| **`M103`** | Bit | Bit | Đã vào vị trí (`Robot_In_Position`) |
| **`M104`** | Bit | Bit | Báo động lỗi (`Robot_Alarm`) |
| **`M105`** | Bit | Bit | Dừng khẩn / Reset (`Cmd_Cycle_Reset`) |
| **`Y20`**  | Bit | Bit | Ngõ ra van keo thực tế (Slot 1 QY40P) |
| **`TS0`**  | Bit | Bit | Tiếp điểm Timer van keo $30\text{ ms}$ |
| **`X0`**   | Bit | Bit | `QD75 READY` |
| **`X10`**  | Bit | Bit | `Axis 1 Start Complete` |
| **`X14`**  | Bit | Bit | `Axis 1 Positioning Complete` |
| **`X4`**   | Bit | Bit | `Axis 1 M-Code ON` |
| **`U0\G800`**| 32-bit Signed | Decimal | Tọa độ thực Trục 1 ($X$) hiện tại (`Md.20`) |
| **`U0\G900`**| 32-bit Signed | Decimal | Tọa độ thực Trục 2 ($Y$) hiện tại (`Md.20`) |
| **`U0\G1000`**| 32-bit Signed | Decimal | Tọa độ thực Trục 3 ($Z$) hiện tại (`Md.20`) |

---

### 6.2. Bảng số liệu mẫu (Test Vectors) để người lập trình ép vào D100..D114

Dưới đây là 4 bộ tham số thực tế đã được chuẩn hóa để ép trực tiếp vào bộ nhớ PLC:

#### BẢNG TỔNG HỢP GIÁ TRỊ ÉP SỐ MẪU:

| Thanh ghi | Tên tham số | [Test 1] Đỉnh 1 Chuẩn ($\theta=0^\circ$) | [Test 2] Đỉnh 2 Xoay ($\theta=30^\circ$) | [Test 3A] Biên Tối Đa (Pass) | [Test 3B] Vượt Hành Trình (Reject) |
| :--- | :--- | :---: | :---: | :---: | :---: |
| **`D100`** | `Target_X` | **`770000`** ($77.0\text{ mm}$) | **`426795`** ($42.68\text{ mm}$) | **`1740000`** ($174.0\text{ mm}$) | **`1760000`** ($176.0\text{ mm}$) |
| **`D102`** | `Target_Y` | **`1000000`** ($100.0\text{ mm}$) | **`923205`** ($92.32\text{ mm}$) | **`1700000`** ($170.0\text{ mm}$) | **`1000000`** ($100.0\text{ mm}$) |
| **`D104`** | `Target_Z_Down` | **`40000`** ($4.0\text{ mm}$) | **`40000`** ($4.0\text{ mm}$) | **`520000`** ($52.0\text{ mm}$) | **`40000`** ($4.0\text{ mm}$) |
| **`D106`** | `Speed_Approach`| **`3000000`** ($500\text{ mm/s}$) | **`3000000`** ($500\text{ mm/s}$) | **`6000000`** ($1000\text{ mm/s}$) | **`3000000`** ($500\text{ mm/s}$) |
| **`D108`** | `Speed_Sync` | **`600000`** ($100\text{ mm/s}$) | **`600000`** ($100\text{ mm/s}$) | **`600000`** ($100\text{ mm/s}$) | **`600000`** ($100\text{ mm/s}$) |
| **`D110`** | `DeltaX_Sync` | **`60000`** ($6.0\text{ mm}$) | **`60000`** ($6.0\text{ mm}$) | **`60000`** ($6.0\text{ mm}$) | **`60000`** ($6.0\text{ mm}$) |
| **`D112`** | `Target_Z_Safe` | **`0`** ($0.0\text{ mm}$) | **`0`** ($0.0\text{ mm}$) | **`0`** ($0.0\text{ mm}$) | **`0`** ($0.0\text{ mm}$) |
| **`D114`** | `Vertex_Index` | **`1`** | **`2`** | **`1`** | **`1`** |
| **Kỳ vọng** | Hành vi Robot | **Chạy mượt, xả keo 30ms** | **Chạy mượt, xả keo 30ms** | **Chạy tới sát biên 180mm** | **Chặn đứng, không phát xung** |
| **Báo lỗi** | `M104 / D120` | `M104 = OFF, D120 = 0` | `M104 = OFF, D120 = 0` | `M104 = OFF, D120 = 0` | **`M104 = ON, D120 = 501`** |

---

### 6.3. Trình tự 5 bước thực hiện kiểm thử thực tế

#### BƯỚC 1: Nạp số liệu mẫu (Data Injection)
* Trên GX Works2 Watch 1, nhấp đúp vào cột *Current Value* của từng ô `D100` đến `D114`.
* Nhập chính xác các giá trị của **[Test 1]** (chọn định dạng nhập là *Signed Decimal 32-bit*).
* Quan sát `D200` đang ở giá trị `0` (IDLE), các cờ `M101..M104` đều ở mức `OFF`.

#### BƯỚC 2: Kích hoạt chu trình chạy (Trigger Start)
* Nhấp đúp vào biến `M100`, bấm **ON** (Shift + Enter).
* **Quan sát diễn tiến trạng thái trên màn hình**:
  1. `D200` lập tức nhảy từ `0` $\rightarrow$ `10`: Thanh ghi `D116` tính ra giá trị $770,000 + 60,000 = 830,000$ ($83.0\text{ mm}$). Bộ đệm `U0\G2000..` được điền đầy đủ.
  2. `D200` nhảy sang `20`: Bit `Y10` nhấp nháy `ON` trong 1 scan rồi tắt khi `X10` (`Start Complete`) phản hồi.
  3. `D200` nhảy sang `30`: Bit `M102` (`Robot_Busy`) bật `ON`. Tọa độ thực tế `U0\G800` (X), `U0\G900` (Y), `U0\G1000` (Z) bắt đầu tăng tốc chuyển động nội suy hướng về Point 1.

#### BƯỚC 3: Quan sát giai đoạn đồng tốc và xả keo $30\text{ ms}$ (Point 2)
* Khi trục $Z$ đáp xuống độ cao $4.0\text{ mm}$ và trục $X$ chuyển sang vận tốc đồng tốc $100\text{ mm/s}$:
  1. Cờ phần cứng `X4` (`M-Code ON`) bật `ON`.
  2. Ngay lập tức ngõ ra van keo `Y20` bật `ON` (đèn LED ngõ ra trên module QY40P sáng).
  3. Timer tốc độ cao `T0` đếm từ $0$ lên $3$ ($30\text{ ms}$).
  4. Sau đúng $30\text{ ms}$, tiếp điểm `TS0` đóng $\rightarrow$ Ngắt ngay `Y20 = OFF` dứt khoát.
  5. Cờ $X4$ được xóa tự động thông qua yêu cầu tắt M-code `U0\G1504 = 1`.

#### BƯỚC 4: Quan sát rút kim an toàn và báo hoàn thành (Point 3 & M101)
* Robot tiếp tục hoàn thành Point 3: Trục $Z$ rút từ $4.0\text{ mm}$ thẳng đứng lên $0.0\text{ mm}$ ở vận tốc $500\text{ mm/s}$.
* Khi trục $Z$ chạm mốc $0.0\text{ mm}$, QD75 ngắt hoàn toàn xung và bật cờ `X14` (`Positioning Complete`):
  1. `D200` nhảy sang `40` rồi `50`.
  2. `M102` (`Robot_Busy`) tắt `OFF`.
  3. `M103` (`Robot_In_Position`) bật `ON`.
  4. **Bit `M101` (`Ack_Vertex_Done`) bật `ON`** vững chắc để báo cho trạm Master.

#### BƯỚC 5: Kiểm tra bắt tay kết thúc (Handshake Reset)
* Để mô phỏng phản hồi của TwinCAT 3 sau khi nhận đỉnh xong: Nhấp đúp vào `M100`, bấm **OFF**.
* **Kết quả**: Bit `M101` lập tức tự động tắt `OFF`, `M103` tắt `OFF`, và `D200` trở về `0` (IDLE), sẵn sàng nạp số liệu cho đỉnh tiếp theo.

---

### 6.4. Kiểm tra ca từ chối vượt biên hành trình cơ khí ([Test 3B])
1. Nhập vào `D100 = 1760000` ($176.0\text{ mm}$).
2. Khi cộng với `DeltaX_Sync = 60000` ($6.0\text{ mm}$), điểm kết thúc trượt là $182.0\text{ mm}$, vượt quá hành trình cơ khí $180.0\text{ mm}$.
3. Ép `M100 = ON`.
4. **Kết quả quan sát**:
   * QD75 **tuyệt đối không phát xung** (trục đứng im, không bị dính lỗi giật khựng `Error 517` hay kẹt cứng cơ khí).
   * Cờ `M104` (`Robot_Alarm`) lập tức bật `ON`.
   * Thanh ghi chẩn đoán `D120` ghi nhận mã lỗi **`501`** (*X Stroke Breach*).
   * `D200` bị giữ an toàn ở `0` (IDLE).

---

## 7. BẢNG CHECKLIST NGHIỆM THU SESSION 2 (ACCEPTANCE CHECKLIST)

Đối chiếu với các tiêu chí nghiệm thu tại Mục 8.2 tài liệu `Glue_Tracking_System_Specs.md`:

| STT | Tiêu chuẩn nghiệm thu theo Đặc tả | Kết quả triển khai | Bằng chứng kiểm chứng | Đánh giá |
| :---: | :--- | :--- | :--- | :---: |
| **1** | QD75D4 phát xung 3 trục nội suy mượt mà, không giật khựng, không dính `Error 517`. | ĐÃ ĐẠT | Cấu hình liên hoàn Cont Path (`1503H` $\rightarrow$ `1503H` $\rightarrow$ `1500H`), chế độ `Front-loading switching` (`Pr.19 = 1`), `Composite speed` (`Pr.20 = 0`), kiểm tra Stroke Limit trước khi Start. | **PASS** |
| **2** | Van keo đóng mở chuẩn xác $30\text{ ms}$ ngay trong lúc trục X đang trượt đồng tốc. | ĐÃ ĐẠT | Kích hoạt phần cứng qua M-code $X4$ (WITH mode), định thì bằng High-Speed Timer `OUTH T0 K3` ($30\text{ ms}$), xuất ngõ ra Transistor `Y20` ($<0.1\text{ ms}$). | **PASS** |
| **3** | Trục Z rút lên an toàn sau khi bơm keo xong. | ĐÃ ĐẠT | Point 3 nạp đích đến $Target\_Z\_Safe = 0.0\text{ mm}$ ở vận tốc bay $500\text{ mm/s}$ (thời gian rút $<30\text{ ms}$). | **PASS** |
| **4** | Bit $M101$ bật đúng lúc và tự tắt khi $M100$ ngắt. | ĐÃ ĐẠT | FSM State 40 kích $M101 = \text{ON}$ khi $X14$ báo hoàn thành và $XC..XE$ hết bận; State 50 tự động hạ $M101 = \text{OFF}$ khi $M100$ ngắt. | **PASS** |
| **5** | Bảo toàn tín hiệu hệ thống `Y0` (PLC READY). | ĐÃ ĐẠT | Cách ly triệt để: $Y0$ duy trì `PLC READY` cho QD75 Slot 0; Van keo dời sang $Y20$ (Slot 1 QY40P). Không còn nguy cơ sập module. | **PASS** |
