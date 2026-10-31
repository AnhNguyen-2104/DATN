# HỢP ĐỒNG GIAO DIỆN — SESSION 1 → SESSION 2
## TwinCAT/Vision Master gửi dữ liệu cho Mitsubishi Q06UDEH + QD75D4

**Mục đích:** Session 1 chỉ tính Vision/Encoder/đón đầu, ghi dữ liệu vào PLC và thực hiện handshake. Session 2 chỉ kiểm tra biên, nạp Point 1–3 vào QD75D4, phát chuyển động và xả keo.

## 1. Quyền sở hữu — bắt buộc không ghi chéo

| Thành phần | Owner | Quy tắc |
|---|---|---|
| Camera, encoder, góc xoay, tính 4 đỉnh, bù trôi/đón đầu | Session 1 / TwinCAT | PLC-Q không tính lại toán học này. |
| D100..D114, M1000 | Session 1 | Chỉ ghi khi PLC đang rảnh và trước xung Start. |
| QD75 Buffer Memory `U0\G...`, FSM `D200`, D116, D120, M1001 | Session 2 / PLC-Q | Session 1 chỉ đọc trạng thái; không ghi. |
| `Y0` | `MAIN` Ladder | Luôn ON liên tục: `SM400 -> Y0`. Không dùng làm van keo. |
| `Y10` | `MAIN` Ladder | Chỉ có **một coil Y10** trong MAIN; Tracking ST yêu cầu phát xung qua `M110`. |
| `Y20` | Session 2 / PLC-Q | Ngõ ra van keo vật lý; Session 1 không ghi. |
| `M100`, `M101` | `MAIN` Homing hiện có | **Cấm dùng cho Tracking** vì MAIN đang dùng `PLS M100` và `PLS M101`. |

## 2. Cấu hình Ethernet MC Protocol

| Tham số | Giá trị |
|---|---|
| PLC | Mitsubishi Q06UDEH Built-in Ethernet |
| Giao thức | MC Protocol QnA 3E Binary / TCP |
| Port PLC lắng nghe | `5010` |
| Port bị cấm | `5000..5009` — CPU không cho mở; không dùng `5000`. |
| Operation setting GX Works2 | TCP / MC Protocol / Host Station Port No. = `5010` |
| Nạp cấu hình | Write `Parameter` xuống PLC, sau đó STOP → RESET → RUN hoặc tắt/bật nguồn. |

Session 1 phải kết nối TCP tới `<PLC_IP>:5010`.

## 3. Vùng dữ liệu Session 1 phải ghi trước Start

Tất cả các DINT là **little-endian Mitsubishi**: word thấp tại địa chỉ đầu, word cao tại địa chỉ kế tiếp.

| PLC device | Tên | Kiểu | Đơn vị | Session 1 phải gửi |
|---|---|---:|---|---|
| `D100-D101` | `Target_X` | DINT | `0.1 µm` | X của **một đỉnh đã đón đầu**. `1 mm = 10000`. |
| `D102-D103` | `Target_Y` | DINT | `0.1 µm` | Y của đỉnh. `1 mm = 10000`. |
| `D104-D105` | `Target_Z_Down` | DINT | `0.1 µm` | Cao độ xả keo. Ví dụ `4.0 mm = 40000`. |
| `D106-D107` | `Speed_Approach` | DINT | `0.01 mm/min` | Tốc độ Point 1 và Point 3. Ví dụ `500 mm/s = 3000000`. |
| `D108-D109` | `Speed_Sync` | DINT | `0.01 mm/min` | Tốc độ Point 2, phải bằng tốc độ băng tải. Ví dụ `100 mm/s = 600000`. |
| `D110-D111` | `DeltaX_Sync` | DINT | `0.1 µm` | Quãng trượt X trong Point 2. Ví dụ `100 mm/s × 60 ms = 6.0 mm = 60000`. |
| `D112-D113` | `Target_Z_Safe` | DINT | `0.1 µm` | Cao độ rút kim. Ví dụ `0.0 mm = 0`. |
| `D114` | `Vertex_Index` | INT | `1..4` | Đỉnh đang thực hiện; chỉ để theo dõi/chẩn đoán. |

### Phép quy đổi ở Session 1

```text
position_01um = round(position_mm × 10000)
speed_001mm_min = round(speed_mm_s × 6000)
deltaX_01um = round(speed_conveyor_mm_s × 0.060 × 10000)
```

## 4. Dữ liệu Session 1 phải tính trước khi ghi PLC

Với phôi vuông `40 × 40 mm`, Session 1 chịu trách nhiệm:

1. Nhận tâm phôi `(Xc, Yc)` và góc xoay `θ` từ Vision.
2. Tính bốn đỉnh tại thời điểm trigger.
3. Đọc encoder hiện tại và tính trôi:

```text
X_drift = (Encoder_current - Encoder_at_trigger) × 0.476475 mm/pulse
X_fly   = Conveyor_speed_filtered × t_fly
Target_X = X_vertex_at_trigger + X_drift + X_fly
Target_Y = Y_vertex_at_trigger
```

4. Chỉ gửi đỉnh nếu nó có thể hoàn thành trong vùng làm việc của gantry.
5. Chờ hoàn tất đỉnh trước khi tính/gửi đỉnh kế tiếp; không ghi đè D100..D114 khi PLC đang bận.

`Target_X` phải là tọa độ X **ngay khi Point 1 bắt đầu**, không phải tọa độ tại thời điểm camera chụp.

## 5. Handshake chính thức từng đỉnh

### Bit sử dụng

| PLC Device | Nhãn | Owner | Ý nghĩa |
|---|---|---|---|
| `M1000` | `Cmd_Start_Vertex` | Session 1 ghi | Lệnh chạy một đỉnh. |
| `M1001` | `Ack_Vertex_Done` | Session 2 ghi | Point 3 hoàn tất; Z đã rút lên Safe. |
| `M102` | `Robot_Busy` | Session 2 ghi | PLC đang kiểm tra/nạp/chạy Point 1–3. |
| `M103` | `Robot_In_Position` | Session 2 ghi | Đã hoàn tất một đỉnh và đang chờ Master hạ Start. |
| `M104` | `Robot_Alarm` | Session 2 ghi | Lỗi QD75 hoặc kiểm tra biên thất bại. |
| `M105` | `Cmd_Cycle_Reset` | Session 1/HMI ghi | Hủy chu trình, đóng van, dừng các trục. |

> Cần đổi Local Label trong Tracking: `Cmd_Start_Vertex -> M1000`, `Ack_Vertex_Done -> M1001`. Không dùng `M100/M101` vì chúng thuộc Homing trong MAIN.

### Trình tự bắt tay 4 pha

```mermaid
sequenceDiagram
    participant S1 as Session 1 / TwinCAT
    participant Q as Session 2 / PLC-Q
    S1->>Q: Ghi nguyên khối D100..D114
    S1->>Q: Đọc M102=0, M104=0
    S1->>Q: M1000 = 1
    Q->>Q: Kiểm tra biên, nạp QD75 Point 1→2→3
    Q->>S1: M102 = 1 trong lúc chạy
    Q->>S1: M1001 = 1, M102 = 0, M103 = 1
    S1->>Q: Xác nhận đã đọc M1001; M1000 = 0
    Q->>S1: M1001 = 0, M103 = 0; sẵn sàng đỉnh tiếp theo
```

### Pseudocode bắt buộc cho Session 1

```text
for vertex in ordered_vertices:
    wait until M102 == 0 and M104 == 0 and M1001 == 0
    calculate interception_target(vertex)
    write D100..D114 atomically
    write M1000 = 1
    wait until M1001 == 1 or M104 == 1 or timeout
    if M104 == 1:
        stop cycle; read D120 and QD75 error diagnostics
    write M1000 = 0
    wait until M1001 == 0 and M103 == 0
```

Không tạo xung Start ngắn. `M1000` phải duy trì ON đến khi PLC đặt `M1001 = ON`; sau đó Master mới hạ `M1000`.

## 6. Session 2 xử lý dữ liệu nhận được như thế nào

| Pha QD75 | Identifier | X | Y | Z | Tốc độ | Hành vi |
|---|---:|---|---|---|---|---|
| Point 1 | `1503H` | `Target_X` | `Target_Y` | `Target_Z_Down` | `Speed_Approach` | Bay nội suy ABS3 đến đỉnh. |
| Point 2 | `1503H` | `Target_X + DeltaX_Sync` | `Target_Y` | `Target_Z_Down` | `Speed_Sync` | Trượt đồng tốc; QD75 phát M-code `X4`, PLC mở `Y20` trong 30 ms. |
| Point 3 | `1500H` | `Target_X + DeltaX_Sync` | `Target_Y` | `Target_Z_Safe` | `Speed_Approach` | Rút Z, dừng chuỗi; sau X14 đặt Ack. |

## 7. Kiểm tra biên PLC trước khi chạy

Session 2 từ chối lệnh và đặt `M104 = ON` nếu:

| Điều kiện lỗi | `D120` |
|---|---:|
| `Target_X` hoặc `Target_X + DeltaX_Sync` ngoài `0..1800000` | `501` |
| `Target_Y` ngoài `0..1700000` | `502` |
| `Target_Z_Down`/`Target_Z_Safe` ngoài `0..520000`, hoặc `Z_Down < Z_Safe` | `503` |
| QD75 chưa Ready, đang bận hoặc lỗi trước Start | `901` |

Session 1 phải kiểm tra trước các điều kiện này để tránh mất thời gian chu trình; PLC vẫn là lớp bảo vệ cuối cùng.

## 8. Điều kiện Session 1 không được vi phạm

1. Không viết `Y0`, `Y10`, `Y20`, `Y4..Y6`, `U0\G...`, `D116`, `D120`, `D200`.
2. Không dùng port TCP `5000..5009`; dùng `5010`.
3. Không dùng `M100/M101` cho giao tiếp Tracking.
4. Không cập nhật bất kỳ word nào trong `D100..D114` sau khi `M1000 = ON` và trước khi `M1001 = ON`.
5. Nếu `M104 = ON`, không tự kích Start lại; hạ `M1000`, ghi `M105` nếu cần dừng, lấy mã `D120`, sau đó yêu cầu người vận hành/logic reset xác nhận.
6. Không giả định điểm 1–4 có cùng `Target_X`: mỗi đỉnh cần tính bù trôi lại đúng trước thời điểm phát Start.

## 9. Test kết nối tối thiểu từ Session 1

1. Kết nối TCP MC Protocol tới PLC port `5010`.
2. Đọc `M102`, `M104`, `D120` thành công.
3. Với gantry an toàn và QD75 Ready, ghi Test Vector dưới đây:

| Device | Giá trị |
|---|---:|
| `D100` | `770000` |
| `D102` | `1000000` |
| `D104` | `40000` |
| `D106` | `3000000` |
| `D108` | `600000` |
| `D110` | `60000` |
| `D112` | `0` |
| `D114` | `1` |

4. Ghi `M1000 = ON`; kiểm tra PLC trả `M102 = ON` rồi sau Point 3 trả `M1001 = ON`.
5. Ghi `M1000 = OFF`; kiểm tra `M1001 = OFF` và `M103 = OFF`.

## Nguồn kỹ thuật

- `Glue_Tracking_System_Specs.md:89-111,115-153,158-166,229-267`
- `PLC_Q_Session2_Logic.st` — FSM, QD75 Point 1–3, giới hạn và owner tín hiệu.
- `Session2_Mitsubishi_Q_Guide.md` — cấu hình QD75, Ethernet, kiểm thử độc lập.
- `MELSEC-Q-ST-Rules.md:177-186,203-205,217-222` — Y0/QD75, điều kiện Start, map buffer và device.

## Tài liệu liên quan

- `Session2_BenchTest_Without_TwinCAT.md` — Quy trình test gantry thủ công qua Watch Window, không cần TwinCAT. Dùng để chứng minh PLC-Q hoạt động đúng **trước khi** cắm Session 1 vào.
