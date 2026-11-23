# Quy trình bench đọc vị trí ABS MR-J4-A qua QJ71C24N CH2 — Robot_SCARA

- Ngày soạn: 2026-11-11
- Dự án native: `PLC/Mitsubishi/DATN_LINE/Robot_SCARA` — CPU Q06UDEHCPU
- Module serial: QJ71C24N, start XY `0080` (hex) ⇒ `U8`; dùng CH2 RS-422
- POU: `ABS_Home [PRG] Program [ST]`, đăng ký **Standby Program → MAIN1** ⇒ **không chạy trong scan thường**
- Phạm vi: chỉ đọc vị trí ABS ở chế độ bench; không motion, không SON, không đồng bộ counter QD75, không thay đổi an toàn

> Tài liệu này là **quy trình thao tác**, không phải giấy phép thực hiện. Không có nội dung nào dưới đây được chạy trong lần soạn tài liệu.

Nguồn đối chiếu hành vi: `PLC/Mitsubishi/ABS_Home.st` (máy trạng thái `State` 10/11/12/20/30/35/40/50/60/90), `Docs/MRJ4A_ABS_SCARA_20261110.md`, `Docs/TASK_MRJ4A_QJ71C24N_ABS_SCARA_20261110.md`, `Docs/TAG_MATRIX.md`.

---

## 1. Điều kiện tiên quyết

### 1.1 Phần cứng và firmware

| # | Hạng mục | Yêu cầu | Cách xác nhận | Trạng thái |
|---|---|---|---|---|
| 1 | Servo | MR-J4-A (không phải -RJ) | Nhãn máy | CHƯA XÁC MINH tại bench |
| 2 | Firmware servo | **A3 trở lên** mới hỗ trợ RS-422 | MR Configurator2 / nhãn version | CHƯA XÁC MINH |
| 3 | Cổng giao tiếp | CN3 RS-422 **loại trừ** CN5 USB | Không cắm USB đồng thời | CHƯA XÁC MINH |
| 4 | Cáp nối | 1 trục: DSV-CABV; nhiều trục: BMJ-8 | Kiểm tra vật lý | CHƯA XÁC MINH |
| 5 | Kết cuối | Chỉ multidrop: 150 Ω giữa RDP(3)–RDN(6) tại trục cuối phía nhận | Đo đồng hồ khi mất điện | CHƯA XÁC MINH |
| 6 | Shield | Nối vỏ shield về FG tại một điểm | Kiểm tra vật lý | CHƯA XÁC MINH |

### 1.2 Sơ đồ chân CN3 (MR-J4, RJ45 8 chân)

| Chân | Tín hiệu | Ghi chú |
|---:|---|---|
| 1 | LG | Ground logic |
| 2 | P5D | +5 V (không dùng cho RS-422) |
| 3 | RDP | Receive Data + (servo nhận) |
| 4 | SDN | Send Data − (servo phát) |
| 5 | SDP | Send Data + (servo phát) |
| 6 | RDN | Receive Data − (servo nhận) |
| 7 | LG | Ground logic |
| 8 | NC | Không nối |

Phía QJ71C24N CH2 (RS-422 full-duplex): `SDA/SDB` là cặp phát của module, `RDA/RDB` là cặp nhận, kèm `SG/FG`.

| Tín hiệu C24 | Nối CN3 | Chiều |
|---|---|---|
| SDA (send +) | RDP — chân 3 | C24 → servo |
| SDB (send −) | RDN — chân 6 | C24 → servo |
| RDA (receive +) | SDP — chân 5 | servo → C24 |
| RDB (receive −) | SDN — chân 4 | servo → C24 |
| SG | LG — chân 1 hoặc 7 | Tham chiếu chung |
| FG | Vỏ shield | Một điểm |

> **CHƯA XÁC MINH:** số thứ tự vật lý trên block terminal CH2 của QJ71C24N chưa được đối chiếu từ tài liệu cục bộ. Bắt buộc đọc nhãn in trên nắp module trước khi đấu dây; không suy đoán theo bảng trên.

### 1.3 Tham số servo — chỉ ĐỌC, KHÔNG GHI

| Tham số | Ý nghĩa | Kỳ vọng bench | Trạng thái |
|---|---|---|---|
| `PA03` | Chọn hệ thống ABS | Chọn chế độ ABS theo giao tiếp (bit-field) | **CHƯA XÁC MINH** — cả bit-field lẫn giá trị live |
| `PC20` | Số station | `0` | CHƯA XÁC MINH giá trị live |
| `PC21` | Baud + response delay | Baud khớp 19200; delay là trường riêng | CHƯA XÁC MINH giá trị live |

- `PA03` là **bit-field**, không phải số nguyên đơn nghĩa. Không đưa ra và không nhập **giá trị hex nguyên tham số** dựa trên tài liệu này.
- Không ghi `PA03`, `PC20`, `PC21` hay bất kỳ tham số servo nào trong quy trình này. Nếu sai: dừng, chuyển quy trình cấu hình có phê duyệt riêng.
- Sau cấp nguồn servo phải chờ **≥ 3,5 s** trước truyền thông thường (`ReadyTimer`, `T#3500ms`, dòng 14).

### 1.4 Cấu hình QJ71C24N CH2 — chỉ kiểm tra, không sửa

POU từ chối cấu hình không khớp thay vì ghi đè module (`State 10`, dòng 52–62):

| Kiểm tra | Giá trị yêu cầu | Nguồn trong `.st` |
|---|---|---|
| Giao thức | `6` (nonprocedure) | `U8\G610<>6` |
| Baud + frame | `H070E`: b8-b11 = `7` (19200 bps), b2 = parity Có, b3 = Even, b1 = 8 data bit, b4 = 1 stop, b5 = không sum check | `(U8\G611 AND HFF3F)<>H070E` |
| Đơn vị | byte (`1`) | `U8\G310<>1` |
| Số byte nhận | `1` | `U8\G324<>1` |
| Mã kết thúc | `HFFFF` (không có) | `U8\G325<>HFFFF` |
| Chuyển đổi ASCII/BIN | `0` | `U8\G449<>0` |
| Mã trong suốt | `0` | `U8\G447<>0` |
| User frame | `0` | `U8\G448<>0` |
| Bit cấm/cho phép | `(G307 AND H0300)=0` | `(U8\G307 AND H0300)<>0` |
| Echo back | `0` (tắt) | `U8\G333<>0` |

Lệch bất kỳ mục nào ⇒ `ErrorCode := 2`, về `State := 0` qua nhánh `90`; POU **không** tự sửa tham số module.

#### 1.4.1 Bit-field đã đối chiếu manual và đối chiếu cấu hình thực tế (2026-11-13)

Nguồn: SH(NA)-080006-W §4.5.2, p4-15…p4-18. `Un\G610` = protocol **hiện hành** CH2, `Un\G611` = transmission **hiện hành** CH2;
switch setting nằm ở `Un\G608`/`Un\G609` (§3.9, p3-29…31). POU đọc trạng thái đang chạy, không đọc giá trị khai báo.

| Bit trong `G611` | Ý nghĩa | `0x0E` | Yêu cầu |
|---|---|---|---|
| b0 | Operation setting (0 Independent / 1 Link) | 0 | 0 |
| b1 | Data bit (0 = 7 bit, 1 = 8 bit) | 1 | 1 |
| b2 | Parity bit (0 = No, 1 = Yes) | 1 | **1** |
| b3 | Even/odd (0 = Odd, 1 = Even) | 1 | **1** |
| b4 | Stop bit (0 = 1 bit, 1 = 2 bit) | 0 | 0 |
| b5 | Sum check code (0 = No, 1 = Yes) | 0 | 0 |
| b6 | Write during RUN (0 = Prohibited) | 0 | 0 |
| b7 | Setting modification (0 = Prohibited) | 0 | 0 |
| b8–b11 | Communication rate code | `7` | `7` = 19200 bps |
| b12–b15 | System, phải 0 | 0 | 0 |

Mã tham chiếu: protocol `6H` = Non procedure, `0H` = GX Developer connection (dialog GX gọi là "MELSOFT Connection");
rate `05H` = 9600, `07H` = 19200. `Auto Setting` chỉ có nghĩa với protocol `0H` ("GX Developer communication rate and
transmission specifications are automatically set"); manual KHÔNG nêu auto-rate cho non-procedure.

Đối chiếu ảnh `Switch Setting 0080:QJ71C24N` (CH2) ngày 2026-11-13:

| Mục trên dialog | Đang đặt | Cần | Kết luận |
|---|---|---|---|
| Communication protocol setting | MELSOFT Connection (`0H`) | Non procedure (`6H`) | **SAI** |
| Communication rate setting | Auto Setting | `19200` (`07H`) | **SAI** |
| Parity Bit | None | Even (b2=1, b3=1) | **SAI** |
| Data Bit | 8 | 8 | đúng |
| Stop Bit | 1 | 1 | đúng |
| Sum check code | None | None | đúng |
| Operation setting | Independent | Independent | đúng |
| Station number | 0 | không dùng ở non-procedure | đúng |

Ba mục sai ⇒ `G610` sẽ là `0` (không phải `6`) và `G611` low byte `06H` (không phải `0EH`) ⇒ POU báo `ErrorCode := 2`
ngay tại State 10. Cả ba là switch setting của module (thuộc parameter PLC): muốn áp dụng phải `Write to PLC` parameter;
vì b6 (`Write during RUN`) = Prohibited nên cần CPU **STOP**. POU chỉ đọc và từ chối, không tự ghi module.

### 1.5 Phân biệt U8 và U10

`Un = start XY (hex) / 16`. Sơ đồ rack:

| Module | Start XY (hex) | Un |
|---|---:|---:|
| QD75D4 | `0000` | `U0` |
| QY42P | `0020` | `U2` |
| QX41 | `0060` | `U6` |
| QJ71C24N | `0080` | `U8` |
| QJ61BT11N | `0100` | `U10` |

- ABS (`ABS_Home.st`) chỉ dùng **`U8`** — buffer QJ71C24N CH2. Mọi BFM `G610`, `G611`, `G310`, `G324`, `G325`, `G328`, `G307`, `G333`, `G447`, `G448`, `G449`, `G2560` trong POU đều thuộc `U8`.
- **`U10`** là QJ61BT11N (CC-Link master), **không liên quan** tới ABS; `ABS_Home.st` không có BFM CC-Link nào.
- Sai lầm thường gặp: gán C24 cho `U10` theo thứ tự slot, hoặc đọc buffer CC-Link khi tìm dữ liệu serial. `U` suy ra từ start XY, không từ slot.
- Chi tiết: `Docs/MODULE_ADDRESSING_U8_U10_20261112.md`.

---

## 2. Cổng uỷ quyền — đọc thật chỉ khi hội đủ

| Mã | Điều kiện | Vì sao bắt buộc |
|---|---|---|
| A | **Lên lịch/gọi POU Standby để nó thực sự chạy** | `ABS_Home` đang ở Standby → MAIN1 nên **không được scan**; không gọi thì mọi biến đứng yên |
| B | **Hạ tải/ghi PLC có uỷ quyền** | Thay đổi lịch trình/gọi POU phải viết xuống Q06UDEHCPU; tài liệu này không cho phép ghi |
| C | **Servo đã cấp nguồn và đấu dây** | Không nguồn/không CN3 thì không có phản hồi; cấp nguồn theo quy trình an toàn riêng của máy |
| D | **Máy cô lập, không có đường lệnh chuyển động** | Lệnh 02/91 còn ghi giá trị lệnh vị trí vào amplifier; không được để motion/SON/QD75 hoạt động |

Bổ sung:

- Chỉ **một** chương trình sở hữu CH2. Không chạy đồng thời user frame, giao thức khác, hay ghi/monitor-write trên CH2.
- Không force I/O, không sửa safety, không đổi tham số servo/C24 trong phiên bench.
- Hạ tải phải có backup và biên bản riêng. Compile 0 lỗi/0 cảnh báo (theo checkpoint 2026-11-10) chỉ chứng minh cú pháp, không chứng minh vận hành.

> **Quy trình này KHÔNG phải là uỷ quyền.** Thiếu bất kỳ mục A–D nào thì dừng ở mức đọc tài liệu.

---

## 3. Trình tự bench từng bước

### 3.1 Bảng trạng thái `State`

| State | Ý nghĩa | Chờ | Chuyển tiếp |
|---:|---|---|---|
| 0 | Idle | — | `StartEdge` hợp lệ → 10 |
| 10 | Xả dữ liệu cũ + kiểm tra cấu hình | 500 ms | Hợp lệ → `U8\G328 := 1`, sang 11; lệch cấu hình → 2/90; hết giờ → 3/90 |
| 11 | Chờ xoá bộ nhận | 500 ms | `U8\G328 = 0` → 12; hết giờ → 3/90 |
| 12 | Chờ 100 ms, nạp khung TX | 100 ms | `RetryExpired` → phát 10 byte, sang 20 |
| 20 | Chờ hoàn tất phát lệnh | 500 ms | `TxDone[0]` & `TxDone[1]` → 4/90; bình thường → 30; hết giờ → 4/90 |
| 30 | Cửa sổ nhận | **300 ms** | Đủ byte + thấy ETX → 35; lỗi nhận → 6/40; tràn → 7/40; hết giờ → 5/40 |
| 35 | Phân tích khung | 1 scan | Hợp lệ → 0; lỗi → 40; riêng 12 → 90 |
| 40 | Quyết định retry | 500 ms | `Attempt>=3` → 90; hết giờ → 4/90; idle → phát EOT, sang 50 |
| 50 | Chờ hoàn tất EOT | 500 ms | Xong → 60; lỗi/hết giờ → 4/90 |
| 60 | Chờ 100 ms trước retry | 100 ms | `RetryExpired` → `Attempt+1`, về 10 |
| 90 | Kết thúc lỗi | — | `LastError := ErrorCode`, `PositionValid := FALSE`, `Busy := FALSE`, `State := 0` |

Thời gian lấy từ `WaitTimer` (dòng 14–20) và `RetryTimer` (dòng 21–22): `WaitPreset` 500 ms, riêng State 30 là 300 ms.

### 3.2 Các bước thao tác

| Bước | Thao tác | Quan sát / điều kiện | Căn cứ |
|---:|---|---|---|
| B1 | Giữ `BenchEnable := FALSE`, `ManualRead := FALSE` | `State=0`, `Busy=FALSE`, `PositionValid=FALSE` | dòng 28–33 |
| B2 | Đặt `BenchEnable := TRUE`, giữ liên tục | `StartupReady` chỉ TRUE sau **3,5 s** liên tục | dòng 14 |
| B3 | Khi `StartupReady=TRUE`, phát một xung `ResetResult` lúc `Busy=FALSE` | Xoá `ReadSuccess`, `ErrorCode`, `LastError`; **không** xoá `Position`/`PositionValid` | dòng 23–27 |
| B4 | Kiểm tra trạng thái nghỉ | `State=0`, `Busy=FALSE`, `ErrorCode=0`, `LastError=0`, `StartupReady=TRUE` | dòng 23–46 |
| B5 | Tạo **đúng một sườn lên** `ManualRead` (TRUE) | `StartEdge` chỉ 1 scan; chỉ nhận khi `NOT Busy` và `StartupReady` | dòng 10–11, 36–46 |
| B6 | Hạ `ManualRead := FALSE` ngay sau khi đã vào chuỗi | Cho phép tạo sườn lên lần sau; tránh trigger lặp | dòng 36 |
| B7 | Theo dõi `State`: 10→11→12→20→30→35→0 | Thành công: `ReadSuccess=TRUE`, `PositionValid=TRUE`, `Count=14` | dòng 47–148 |
| B8 | Nếu lỗi: 40→50→60→10 (retry) hoặc 90 | Tối đa **3 lần thử** (`Attempt` 1→2→3) | dòng 149–164 |
| B9 | Kết thúc: `BenchEnable := FALSE` | Cục bộ về `State=0`, `ErrorCode=1`; **không** huỷ truyền đang bay | dòng 28–33 |

Lưu ý bắt buộc:

- Sườn lên khi `StartupReady=FALSE` ⇒ `ErrorCode := 13`, **không** khởi động (dòng 35). Phải hạ `ManualRead`, chờ đủ 3,5 s rồi trigger lại.
- Sườn lên khi `Busy=TRUE` bị bỏ qua (điều kiện `NOT Busy`), không xếp hàng.
- `ResetResult` là mức, không phải sườn: giữ TRUE lúc rảnh sẽ xoá lặp mỗi scan; chỉ nên phát xung.
- `BenchEnable := FALSE` **huỷ cục bộ** (State 0, Busy FALSE, ErrorCode 1) nhưng **không huỷ** lệnh truyền đã được C24 nhận. **Không re-arm** cho tới khi `X89` (đang phát), `X8A` (yêu cầu đọc), `X8B` (nhận bất thường) đều **OFF**.

---

## 4. Danh sách theo dõi (Watch list)

Dùng đúng tên nhãn; GX tự cấp phát device khi rebuild nên **không ghi trực tiếp** địa chỉ D/M.

| Nhãn | Kiểu | Ý nghĩa / giá trị cần hiểu |
|---|---|---|
| `BenchEnable` | BOOL | Công tắc arm. FALSE ⇒ xoá cục bộ, `ErrorCode=1` |
| `ManualRead` | BOOL | Trigger đọc; chỉ tác dụng ở **sườn lên** |
| `Busy` | BOOL | TRUE từ lúc nhận trigger tới thành công hoặc State 90 |
| `State` | INT | 0/10/11/12/20/30/35/40/50/60/90 (bảng 3.1) |
| `Attempt` | INT | Lần thử hiện tại, 1..3; tăng tại State 60 |
| `ErrorCode` | INT | Mã lỗi hiện hành; 0 khi rảnh/thành công |
| `LastError` | INT | Mã lỗi của lần thử trước; **giữ qua retry**; gán lại tại State 40/90 |
| `Count` | INT | Số byte đã gom vào `Raw`; kỳ vọng 14 |
| `Raw[0..31]` | ARRAY OF INT | Byte nhận 0..255; chỉ `Raw[0..Count-1]` có nghĩa |
| `ReadSuccess` | BOOL | Chốt TRUE khi phân tích thành công; xoá bởi reset/trigger mới/disable |
| `PositionValid` | BOOL | TRUE chỉ khi `Position` đến từ khung đã kiểm tra |
| `ServoAlarm` | BOOL | TRUE khi status `'a'`; xoá ở trigger mới, **không** xoá bởi reset/disable |
| `Position` | DINT | Vị trí signed 32-bit, đơn vị xung lệnh (command pulse) |
| `TxDone[0..1]` | ARRAY OF BOOL | `[0]` hoàn tất phát; `[1]` hoàn tất bất thường |
| `RxDone[0..1]` | ARRAY OF BOOL | `[0]` hoàn tất nhận; `[1]` nhận bất thường |
| `TimerExpired` | BOOL | Hết thời gian dwell của state hiện tại (500 ms; 300 ms ở State 30) |
| `RetryExpired` | BOOL | Hết 100 ms ở State 12/60 |
| `StartupReady` | BOOL | TRUE sau 3,5 s `BenchEnable` liên tục |
| `StartEdge` | BOOL | Xung 1 scan của `ManualRead` |
| `PrevRead` | BOOL | Giá trị `ManualRead` scan trước |
| `ResetResult` | BOOL | Mức xoá `ReadSuccess`/`ErrorCode`/`LastError` khi `NOT Busy` |

Chẩn đoán BFM **chỉ đọc** (không ghi):

| BFM | Giá trị POU yêu cầu | Ghi chú |
|---|---|---|
| `U8\G610` | `6` | Giao thức CH2 nonprocedure |
| `U8\G611` | `(G611 AND HFF3F) = H070E` | Baud 07 (bits 8..11) + 8E1 (`0E` low) |
| `U8\G324` | `1` | Điều kiện cấu hình nhận của POU |
| `U8\G325` | `HFFFF` | Điều kiện cấu hình nhận của POU |

Ngoài ra POU đọc `G310`, `G307`, `G447`, `G448`, `G449`, `G333`, `G2560`, `G328`; chỉ `G328` do chính POU ghi `1` để xoá nhận.

---

## 5. Kỳ vọng khung truyền/nhận

### 5.1 TX — 10 byte (dòng 72–74)

| # | Hex | ASCII | Ý nghĩa |
|---:|---|---|---|
| 1 | `01` | SOH | Start of header |
| 2 | `30` | `0` | Station 0 (1 byte ASCII) |
| 3 | `30` | `0` | Command `02`, ký tự 1 |
| 4 | `32` | `2` | Command `02`, ký tự 2 |
| 5 | `02` | STX | Start of text |
| 6 | `39` | `9` | Data No. `91`, ký tự 1 |
| 7 | `31` | `1` | Data No. `91`, ký tự 2 |
| 8 | `03` | ETX | End of text |
| 9 | `30` | `0` | Checksum high nibble |
| 10 | `31` | `1` | Checksum low nibble |

Checksum = byte thấp của tổng từ **station đến ETX** (byte 2..8):
`30+30+32+02+39+31+03 = 0x0101` ⇒ low byte `01` ⇒ ASCII `"01"` (`30 31`).

### 5.2 RX chuẩn — 14 byte

Cấu trúc: `STX station status data8 ETX checksum2`.

| Raw idx | Nội dung | Ví dụ (Position = 0) |
|---:|---|---|
| 0 | STX `02` | `02` |
| 1 | Station `30` | `30` |
| 2 | Status: `41`=`A` bình thường, `61`=`a` báo alarm | `41` |
| 3..10 | 8 ký tự hex của vị trí 32-bit | `30 30 30 30 30 30 30 30` |
| 11 | ETX `03` | `03` |
| 12..13 | 2 ký tự hex checksum | `46 34` (`"F4"`) |

Kiểm tra checksum: tổng `Raw[1..11]` = `30+41+(8×30)+03 = 500 = 0x1F4` ⇒ low byte `F4` ⇒ `"F4"` khớp.
`Count = 14`; `Raw[14..31] = 0` (đã xoá tại State 12).

### 5.3 RX âm — 6 byte

Cấu trúc: `STX station status ETX checksum2` ⇒ `Count = 6`, ETX nằm tại `Raw[3]`.

| Raw idx | Nội dung | Ví dụ status `B` |
|---:|---|---|
| 0 | `02` | `02` |
| 1 | `30` | `30` |
| 2 | Status lỗi (`B`..`F`/`b`..`f`) | `42` |
| 3 | `03` | `03` |
| 4..5 | Checksum ASCII | `37 35` (`"75"`) |

Tổng `Raw[1..3] = 30+42+03 = 0x75` ⇒ `"75"` khớp. Status khác `A`/`a` ⇒ `ErrorCode=11`.
Status `a` (`61`) ⇒ `ServoAlarm=TRUE`, `ErrorCode=12`, sang State 90.
Khung 6 byte với status `A` vẫn **không** hợp lệ vì `Count<>14` ⇒ `ErrorCode=8`.

### 5.4 Cách đọc `Position`

- 8 ký tự `Raw[3..10]` là hex 32-bit, MSB trước; parse tại dòng 128–140.
- Nibble đầu ≥ 8 ⇒ số âm: giá trị = nibble − 16 rồi dịch trái (bù hai). Ví dụ `FFFFFFFF` ⇒ `-1`; `80000000` ⇒ `-2147483648`.
- Đơn vị là **xung lệnh** (command pulse), lấy từ data No. **91** (không dùng data 90).
- Lệnh 02/91 còn **ghi giá trị lệnh vị trí vào amplifier** khi nhận; không được coi là thao tác vô hại.

---

## 6. Bảng mã lỗi (trích từ `.st`)

| Mã | Tên | Điều kiện phát sinh | Dòng |
|---:|---|---|---|
| 1 | Disabled | `BenchEnable = FALSE` | 28–33 |
| 2 | Configuration | State 10: G610/G611/G310/G324/G325/G449/G447/G448/G307/G333 lệch | 52–62 |
| 3 | Stale/clear | Hết giờ xả dữ liệu/xoá nhận tại State 10/11 | 64, 67 |
| 4 | TX abnormal/timeout | `TxDone[1]` hoặc hết giờ tại State 20/50; hết giờ idle tại State 40 | 80, 82, 160, 162, 152 |
| 5 | RX timeout | State 30 hết 300 ms chưa có khung hợp lệ | 105 |
| 6 | RX abnormal/count | `RxDone[1]` hoặc `RxControl[2]` ngoài 1..32 | 88 |
| 7 | Overflow | `G2560 > 32` hoặc `Count + RxControl[2] > 32` | 85, 90 |
| 8 | Frame | `Raw[0]<>2`, `Raw[1]<>48`, `Raw[Count-3]<>3`, hoặc `Count<>14` | 108, 126 |
| 9 | Checksum | Checksum tính lại khác 2 ký tự hex nhận được | 122 |
| 10 | Invalid hex | Ký tự checksum hoặc dữ liệu không phải `0-9/A-F` | 119, 133 |
| 11 | Negative status | Status khác `A` và khác `a` | 125 |
| 12 | Servo alarm | Status `a` (`97`) | 124 |
| 13 | Startup guard | `StartEdge` khi `StartupReady=FALSE` | 35 |

Ghi chú:

- State 35 đặt `ErrorCode := 0` khi bắt đầu phân tích (dòng 107), nên mã lỗi cũ không rò sang khung mới.
- `LastError` được gán tại State 40 và 90; **giữ nguyên qua các lần retry**. Sau một lần thành công ở lần thử 2/3, `LastError` có thể vẫn là mã lỗi cũ — không dùng một mình để kết luận fail.
- Tại State 40, nếu hết giờ idle, `ErrorCode` bị ghi đè thành 4; `LastError` đã chốt trước đó nhưng sẽ được gán lại bằng `ErrorCode` khi vào State 90.

---

## 7. Tiêu chí pass/fail và ghi chép

### 7.1 Pass thật

Hội đủ: `ReadSuccess=TRUE`, `PositionValid=TRUE`, `Count=14`, `Busy=FALSE`, `State=0`, `ErrorCode=0`, `ServoAlarm=FALSE`, `Raw[0]=2`, `Raw[1]=48`, `Raw[2]=65`, `Raw[11]=3`, checksum `Raw[12..13]` khớp tổng `Raw[1..11]`, 8 ký tự `Raw[3..10]` là hex hợp lệ.

`Attempt` có thể là 2 hoặc 3 và `LastError` có thể khác 0 do lần lỗi trước; điều đó **không** phủ định kết quả cuối nếu `ErrorCode=0`.

### 7.2 Chưa kết luận

- Không có phản hồi, dừng ở 5/4/3: chưa chứng minh được giao thức hay vị trí.
- `PositionValid=TRUE` nhưng chưa đối chiếu vị trí cơ khí/encoder độc lập: chỉ chứng minh khung đọc hợp lệ, **không** chứng minh vị trí tuyệt đối đúng.
- Một lần đọc đơn lẻ: nên lặp 2–3 lần, mỗi lần một sườn lên mới, và ghi lại tính nhất quán.
- Lệch cấu hình (2): chưa kết luận về servo; dừng và xử lý cấu hình.

### 7.3 Điều kiện dừng bắt buộc

- Servo báo alarm, status `a`, `ServoAlarm=TRUE` hoặc `ErrorCode=12`.
- Có chuyển động ngoài ý muốn hoặc bất kỳ đường lệnh motion/SON nào đang hoạt động.
- Lỗi checksum/frame/hex lặp lại; chạm giới hạn 3 lần thử (`Attempt>=3`).
- `ErrorCode=2` (sai cấu hình) hoặc nghi ngờ sai firmware/đấu dây/kết cuối.
- Bất kỳ can thiệp force I/O, bypass safety, hoặc thay đổi tham số ngoài phạm vi.

### 7.4 Ghi chép bắt buộc

| Trường | Nội dung |
|---|---|
| Thời điểm / người thực hiện | Ngày giờ, tên, ca |
| Trạng thái ban đầu | `BenchEnable`, `StartupReady`, `State`, `ErrorCode`, `LastError` |
| Trigger | Thời điểm sườn lên `ManualRead`; `Attempt` |
| Chuỗi State | Các giá trị `State` quan sát được |
| Khung | `Count`, hex `Raw[0..Count-1]`, checksum tính lại |
| Kết quả | `Position`, `PositionValid`, `ReadSuccess`, `ServoAlarm` |
| Kết thúc | `X89/X8A/X8B`, `State`, `Busy` sau khi disarm |
| Kết luận | Pass / chưa kết luận / dừng, kèm lý do |

---

## 8. Danh sách CHƯA TEST

- Chưa hạ tải/ghi PLC; chưa thay đổi lịch trình Standby.
- Chưa chạy scan thật hoặc mô phỏng scan.
- Chưa có phản hồi phần cứng nào từ servo/C24.
- Chưa xác minh vị trí thực tế hay đơn vị xung so với cơ khí.
- Chưa đo thời gian truyền thực, chưa kiểm tra kết cuối/đấu dây thực.
- Chưa đọc giá trị live `PA03`/`PC20`/`PC21` hay firmware servo.

---

## 9. Trạng thái xác minh

| Hạng mục | Trạng thái | Căn cứ |
|---|---|---|
| Nội dung POU và máy trạng thái 10/11/12/20/30/35/40/50/60/90 | ĐÃ KIỂM CHỨNG | Đọc trực tiếp `PLC/Mitsubishi/ABS_Home.st` |
| Mã lỗi 1–13 và thứ tự ưu tiên | ĐÃ KIỂM CHỨNG | Đọc mã dòng 28–166 |
| Khung TX 10 byte + checksum `0x101` → `"01"` | ĐÃ KIỂM CHỨNG | Hằng số `TxData` dòng 72–73; tính tay |
| Parser 14/6 byte, signed 32-bit | ĐÃ KIỂM CHỨNG | Dòng 106–148 |
| Chân CN3 1/3/4/5/6/7 (LG, RDP, SDN, SDP, RDN) | ĐÃ KIỂM CHỨNG | Trích SH030107V §14.2.1 do giám đốc xác minh |
| Số chân terminal CH2 của QJ71C24N | CHƯA XÁC MINH | Chưa đối chiếu tài liệu cục bộ; xem nắp module |
| `PA03` bit-field và giá trị live | CHƯA XÁC MINH | Không đọc/không ghi |
| `PC20`/`PC21` giá trị live | CHƯA XÁC MINH | Không đọc/không ghi |
| Firmware servo thực tế | CHƯA XÁC MINH | Chưa kiểm tra tại bench |
| Compile native 0 lỗi/0 cảnh báo | ĐÃ KIỂM CHỨNG (theo checkpoint) | `Docs/MRJ4A_ABS_SCARA_20261110.md`; không chạy lại |
| Thực thi scan / hạ tải / phản hồi phần cứng / vị trí | CHƯA XÁC MINH | Không thực hiện |
