# ABS_Home — Lần chạy bench thật lần 2 (MR-J4 ABS read)

- Ngày: 2026-11-12
- Môi trường: GX Works2 (GD2.EXE, PID 14472), project `E:\Orca\DATN\PLC\Mitsubishi\DATN_LINE\Robot_SCARA`
- PLC đích: Q06UDEH @ `192.168.3.40`
- Nguồn chuẩn của POU: `E:/Orca/DATN/PLC/Mitsubishi/ABS_Home.st`
- Bằng chứng thô: `Docs/GX_EVIDENCE_20261112/verify_and_tree_raw_20261112.txt`,
  ảnh: `verify_dialog_20261112.png`, `nav_programsetting_20261112.png`, `gx_initial_20261112.png`

## 0. Kết luận nhanh

| Câu hỏi | Kết quả |
|---|---|
| GX Works2 có phải khởi động lại? | **KHÔNG.** GD2.EXE đã chạy sẵn (PID 14472) và đang mở đúng project; không kill/restart. |
| Verify với PLC | **CHƯA CHẠY ĐƯỢC phép so sánh.** Hộp thoại `Online Data Operation` (Verify) đã mở và đọc được danh sách chương trình phía PLC, nhưng không chọn được target (grid `VSFlexGrid8N` không lộ checkbox cho UIA; `Select All` / `Parameter+Program` không tác dụng) nên nút Execute báo "No target data has been selected". **Không có dòng kết quả Match / Dest. Only / Source only.** |
| ABS_Home có thực thi? | **KHÔNG.** Project: `Program Setting > Scan Program` chỉ có `MAIN1`, `MAIN`; `Standby`/`Fixed Scan`/`No Execution` rỗng; `ABS_Home` chỉ nằm dưới `POU > Program`. Runtime (mục 10): `Program List Monitor` của PLC chỉ liệt kê `CONVEYOR`, `MAIN` (đều `Scan`), **không có ABS_HOME**. |
| Chạy bench S1→S7 | **KHÔNG CHẠY.** Đã vào monitor read-only và xác định dứt điểm bằng `Program List Monitor` (mục 10), nhưng không chạy bench vì POU không được quét; không Modify Value, không ghi giá trị nào. |

## 1. Bằng chứng kết nối

| Hạng mục | Giá trị đọc được | Nguồn |
|---|---|---|
| Trạng thái CPU trên status bar | `Q06UDEH` | AX statusbar của GXW2FrameWnd |
| Host kết nối | `Host-192.168.3.40` | AX statusbar |
| Line | `Line: 1` | AX statusbar |
| Kênh kết nối trong hộp thoại Online | `Ethernet Board Communication PLC Module Connection` | AX textfield của `Online Data Operation` |
| Connection Test riêng trong phiên này | **CHƯA XÁC MINH** (không chạy lại; không đọc lại hộp thoại Connection Test) | — |
| Kết quả Connection Test trước đó (do người dùng cung cấp) | `Successfully connected with the Q06UDEHCPU.` | Không phải giá trị tôi đọc trong phiên này |

Việc hộp thoại Online đọc được danh sách chương trình/parameter từ PLC cho thấy đường truyền đang hoạt động, nhưng đó **không** phải một Connection Test được ghi nhận trong phiên này.

## 2. Verify — dữ liệu đọc được (nguyên văn)

Hộp thoại: `Online → Verify with PLC...` → `Online Data Operation`, radio **Verify** (AutomationId 208).
Bảng target phía PLC (cột `Module Name/Data Name | … | Last Change | Size`), đọc nguyên văn:

| Row | Module Name/Data Name | Last Change | Size |
|---|---|---|---|
| 1 | `Q06UDEHCPU` | — | — |
| 2 | `PLC Data` (Target Memory Setting: `Program Memory/Device Memory`) | — | — |
| 3 | `Program(Program File)` | — | — |
| 4 | `MAIN1` | `2026/11/12 15:38:54` | `6380 Bytes` |
| 5 | `MAIN` | `2026/11/12 17:13:12` | `2152 Bytes` |
| 6 | `TRACKING` | `2026/10/09 14:33:12` | `8580 Bytes` |
| 7 | `ABS_HOME` | `2026/11/12 17:13:12` | `6380 Bytes` |
| 8 | `CONVEYOR` | `2026/11/12 17:13:12` | `4132 Bytes` |
| 9 | `Parameter` | — | — |
| 10 | `PLC/Network/Remote Password/Switch Setting` | `2026/11/12 17:13:12` | `1656 Bytes` |
| 11 | `Device Memory` (Detail: `Detail`) | — | — |
| 12 | `Device Data` | — | — |

Bộ đếm target luôn là `Execution Target Data( No / Yes )`, `Writing Size = 0Bytes`.
Bấm `Execute` → popup `MELSOFT Application`: *"No target data has been selected. Please try again after selecting the target data."*

**Kết luận mục 2:** `ABS_HOME` **có** trong danh sách program file của PLC (6380 Bytes, Last Change 2026/11/12 17:13:12, cùng mốc với `MAIN`, `CONVEYOR`, `Parameter`). Tuy nhiên:
- So sánh Match / Dest. Only / Source only: **CHƯA XÁC MINH** — không thực hiện được.
- Nội dung `ABS_HOME` trong PLC có trùng với `ABS_Home.st` trong project hay không: **CHƯA XÁC MINH**.

## 3. Program Setting — bằng chứng cây (nguyên văn)

`Navigation → Parameter > Program Setting` (mở rộng bằng click + phím Right):

```
Program Setting
├─ Initial Program            (không có con)
├─ Scan Program
│   ├─ MAIN1
│   └─ MAIN
├─ Standby Program            (không có con — Right không mở rộng)
├─ Fixed Scan Program         (không có con)
└─ No Execution Type          (không có con)
```

`Navigation → POU > Program`:

```
POU
└─ Program
   ├─ ABS_Home  → Program, Local Label
   ├─ Conveyor  → Program, Local Label
   └─ POU_01    → Program, Local Label
```

Diễn giải:
- `ABS_Home` **không** nằm trong `Scan Program` → theo project đang mở, POU này **không được quét**.
- `Standby Program` **rỗng**, không thấy nhánh `MAIN1` chứa `ABS_Home` như người dùng mô tả.
- Vậy trong project hiện tại, `ABS_Home` không thuộc **bất kỳ** execution type nào (Scan/Standby/Fixed Scan/No Execution) — chỉ tồn tại như POU.
- Danh sách program file của PLC (`MAIN1, MAIN, TRACKING, ABS_HOME, CONVEYOR`) **không khớp** danh sách POU của project (`ABS_Home, Conveyor, POU_01`), tức project và PLC không đồng nhất. Vì `Read from PLC` bị cấm trong brief, **execution type phía PLC là CHƯA XÁC MINH**.

## 4. Các bước bench (S1→S7)

Không thực hiện bước nào. Không mở Watch, không vào monitor, không ghi `BenchEnable` / `ManualRead` / `ResetResult`.

| Bước | Nội dung | Giá trị quan sát |
|---|---|---|
| S1 | Trạng thái idle | **CHƯA XÁC MINH** (không vào monitor) |
| S2 | `BenchEnable=TRUE`, chờ 4 s, đọc `StartupReady` | **CHƯA XÁC MINH** |
| S3 | Pulse `ResetResult` | **CHƯA XÁC MINH** |
| S4 | Pulse `ManualRead`, poll `State` 100 ms × 3 s | **CHƯA XÁC MINH** |
| S5 | Lặp S4 lần 2 | **CHƯA XÁC MINH** |
| S7 | `BenchEnable=FALSE`, `ManualRead=FALSE`, `X89/X8A/X8B` off | **CHƯA XÁC MINH** |

| Nhóm giá trị | Giá trị |
|---|---|
| `Count`, `Raw[0..15]`, `ErrorCode`, `LastError`, `ReadSuccess`, `PositionValid`, `ServoAlarm`, `Position` | **CHƯA XÁC MINH** |
| Raw frame hex | **không có** (không có khung nào được đọc) |
| Position đã parse | **không có** |
| `U8\G610`, `U8\G611`, `U8\G324`, `U8\G325`, `U8\G310`, `U8\G449`, `U8\G447`, `U8\G448`, `U8\G328`, `X89`, `X8A`, `X8B` | **CHƯA XÁC MINH** |

## 5. Chẩn đoán

1. **Nguyên nhân chặn chính:** POU `ABS_Home` không được đăng ký vào `Scan Program` của project (chỉ `MAIN1`, `MAIN`). Một POU ST trong GX Works2 chỉ chạy khi được gán vào program setting/task; nằm ngoài mọi execution type thì khối `CASE State` không bao giờ được gọi, mọi giá trị `BenchEnable/ManualRead/State` chỉ là dữ liệu local tĩnh. Vì vậy bench sẽ không đo được gì nếu chạy.
2. **Không thể xác nhận phía PLC:** execution type thật nằm trong parameter PLC; đọc parameter từ PLC bị cấm, nên chỉ có thể nói theo project: chưa đăng ký. Khả năng project đã bị lưu trước khi đăng ký (`Standby → MAIN1`) chưa loại trừ được.
3. **Verify comparison không thực hiện được** vì ô chọn target là `VSFlexGrid8N` (COM grid) — UIA chỉ lộ phần detail bên phải, không lộ checkbox bên trái; `Select All`/`Parameter+Program` không đổi bộ đếm target. Đây là giới hạn công cụ, không phải lỗi PLC.
4. **Lưu ý về địa chỉ module:** file nguồn `ABS_Home.st` dùng **`U8\G...`** (không phải `U10\G224` như giả thuyết trong brief). Nếu CC-Link master thực tế bắt đầu ở XY `0100` thì cần rà lại mapping U8/U10 **trước khi** chạy bench, nhưng đây là việc thiết kế, chưa kiểm chứng trên bench.
5. **Sự cố UI cuối phiên:** sau lần đọc cây cuối cùng, cửa sổ GX (`985126`, class `GXW2FrameWnd`) không còn được desktop của công cụ liệt kê (chỉ còn Explorer/Orca/e-Manual), trong khi `GD2.exe` PID 14472 vẫn `Responding=True` và còn HWND `GXW2FrameWnd`. Nhiều khả năng phiên RDP đổi (`RDP-Tcp#56` → `RDP-Tcp#58`), đưa GUI sang desktop mà phiên này không điều khiển được. Không có thao tác nào được gửi sau đó.

## 6. Cơ chế UI đã dùng (chính xác)

- `computer.window({app:"GX Works2"})` → `GXW2FrameWnd` `985126`; đọc bằng `win.ax({maxDepth:n})` (mỗi bước một lần).
- Mở menu Online bằng click trái vào bounds của menuitem `Online` (AX bounds `284,51,51,21`); menu hiện ra thành cửa sổ `Online`; chọn `Verify with PLC...` bằng click vào menuitem (một lần UIA Invoke báo `SetWindowsHookEx() succeeded but hooked target is not responding`, hộp thoại vẫn mở).
- Liệt kê control con của hộp thoại bằng PowerShell `user32!EnumChildWindows` (một helper, tái sử dụng) để lấy HWND/class: `VSFlexGrid8N`, các `Button` `&Parameter+Program`, `Select &All`, `Ca&ncel All Selections`, `&Execute`, `Close`; thử `BM_CLICK` cho `Select All` (không đổi target).
- Mở rộng cây: click vào text node + phím `Right`; thu gọn/mở `Program Setting` bằng click + `Right`.
- Không dùng vòng lặp MSAA/UIA; không đọc pixel để suy đoán giá trị; không nhập bàn phím vào ô soạn thảo của project.

## 7. Trạng thái cuối & tuân thủ giới hạn

- Không Write to PLC, không Download, không Read from PLC, không Delete/Format, không ghi parameter/program, không Force I/O, không RUN/STOP, không servo/motion, không thay đổi an toàn.
- Không Modify Value; `BenchEnable`/`ManualRead`/`ResetResult` **không bị ghi** — nên yêu cầu "kết thúc với BenchEnable=FALSE" đương nhiên thỏa (chưa từng bật). Monitor chưa từng bật.
- Project không được lưu; không có prompt lưu nào được xử lý; không có thay đổi nội dung project (chỉ mở/đóng cây và hộp thoại).
- Không tạo file nào ở thư mục gốc `E:/Orca/DATN`; mọi file bằng chứng nằm trong `Docs/GX_EVIDENCE_20261112/`.

## 8. Việc cần làm để chạy bench thật (khi được phép)

1. Đăng ký `ABS_Home` vào **Scan Program** (hoặc task được quét) trong `Parameter > Program Setting`, hoặc xác nhận đúng execution type phía PLC bằng chứng cứ hợp lệ.
2. Compile + download parameter/program (cần phê duyệt riêng; hiện **không** được phép).
3. Chạy lại Verify với target được chọn để lấy dòng Match/Dest. Only/Source only.
4. Chỉ khi đó mới thực hiện S1→S7 và đọc `Count`/`Raw[]`/`ErrorCode` như brief.

## 9. CHƯA XÁC MINH (tổng hợp)

- Kết quả Connection Test trong phiên này.
- Dòng kết quả Verify (Match/Dest. Only/Source only) và việc `ABS_HOME` trong PLC có trùng `ABS_Home.st`.
- Execution type của `ABS_HOME` phía PLC: đã xác định ở mục 10 bằng runtime (`Program List Monitor` chỉ có CONVEYOR, MAIN) — ABS_HOME không nằm trong danh sách thực thi.
- Toàn bộ giá trị runtime S1→S7, raw frame, position, chẩn đoán error code.
- Việc `U8\G...` có đúng module CC-Link master hay không.

---

## 10. Lần 2 — monitor-only (bằng chứng hành vi, 2026-11-12)

Mục đích: xác định **ABS_Home có thực thi hay không** bằng quan sát runtime, không đọc parameter từ PLC.
Toàn bộ mục này **chỉ đọc**; không có Modify Value, không ghi bất kỳ giá trị nào.

### 10.1. Khôi phục automation

- `computer.windows()` không thấy `GXW2FrameWnd` → kiểm tra bằng Win32: `IsIconic(GX 985126) = True`
  (rect `-32000,-32000`), `DwmGetWindowAttribute(DWMWA_CLOAKED) = 0`, desktop của GX = `Default`,
  input desktop = `Default`, cùng session 1.
- Nguyên nhân: cửa sổ GX bị **thu nhỏ (minimized)**, không phải crash và không phải đổi desktop.
  Khôi phục bằng `ShowWindow(hwnd, SW_RESTORE)` → `IsIconic = False`; sau đó `computer.windows()`
  liệt kê lại được `985126 "… [ABS_Home [PRG] Program [ST] ]"`.

### 10.2. Vào monitor (read-only)

| Thao tác | Kết quả |
|---|---|
| `Window` menu → item `2 ABS_Home [PRG] Program [ST]` | Document ABS_Home được kích hoạt (title frame đổi sang `[ABS_Home [PRG] Program [ST] ]`) |
| `Online → Monitor → Start Monitoring (All Windows)` | Popup `MELSOFT Series GX Works2`: *"The monitor might not operate normally because the uncompiled program exists. …"* → bấm **Yes** |
| Kết quả | Thanh `Monitor Status` xuất hiện với `0.900ms`; document POU_01 đổi thành `[PRG]Monitor Executing POU_01 (Read Only) 1 Step`; tab Watch đổi thành `Watch 1(Monitor Executing)` |

Popup thứ hai (`MELSOFT Series GX Works2`) xuất hiện rồi tự đóng trước khi đọc được nội dung → **nội dung CHƯA XÁC MINH**.
Không có hộp thoại nào yêu cầu ghi xuống PLC.

### 10.3. Verdict: ABS_Home KHÔNG thực thi

`Online → Monitor → Program List...` mở `Program List Monitor` (window 4130926);
ô `Execution Status of Programs` (control `SPR32AU70_SpreadSheet`, HWND 1379428 — không lộ nội dung qua UIA)
được đọc bằng ảnh chụp + OCR (Windows.Media.Ocr, ảnh phóng 8×). Bảng dưới là **giá trị OCR đọc được**;
cột được gán theo vị trí x của từ trong ảnh (`x≈135` = Execution, `x≈241` = Scan Time, `x≈327` = Execution Count):

| Row | Program | Execution | Scan Time (ms) | Execution Count |
|---|---|---|---|---|
| 1 | `ABS-HOME` (OCR lần 1; lần 2 chỉ đọc được số) | `Scan` | `0.000` | `20374` |
| 2 | `CONVEYOR` | `Scan` | `0.000` | `20374` |
| 3 | `MAIN` | `Scan` | `0.000` | `20374` |

Đọc lại lần hai (sau khi Stop Monitoring, cùng ảnh vùng grid): chỉ nhận được `CONVEYOR`, `MAIN`, cả hai `Scan`.

Diễn giải: danh sách chương trình **đang thực thi** trên PLC chỉ có `CONVEYOR` và `MAIN`.
**`ABS_HOME` không có trong danh sách thực thi** → POU này nằm trong bộ nhớ PLC nhưng **không được quét**.
Khớp với cây `Program Setting` của project (chỉ `Scan Program = MAIN1, MAIN`; `Standby`/`Fixed Scan`/`No Execution` rỗng).

Kiểm tra `ErrorCode` (giả thuyết "ErrorCode=1 mỗi scan") **không thực hiện được**: Watch grid (cũng là
`SPR32AU70_SpreadSheet`) không lộ giá trị qua UIA; gõ `ABS_Home.ErrorCode` vào ô entry của Watch không tạo được
dòng watch đọc được (grid vẫn chỉ có header `No. | Result | Data Name | Class`, `Row 0`).
Vì Program List đã kết luận dứt điểm nên không cần suy ra từ local label.
→ `ErrorCode`, `State`, `StartupReady` runtime: **CHƯA XÁC MINH**.

### 10.4. Bench

**Không chạy.** S1→S7 không thực hiện; `BenchEnable`, `ManualRead`, `ResetResult` chưa từng bị ghi;
`Count`, `Raw[]`, `LastError`, `ReadSuccess`, `PositionValid`, `ServoAlarm`, `Position`, `Attempt`,
`U8\G610/611/324/325/310/449/447/448/328`, `X89/X8A/X8B`: **CHƯA XÁC MINH** (không vào Watch, không Modify).

### 10.5. Trạng thái cuối

- Monitoring **đã dừng** (`Online → Monitor → Stop Monitoring (All Windows)`; document chuyển
  `[PRG]Monitor Stopping POU_01 (Read Only) 1 Step`, thanh `Monitor Status` biến mất).
- Không ghi xuống PLC, không download, không read-from-PLC, không Force, không RUN/STOP, không servo/motion.
- Không lưu project; không có prompt lưu nào hiện ra.
- File bằng chứng mới: `proglist_monitor_20261112.png` (ảnh cửa sổ Program List Monitor),
  `proglist_execstatus_ocr_20261112.png` (vùng `Execution Status of Programs` phóng 8×).

### 10.6. Kết luận cho lần 2

| Câu hỏi | Kết quả |
|---|---|
| GX có phải restart? | Không. Cửa sổ chỉ bị minimize; đã restore bằng `SW_RESTORE`. |
| ABS_Home có thực thi? | **KHÔNG** — `Program List Monitor` (runtime, phía PLC) chỉ có `CONVEYOR`, `MAIN`. |
| Có chạy được bench? | **KHÔNG**, đúng điều kiện chặn: phải đăng ký ABS_Home vào Scan Program + compile + download (chưa được phép). |
| Giá trị runtime | **CHƯA XÁC MINH** toàn bộ (không có dòng watch đọc được). |
