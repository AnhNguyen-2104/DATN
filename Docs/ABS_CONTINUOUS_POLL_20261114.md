# Đọc ABS liên tục trên GX Works2 — 2026-11-14/15

Mục tiêu: thêm chế độ polling có giới hạn cho `ABS_Home` để quan sát vị trí encoder MR-J4-A **liên tục ngay trong GX Works2**, chỉ qua đường PLC:

`ABS_Home (ST)` → QJ71C24N CH2 (`U8`, start XY `0080`) → RS-422 → MR-J4-A CN3.

> **MR Configurator2 / USB (CN5) bị loại khỏi tài liệu này.** Drive đang cắm khác và danh tính chưa xác minh, nên mọi số liệu MR2 (kể cả mẫu 2026-11-14 21:13 trong `Docs/ABS_POSITION_READ_20261113.md` §6) **không được dùng**. Không mở MR2, không kết nối servo, không đọc/ghi tham số servo.

## 1. Thay đổi trong mã nguồn

Nhãn local thêm vào `ABS_Home`:

| Nhãn | Kiểu | Vai trò |
|---|---|---|
| `PollEnable` | Bit | Công tắc tổng của chế độ polling, **mặc định FALSE**, POU không tự bật |
| `PollPeriod` | Time | Chu kỳ polling, mặc định `T#500ms` |
| `PollTimer` | TON | Timer tạo nhịp polling |
| `PollTick` | Bit | Xung một scan khi hết chu kỳ và không có giao dịch đang chạy |
| `PollCount` | Word [Signed] | Số lần poll hoàn tất kể từ khi `PollEnable` lên TRUE |
| `PrevPollEnable` | Bit | Bộ nhớ cạnh lên `PollEnable` |
| `PrevResetResult` | Bit | Bộ nhớ cạnh lên `ResetResult` |

Logic (đầu POU):

```st
PollTimer(IN := PollEnable AND NOT Busy AND (State = 0), PT := PollPeriod, Q := PollTick);
StartEdge := (ManualRead AND NOT PrevRead) OR PollTick;
PrevRead := ManualRead;
IF PollPeriod <= T#0ms THEN PollPeriod := T#500ms; END_IF;
IF PollEnable AND NOT PrevPollEnable THEN PollCount := 0; END_IF;
PrevPollEnable := PollEnable;
```

Cổng vô hiệu **giữ nguyên** như bản gốc — chỉ `BenchEnable`:

```st
IF NOT BenchEnable THEN
```

nghĩa là **chế độ đọc đơn chỉ cần `BenchEnable`** (cạnh lên `ManualRead`); `PollEnable` chỉ mở khoá phần tự động (`PollTimer` đã có `IN := PollEnable AND NOT Busy AND (State = 0)`). **Chế độ liên tục cần cả `BenchEnable` và `PollEnable`.**

Nhánh khởi động giữ nguyên điều kiện cũ, thêm đếm poll:

```st
  Busy := TRUE;
  State := 10;
  IF PollTick THEN PollCount := PollCount + 1; END_IF;
```

`PollCount` reset khi cạnh lên `ResetResult` (trong khối reset cũ: `IF NOT PrevResetResult THEN PollCount := 0; END_IF;`) và khi cạnh lên `PollEnable`; **không** reset khi `PollEnable` xuống FALSE.

Kiểm tra cấu hình cổng giữ đủ 9 điều kiện như bản gốc:

```st
   IF (U8\G610<>6) OR ((U8\G611 AND HFF3F)<>H070E) OR
    (U8\G310<>1) OR (U8\G324<>1) OR (U8\G325<>HFFFF) OR
    (U8\G449<>0) OR (U8\G447<>0) OR (U8\G448<>0) OR
    ((U8\G307 AND H0300)<>0) OR (U8\G333<>0) THEN
```

Thân POU kết thúc bằng `END_IF;` rồi tới các lệnh chuyên dụng:

```st
END_IF;
TxControl[0]:=2;
RxControl[0]:=2;
RxControl[3]:=16;
G_OUTPUT(TxFire, H8, TxControl, TxData[0], TxDone);
G_INPUT(RxFire, H8, RxControl, RxData[0], RxDone);
```

Giữ nguyên `ReadyTimer` 3.5 s và giới hạn 3 lần thử. Đọc lỗi trong chế độ liên tục không dừng polling; `PollTick` kế tiếp thử lại sau `PollPeriod`; `LastError` vẫn ghi lỗi.

## 2. Vì sao KHÔNG có bản đồ thiết bị (D/M)

Bản thử đầu tiên ghi kết quả ra `D9000` (DINT), `D9002` (INT), `M2000` (bit). Cách đó **đã bị gỡ bỏ hoàn toàn**:

- Compile trả về `A device of the device range set in the device/label automatic-assign setting or a device which cannot be used due to system reservation is being used.` — ghi trực tiếp vào `D9000`/`M2000` đụng **device/label automatic-assign range** khai báo trong PLC Parameter của project.
- Không cần thiết: khi monitor, toàn bộ nhãn local của POU (kể cả `Position`) hiển thị trực tiếp trong Watch.

Vì vậy **không có thiết bị toàn cục nào** cho chức năng này. Dải auto-allocated hiện tại (sau Rebuild All sạch):

| Loại | Dải | Số điểm |
|---|---|---|
| Word | `D12159`–`D12287` | 129 |
| Bit | `M8160`–`M8191` | 32 |
| Pointer | `P2048`–`P2123` | 76 |
| Timer | `T2044`–`T2047` | 4 |

Không dùng `D100..D114` (hợp đồng toạ độ offline) và không dùng các dải trên.

## 3. Cách xem giá trị liên tục trong GX Works2 (chỉ sau khi download, chưa làm)

1. `Online` → `Monitor` → `Start Monitoring` (cần kết nối PLC và `ABS_Home` phải nằm trong Scan Program).
2. Mở cửa sổ `ABS_Home [PRG] Program [ST]`.
3. `View` → `Watch` (hoặc tab **Watch 1** sẵn có).
4. Thêm từng dòng bằng **tên nhãn** (không phải địa chỉ):

| Label | Kiểu | Ý nghĩa |
|---|---|---|
| `Position` | Double Word [Signed] | Vị trí ABS cuối cùng, đơn vị **xung lệnh** (command pulse) |
| `PositionValid` | Bit | 1 = `Position` là giá trị đọc hợp lệ |
| `LastError` | Word [Signed] | Mã lỗi lần đọc gần nhất (giữ qua các lần retry) |
| `PollCount` | Word [Signed] | Số lần poll đã hoàn tất |
| `State` | Word [Signed] | Trạng thái máy trạng thái (0 = rảnh) |
| `ErrorCode` | Word [Signed] | Mã lỗi hiện hành |
| `Attempt` | Word [Signed] | Số lần thử trong chuỗi retry |
| `Count` | Word [Signed] | Số byte đã nhận |

5. `Position` chỉ cập nhật **một lần mỗi `PollPeriod`** (mặc định `T#500ms`) và **chỉ khi cả `BenchEnable` và `PollEnable` đều TRUE**; nếu `BenchEnable` FALSE thì `PollTimer` không chạy và `PositionValid` bị xoá.

## 4. Điều kiện tiên quyết trên PLC

| # | Hạng mục | Hiện trạng | Cần làm |
|---|---|---|---|
| 1 | Switch Setting CH2 của `0080:QJ71C24N` | **MELSOFT Connection / Auto Setting / None** | **Non procedure**, **19200**, **Even**, 8 data, 1 stop |
| 2 | `ABS_Home` trong Scan Program | **chưa có trong scan** | Đăng ký vào Scan Program |
| 3 | CN5 (USB) và CN3 (RS-422) | USB đang cắm | **Rút USB khỏi servo** — CN5 và CN3 loại trừ lẫn nhau |
| 4 | Ghi switch setting | — | **Write to PLC** với **CPU STOP** (b6 Write-during-RUN bị cấm) |
| 5 | An toàn | — | Không force, không servo ON, không motion, không sửa tham số servo |

## 5. Trạng thái thực hiện

- **Bảng Local Label**: nhập lại toàn bộ bằng `Read from CSV File` → `PLC/Mitsubishi/ABS_Home_labels.csv` (48 nhãn + header). Các dòng trước đây bị sửa sai ô đã đúng lại: `StartupReady` Bit (dòng 40), `PollEnable` Bit (41), `PollPeriod` Time (42), `PollTimer` TON (43), `PollTick` Bit (44), `PollCount` Word[Signed] (45), `PrevPollEnable` Bit (46), `PrevResetResult` Bit (47). Không còn dòng nào có text sai cột.
- **Thân ST**: kết thúc bằng `END_IF;` + `G_OUTPUT`/`G_INPUT`, không có lệnh ghi thiết bị.
- **Global label**: `G_ABS_Position` / `G_ABS_LastError` / `G_ABS_PositionValid` đã xoá; bảng Global1 trống.
- **Compile**: output verbatim `Rebuild All Completed.Error: 0, Warning: 0, CheckWarning: 0` (ảnh: `Docs/GX_EVIDENCE_20261114/rebuild_0_0_labels_20261115.png`).
- **Lưu project**: qua `Project` → `Save` (không dùng Ctrl+S — xem ghi chú §6). Tiêu đề cửa sổ không có dấu sửa đổi.
- **Readback**: GX tự xuống dòng lại comment header (thêm một dòng trống sau `...(bench isolation) and` và một khoảng trắng trong `each scan`); `PLC/Mitsubishi/ABS_Home.st` đã được sửa cho khớp đúng từng ký tự với bản GX lưu. Sau đó phần đầu 4095 ký tự (giới hạn đọc của textfield) khớp 4095/4095; so sánh toàn văn sau khi chuẩn hoá khoảng trắng cũng khớp.

## 6. Ghi chú kỹ thuật quan trọng

- **Ctrl+S trong phiên này gây hỏng nội dung ST**: mỗi lần Ctrl+S, GX chèn thêm ký tự rác vào textfield (`acac16`, `ac`, `s`, dòng trống). Nội dung đúng đã được nạp lại và lưu bằng `Project` → `Save`; sau đó readback sạch. Nếu người dùng mở lại project, nên kiểm tra lại bằng Ctrl+A/Ctrl+C một lần trước khi build.
- Textfield của GX chỉ trả tối đa 4095 ký tự qua UI Automation, nên readback đầy đủ phải dùng clipboard (Ctrl+A/Ctrl+C).

## 7. CHƯA XÁC MINH

1. **Chưa download** xuống PLC, chưa chạy hardware: không có bằng chứng runtime (chu kỳ thực, `PollCount`, giá trị `Position`).
2. **Chưa xác minh** CH2 đã đổi sang Non procedure/19200/Even; ảnh 2026-11-13 vẫn là MELSOFT/Auto/None.
3. **Chưa xác minh** USB đã rút và CN3 nối tới QJ71C24N CH2.
4. **Chưa xác minh** `ABS_Home` đã được đăng ký Scan Program.
5. **Chưa kiểm chứng** việc Watch hiển thị nhãn local khi monitor — phụ thuộc việc POU nằm trong scan; chỉ suy luận từ hành vi chuẩn của GX.
6. Giá trị vị trí trong tài liệu 2026-11-13 (ví dụ 215 159 xung) **không dùng làm tham chiếu** cho phiên này.
