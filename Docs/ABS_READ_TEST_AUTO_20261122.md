# Bench đọc ABS tự động — ABS_Read_Test_Auto

- Ngày soạn: 2026-11-22
- POU: `ABS_Read_Test_Auto [PRG] Program [ST]` — biến thể tự động của `ABS_Read_Test`
- Nhãn: dùng lại `PLC/Mitsubishi/ABS_Read_Test_labels.csv` (UTF-16LE, tab-delimited). Không cần CSV mới.
- Phạm vi: **chỉ đọc vị trí ABS ở chế độ bench**. Không motion, không SON, không QD75, không sửa an toàn.

> Tài liệu này là quy trình thao tác, **không phải giấy phép thực hiện**. Chưa chạy trên phần cứng, chưa compile GX. Trạng thái: **CHƯA XÁC MINH**.

## 1. Khác biệt so với `ABS_Read_Test`

| Điểm | `ABS_Read_Test` | `ABS_Read_Test_Auto` |
|---|---|---|
| Kích hoạt | Đặt tay `BenchEnable` / `PollEnable` | Tự đặt cả hai TRUE ở scan RUN đầu tiên (`SM402`) |
| Polling | Cần `PollEnable` hoặc sườn lên `ManualRead` | Poll liên tục mỗi `PollPeriod` (mặc định `T#500ms`) |
| `ManualRead` | Cần cho một lần đọc đơn | Không cần |
| Cổng `PollTimer` | `PollEnable AND NOT Busy AND (State = 0)` | Thêm `AND StartupReady` để không phát `ErrorCode=13` trong 3,5 s khởi động |
| Parser / TX / mã lỗi | — | **Giữ nguyên byte-for-byte** |

Khác biệt duy nhất trong mã: header, khối `IF SM402 ...`, và điều kiện `PollTimer`. Không có logic nào khác thay đổi.

## 2. Import và đăng ký

1. **Backup project** trước khi sửa.
2. Mở project đích trong GX Works2.
3. Tạo POU mới `ABS_Read_Test_Auto [PRG] Program [ST]`; dán toàn bộ `PLC/Mitsubishi/ABS_Read_Test_Auto.st`.
4. Trong bảng **Local Label**, dùng `Read from CSV File` → chọn `PLC/Mitsubishi/ABS_Read_Test_labels.csv`.
5. Đăng ký `ABS_Read_Test_Auto` vào **Scan Program**.
6. **Gỡ `ABS_Home` khỏi Scan Program** (CH2 chỉ có một chủ sở hữu). Không chạy `ABS_Home` đồng thời với POU auto.
7. Compile / Rebuild All; chỉ coi là đạt cú pháp khi `Error: 0, Warning: 0, CheckWarning: 0`.
8. Download xuống PLC (hành động riêng, cần uỷ quyền), rồi đưa CPU sang **RUN**.

## 3. Đọc giá trị

Trong **POU inline monitor** (khung phải, cuộn xuống) — hoặc Watch nếu GX resolve được local label; POU này không tạo global label/D-M output:

- `Position` — DINT, vị trí ABS theo **xung lệnh** (command pulse). Cập nhật mỗi `PollPeriod`.
- `PositionValid`, `ReadSuccess`
- `Count` (kỳ vọng 14), `Raw[0..13]`
- `ErrorCode`, `LastError`, `State`, `PollCount`

`Position` chỉ có nghĩa khi `PositionValid = TRUE` và `ReadSuccess = TRUE`.

## 4. Lần đọc đầu tiên

- Sau RUN, chờ **~4 s** để `StartupReady` bật (timer 3,5 s).
- Với `PollTimer` đã chặn bằng `StartupReady`, **không** có `ErrorCode=13` trong giai đoạn khởi động.
- Sau đó POU tự poll; không cần thao tác tay.

## 5. Mã lỗi thường gặp

| Mã | Ý nghĩa | Xử lý |
|---:|---|---|
| 1 | `BenchEnable = FALSE` | Không xảy ra ở chế độ auto; nếu có, kiểm tra `SM402`/POU có được scan |
| 2 | Cấu hình CH2 sai | Kiểm tra G610/G611/G310/G324/G325 và detailed setting |
| 5 | Không phản hồi | USB CN5 còn cắm? Station/baud/parity? Servo có nguồn? |
| 8 | Station/frame | `PC20 = 1` khớp station 1; kiểm tra khung/độ dài |
| 9 | Checksum | Nhiễu, parity/baud, cáp/kết cuối |
| 12 | Servo alarm | **Dừng ngay**, xử lý alarm servo |

## 6. Điều kiện tiên quyết

- CH2: **Non procedure / 19200 / Even / 8 / 1 / None**.
- `G310 = 1`, `G324 = 1`, `G325 = FFFF`.
- Ghi switch/detailed setting cần **CPU STOP**.
- CH2 đã được giải phóng khỏi MELSOFT Connection.
- USB CN5 đã rút (USB và RS-422 loại trừ nhau).
- Servo **OFF**, không chuyển động, máy đã cô lập cơ khí.
- `PC20 = 1` khớp station 1 của khung TX.

## 7. An toàn

- Lệnh `02`/data No. `91` **ghi** giá trị lệnh vị trí nội bộ của amplifier, không phải đọc thụ động.
- **Power-cycle (hoặc khởi tạo lại) servo trước khi cho phép SON.** Đọc lại chỉ đặt lại vị trí lệnh nội bộ, **không** hoàn tác hiệu ứng phụ.
- Dừng ngay nếu servo alarm, có chuyển động ngoài ý muốn, hoặc bất kỳ đường SON/motion/QD75 nào active.

## 8. CHƯA XÁC MINH

- Compile GX Works2 của `ABS_Read_Test_Auto`.
- Chạy thực tế trên phần cứng (servo, QJ71C24N, cáp).
- Giá trị live `PA03`/`PA05`/`PA06`/`PA07`/`PC20`/`PC21`.

Không có nội dung nào trong tài liệu này là bằng chứng đã chạy thử phần cứng hay đã compile thành công.
