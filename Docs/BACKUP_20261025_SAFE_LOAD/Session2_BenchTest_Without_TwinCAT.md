# QUY TRÌNH TEST CHUYỂN ĐỘNG GANTRY KHÔNG CẦN TWINCAT 3
## (BENCH TEST — Đóng vai Master bằng Watch Window GX Works2)

**Mục đích**: Kiểm thử toàn bộ chuỗi nội suy 3 trục, hạ Z, trượt đồng tốc, xả keo 30 ms và rút Z an toàn **hoàn toàn bằng GX Works2**, không cần Camera, không cần Encoder, không cần TwinCAT 3.

**Đối tượng đọc**: Kỹ sư Session 2 (PLC-Q) khi chạy bench-test, và Session 1 để hiểu dữ liệu giả lập cần khớp định dạng với dữ liệu thật.

---

## 1. NGUYÊN LÝ

Chương trình `Tracking` **không biết** dữ liệu đến từ TwinCAT hay từ tay kỹ sư. Nó chỉ đọc `D100..D114` rồi kích hoạt FSM khi thấy `M1000` chuyển `FALSE → TRUE`. Vì vậy, chỉ cần gõ số vào các thanh ghi D và ép `M1000 = ON` là gantry chạy thật.

```mermaid
flowchart LR
    A[Gõ D100..D114<br/>bằng Watch Window] --> B[Ép M1000 = ON]
    B --> C[FSM State 10<br/>Kiểm tra biên]
    C --> D[Nạp Buffer QD75<br/>Point 1/2/3]
    D --> E[Phát xung Y10<br/>Gantry bay thật]
    E --> F[X4 ON → Y20 bật<br/>30 ms]
    F --> G[X14 ON → M1001 ON]
    G --> H[Ép M1000 = OFF<br/>Về IDLE, đỉnh kế tiếp]
```

**Điều kiện tiên quyết (chỉ làm 1 lần)**:
* Tiếp điểm `M110` đã mắc song song với cuộn `Y10` trong `MAIN`.
* `Cmd_Start_Vertex` gán `M1000`, `Ack_Vertex_Done` gán `M1001` trong Local Label.
* QD75D4 đã nạp Basic Parameter (`Pr.1..Pr.14`, `Pr.18..Pr.20`) và mở cổng Ethernet nếu cần.
* Switch CPU đang ở `RUN`, `X0 = ON`.

---

## 2. BẢNG SỐ LIỆU TEST 1 — ĐỈNH 1 CHUẨN (θ = 0°)

### 2.1. Cách nạp nhanh (khuyên dùng — 1 dòng / thanh ghi)

Trong Watch Window, đặt **Data Type = `Double Word (Signed)`** cho các biến `D100, D102, D104, D106, D108, D110, D112`. Khi đó GX Works2 tự phân bổ 2 word, chỉ cần gõ số thập phân:

| Device | Data Type chọn trong Watch | Giá trị gõ vào | Ý nghĩa |
|:---|:---:|---:|:---|
| `D100` | Double Word (Signed) | `770000` | Target_X = 77.0 mm |
| `D102` | Double Word (Signed) | `1000000` | Target_Y = 100.0 mm |
| `D104` | Double Word (Signed) | `40000` | Z chấm keo = 4.0 mm |
| `D106` | Double Word (Signed) | `3000000` | V bay = 500 mm/s |
| `D108` | Double Word (Signed) | `600000` | V đồng tốc = 100 mm/s |
| `D110` | Double Word (Signed) | `60000` | ΔX trượt = 6.0 mm |
| `D112` | Double Word (Signed) | `0` | Z an toàn = 0.0 mm |
| `D114` | Word (Signed) | `1` | Đỉnh 1 |

### 2.2. Bảng dự phòng khi phải gõ từng Word 16-bit

*(Dùng khi cột Data Type trong Watch không chọn được Double Word — thao tác đúng thứ tự **thấp trước, cao sau**)*

| Word thấp | Decimal | Word cao | Decimal | Cặp DINT | Giá trị thực |
|:---:|---:|:---:|---:|:---:|---:|
| `D100` | `49104` | `D101` | `11` | D100-D101 | 770000 (77.0 mm) |
| `D102` | `16960` | `D103` | `15` | D102-D103 | 1000000 (100.0 mm) |
| `D104` | `40000` | `D105` | `0` | D104-D105 | 40000 (4.0 mm) |
| `D106` | `50880` | `D107` | `45` | D106-D107 | 3000000 (500 mm/s) |
| `D108` | `10176` | `D109` | `9` | D108-D109 | 600000 (100 mm/s) |
| `D110` | `60000` | `D111` | `0` | D110-D111 | 60000 (6.0 mm) |
| `D112` | `0` | `D113` | `0` | D112-D113 | 0 (0.0 mm) |
| `D114` | `1` | — | — | — | 1 |

> ⚠️ **Bẫy thường gặp**: `D100` hiển thị `-16432` (âm) là do watch window đang hiển thị dạng **Word (Signed)** trên word thấp `0xBFD0`. Đổi sang Double Word Signed để thấy đúng `770000`. Đây không phải lỗi dữ liệu.

---

## 3. KẾT QUẢ MONG ĐỢI — GANTRY SẼ CHẠY NHƯ THẾ NÀO

Sau khi gõ đủ bảng trên, ép `M1000 = ON`:

| Bước | Trạng thái | Quan sát trên Watch | Quan sát cơ khí |
|:---:|:---:|:---|:---|
| 1 | `D200 = 0 → 10` | `D116 = 830000` (83.0 mm) tự tính | Gantry nhún nhẹ, servo enable |
| 2 | `D200 = 10 → 20` | `Y10` nhấp `ON` 1 scan, `X10` phản hồi `ON` | Bắt đầu có tiếng động cơ chạy |
| 3 | `D200 = 20 → 30` | `M102 = ON` (Robot_Busy) | X chạy về 77 mm, Y về 100 mm, Z hạ xuống 4 mm |
| 4 | Đang ở Point 2 | **`X4` bật `ON`**, **`Y20` bật `ON`** | Nghe tiếng van keo "tạch" mở, đèn LED Slot 1 sáng |
| 5 | Sau 30 ms | `Y20` tắt ngay, `X4` tắt | Nghe tiếng van đóng, keo chỉ xả 1 giọt ngắn |
| 6 | Đang ở Point 3 | Trục Z đọc về 0 mm | Z rút lên nhanh (~30 ms) |
| 7 | Kết thúc | **`X14 = ON`** → `D200 = 40 → 50`, **`M1001 = ON`**, `M103 = ON` | Gantry đứng yên ở vị trí cuối, chờ lệnh |
| 8 | Ép `M1000 = OFF` | `M1001 = OFF`, `M103 = OFF`, `D200 = 0` | Về IDLE, sẵn sàng đỉnh tiếp theo |

---

## 4. CHẠY LIÊN HOÀN NHIỀU ĐỈNH (MÔ PHỎNG BẰNG TAY)

Để test gần giống chu trình thật, lặp lại vòng sau cho từng đỉnh:

| Đỉnh | Nạp mới các giá trị | Rồi ép |
|:---:|:---|:---|
| **Đỉnh 1** | `D100=770000, D102=1000000, D114=1` | `M1000=ON` → chờ `M1001=ON` → `M1000=OFF` |
| **Đỉnh 2** | `D100=300000, D102=1000000, D114=2` *(giả lập đã trôi)* | `M1000=ON` → chờ `M1001=ON` → `M1000=OFF` |
| **Đỉnh 3** | `D100=1610000, D102=1300000, D114=3` | `M1000=ON` → chờ `M1001=ON` → `M1000=OFF` |
| **Đỉnh 4** | `D100=1690000, D102=900000, D114=4` | `M1000=ON` → chờ `M1001=ON` → `M1000=OFF` |

---

## 5. TEST BIÊN AN TOÀN (PHẢI CHẠY ĐỂ XÁC NHẬN BẢO VỆ HOẠT ĐỘNG)

### Test A — Vượt biên Trục X (mong đợi: từ chối, `D120 = 501`)
Nạp `D100 = 1760000` (176.0 mm), `D110 = 60000` (6.0 mm). Khi `M1000 = ON`:
* `D116` tính = 182.0 mm > 180 mm.
* **Kết quả đúng**: Gantry **không nhúc nhích**, `M104 = ON`, `D120 = 501`, `D200 = 0`.

### Test B — Vượt biên Trục Y (mong đợi: từ chối, `D120 = 502`)
Nạp `D102 = 1750000` (175 mm) > 170 mm. Kết quả đúng: `D120 = 502`, `M104 = ON`.

### Test C — Lỗi logic Z (mong đợi: từ chối, `D120 = 503`)
Nạp `D104 = 10000` (1 mm, cao độ chấm) và `D112 = 50000` (5 mm, cao độ rút) → rút cao hơn chấm, vô lý. Kết quả đúng: `D120 = 503`, `M104 = ON`.

### Test D — Kích Start khi QD75 chưa Ready (mong đợi: `D120 = 901`)
Ép `M1000 = ON` khi `X0 = OFF` (ví dụ vừa RESET PLC). Kết quả đúng: `D120 = 901`, `M104 = ON`, không phát xung.

### Cách xóa lỗi sau mỗi test biên:
1. Ép `M105 = ON` (Cmd_Cycle_Reset) → PLC tự xóa `D120 = 0`, `M104 = OFF`.
2. Ép `M105 = OFF`.
3. Nạp lại số hợp lệ và chạy lại bình thường.

---

## 6. TEST VAN KEO TỐC ĐỘ CAO 30 MS (KHI GANTRY ĐÃ DỪNG AN TOÀN)

Nếu muốn kiểm tra **riêng** mạch van mà không chạy gantry (ví dụ đã tháo đầu kim, đấu đồng hồ đo), có thể dùng một trong các cách:

1. **Chạy đúng test 1** ở chế độ Point 2 (như mục 3). Van mở đúng khi `X4` bật.
2. **Kiểm tra thời gian thực bằng PLC**: đặt đồng hồ đo ở `Y20` (hoặc module QY40P chân tương ứng). Đo độ rộng xung — phải **đúng 30 ms ±1 ms** (3 × 10 ms của timer tốc độ cao).
3. **Kiểm tra khi Timer chưa ngắt kịp**: nếu xung rộng hơn 30 ms, kiểm tra `TIMER_H_M` được khai báo đúng và `T0` không bị ghi đè bởi chương trình khác.

---

## 7. NHỮNG ĐIỀU CẦN LƯU Ý (SAFETY & TROUBLESHOOTING)

| Triệu chứng | Nguyên nhân | Cách sửa |
|:---|:---|:---|
| `M1000 = ON` nhưng `D200` vẫn `= 0` | `Cmd_Start_Vertex` chưa gán `M1000` hoặc `bM1000_Old` chưa cập nhật | Kiểm tra Local Label; kiểm tra dòng cuối ST `bM1000_Old := Cmd_Start_Vertex;` |
| `D200` chạy đến 20 nhưng Gantry không động | Cuộn `Y10` chưa mắc `M110` song song | Mở `MAIN`, thêm tiếp điểm `M110` song song trước coil `Y10` |
| `D200 = 10 → 30` nhưng trục đứng im | `Speed_Approach` hoặc `Target_Z_Down` đang là 0 | Nạp lại đúng bảng mục 2 |
| Chạy xong mà `M1001` không ON | Mất `X14` hoặc còn 1 trục bận (`XC/XD/XE`) | Đọc `U0\G800..G1000` (`Md.20`) xem trục nào chưa tới; đọc `U0\G806` (`Md.23`) xem có lỗi QD75 |
| `M104 = ON` ngay từ đầu | Có lỗi QD75 từ trước (`X8..XA`) | Đọc `D121..D123`, reset bằng `M105` hoặc `Cd.5` |
| `Y20` mở kéo dài hơn 30 ms | Timer bị reset bởi scan hoặc code khác ghi đè `TC0` | Kiểm tra chỉ có **1 chỗ duy nhất** gọi `TIMER_H_M(M200, TC0, 3)` |

**An toàn bắt buộc trước khi chạy**:
* Đặt tay lên E-Stop cứng trong suốt quá trình bench-test.
* Kiểm tra Stroke Limit mềm (`Pr.12/Pr.13`) đã nạp: X 0..180 mm, Y 0..170 mm, Z 0..52 mm.
* Đảm bảo không có vật cản trong vùng làm việc 180 × 170 × 52 mm.
* Chạy test biên **trước** chạy test 1 (test biên chứng minh lớp bảo vệ còn hoạt động).

---

## 8. KHI NÀO CHUYỂN SANG TWINCAT 3 THẬT

Sau khi hoàn tất mục 3, 4, 5 và 6 của tài liệu này, gantry đã sẵn sàng nhận lệnh thật từ Session 1:

1. Trong TwinCAT 3, đặt `bSimulatePLC := FALSE;`.
2. Kiểm tra `FB_MC_Protocol_3E` kết nối tới `192.168.3.39:5010`.
3. Kích Camera Trigger thật (hoặc tay) → TwinCAT ghi `D100..D114` + bật `M1000` → PLC chạy cả 4 đỉnh tự động.

Theo hợp đồng trong `Session2_Contract_For_Session1.md`, dữ liệu TwinCAT gửi xuống **phải cùng định dạng** đã test thủ công trong tài liệu này, vì vậy kết quả chạy thật sẽ khớp với kết quả bench-test.
