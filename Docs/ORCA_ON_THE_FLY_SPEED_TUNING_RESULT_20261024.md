# KẾT QUẢ TỐI ƯU HỆ THỐNG: GỠ BỎ CHẾ ĐỘ MÔ PHỎNG & ĐIỀU CHỈNH TỐC ĐỘ ANALOG TỨC THỜI (ON-THE-FLY)
**Ngày thực hiện:** 2026-10-24 16:35:00+07:00  
**Workspace:** `E:\Orca\DATN`  
**Thiết bị kiểm chứng:** AML-IPC (TwinCAT Port 851, Crevis ST-4424 AO1, ST-2318 DO0, ST-5114 Encoder)

---

## 1. GỠ BỎ HOÀN TOÀN CHẾ ĐỘ MÔ PHỎNG (CHẾ ĐỘ B - TEST ONLY)
* **Giao diện sạch và gọn:** Đã gỡ bỏ hoàn toàn ô chọn `[ ] TEST ONLY` (`OvMockToggle`) khỏi màn hình `LINE OVERVIEW`.
* **Loại bỏ mã nguồn mô phỏng:** Xóa bỏ toàn bộ cờ `_lineMockMode`, các nhánh logic giả lập và các chuỗi văn bản `" · TEST ONLY"`, giúp chương trình gọn gàng, giảm dung lượng bộ nhớ và tránh gây rối mắt cho người vận hành.
* **Giữ nguyên 2 chế độ chuẩn duy nhất:**
  * **Chế độ A:** Băng tải Thủ công (`MANUAL CONVEYOR`) — Đang hoạt động hoàn hảo.
  * **Chế độ C:** Tự động Toàn chuyền (`FULL-AUTO`) — Sẵn sàng cho Robot Gantry bôi keo khi nghiệm thu cơ khí.

---

## 2. XUẤT ÁP ANALOG ĐỔI TỐC ĐỘ TỨC THÌ KHI BẤM "APPLY" (ON-THE-FLY RETUNING)
* **Nguyên nhân kỹ thuật trước đây:**
  * Trong `MAIN.TcPOU`, logic cũ có điều kiện chặn:
    `ELSIF bMD200_Start THEN nBeltCommandResult := 12; // not allowed to retune while running`
    Đồng thời, khi phát hiện tốc độ đặt thay đổi lúc động cơ đang quay, PLC tự động ngắt `bMD200_Start := FALSE` (cưỡng chế dừng băng tải để bắt buộc dừng hẳn rồi mới được đổi tần số).
* **Đã chỉnh sửa trong TwinCAT PLC (`MAIN.TcPOU`):**
  * Cho phép chấp nhận lệnh `SET` ngay cả khi `bMD200_Start = TRUE`.
  * Cập nhật tức thời `rMD200_SetSpeed_mm_s` $\to$ đổi thẳng sang `rMD200_SetFrequency_Hz` mà **không ngắt động cơ**.
  * Module ngõ ra tương tự **Crevis ST-4424 AO1** lập tức thay đổi điện áp theo tốc độ mới.
  * Đã nạp thành công xuống Port 851 bằng **Online Change**.
* **Đã chỉnh sửa trong WPF SCADA (`Dashboard.cs`):**
  * Khi người vận hành nhập số mm/s mới và bấm `APPLY` (hoặc Enter): Lệnh `SET` mới được gửi xuống PLC qua ADS ngay tức thì.
  * Dữ liệu telemetry hiển thị điện áp và tốc độ mới được làm mới ngay (0 ms trễ hiển thị).

---

## 3. BẰNG CHỨNG THỰC NGHIỆM TRỰC TIẾP TRÊN PHẦN CỨNG
Đã thực hiện kịch bản kiểm tra thực tế trên SCADA:
1. **Khởi động băng tải ở $100\text{ mm/s}$:**
   * Tần số: `15.0 Hz`
   * Điện áp ST-4424 AO1: `3.00 V` (`Raw = 1229`)
   * Băng tải đang quay kéo phôi bình thường.
2. **Băng tải VẪN ĐANG CHẠY, nhập số `60` mm/s và bấm `APPLY`:**
   * **Băng tải KHÔNG BỊ DỪNG.**
   * Điện áp ST-4424 AO1 lập tức chuyển về: **`1.80 V`** (`Raw = 737`)
   * Tần số biến tần Inovance MD200 hạ ngay về: **`9.0 Hz`**
   * Vận tốc thực tế đo bởi Encoder ST-5114 giảm êm dịu từ $85.8\text{ mm/s}$ xuống: **`52.4 mm/s`** (Ảnh 1).
3. **Bấm `MANUAL STOP`:**
   * Hệ thống ngắt tiếp điểm RUN, điện áp về `0.00 V`, vận tốc về `0.0 mm/s` an toàn tuyệt đối (Ảnh 2).
