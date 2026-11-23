# Snapshot tham số ABS — MR-J4-A / SCARA (2026-11-13)

- Nguồn: `PLC/Mitsubishi/Servo/Scara.prm2` (XML) và `PLC/Mitsubishi/Servo/Scara.mrc2` (OLE2/CFB).
- Phạm vi: chỉ đọc file đã lưu. Không kết nối USB/RS-422, không đọc live drive, không ghi/nạp tham số, không build, không GUI.
- Mọi giá trị là **giá trị lưu trong file dự án**, không phải giá trị đang chạy trên amplifier.

## 1. Tham số ABS / truyền thông / encoder

Cột nguồn: `dòng` 1-based; `byte` 0-based tính từ đầu file `.prm2`; `value@` là byte bắt đầu của cột giá trị (trường thứ 4 của dòng CSV).

| Tham số | Tên trong file | Giá trị lưu | Kiểu | Diễn giải theo MR-J4 | Nguồn (dòng · byte · value@) |
|---|---|---|---|---|---|
| PA01 | `*STY` Operation mode | `1000` | Hex → 0x1000 = 4096 | Chế độ điều khiển vị trí (position control mode); nibble thấp `0` = position control | 673 · 57150 · 57175 |
| PA03 | `*ABS` Absolute position detection system | `0002` | Hex → 2 | `2` = bật ABS theo giao tiếp (communication-based); `0` = tắt/incremental, `1` = ABS qua DIO | 675 · 57239 · 57284 |
| PA05 | `*FBP` Number of command input pulses per revolution | `10000` | Dec | 10 000 xung lệnh/vòng động cơ (khi electronic gear 1:1) | 677 · 57352 · 57408 |
| PA06 | `CMX` Elec. gear numerator | `1` | Dec | Tử số bánh răng điện tử = 1 | 678 · 57429 · 57489 |
| PA07 | `CDV` Elec. gear denominator | `1` | Dec | Mẫu số bánh răng điện tử = 1 | 679 · 57504 · 57566 |
| PC20 | `*SNO` Station number setting | `0` | Dec | Station 0 (dải 0–31) | 788 · 63971 · 64004 |
| PC21 | `*SOP` RS-422 Communication function selection | `0010` | Hex → 0x0010 = 16 | Trường chọn baud = `1` → 19200 bps; trường response delay = `0` → tắt/0 ms | 789 · 64020 · 64070 |
| PC22 | `*COP1` Function selection C-1 | `0000` | Hex | Lựa chọn chức năng C-1, không bật bit nào | 790 · 64087 · 64121 |
| PC23 | `*COP2` Function selection C-2 | `0000` | Hex | Lựa chọn chức năng C-2, không bật bit nào | 791 · 64138 · 64172 |
| PC24 | `*COP3` Function selection C-3 | `0000` | Hex | Lựa chọn chức năng C-3, không bật bit nào | 792 · 64189 · 64223 |
| PB01 | `FILT` Adaptive tuning mode (Adaptive filter II) | `0000` | Hex | **Không phải** độ phân giải encoder; chế độ adaptive filter II | 705 · 58877 · 58929 |
| PB02 | `VRFT` Vib. supp. ctrl. tuning mode | `0000` | Hex | **Không phải** độ phân giải encoder; chế độ tuning vibration suppression | 706 · 58946 · 59012 |
| PB03 | `PST` Pos. cmd. accel./decel. time const. | `0` | Dec, ms | **Không phải** độ phân giải encoder; hằng số thời gian smoothing lệnh vị trí | 707 · 59029 · 59091 |
| PA15 | `*ENR` Encoder output pulse | `10000` | Dec, pulse/rev | Xung **output encoder** (không phải độ phân giải encoder động cơ) | 687 · 57923 · 57954 |
| PA16 | `*ENR2` Encoder output pulse 2 | `1` | Dec | Hệ số xung output encoder 2 | 688 · 57981 · 58015 |
| PC19 | `*ENRS` Encoder output pulse selection | `0000` | Hex | Chọn nguồn/kiểu xung output encoder; giá trị lưu 0 | 787 · 63912 · 63954 |
| PA17 | `*MSR` Servo motor series setting | `0000` | Hex | Khai báo series động cơ; giá trị lưu 0 | 689 · 58029 · 58066 |
| PA18 | `*MTY` Servo motor type setting | `0000` | Hex | Khai báo kiểu động cơ; giá trị lưu 0 | 690 · 58083 · 58118 |
| PF34 | `*SOP3` RS422 Communication function selection 3 | `0000` | Hex | Lựa chọn chức năng RS-422 số 3; giá trị lưu 0 | 994 · 74854 · 74906 |

Ghi chú:

- Tham số tên `*ENC`: **KHÔNG CÓ TRONG FILE**.
- Tham số độ phân giải encoder động cơ (ví dụ `*ENC`, `ENC`, `resolution`): **KHÔNG CÓ TRONG FILE**.
- Chỉ có các tham số liên quan encoder output: `*ENR` (PA15), `*ENR2` (PA16), `*ENRS` (PC19). PA15 = `10000` pulse/rev là xung **output**, không phải độ phân giải encoder động cơ.
- MR-J4 dùng encoder 22-bit cố định **4 194 304 xung/vòng** theo manual; đây là thông số phần cứng, không phải giá trị lưu trong hai file này.
- `PA03 = 0002` là lựa chọn ABS theo giao tiếp, phù hợp tuyến RS-422; điều kiện firmware A3+ **chưa xác minh** từ file.

## 2. Nhận dạng amplifier và metadata file

### 2.1 `Scara.prm2`

| Trường | Giá trị nguyên văn | Vị trí |
|---|---|---|
| Ứng dụng | `MELSOFT MR Configurator2` version `1.165X` | dòng 3 · byte 64 |
| dataType / lang / connection | `PARAMETER` / `en` / `USB` | dòng 5–7 · byte 162–215 |
| ampType | `MR-J4-A` | dòng 15 · byte 411 |
| ampBaseType | `17` | dòng 16 · byte 442 |
| ampSoftwareNo | `BCD-B46W100 (factory setting)` | dòng 17 · byte 476 |
| ampOptionUnitType | `No Connection` | dòng 18 · byte 541 |
| AmpType / AmpTypeName | `MR-J4-A` / `MR-J4-A (-RJ) Standard` | dòng 22–23 · byte 633–663 |
| WritTime / UnitSerialID | rỗng (`<UnitSerialID/>`) | dòng 27–28 · byte 789–803 |

- Không có timestamp hay serial drive bên trong XML.
- Metadata hệ thống tệp: `Scara.prm2` size `91799` byte, mtime `2026-11-11 13:28:33.7258367 +07:00`.
- `ampSoftwareNo` chỉ là chuỗi lưu kèm `(factory setting)`; không đủ kết luận firmware đang chạy.

### 2.2 `Scara.mrc2`

- 8 byte đầu: `D0 CF 11 E0 A1 B1 1A E1` → OLE2/CFB; sector 512 byte, mini-sector 64 byte.
- Size `448512` byte; mtime `2026-11-11 18:22:51.820000 +07:00`.
- Stream cấp 1: `checkout.xml`, `dataprotection.xml`, `history.xml`, `label.xml`, `labellink.xml`, `Project.mr2` (0 byte), `projectdatalist.xml`, `projectlist.xml`, `securitylevel.xml`, `storedhistory.xml`, `user.xml`, `_hdb`.
- `_hdb` là CFB lồng, chứa `AxisData.bin` 71 286 byte (stream số 3), `systemData.bin` 130 byte, `GenericData.bin` 578 byte.
- Timestamp nội bộ:
  - Project `Scara`: `2026-11-11T18:22:51.5455663+07:00`.
  - `Scara.prj`: create `2026-11-11T18:22:51.265303+07:00`, update `2026-11-11T18:22:51.2663025+07:00`.
  - `AxisData.bin`: create/update `2026-11-11T18:22:51.2703276+07:00`; MD5 trong `history.xml`: `1i0GNiOn/bmIHb5vtXwXFQ==`.
  - `systemData.bin`: `2026-11-11T18:22:51.2698059+07:00`; `GenericData.bin`: `2026-11-11T18:22:51.2703276+07:00`.
- `AxisData.bin` là nhị phân, **không có nhãn `PA/PC`, `*ABS`, `*SOP`, `No. 90`, `No. 91`** dạng text (ASCII lẫn UTF-16LE). Không có giá trị vị trí ABS live nào đọc được từ `Scara.mrc2`; đây chỉ là container dự án đã lưu.

## 3. Quy đổi vị trí ABS → vòng động cơ

- `PA05 = 10000` (xung lệnh/vòng), `PA06 = 1`, `PA07 = 1` → electronic gear 1:1.

Công thức với vị trí đọc từ **data No. 91** (đơn vị **xung lệnh**):

```text
vòng động cơ = vị trí ABS (xung lệnh) / (PA05 × PA07 / PA06)
             = vị trí ABS / 10000        (vì PA06 = PA07 = 1)
```

Ví dụ: `10000` xung lệnh → `1.000` vòng động cơ.

Phân biệt hai nguồn:

- **Data No. 91**: vị trí phía lệnh, đơn vị **command pulse** → dùng `PA05` (và tỉ số `PA06/PA07`) như trên.
- **Data No. 90**: vị trí phía động cơ, đơn vị **motor-side pulse** (xung encoder) → **không dùng PA05**; muốn ra vòng phải chia cho độ phân giải encoder. MR-J4 22-bit = **4 194 304 xung/vòng** theo manual (không phải giá trị lưu trong file).
- Giá trị vị trí là số có dấu 32-bit; dấu thể hiện chiều quay. Kết quả là số vòng động cơ, có thể nhiều vòng.

## 4. Giới hạn — giá trị file lưu, không phải giá trị live

Toàn bộ số liệu trên là **giá trị đã lưu trong file**, không phải giá trị đọc trực tiếp từ drive. Tài liệu này **không thực hiện bất kỳ lần đọc live nào**: không mở cổng USB/RS-422, không kết nối MR Configurator2 tới amplifier, không đọc monitor data No. 90/91, không ghi hay nạp tham số. `PA03`, `PA05`, `PC20`, `PC21` trên drive thực tế có thể khác file. Timestamp của `Scara.mrc2` là metadata của file dự án, không chứng minh trạng thái hay thời điểm hoạt động của drive.

## 5. Cách tra cứu (audit)

- `Scara.prm2`: đọc nhị phân, tách dòng theo `\n`; số dòng 1-based; offset byte 0-based. Giá trị nằm ở trường thứ 4 của khối `/Root/PARAMETER/Parameters` (dòng CSV `Number,Symbol,Name,Value,Unit,Range`). Bảng mục 1 ghi dòng, byte đầu dòng và byte đầu giá trị.
- `Scara.mrc2`: parse header CFB tại byte 0, đọc directory entry và stream; `AxisData.bin` nằm trong CFB lồng `_hdb`; timestamp lấy từ `projectlist.xml`, `projectdatalist.xml`, `history.xml`. Không có nhãn tham số servo trong các stream.

_Tài liệu chỉ đọc; không thay đổi file nguồn._
