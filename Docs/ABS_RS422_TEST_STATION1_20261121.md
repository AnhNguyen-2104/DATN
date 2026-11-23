# Bench đọc ABS MR-J4-A qua QJ71C24N CH2 — Station 1 (ABS_Read_Test)

- Ngày soạn: 2026-11-21
- POU: `ABS_Read_Test [PRG] Program [ST]` — bản standalone từ `ABS_Home.st`
- Nhãn: `PLC/Mitsubishi/ABS_Read_Test_labels.csv` (UTF-16LE, tab-delimited, giữ nguyên tên nhãn của `ABS_Home_labels.csv`)
- Module serial: QJ71C24N start XY `0080` (hex) ⇒ `U8`, dùng **CH2 RS-422**
- Servo: MR-J4-A, **station 1** (`PC20 = 1`), lệnh `02` + data No. `91`
- Phạm vi: **chỉ đọc vị trí ABS ở chế độ bench**. Không motion, không SON, không QD75, không sửa an toàn.

> Tài liệu này là quy trình thao tác, **không phải giấy phép thực hiện**. Không có nội dung nào dưới đây đã được chạy trên phần cứng. Trạng thái chung: **CHƯA XÁC MINH phần cứng / CHƯA XÁC MINH compile GX**.

---

## 0. Cảnh báo hiệu ứng phụ — đọc trước

Lệnh `02` + data No. `91` **không phải lệnh chỉ đọc thụ động**. Theo SH030107V §12.8.2(2) p12-27, khi nhận lệnh, amplifier **đồng thời đặt vị trí hiện tại làm giá trị lệnh vị trí nội bộ**. Vì vậy:

- Chỉ chạy khi **servo OFF**, **không SON**, **không chuyển động**, **không lệnh QD75**, máy đã cô lập cơ khí.
- Nút E-stop phải sẵn sàng; không ai đứng trong vùng chuyển động.
- Sau khi test: **power-cycle servo** (hoặc khởi tạo lại) trước khi cho phép SON. Đọc lại chỉ đặt lại vị trí lệnh nội bộ, **KHÔNG** hoàn tác hiệu ứng phụ.
- Dừng ngay nếu: servo alarm / status `a` / `ErrorCode=12`; có chuyển động ngoài ý muốn; bất kỳ đường SON/motion/QD75 nào active; lỗi checksum/frame lặp lại; chạm 3 lần thử.

---

## 1. Phần cứng và đấu dây

### 1.1 Điều kiện tiên quyết

| # | Hạng mục | Yêu cầu | Trạng thái |
| 1 | Servo | MR-J4-A (không phải -RJ), firmware **A3 trở lên** mới có RS-422 | CHƯA XÁC MINH |
| 2 | Cổng | CN3 RS-422 **loại trừ** CN5 USB — rút USB trước khi test | CHƯA XÁC MINH |
| 3 | Cáp | Cáp 4 dây RS-422 đấu theo §1.3 (SDA→RDP(3), SDB→RDN(6), RDA→SDP(5), RDB→SDN(4), SG→LG(1/7)). DSV-CABV là cáp PC RS-232C/RS-422 và BMJ-8 là cáp multidrop phía servo — **KHÔNG phải cáp C24→CN3**. Loại cáp/đầu nối cụ thể: **CHƯA XÁC MINH** | CHƯA XÁC MINH |
| 4 | Kết cuối | 150 Ω giữa RDP(3)–RDN(6) tại trục cuối phía nhận **nếu** cần kết cuối; đấu 1:1 cho một trục. Lựa chọn 1:1 hay kết cuối: **CHƯA XÁC MINH**. Cũng cần xét cặp nhận RDA/RDB và kết cuối nội bộ của C24; lựa chọn kết cuối 1:1 chính xác: **CHƯA XÁC MINH**. | CHƯA XÁC MINH |
| 5 | Shield | Nối vỏ shield về FG tại **một điểm** | CHƯA XÁC MINH |

### 1.2 CN3 (RJ45 8 chân, SH030107V §14.1.1(2)(b) p14-3)

| Chân | Tín hiệu | Ghi chú |
|---:|---|---|
| 1 | LG | Ground logic |
| 2 | P5D | +5 V — **để hở**, không dùng cho RS-422 |
| 3 | RDP | Receive Data + (servo nhận) |
| 4 | SDN | Send Data − (servo phát) |
| 5 | SDP | Send Data + (servo phát) |
| 6 | RDN | Receive Data − (servo nhận) |
| 7 | LG | Ground logic |
| 8 | NC | Không nối |

### 1.3 Nối CN3 ↔ QJ71C24N CH2

QJ71C24N CH2 (RS-422 full-duplex): `SDA/SDB` = cặp phát của module, `RDA/RDB` = cặp nhận, kèm `SG/FG`.

| Tín hiệu C24 | Nối CN3 | Chiều |
|---|---|---|
| SDA (send +) | RDP — chân 3 | C24 → servo |
| SDB (send −) | RDN — chân 6 | C24 → servo |
| RDA (receive +) | SDP — chân 5 | servo → C24 |
| RDB (receive −) | SDN — chân 4 | servo → C24 |
| SG | LG — chân 1 hoặc 7 | Tham chiếu chung |
| FG | Vỏ shield | Một điểm |

> **CHƯA XÁC MINH:** số thứ tự vật lý trên block terminal CH2 của QJ71C24N chưa được đối chiếu. **Bắt buộc đọc nhãn in trên nắp module** trước khi đấu dây; không suy đoán theo bảng trên.

---

## 2. Tham số servo — chỉ ĐỌC, KHÔNG GHI

| Tham số | Ý nghĩa | Kỳ vọng bench | Trạng thái |
|---|---|---|---|
| `PA03` | Chọn hệ thống ABS | `0002` (chọn ABS theo giao tiếp — bit-field) | CHƯA XÁC MINH giá trị live |
| `PA05` | Số xung lệnh / vòng (command input pulses per revolution) | `10000` | CHƯA XÁC MINH |
| `PA06` | Tử số điện tử gear (CMX) | `1` | CHƯA XÁC MINH |
| `PA07` | Mẫu số điện tử gear (CDV) | `1` | CHƯA XÁC MINH |
| `PC20` | Số station | `1` | CHƯA XÁC MINH |
| `PC21` | Baud + response delay | `0010h` (baud 1 = 19200, delay 0) | CHƯA XÁC MINH |
| Firmware | Hỗ trợ RS-422 | **A3 trở lên** | CHƯA XÁC MINH |

> Encoder MR-J4 là 22-bit cố định 4 194 304 xung/vòng; PA05 không phải độ phân giải encoder.

Lưu ý:

- `PA03` là **bit-field**, không phải số nguyên đơn nghĩa. Không nhập giá trị hex nguyên dựa trên tài liệu này.
- **Danh tính drive đang kết nối phải được xác nhận** trước khi tin bất kỳ giá trị nào. USB (CN5) và RS-422 (CN3) **loại trừ lẫn nhau**: cắm USB thì RS-422 không hoạt động.
- Không ghi bất kỳ tham số servo nào trong quy trình này. Nếu sai: dừng, chuyển quy trình cấu hình có phê duyệt riêng.
- Sau cấp nguồn servo phải chờ **≥ 3,5 s** trước truyền thông thường (`ReadyTimer`, `T#3500ms`).

---

## 3. Cấu hình QJ71C24N CH2

### 3.1 Switch Setting — hiện tại vs yêu cầu

Ảnh hiện trường gần nhất của người dùng: CH2 đang là MELSOFT Connection / Auto / parity None.

| Mục trên dialog `Switch Setting 0080:QJ71C24N` (CH2) | Đang đặt | Cần | Kết luận |
|---|---|---|---|
| Communication protocol setting | MELSOFT Connection (`0H`) | **Non procedure (`6H`)** | SAI |
| Communication rate setting | Auto Setting | **19200 (`07H`)** | SAI |
| Parity Bit | None | **Even** | SAI |
| Data Bit | 8 | 8 | đúng |
| Stop Bit | 1 | 1 | đúng |
| Sum check code | None | None | đúng |
| Operation setting | Independent | Independent | đúng |
| Station number | 0 | 0 (không dùng ở non-procedure) | đúng |

Hệ quả mong đợi sau khi đổi đúng:

- `U8\G610 = 6`
- `(U8\G611 AND HFF3F) = H070E`

Giải mã `H070E`:

| Bit | Ý nghĩa | Giá trị |
|---|---|---|
| b0 | Independent (0) | 0 |
| b1 | 8 data bit | 1 |
| b2 | Parity có | 1 |
| b3 | Even | 1 |
| b4 | 1 stop bit | 0 |
| b5 | Không sum check | 0 |
| b6 | Write during RUN = Prohibited | 0 |
| b7 | Setting modification = Prohibited | 0 |
| b8–b11 | Rate code `7` = 19200 bps | 7 |

`HFF3F` chỉ che b6/b7; b12–b15 **không** được che và vẫn phải bằng 0 vì `H070E` có chúng bằng 0.

### 3.2 Điều kiện bắt buộc khi đổi switch setting

- **CH2 đang MELSOFT Connection phải được giải phóng.** Nếu GX Works2 đang kết nối qua CH2, đổi sang non-procedure sẽ mất kết nối. Dùng **Ethernet** (hoặc cổng USB của CPU) cho GX Works2.
- Switch setting là parameter PLC: phải **Write to PLC**. Vì b6 (Write during RUN) = Prohibited, cần **CPU STOP**.
- Chỉ **một** chương trình sở hữu CH2. Không chạy đồng thời `ABS_Home` và `ABS_Read_Test`; không dùng user frame/giao thức khác trên CH2.

### 3.3 Detailed Setting — giá trị cần kiểm tra

| BFM | Ý nghĩa | Giá trị POU yêu cầu | Ghi chú |
|---|---|---|---|
| `U8\G310` | Đơn vị truyền/nhận | `1` = byte | theo SH080006W — CHƯA XÁC MINH trên module thực tế: Mặc định `0` (word) ⇒ phải đổi |
| `U8\G324` | Số byte dữ liệu nhận | `1` | theo SH080006W — CHƯA XÁC MINH trên module thực tế: Mặc định `1FFh` ⇒ phải đổi |
| `U8\G325` | Mã kết thúc nhận | `HFFFF` = không có delimiter | theo SH080006W — CHƯA XÁC MINH trên module thực tế: Mặc định `0D0Ah` ⇒ phải đổi |
| `U8\G449` | ASCII/BIN | `0` | Mặc định đủ |
| `U8\G447` | Transmit transparent code | `0` | Mặc định đủ |
| `U8\G448` | Receive transparent code | `0` | Mặc định đủ |
| `U8\G333` | User frame | `0` | Mặc định đủ |
| `U8\G307` b8/b9 | DC control (**CHƯA XÁC MINH** nghĩa BFM; code check giữ nguyên) | `0` | Mặc định đủ |
| `U8\G450` | Echo back | `0` (4-wire RS-422) | **POU không kiểm tra G450**; không thêm điều kiện đòi `1` |

### 3.4 Cách kiểm tra bằng buffer-memory monitor

Trong GX Works2: `Online` → `Monitor` → `Device/Buffer Memory Batch Monitor`, nhập lần lượt:

- `U8\G610` — kỳ vọng `6`
- `U8\G611` — kỳ vọng `070E` (sau khi AND `HFF3F`)
- `U8\G310` — kỳ vọng `1`
- `U8\G324` — kỳ vọng `1`
- `U8\G325` — kỳ vọng `FFFF`

Nếu POU báo `ErrorCode = 2` tại `State 10`, nghĩa là một trong các giá trị trên (hoặc G449/G447/G448/G307/G333) không khớp. POU **không tự sửa** cấu hình module.

---

## 4. GX Works2 — import và chạy

1. **Backup project** trước khi sửa (copy thư mục project khi GX đã đóng, hoặc dùng chức năng backup của GX).
2. Mở project `PLC/Mitsubishi/DATN_LINE/Robot_SCARA` (hoặc project đích).
3. Tạo POU mới `ABS_Read_Test [PRG] Program [ST]`; dán nội dung `PLC/Mitsubishi/ABS_Read_Test.st`.
4. Trong bảng **Local Label**, dùng `Read from CSV File` → chọn `PLC/Mitsubishi/ABS_Read_Test_labels.csv`. Kiểm tra không có dòng lệch cột.
5. Đăng ký `ABS_Read_Test` vào **Scan Program** (theo biên bản bench 2026-11-12, `ABS_Home` hiện không được scan; chỉ có MAIN/CONVEYOR). Không đăng ký cả hai POU.
6. Compile / Rebuild All. Chỉ coi là đạt cú pháp khi `Error: 0, Warning: 0, CheckWarning: 0`; đây **không** chứng minh vận hành.
7. Download xuống PLC là hành động **riêng, cần uỷ quyền**; tài liệu này không cho phép.
8. Sau download: `Online` → `Monitor` → `Start Monitoring`, mở `ABS_Read_Test`, mở **POU inline monitor** (khuyến nghị) hoặc Watch với global label; lưu ý GX Watch có thể không resolve local label (xem §10.9), nên đừng nhập địa chỉ D/M.

> Không chạy `ABS_Home` đồng thời. CH2 chỉ có một chủ sở hữu.

---

## 5. Watch list

| Nhãn | Kiểu | Ý nghĩa |
|---|---|---|
| `BenchEnable` | BOOL | Công tắc arm; FALSE ⇒ `ErrorCode=1`, về `State 0` |
| `ManualRead` | BOOL | Trigger đọc đơn, chỉ tác dụng ở **sườn lên** |
| `PollEnable` | BOOL | Bật polling liên tục; mặc định FALSE |
| `PollPeriod` | TIME | Chu kỳ polling, mặc định `T#500ms` |
| `Busy` | BOOL | TRUE từ trigger tới thành công hoặc `State 90` |
| `State` | INT | 0/10/11/12/20/30/35/40/50/60/90 |
| `Attempt` | INT | Lần thử hiện tại 1..3 |
| `ErrorCode` | INT | Mã lỗi hiện hành |
| `LastError` | INT | Mã lỗi lần trước, giữ qua retry |
| `Count` | INT | Số byte đã gom; kỳ vọng 14 |
| `Raw[0..31]` | ARRAY OF INT | Byte nhận; chỉ `Raw[0..Count-1]` có nghĩa |
| `ReadSuccess` | BOOL | Chốt TRUE khi phân tích thành công |
| `PositionValid` | BOOL | TRUE khi `Position` đến từ khung hợp lệ |
| `ServoAlarm` | BOOL | TRUE khi status `'a'` |
| `Position` | DINT | Vị trí signed 32-bit, đơn vị **xung lệnh** |
| `StartupReady` | BOOL | TRUE sau 3,5 s `BenchEnable` liên tục |
| `TxDone[0..1]` | ARRAY OF BOOL | `[0]` xong, `[1]` bất thường |
| `RxDone[0..1]` | ARRAY OF BOOL | `[0]` xong, `[1]` bất thường |
| `X89` | Bit | Module đang phát |
| `X8A` | Bit | Module yêu cầu đọc |
| `X8B` | Bit | Nhận bất thường |

Chẩn đoán thêm: `TimerExpired`, `RetryExpired`, `StartEdge`, `PrevRead`, `PollCount`, `TxControl[0..2]`, `RxControl[0..3]`.

---

## 6. Trình tự trigger

1. Servo OFF, máy cô lập, USB đã rút, E-stop sẵn sàng.
2. Cấp nguồn servo; chờ **≥ 3,5 s**.
3. `BenchEnable := TRUE` (giữ liên tục).
4. Chờ `StartupReady = TRUE`.
5. Phát xung `ResetResult` khi `Busy = FALSE` (xoá `ReadSuccess`/`ErrorCode`/`LastError`; **không** xoá `Position`).
6. Đọc đơn: phát đúng **một sườn lên** `ManualRead` rồi hạ xuống.
   Hoặc đọc liên tục: `PollEnable := TRUE` (cần cả `BenchEnable`).
7. Theo dõi `State`: `10 → 11 → 12 → 20 → 30 → 35 → 0`.
8. Kỳ vọng thành công: `Count=14`, `Raw[0]=2`, `Raw[1]=49`, `Raw[2]=65`, `Raw[11]=3`, checksum hợp lệ, `PositionValid=TRUE`, `ReadSuccess=TRUE`, `ErrorCode=0`.
9. Kết thúc: `BenchEnable := FALSE`. Không re-arm cho tới khi `X89`, `X8A`, `X8B` đều OFF.

Lưu ý:

- Sườn lên khi `StartupReady=FALSE` ⇒ `ErrorCode=13`, không khởi động. Hạ `ManualRead`, chờ đủ 3,5 s, trigger lại.
- Trigger khi `Busy=TRUE` bị bỏ qua, không xếp hàng.
- `BenchEnable := FALSE` huỷ cục bộ nhưng **không huỷ** lệnh đã được C24 nhận.
- Tối đa 3 lần thử; lỗi ở chế độ polling được thử lại ở `PollTick` kế tiếp, `LastError` vẫn ghi.

---

## 7. Khung truyền/nhận và số học checksum

Cấu trúc: `SOH | station (1 ASCII) | command (2 ASCII) | STX | data No. (2 ASCII) | ETX | checksum (2 ASCII hex)`.

Checksum = **byte thấp** của tổng từ **station đến ETX** (bao gồm station, command, STX, data, ETX; **không** gồm byte mở khung (SOH ở request, STX ở response); STX nằm giữa request vẫn được tính.

Station trong khung là **một byte ASCII**: station 0–15 = `'0'`–`'F'` (30h–46h), station 16–31 = `'G'`–`'V'` (47h–56h). Station 1 ⇒ `31h` (`'1'`). Không gửi hai byte station.

### 7.1 TX station 1 — 10 byte

| # | Hex | ASCII | Ý nghĩa |
|---:|---|---|---|
| 1 | `01` | SOH | Start of header |
| 2 | `31` | `1` | Station 1 |
| 3 | `30` | `0` | Command `02`, ký tự 1 |
| 4 | `32` | `2` | Command `02`, ký tự 2 |
| 5 | `02` | STX | Start of text |
| 6 | `39` | `9` | Data No. `91`, ký tự 1 |
| 7 | `31` | `1` | Data No. `91`, ký tự 2 |
| 8 | `03` | ETX | End of text |
| 9 | `30` | `0` | Checksum high |
| 10 | `32` | `2` | Checksum low |

Chuỗi byte: `01 31 30 32 02 39 31 03 30 32`.

Số học checksum:

```text
31h + 30h + 32h + 02h + 39h + 31h + 03h
= 49 + 48 + 50 + 2 + 57 + 49 + 3
= 258 = 0102h
low byte = 02h  -> ASCII "02" = 30h 32h
```

Ánh xạ word trong POU (`TxData`, low byte trước):

| Word | Giá trị | Byte phát ra |
|---|---|---|
| `TxData[0]` | `H3101` | `01 31` |
| `TxData[1]` | `H3230` | `30 32` |
| `TxData[2]` | `H3902` | `02 39` |
| `TxData[3]` | `H0331` | `31 03` |
| `TxData[4]` | `H3230` | `30 32` |

### 7.2 RX dương — 14 byte

`STX | station | status | data8 | ETX | checksum2`

Ví dụ Position = 0, station 1:

```text
02 31 41 30 30 30 30 30 30 30 30 03 46 35
```

| Raw idx | Nội dung | Ví dụ |
|---:|---|---|
| 0 | STX | `02` |
| 1 | Station `1` | `31` (= 49) |
| 2 | Status `A` | `41` (= 65) |
| 3..10 | 8 ký tự hex vị trí 32-bit | `30` × 8 |
| 11 | ETX | `03` |
| 12..13 | Checksum ASCII | `46 35` = `"F5"` |

Checksum:

```text
31h + 41h + (8 x 30h) + 03h
= 49 + 65 + 384 + 3
= 501 = 01F5h
low byte = F5h -> "F5" = 46h 35h
```

### 7.3 RX lỗi — 6 byte

`STX | station | status | ETX | checksum2`

Ví dụ status `B`, station 1:

```text
02 31 42 03 37 36
```

```text
31h + 42h + 03h = 49 + 66 + 3 = 118 = 0076h
low byte = 76h -> "76" = 37h 36h
```

### 7.4 Status

| Status | ASCII | Hex | Ý nghĩa |
|---|---|---|---|
| `A` | 41h | 65 | Bình thường |
| `a` | 61h | 97 | Bình thường nhưng servo alarm ⇒ `ServoAlarm=TRUE`, `ErrorCode=12` |
| `B`..`F` / `b`..`f` | 42h..46h / 62h..66h | — | Lỗi parity / checksum / character / command / data No. (theo thứ tự manual §14.3.3) |

Khung 6 byte với status `A` vẫn **không** hợp lệ vì `Count <> 14` ⇒ `ErrorCode=8`.

### 7.5 Đọc `Position` (signed 32-bit)

- 8 ký tự `Raw[3..10]`, MSB trước.
- Nibble đầu ≥ 8 ⇒ số âm (bù hai): giá trị = nibble − 16 rồi dịch trái.
- `FFFFFFFF` = `-1`
- `80000000` = `-2147483648`
- `7FFFFFFF` = `2147483647`
- Đơn vị: **xung lệnh** (command pulse), data No. **91** — không dùng data No. 90.

### 7.6 Quy đổi vòng

Chỉ dùng cho bench case đã xác nhận `PA05 = 10000`, `PA06 = PA07 = 1` (CMX/CDV = 1:1); không tổng quát hóa vì `PA21` có thể chọn chế độ PA05 hoặc electronic gear. Kiểm tra `PA21` trước khi dùng công thức khác. Với bench case:

Với `PA05 = 10000`, `PA06 = 1`, `PA07 = 1`, `CMX/CDV = 1/1`:

```text
rev = Position / 10000
```

Ví dụ `Position = 25000` ⇒ `2.5` vòng. **CHƯA XÁC MINH** các tham số/gear ratio thực tế; chỉ dùng công thức sau khi đã đọc và xác nhận chúng.

---

## 8. Bảng mã lỗi và xử lý

| Mã | Tên | Nguyên nhân | Xử lý |
|---:|---|---|---|
| 1 | Disabled | `BenchEnable = FALSE` | Bật `BenchEnable` và chờ `StartupReady` |
| 2 | Configuration | G610/G611/G310/G324/G325/G449/G447/G448/G307/G333 lệch | Sửa switch setting + detailed setting; CPU STOP để ghi; kiểm tra lại bằng buffer monitor |
| 3 | Stale/clear | Hết giờ xả dữ liệu/xoá nhận tại State 10/11 | Kiểm tra `X89/X8A/X8B`; đảm bảo CH2 rảnh; thử lại |
| 4 | TX abnormal/timeout | `TxDone[1]` hoặc hết giờ ở State 20/50/40 | Kiểm tra cáp, module, `X89`; thử lại |
| 5 | RX timeout | State 30 hết 300 ms (tổng thời gian ở State 30) chưa có khung | Kiểm tra servo có nguồn, station, baud, parity, dây RX; USB đã rút chưa |
| 6 | RX abnormal/count | `RxDone[1]` hoặc `RxControl[2]` ngoài 1..32 | Kiểm tra cấu hình nhận, nhiễu, kết cuối |
| 7 | Overflow | `Count + RxControl[2] > 32` | Có dữ liệu rác/khung lạ; xả và thử lại |
| 8 | Frame | `Raw[0]<>2`, `Raw[1]<>49`, `Raw[Count-3]<>3`, hoặc `Count<>14` | **Station mismatch** (PC20 vs TX), sai frame, thiếu byte |
| 9 | Checksum | Checksum tính lại khác 2 ký tự nhận | Nhiễu, sai parity/baud, cáp/kết cuối |
| 10 | Invalid hex | Ký tự không phải `0-9/A-F` | Nhiễu, sai cấu hình, khung lệch |
| 11 | Negative status | Status khác `A` và `a` | Xem status B..F/b..f; xử lý theo lỗi tương ứng |
| 12 | Servo alarm | Status `a` | **Dừng**, xử lý alarm servo; không tiếp tục đọc |
| 13 | Startup guard | Trigger khi `StartupReady=FALSE` | Hạ trigger, chờ đủ 3,5 s, trigger lại |

### 8.1 Chẩn đoán nhanh

| Hiện tượng | Kiểm tra |
|---|---|
| `ErrorCode=2` | Switch setting CH2 (protocol/baud/parity) và detailed setting (G310/G324/G325) |
| `ErrorCode=4/5/6` | TX/RX vật lý: cáp, nguồn servo, module, `X89/X8A/X8B` |
| `ErrorCode=8` | Station: `PC20` phải = 1 và TX phải gửi `31`; kiểm tra frame/độ dài |
| `ErrorCode=9` | Checksum: nhiễu, parity/baud, kết cuối |
| `ErrorCode=12` | Servo alarm — dừng ngay |
| Không có phản hồi | USB còn cắm? CH2 còn MELSOFT? Sai station/baud/parity? Chưa chờ 3,5 s? |
| Không thấy dữ liệu trong Watch | POU chưa ở Scan Program; chưa download; chưa Start Monitoring |

---

## 9. Tiêu chí pass/fail

### 9.1 Pass

Đồng thời: `ReadSuccess=TRUE`, `PositionValid=TRUE`, `Count=14`, `Busy=FALSE`, `State=0`, `ErrorCode=0`, `ServoAlarm=FALSE`, `Raw[0]=2`, `Raw[1]=49`, `Raw[2]=65`, `Raw[11]=3`, checksum `Raw[12..13]` khớp tổng `Raw[1..11]`, 8 ký tự `Raw[3..10]` là hex hợp lệ.

`Attempt` có thể là 2/3 và `LastError` có thể khác 0 do lần lỗi trước; điều đó không phủ định kết quả cuối nếu `ErrorCode=0`.

### 9.2 Chưa kết luận

- Không phản hồi, dừng ở 3/4/5: chưa chứng minh giao thức hay vị trí.
- `PositionValid=TRUE` nhưng chưa đối chiếu vị trí cơ khí/encoder độc lập: chỉ chứng minh khung hợp lệ, **không** chứng minh vị trí tuyệt đối đúng.
- Một lần đọc đơn lẻ: nên lặp 2–3 lần, mỗi lần một sườn lên mới.

### 9.3 Bảng ghi chép

| Trường | Nội dung |
|---|---|
| Thời điểm / người thực hiện | Ngày giờ, tên, ca |
| Trạng thái ban đầu | `BenchEnable`, `StartupReady`, `State`, `ErrorCode`, `LastError` |
| Trigger | Thời điểm sườn lên `ManualRead` hoặc `PollEnable`; `Attempt` |
| Chuỗi State | Các giá trị `State` quan sát được |
| Khung | `Count`, hex `Raw[0..Count-1]`, checksum tính lại |
| Kết quả | `Position`, `PositionValid`, `ReadSuccess`, `ServoAlarm` |
| Kết thúc | `X89/X8A/X8B`, `State`, `Busy` sau khi disarm |
| Kết luận | Pass / chưa kết luận / dừng, kèm lý do |

---

## 10. CHƯA XÁC MINH

1. Firmware servo thực tế **A3 trở lên**.
2. Danh tính drive thực tế đang kết nối (MR-J4-A, không phải -RJ hay drive khác).
3. Đấu dây vật lý và kết cuối 150 Ω thực tế.
4. Số thứ tự terminal CH2 của QJ71C24N (phải đọc nhãn trên module).
5. Cách hiểu signedness trên từng khung thực tế.
6. Phản hồi phần cứng, vị trí thực, thời gian truyền thực.
7. Compile GX Works2 của `ABS_Read_Test` (chưa import/chưa build).
8. Giá trị live `PA03`/`PA05`/`PA06`/`PA07`/`PC20`/`PC21`.
9. Việc Watch hiển thị nhãn local khi monitor.
10. Việc CH2 đã được giải phóng khỏi MELSOFT và USB đã rút.

Không có nội dung nào trong tài liệu này là bằng chứng đã chạy thử phần cứng hay đã compile thành công.
