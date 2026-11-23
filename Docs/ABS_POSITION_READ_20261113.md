# Đọc vị trí ABS servo MR-J4-A — Biên bản 2026-11-13

Mục tiêu: đọc vị trí tuyệt đối hiện tại (xung encoder / xung lệnh) và các tham số ABS của servo MR-J4-A (trục SCARA, trạm 0) bằng đường ít xâm nhập nhất. **Chỉ đọc** — không ghi tham số, không test operation, không servo ON, không homing, không reset alarm.

Thời điểm đọc: **2026-11-13 18:05–18:07 (UTC+7)**, máy AML-IPC, phiên RDP #3 (Session 1).

## 1. Kết luận nhanh

| Hạng mục | Giá trị đọc được |
|---|---|
| Đường kết nối hoạt động | **USB (CN5)** — MR Configurator2 online, status bar: `[Station 00] MR-J4-A (-RJ) Standard Servo amplifier connection: USB` |
| **Vị trí tuyệt đối hiện tại (xung lệnh)** | **215 159 pulse** (không đổi trong suốt thời gian quan sát) |
| Vị trí tuyệt đối (xung cạnh motor) | **90 244 616 … 90 244 619 pulse** (dao động ±4 pulse) |
| CYC (Motor edge pulse unit) | 2 164 232 … 2 164 235 pulse |
| ABS (số vòng motor) | **21 rev** |
| PA03 (Absolute position detection system) | **0002h = ABS by communication** |
| PA01 (Operation mode) | 1000h |
| PA05 (Number of command input pulses per revolution) | 10000 |
| PC20 (Station number setting) | **0** |
| PC21 (RS-422 communication function selection) | **0010h = 19200 bps, delay 0** |
| PC22 (Function selection C-1) | 0000h |
| Phiên bản phần mềm amplifier (S/W No.) | **BCD-B46W100 D7** (MR-J4-20A, serial D133R011) |
| Phiên bản MR Configurator2 | 1.32.0.1 (MR2.exe, `C:\Program Files (x86)\MELSOFT\MRC2`) |

Nguồn số liệu: cửa sổ **`[Monitor] → [ABS Data Display/Home Position Setting]`**, trục `Axis1` (MR-J4-A (-RJ) Standard); cửa sổ **`[Monitor] → [Display All]`**; bảng `Parameter Setting` sau khi Read và Verify; cửa sổ `[Diagnosis] → [System Configuration]`.

> Đơn vị: ô **“Command pulse unit value”** là vị trí tuyệt đối theo **xung lệnh** (đã quy đổi qua CDV/CMX từ xung cạnh motor), gốc 0 tại home position. Ô **“Motor edge pulse unit value”** là **xung cạnh motor** (encoder edge count) — con số encoder thô, không phải xung lệnh.

## 2. Các đường kết nối khả dụng trên IPC

### 2.1 Thiết bị USB (`Get-PnpDevice -Class USB`)

| Trạng thái | FriendlyName | InstanceId |
|---|---|---|
| **OK** | **MITSUBISHI MELSERVO USB Controller** | `USB\VID_06D3&PID_01D0\6&2EC5E233&0&6` |
| Unknown (không hiện diện) | MITSUBISHI MELSERVO USB Controller | `USB\VID_06D3&PID_01D0\6&2EC5E233&0&5` |
| Unknown (không hiện diện) | MITSUBISHI MELSERVO USB Controller | `USB\VID_06D3&PID_01D0\6&C16AD95&0&3` |
| Unknown | MITSUBISHI Easysocket Driver | `USB\VID_06D3&PID_1800\...` (×3) |
| Unknown | MITSUBISHI FREQROL E700 USB Controller | `USB\VID_06D3&PID_01E2\6&C16AD95&0&3` |
| Unknown | USB Serial Converter (FTDI) | `USB\VID_0403&PID_6001\A5069RR4` |

Chi tiết thiết bị đang OK: Manufacturer `MITSUBISHI ELECTRIC CORPORATION`, Service `EZSocketSv`, driver `oem49.inf` v16.2.0.0 (2023-11-28), `Present=True`, `Problem=CM_PROB_NONE`, vị trí `Port_#0006.Hub_#0006` (Intel USB2 EHCI), ngày cài/arrival **2026-11-13 16:39:07**.

→ Có thiết bị USB servo Mitsubishi hiện diện, driver nạp thành công. Đây là đường đã dùng.

### 2.2 Cổng COM hiện có

| COM | Thiết bị | PNPDeviceID |
|---|---|---|
| COM1 | Communications Port (COM1) | `ACPI\PNP0501\1` |
| COM2 | Communications Port (COM2) | `ACPI\PNP0501\2` |
| COM3 | Communications Port (COM3) | `ACPI\PNP0501\11` |
| COM4 | Communications Port (COM4) | `ACPI\PNP0501\12` |
| COM5 | Communications Port (COM5) | `ACPI\PNP0501\13` |
| COM6 | Communications Port (COM6) | `ACPI\PNP0501\14` |
| COM7 | Intel(R) Active Management Technology - SOL | `PCI\VEN_8086&DEV_1E3D&...` |

→ COM1–COM6 là cổng serial onboard/ACPI, COM7 là AMT SOL. **Không có COM nào do USB-Serial adapter tạo** (FTDI `VID_0403&PID_6001` đang Unknown, không được cấp COM). Vì vậy đường RS-422/232 trực tiếp qua USB-Serial **không khả dụng**; các cổng onboard cũng không nối tới CN3 (CN3 đang đi về QJ71C24N CH2 của PLC).

## 3. Đường đã dùng và thao tác thực hiện

1. MR Configurator2 (PID 10580) mở sẵn project `E:\Orca\DATN\PLC\Mitsubishi\Servo\Scara.mrc2` ở chế độ offline.
2. Bật online bằng nút **“Switch online/offline”** → hộp thoại `It is online.` → **kết nối USB thành công ngay**, không lỗi truyền thông, không cảnh báo CN3/CN5.
3. Xác nhận online: status bar `[Station 00] MR-J4-A (-RJ) Standard Servo amplifier connection: USB`.
4. Bấm **Read** → `Execute reading. Continue?` → **Yes**. Đọc thành công (bảng tham số hiển thị đầy đủ giá trị).
5. Kiểm chứng bằng **Verify → Amplifier → OK**: **“The parameters match between the comparison source and target.”**
6. Mở `[Monitor] → [ABS Data Display/Home Position Setting]`, `[Monitor] → [Display All]`, `[Diagnosis] → [System Configuration]`.

Không thao tác ghi nào được thực hiện: không bấm “Selected Items Write”, “Axis Writing”, “Set To Default”, “Parameter Copy”, không Test Mode/Jog/Homing/Alarm Reset.

## 4. Vị trí tuyệt đối đọc được

### 4.1 Cửa sổ `ABS Data Display/Home Position Setting` (Axis1)

Màn hình ghi: “Display the current position of home position used as 0.”

| Ô hiển thị | Công thức/nhãn trên màn hình | Giá trị | Đơn vị |
|---|---|---|---|
| Motor edge pulse unit value | `=ABS x Enc. counts No. per rot. + (CYC-CYC0)` | **90 244 616 → 90 244 619** | pulse (xung cạnh motor) |
| Command pulse unit value | `=(CDV/CMX) x Motor edge pulse unit value` | **215 159** (ổn định) | pulse (xung lệnh) |
| CYC (Motor edge pulse unit) | Encoder data / Amp. val | **2 164 232 → 2 164 235** | pulse |
| ABS | Motor rotations No. | **21** | rev |
| CYC0 (Motor edge pulse unit) | Encoder data at home position | **0** | pulse |
| ABS0 | Motor rotations No. at home position | **0** | rev |

Mẫu liên tiếp (≈0,8 s/mẫu): chỉ trường motor-edge/CYC “rung” 1–4 pulse, xung lệnh đứng yên.

| # | Motor edge pulse | Command pulse | CYC | ABS |
|---|---|---|---|---|
| 1 | 90 244 616 | 215 159 | 2 164 235 | 21 |
| 2 | 90 244 618 | 215 159 | 2 164 232 | 21 |
| 3 | 90 244 618 | 215 159 | 2 164 234 | 21 |
| 4 | 90 244 616 | 215 159 | 2 164 232 | 21 |
| 5 | 90 244 618 | 215 159 | 2 164 232 | 21 |
| 6 | 90 244 619 | 215 159 | 2 164 234 | 21 |
| 7 | 90 244 618 | 215 159 | 2 164 232 | 21 |
| 8 | 90 244 619 | 215 159 | 2 164 235 | 21 |

Biên độ ±4 pulse trên 90,2 triệu pulse (≈4,4×10⁻⁸) là nhiễu lượng tử/đọc, không phải chuyển động trục; xung lệnh 215 159 pulse hoàn toàn ổn định.

Ảnh chụp cùng thời điểm hiển thị: Motor edge **90 244 618**, Command **215 159**, CYC **2 164 234**, ABS **21 rev**.

### 4.2 Cửa sổ `Display All` (cùng thời điểm)

| No. | Item | Unit | Axis1 |
|---|---|---|---|
| 1 | Cumulative feedback pulses | pulse | 4 116 742 |
| 2 | Servo motor speed | r/min | 0 |
| 3 | Droop pulse | pulse | 0 |
| 4 | Cumulative cmd. pulses | pulse | 0 |
| 5 | Command pulse frequency | kpulse/s | 0 |
| 6 | Analog speed command voltage | V | −0,08 |
| 9 | Effective load ratio | % | 0 |
| 12 | Within one-revolution position | pulse | 2 164 232 |
| 13 | ABS counter | rev | 21 |
| 14 | Load inertia moment ratio | times | 10,80 |
| 15 | Bus voltage | V | 328 |
| 17 | Internal temperature of encoder | °C | 52 |

→ “Cumulative feedback pulses” là bộ đếm tích luỹ từ lần bật điện gần nhất (reset khi power cycle; 2026-11-13 là 4 116 742, 2026-11-14 là 62), **khác** với vị trí tuyệt đối đa vòng của ABS; không dùng thay cho ABS.

## 5. Tham số ABS đọc live từ amplifier

Nguồn: `Parameter Setting` sau khi Read + Verify (khớp 100% với amp).

| No. | Abbr. | Name | Giá trị (Axis1) |
|---|---|---|---|
| PA01 | *STY | Operation mode | 1000h |
| PA02 | *REG | Regenerative option | 0000h |
| PA03 | *ABS | Absolute position detection system | **0002h** |
| PA04 | *AOP1 | Function selection A-1 | 2000h |
| PA05 | *FBP | Number of command input pulses per revolution | 10000 |
| PA06 | CMX | Elec. gear numerator | 1 |
| PA07 | CDV | Elec. gear denominator | 1 |
| PA08 | ATU | Auto tuning mode | 0001h |
| PA09 | RSP | Auto tuning response | 16 |
| PA10 | INP | In-position range | 100 |
| PA13 | *PLSS | Command pulse input status | 0000h |
| PA14 | *POL | Rotation direction selection | 0 |
| PA15 | *ENR | Encoder output pulse | 10000 |
| PA16 | *ENR2 | Encoder output pulse 2 | 1 |
| PA21 | *AOP3 | Function selection A-3 | 1001h |
| PA22 | *PCS | Position control structure selection | 0020h |
| PA24 | AOP4 | Function selection A-4 | 0002h |
| PC20 | *SNO | Station number setting | **0** |
| PC21 | *SOP | RS-422 communication function selection | **0010h** |
| PC22 | *COP1 | Function selection C-1 | **0000h** |
| PC23 | *COP2 | Function selection C-2 | 0000h |
| PC24 | *COP3 | Function selection C-3 | 0000h |
| PC25 | *COP4 | Function selection C-4 | 0000h |

PC20/PC21 khớp `Scara.prm2` (0 / 0010); PA03 khớp `Scara.prm2` (0002).

## 6. Hiển thị liên tục qua MR Configurator2 (2026-11-14)
> KHÔNG DÙNG ĐƯỢC: theo người vận hành, tại 2026-11-14 đang cắm **drive khác**, nên các mẫu trong §6.1 có thể thuộc amplifier khác, không phải trục SCARA. Giữ lại chỉ để truy vết.

Lần đọc đã xác minh duy nhất còn lại là mẫu **2026-11-13** ở §4.1; ngay cả mẫu đó **không tái xác minh được** khi đang cắm drive khác.

Kiểm tra lúc **2026-11-14 21:13 (UTC+7)**. MR2 vẫn **online qua USB**, không phải reconnect; status bar giữ nguyên:

`[Station 00] MR-J4-A (-RJ) Standard Servo amplifier connection: USB`

Cửa sổ được đưa lên trước và giữ mở: `[Monitor] → [Display All]`, tab **Display All** (project `Scara.mrc2`), trục **Axis1**. Cửa sổ foreground được xác nhận là `MELSOFT MR Configurator2 ... - [Display All]`; nút **Pause** đang bật, **Restart** bị disable → monitor đang chạy.

### 6.1 Ba mẫu cách ~5 s

| Thời điểm (HH:mm:ss) | Cumulative feedback pulses (pulse) | Within one-revolution position (pulse) | ABS counter (rev) | Servo motor speed (r/min) |
|---|---|---|---|---|
| 21:13:14 | 62 | 2 796 360 | 26 | 0 |
| 21:13:19 | 62 | 2 796 360 | 26 | 0 |
| 21:13:25 | 62 | 2 796 359 | 26 | 0 |

Các mẫu **có thay đổi** dù không thao tác: `Within one-revolution position` 2 796 360 → 2 796 360 → 2 796 359 và `Droop pulse` 0 → 1 → 0, chứng tỏ bảng tự cập nhật. `Cumulative feedback pulses` (62), `ABS counter` (26 rev) và `Servo motor speed` (0) không đổi vì **trục đứng yên**; 62 là bộ đếm từ lần bật điện gần nhất, không phải vị trí tuyệt đối.

### 6.2 Chu kỳ làm mới (refresh interval)

**`chưa đo được vì trục đứng yên`** — không được quay trục (hard limit), nên không có trường thay đổi đều để bấm giờ. Ghi nhận từ UI: giá trị đã khác nhau giữa hai lần chụp cách 5 s; monitor đang chạy (Pause bật / Restart disable); giao diện **không có chỉ báo “monitor cycle” dạng số** để đọc thay thế.

### 6.3 Bản chất hiển thị

Đây là **HMI phía drive**: MR Configurator2 đọc trực tiếp amplifier MR-J4-A qua USB (CN5) và tự vẽ lại giá trị; **không phải tag PLC**, không đi qua QJ71C24N hay scan task. Nếu rút USB (hoặc mất kết nối), bảng sẽ **dừng cập nhật** và không còn số liệu live — phải cắm lại và kết nối online để xem tiếp.

Ảnh: `Docs/GX_EVIDENCE_20261112/MR2_display_all_20261114.png` (toàn cửa sổ, status bar USB) và `MR2_display_all_sample1_20261114.png` (mẫu 21:13:14).

## 7. Phiên bản phần mềm và nhận dạng

Nguồn: `[Diagnosis] → [System Configuration]` (online).

| Hạng mục | Giá trị |
|---|---|
| Servo amplifier identification | **MR-J4-20A** |
| Servo amplifier serial number | **D133R011** |
| Servo amplifier S/W No. | **BCD-B46W100 D7** |
| Motor model | HG-KR23 |
| Motor ID | 0111FF23000D |
| Motor serial number | J259W2019 |
| Encoder resolution | 4194304 (22-bit) |
| Accumulated power-on time | 7399 h |
| Option unit / converter | No Connection (không gắn) |
| MR Configurator2 | 1.32.0.1 (ProductVersion/FileVersion của `MR2.exe`) |

## 8. Trạng thái để lại

- MR Configurator2: **đang ONLINE, chế độ chỉ giám sát** (Display All / ABS Data Display), không mở cửa sổ test, không servo ON, không jog, không ghi tham số.
- Amplifier: không thay đổi tham số (Verify báo khớp), không reset alarm, không đổi chế độ.
- Không download/ghi PLC; đường PLC (`ABS_HOME` không nằm trong scan task) không được dùng.

## 9. CHƯA XÁC MINH

1. **Đối chiếu “SW A3+” với firmware BCD-B46W100 D7** — đã đọc S/W No. D7 nhưng chưa tra bảng version chính thức để khẳng định tiêu chí A3+.
2. **Quy đổi 215 159 pulse sang mm/deg** — chưa có bảng điện trở/gear của trục và gốc toạ độ máy; PA05 = 10000 pulse/vòng, CMX/CDV = 1/1.
3. **CN3 (RS-422) có thực sự bị PLC chiếm hay không** — chỉ suy từ sơ đồ đấu dây trong đề bài; không kiểm tra vật lý. USB vẫn hoạt động, cho thấy CN5 đang dùng được.
4. **Độ trôi vị trí ABS theo thời gian dài** — chỉ quan sát ~6 giây (dao động ±4 pulse).
5. **Nguồn gốc home position** — ABS0 = 0, CYC0 = 0, nhưng chưa đối chiếu với lần set home thực tế trên máy.
6. **Giá trị tích luỹ tin cậy dài hạn** — “Accumulated power-on time” 7399 h đọc từ amp; chưa đối chiếu nhật ký bảo trì.
7. **Danh tính drive kết nối CN5 lúc 2026-11-14 21:13 — chưa xác minh; §6.1 không dùng được.**

## 10. Ảnh bằng chứng

| File | Nội dung |
|---|---|
| `Docs/GX_EVIDENCE_20261112/MR2_ABS_position_20261113.png` | ABS Data Display/Home Position Setting (215 159 pulse; motor 90 244 618; CYC 2 164 234; ABS 21 rev) |
| `Docs/GX_EVIDENCE_20261112/MR2_display_all_20261113.png` | Display All (cumulative feedback 4 116 742; ABS counter 21 rev) |
| `Docs/GX_EVIDENCE_20261112/MR2_PA_basic_20261113.png` | Bảng PA01–PA32 (PA03 = 0002h, PA05 = 10000) |
| `Docs/GX_EVIDENCE_20261112/MR2_PC_extension_20261113.png` | Bảng PC01–PC43 (PC20 = 0, PC21 = 0010h) |
| `Docs/GX_EVIDENCE_20261112/MR2_system_config_20261113.png` | System Configuration (MR-J4-20A, S/W BCD-B46W100 D7, encoder 4194304) |
| `Docs/GX_EVIDENCE_20261112/MR2_display_all_20261114.png` | Display All mở phía trước, online USB (2026-11-14 21:13) |
| `Docs/GX_EVIDENCE_20261112/MR2_display_all_sample1_20261114.png` | Mẫu 1 lúc 21:13:14: cumulative 62, within 2 796 360, ABS 26 rev, speed 0 |
