# ABS_Home — Phạm vi chạy thử 2 trục (J1/J2) — 2026-11-28

## 1. Quyết định phạm vi

| Hạng mục | Giá trị |
|---|---|
| Trục đưa vào chạy thử | J1, J2 |
| `AxisMax` | `2` (ABS_Home.st:12) |
| J3, J4 | Hoãn đến khi lắp drive |
| Tài liệu tham chiếu | `ABS_HOME_AXISMAX_REFACTOR_20261125.md` |

## 2. Trạng thái QD75 trục 3/4 và interlock

QD75 axis 3/4 có thể còn giữ lỗi latched `102` với `XA`/`XB = 1` do chưa có drive.
Với `AxisMax = 2`, chương trình **bỏ qua** `XA/XB/XE/XF` — đây là hành vi **có chủ đích** và được ghi nhận tại đây.

| Điều | Ghi chú |
|---|---|
| Bỏ qua `XA/XB` | Không xóa lỗi QD75 trục 3 |
| Dọn lỗi tùy chọn | `Cd.5` tại BFM `1702`; `Pr.22` bit2 = 1 cho trục không đấu dây |
| Thời điểm | Khi thuận tiện, không chặn nghiệm thu 2 trục |

## 3. Tiêu chí nghiệm thu (chỉ 2 trục)

| Tín hiệu | Giá trị mong đợi |
|---|---|
| `SyncState` | `50` |
| `AbsError` | `0` |
| `M120` | `1` |
| `M121` / `M122` | `0` / `0` |
| `PositionValid1` / `PositionValid2` | `TRUE` / `TRUE` |
| `X8` / `X9` | `0` / `0` |
| `Md.20` J1 | `Position1 × 30` |
| `Md.20` J2 | `Position2 × 36` |


## 4. Hiệu chuẩn gốc hiện tại — 2026-11-28

Người vận hành đã đặt vật lý J1/J2 đúng vị trí home. Lần đọc ABS mới tại vị trí này cho `Position1 = 0` pulse và `Position2 = -1` pulse, nhưng encoder dither vài pulse quanh datum. Vì công thức chuyển đổi là `diQ = HomeValue + (raw - ZeroPulse) × ScaleNum / ScaleDen`, cấu hình mặc định first-scan giữ `HomeValue = 0`, đặt J1 `ZeroPulse = 0` và J2 `ZeroPulse = -1`. Trong state 30, delta thô `raw - ZeroPulse` thuộc dải đóng `[-10, +10]` pulse được clamp về `0` trước khi nhân scale; do đó cả hai trục giữ `diQ = 0` tại home dù có jitter vài pulse.

| Trục | Raw ABS tại home | ZeroPulse DINT | Mã hóa low/high word trong `awCfg` | HomeValue |
|---|---:|---:|---|---:|
| J1 | `0` | `0` | `awCfg[10]=0`, `awCfg[11]=0` (giữ nguyên sau khi xóa mảng) | `0` |
| J2 | `-1` | `-1` | `awCfg[34]=-1`, `awCfg[35]=-1` (`HFFFF/HFFFF`, signed DINT `-1`) | `0` |

Deadband ±10 pulse chỉ áp dụng quanh ZeroPulse đã hiệu chuẩn. Chuyển động khoảng 1° tương ứng xấp xỉ 3333 pulse (J1) hoặc 2778 pulse (J2), vượt xa deadband nên phép scale bình thường không bị ảnh hưởng.

Thay đổi này chỉ dịch gốc logic theo encoder tuyệt đối tại datum cơ khí đã được người vận hành xác nhận; không sửa Pr.12/Pr.13, không ghi tham số QD75/MR-J4 và không ra lệnh chuyển động.

Backup trước hiệu chuẩn: `PLC/Mitsubishi/_backup_20261128_pre_current_origin/ABS_Home.st`.
## 5. Khi mở rộng sau này

Tăng `AxisMax` lên 3 hoặc 4 sẽ **tự động bật lại** interlock `XA/XB/XE/XF` cho các trục tương ứng (`AxisMax >= 3 AND XA`, `AxisMax >= 4 AND XB`, …). Không cần sửa logic.

## Sự cố 904 ngày 2026-11-28

### Diễn biến (bằng chứng live)

| Bước | Kết quả |
|---|---|
| State 10, 20 | Qua |
| State 30 | Dừng — `SyncState=90`, `AbsError=904`, `FaultAxis=1`, `AxisIndex=2` |
| Diễn giải | J1 báo lỗi; J2 chưa được đánh giá (dừng ở trục lỗi đầu tiên) |

### Giá trị QD75 và ABS đọc trực tiếp

| Đối tượng | Giá trị | Ý nghĩa |
|---|---|---|
| Pr.12 (giới hạn stroke trên) | G18/G19 = `0xFCC0`/`0x019B` = `27,000,000` | 270.00000 deg |
| Pr.13 (giới hạn stroke dưới) | G20/G21 = `0` | 0 deg |
| J2 Pr.12/Pr.13 | G168..G171 tương tự | 27,000,000 / 0 |
| ABS Position1 (J1) | `-3309` pulses → diQ `-99,270` units | -0.9927 deg |
| ABS Position2 (J2) | `13649` pulses → diQ `491,364` units | 4.91364 deg (trong 0..27,000,000) |

Đơn vị: `1e-5 deg` (`27,000,000` = `270.00000 deg`). Trong lần test này lỗi `XA/XB` (trục 3/4) = `0`.

### Nguyên nhân xác định

| Mục | Kết luận |
|---|---|
| Nhánh phát lỗi | `ABS_Home.st:189` — kiểm tra giới hạn dưới |
| Điều kiện | `diQcalc (-99,270) < Pr.13 (0)` → lỗi `904` |
| Loại trừ | Không phải tràn số, không phải giới hạn bằng 0 |
| Hành vi | Fail-closed **đúng**: từ chối preset `Md.20` ra ngoài soft limit đã cấu hình |
| Chương trình rút gọn | **Không phải nguyên nhân**; không mất hằng số hiệu chuẩn (bản gốc cũng có `ZeroPulse=Home=0`) |

### Bảng quyết định

| # | Phương án | Thay đổi | Rủi ro | Khuyến nghị |
|---|---|---|---|---|
| 1 | Jog J1 tới vị trí `>= 0 deg` (raw ABS `>= 0`), rồi `M131` chạy lại | Không đổi tham số | Thấp | Ưu tiên thử trước |
| 2 | Sửa Pr.13 (giới hạn dưới) J1/J2 theo dải cơ khí thực, ví dụ `-1,800,000` (-18 deg) hoặc đối xứng `-27,000,000` | Ghi + verify tham số QD75, cần phê duyệt rõ ràng | Trung bình (nếu sai dải) | Chỉ sau khi xác nhận dải cơ khí |
| 3 | Đặt ZeroPulse (`D3010/3011` J1, `D3034/3035` J2) theo pulse đã home thật | Ghi bộ nhớ | Cao — che giới hạn, che lỗi gốc | Không khuyến nghị khi chưa có datum cơ khí |
