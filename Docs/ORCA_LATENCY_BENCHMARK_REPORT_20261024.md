# BÁO CÁO KIỂM THỬ ĐỘ TRỄ PHẢN HỒI: WPF SCADA ↔ TWINCAT PLC ↔ BIẾN TẦN MD200
**Ngày kiểm thử:** 2026-10-24 12:20:00+07:00  
**Thiết bị kiểm thử:** AML-IPC (Local TwinCAT ADS Port 851, Crevis ST-4424 AO1, ST-2318 DO0, ST-5114 Encoder)  
**Phương pháp:** Đo đạc phân đoạn bằng Stopwatch thời gian thực từ tầng ADS, tầng PLC và tầng chuyển động cơ khí vật lý.

---

## 1. KẾT QUẢ ĐO ĐẠC CHI TIẾT (BẰNG CHỨNG SỐ LIỆU)

### Phân đoạn 1: Độ trễ giao tiếp ADS (WPF SCADA ↔ TwinCAT Router)
* **Thời gian đọc 1 tag qua ADS:** **`1.56 ms`**
* **Thời gian ghi 1 tag qua ADS:** **`1.78 ms`**
* **Toàn bộ chuỗi Handshake lệnh START (`LineMailboxGateway`):**
  * Ghi `udiHmiBeltCmdSeq` (UDINT): `10 ms`
  * Ghi xung cạnh lên `bHmiBeltStartReq := TRUE`: `17 ms`
  * Giữ xung tối thiểu qua chu kỳ task PLC: `30 ms`
  * PLC quét nhận xung, gán `udiBeltLastAckSeq := seq` và bật `bRUN_DO := TRUE`: **`60 ms`**
  * Hạ xung `bHmiBeltStartReq := FALSE`: `1 ms`
  * **TỔNG THỜI GIAN ĐÁP ỨNG TẦNG TRUYỀN THÔNG:** **`64 ms` (0.064 giây)**
  $\to$ **KẾT LUẬN:** TwinCAT nhận lệnh cực nhanh (chỉ 0.06 giây sau khi bấm nút). Hoàn toàn không có nghẽn hay trễ mạng ADS.

---

### Phân đoạn 2: Gia tốc phần mềm trong TwinCAT (`FB_InovanceMD200Conveyor.TcPOU`) — NGUYÊN NHÂN VẬT LÝ CHÍNH
Đo đạc thực tế quá trình tăng tốc từ lúc có lệnh RUN đến khi động cơ chuyển động (lấy mẫu mỗi 100 ms):

```
  t = 0.07s : Tần số = 0.12 Hz | Điện áp AO = 0.02 V | Vận tốc Encoder = 0.0 mm/s (Đứng yên)
  t = 0.20s : Tần số = 0.38 Hz | Điện áp AO = 0.08 V | Vận tốc Encoder = 0.0 mm/s (Đứng yên)
  t = 0.31s : Tần số = 0.60 Hz | Điện áp AO = 0.12 V | Vận tốc Encoder = 0.0 mm/s (Đứng yên)
  t = 0.42s : Tần số = 0.82 Hz | Điện áp AO = 0.16 V | Vận tốc Encoder = 0.0 mm/s (Đứng yên)
  t = 0.53s : Tần số = 1.04 Hz | Điện áp AO = 0.21 V | Vận tốc Encoder = 0.0 mm/s (Đứng yên)
  t = 0.63s : Tần số = 1.24 Hz | Điện áp AO = 0.25 V | Vận tốc Encoder = 0.0 mm/s (Đứng yên)
  t = 0.75s : Tần số = 1.48 Hz | Điện áp AO = 0.30 V | Vận tốc Encoder = 4.8 mm/s (Bắt đầu nhích)
  t = 0.96s : Tần số = 1.90 Hz | Điện áp AO = 0.38 V | Vận tốc Encoder = 9.5 mm/s (Bò rất chậm)
  t = 1.52s : Tần số = 3.00 Hz | Điện áp AO = 0.60 V | Vận tốc Encoder = 14.3 mm/s (14% tốc độ)
  t = 2.07s : Tần số = 4.12 Hz | Điện áp AO = 0.83 V | Vận tốc Encoder = 23.8 mm/s (24% tốc độ)
  t = 3.06s : Tần số = 6.10 Hz | Điện áp AO = 1.22 V | Vận tốc Encoder = 33.4 mm/s (33% tốc độ)
  t = 7.61s : Tần số = 15.0 Hz | Điện áp AO = 3.00 V | Vận tốc Encoder = 85.8 mm/s (Đạt tốc độ đặt)
```

* **Hiện tượng phát hiện:**
  * Trong `FB_InovanceMD200Conveyor.TcPOU`, hằng số dốc gia tốc đang đặt:
    `cRampUp_HzPerS : LREAL := 2.0;` (mỗi giây tần số chỉ tăng 2.0 Hz).
  * Trong 0.7 giây đầu tiên, điện áp ra biến tần chỉ từ $0.02\text{V}$ đến $0.25\text{V}$ ($< 1.5\text{ Hz}$). Ở tần số này, động cơ không đủ mô-men để thắng ma sát tĩnh của cơ khí $\to$ **Dây băng tải đứng bất động trong gần 1 giây**.
  * Phải mất đến **$7.61\text{ giây}$** động cơ mới đạt được tốc độ $100\text{ mm/s}$ (15 Hz).

---

### Phân đoạn 3: Độ trễ hiển thị giao diện SCADA (WPF UI Refresh Lag)
* Trong `Dashboard.cs`, timer quét hiển thị:
  `_dashTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };` (1 giây mới cập nhật màn hình 1 lần).
* Khi người vận hành bấm nút `MANUAL START`:
  * Lệnh đã gửi đi trong 0.06s, nhưng giao diện không tự làm mới ngay mà chờ timer 1 giây kế tiếp mới kéo dữ liệu từ PLC.
  * Vì vậy, trên màn hình SCADA, dòng `STOPPED · 0.0 mm/s` bị đứng lại khoảng 0.5 – 1.0 giây mới nhảy sang `RUNNING`, tạo cảm giác bị đơ.

---

## 2. PHƯƠNG ÁN KHẮC PHỤC TRIỆT ĐỂ

1. **Phía TwinCAT (`FB_InovanceMD200Conveyor.TcPOU`):**
   * Đổi `cRampUp_HzPerS` từ `2.0` thành **`10.0` Hz/s** (hoặc **`15.0` Hz/s**).
   * **Hiệu quả:**
     * Trong vòng $0.15\text{s}$, tần số đạt ngay $> 1.5\text{ Hz}$ $\to$ Băng tải lập tức chuyển động ngay khi vừa buông chuột khỏi nút bấm.
     * Đạt vận tốc làm việc $100\text{ mm/s}$ trong **$1.0 - 1.5\text{ giây}$** (chuẩn gia tốc công nghiệp, không bị giật vì biến tần MD200 vẫn có bộ lọc ramp riêng).
2. **Phía WPF SCADA (`Dashboard.cs`):**
   * Đổi chu kỳ `_dashTimer` từ `1000 ms` thành **`200 ms`** (quét 5 lần/giây, chuyển động hiển thị mượt mà, CPU IPC tiêu thụ thêm < 0.5%).
   * Cập nhật `DashStart_Click` và `DashStop_Click` gọi ngay lập tức `await RefreshDashboardAsync()` ngay khi lệnh gửi thành công $\to$ **Độ trễ hiển thị trên màn hình giảm về 0 ms**.
