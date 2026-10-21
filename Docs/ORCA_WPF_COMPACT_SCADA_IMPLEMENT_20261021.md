# ORCA / WPF — TRIỂN KHAI HMI COMPACT SCADA (đã được người dùng duyệt, 2026-10-21)

## Người giao / ưu tiên
ChatGPT điều phối theo yêu cầu trực tiếp của người dùng: "ok gửi xuống orca làm cho tôi chi tiết vào".
Thực hiện THẬT trên ứng dụng C# WPF đang có, không làm HTML/WebView thay thế, không chỉ lập kế hoạch. Bản HTML preview ĐÃ CHUYỂN TRỰC TIẾP SANG AML: C:\Users\aml\orca\DATN\Docs\PREVIEW_DATN_SCADA_COMPACT_APPROVED_20261021.html (offline 8 tab). PHẢI đọc file này, lấy bố cục CSS/DOM, nội dung 8 trang và kích thước làm reference chính; bản đặc tả bên dưới bổ sung logic và tiêu chí an toàn. Không nhúng HTML/WebView thay WPF thật. Người dùng DUYỆT mẫu HMI đơn giản, công nghiệp, siêu gọn, tối đa diện tích hữu ích. Kết quả cần kiểm tra thật qua GUI/screenshot sau khi làm.

## 0. PHẢI KHÔI PHỤC BỐI CẢNH ĐÚNG
- Root DUY NHẤT: C:\Users\aml\orca\DATN.
- Solution C#: App\PlateVision50.sln hoặc App\Vision\PlateVision50.csproj (kiểm tra path thật); MainWindow.xaml, code-behind và các partial Dashboard/StationOverview/StationDashboards. Không sửa Desktop\DATN lịch sử.
- Đọc AGENTS.md, README.md, PROJECT_INDEX.md, Docs\TAG_MATRIX.md, Docs\PLC_README_ESTOP.md, Docs\GLOBAL_COMMAND_CONTRACT_PENDING_20261020.md (nếu tồn tại), checkpoint Orca WPF trước đó.
- Kiểm tra git status/diff và các file không commit trước khi sửa; CHỈ sửa đúng phần HMI cần thiết, giữ code người khác.
- Không tạo dự án WPF mới, không xóa, đổi tên hoặc ghi đè release đang chạy.
- Trước mỗi thay đổi tạo BACKUP phục hồi được (bản gốc đầy đủ file liên quan + hash/manifest), lưu Docs checkpoint, git diff sau cùng.

## 1. PHẠM VI ĐƯỢC PHÉP
Cài đặt layout UI bằng XAML + C# ở project hiện tại, wiring chỉ các hàm read-only/phần mềm camera đã có và mô phỏng điều khiển nếu chưa có hợp đồng PLC được xác minh. Build và test OFFLINE an toàn, publish bản RC thư mục riêng nếu thích hợp; không khởi chạy chế độ khiến ADS write hoặc máy thật chuyển động. Không thay đổi bất kỳ file TwinCAT/PLC/Mitsubishi, không Activate/Download/Run/Stop PLC, không điều khiển servo/robot/VFD/camera thật, không ADS writes trong thử nghiệm. Không tự bật chế độ ARMED hoặc auto-confirm.

## 2. THỊ GIÁC CHUNG / KÍCH THƯỚC (mẫu người dùng duyệt)
- Dùng phong cách Siemens-ish industrial, nền xám than trung tính; sắc độ tiết chế, không nút màu sắc rực, phẳng, ít border/layer/shadow, chỉ nhấn màu tối thiểu cho trạng thái.
- Mật độ cao: outer padding 4-6px, giữa panel 3-5px; nút phẳng nhỏ ~25-29px cao, text 11-12px dễ đọc; header ~40px, nav ngang ~28-32px, status footer ~20px (tham khảo, đánh giá thực tế).
- BỎ sidebar dọc ở LINE OVERVIEW và các tab nếu làm mất diện tích; chuyển thành top navigation ngang 8 mục với cùng quy tắc compact.
- Nội dung phải điền hầu hết cửa sổ (Grid star sizing WPF, không hard-code để lãng phí trống), không scroll vô ích hoặc cắt mất nút trên 1366x768; đẹp tại 1649x928. Giữ tỷ lệ nội dung khi resize/window maximize.
- Hai vùng video CAM01/ CAM02 NGANG NHAU, tổng diện tích video lớn nhất trang (50/50 theo chiều ngang, chia đường 1px). Video giãn chiều cao còn lại theo viewport, preserve fit aspect ratio, không méo, không scale sai overlay. Có nhãn nhỏ CAMERA 01 / GANTRY và CAMERA 02 / SCARA, status nguồn đúng.
- Dải dưới mỗi video cực gọn, 4 ô: Track ID; Tracking; Product X/Y (mm); Gantry ACK / SCARA status. Nhãn thuộc ĐÚNG camera/tracker của nó; chống nhảy nhãn giữa track IDs. KHÔNG invent measurement hoặc trạng thái CAMERA02 Connected.
- Thiết kế phải giống bản HTML được duyệt, không thêm card/box màu hay khoảng trống lớn do DockPanel / StackPanel không stretch.

## 3. LINE OVERVIEW / BĂNG TẢI VÀ ĐIỀU KHIỂN TỔNG
Không dành panel riêng lớn cho GLOBAL CONTROL. Dưới 2 camera có một dải SHARED CONVEYOR + MACHINE COMMANDS gồm:
A. READ-ONLY trạng thái: RUN STATUS, ACTUAL SPEED (mm/s), ENCODER POSITION (mm), MACHINE STATE; nếu ADS stale/offline hiển thị UNKNOWN/-- rõ ràng.
B. SYSTEM AUTO: 3 nút nhỏ gọn START, STOP, RESET; 1 nhóm, cạnh điều khiển băng tải. START = yêu cầu HMI bật camera/tracking đã sẵn sàng + đề nghị START tổng TwinCAT; TwinCAT master mới kiểm tra điều kiện và phân phối PLC Mitsubishi, SCARA tương lai. C# TUYỆT ĐỐI KHÔNG trực tiếp dispatch PLC-Q/SCARA trong hệ thống AUTO. STOP là controlled normal stop, không tự nhận là certified EStop. RESET là fault ack khi đủ interlock, không tự start.
C. BELT MANUAL: ô tốc độ tham chiếu [mm/s] có nhãn rõ, nút SET, nút START BELT / STOP BELT nhỏ, role quyền phù hợp; manual bị khóa khi AUTO đang hoạt động hoặc pending; AUTO bị khóa khi manual đang chạy/pending theo hợp đồng và xác minh trạng thái. Không để đồng thời manual+auto command.
D. Tốc độ UI bắt buộc mm/s (nhập, hiển thị). PLC hiện đặt Hz (MAIN.rMD200_SetFrequency_Hz 0..25 Hz); CHƯA ĐỔI TWINCAT. Không dùng chuyển đổi mm/s -> Hz đoán mò, không viết Hz từ ô mm/s khi chưa có mapping/validation. Nếu chưa xác thực calibration, UI setpoint chỉ là proposed/disabled, có giải thích/ trạng thái chưa commissioned. Hiển thị ACTUAL mm/s từ MAIN.rConveyorSpeed nếu quality hợp lệ.
E. Bộ global START/STOP/RESET PLC contract hiện chưa commissioned, CHƯA có verified start/reset ADS symbol. Tạo command/state interface phân lớp chuẩn để tích hợp sau, nút có disabled/uncommissioned tooltips, không gán bừa tag. Có thể chỉ mock UI ở preview/Test mode; không cho nhầm demo là máy đã chạy. Handler DashGlobalCommand_Click từ phiên trước chỉ MessageBox, cần đưa lại nội dung hiển thị/hành vi an toàn, tránh giả chạy.
F. ĐẶC BIỆT AUDIT trước đã phát hiện Dashboard.cs có logic STOP bMD200_Start=false (có thể ADS write khi disarmed). Không test nhấn STOP/START trên runtime đang nối máy. Nếu sửa đường lệnh thật cần audit trạng thái, operator unlock, interlock, confirm; tránh side-effect dù nút bị coi disarmed. Hoàn thành giao diện ở offline mock path và ghi backlog phần wiring chưa commissioned.

## 4. TOÀN BỘ 8 TAB / KẾ THỪA CHỨC NĂNG
1) LINE OVERVIEW — 2 camera lớn và dải SHARED CONVEYOR/CONTROLS như trên.
2) STATION 01 — video Camera 1 chiếm đa số; bên phải robot telemetry PLC-Q, Actual TCP (nếu nguồn VERIFIED), product tracking, FIFO; dưới/bên cạnh tabs SENSORS & I/O / PARAMETERS / LOCAL EVENTS, filter nhanh; giữ nguyên logic detector 4 điểm, queue, track ID, existing overlays.
3) STATION 02 — layout đối xứng Station 01 nhưng dành cho SCARA; Camera 2 chưa commissioned, telemetry UNAVAILABLE/UNMAPPED, mọi robot actuation bị khóa; không fake.
4) SHARED CONVEYOR — mật độ gọn, các read-only live fields + control manual nếu có quyền; tốc độ mm/s, encoder mm, ramp Hz nếu cần trong advanced details (đừng nhầm Hz là mm/s). Tách manual/auto.
5) SETTINGS — tabs nội bộ SYSTEM / STATION 01 / STATION 02 / CONVEYOR / I/O MONITOR / PERMISSIONS; bảng/field gọn, hiển thị trạng thái read-only / disabled đúng nguồn; không gỡ settings cũ.
6) ALARMS & LOGS — ALARMS/EVENTS / TRACKING HISTORY / PLANT SIGNALS; duy trì log thực, không mock thành alarm thật, không xóa handler.
7) VISION WORKSPACE — camera video + detector, debug, thông số hình học, nút Start/Stop Detection riêng chỉ là công cụ kiểm thử/maintenance; START AUTO ở Overview phải là nút chính cho vận hành; không cần user vào tab này mỗi lần chạy.
8) CAMERA / SOURCE — chọn nguồn Hik/Basler/video theo code hiện có, chỉ điều chỉnh layout, không tự mở camera thật trong bài test; hỗ trợ 2 camera theo cấu hình, đánh dấu Camera2 unavailable nếu chưa tích hợp.

Quan trọng: KHÔNG thay toàn bộ logic bằng màn hình giả. Preserve embedded video pipelines, data bindings, camera acquisition, detection overlays, track associations, event logging, TwinCAT ADS read status, video-file test/no-encoder mode. Các control mới không được làm mất click handlers/bindings cũ. Không break cấu trúc XAML.

## 5. KIỂM THỬ BẮT BUỘC
- Kiểm tra compile XAML, C# (build theo phương thức project đã có, off-line/disarmed).
- Test NAV cả 8 tab, settings subtabs, bảng Sensors/Filters, camera surfaces & overlays preserve, keyboard focus, 1366x768 và 1649x928, resize + minimize/maximize, 100%/125% DPI nếu sẵn.
- Dùng computer use / UI automation tương tác WPF OFFLINE/DEMO. Kiểm tra từng nút không vô tình tạo real ADS writes. Ảnh screenshot thật của WPF cho Overview + ít nhất Station01/02/Conveyor/Settings ở hai độ phân giải; rà bố cục, font, pixel clipping, buttons size, margin.
- So sánh với HTML approved về 2 camera chiếm chủ đạo + dải điều khiển dưới camera; fix spacing nếu thừa quá mức. Ở 1366x768 không được có black holes/scroll nội dung quan trọng.
- Ghi build/test logs + vấn đề hiện tại + ảnh + file changed vào Docs\...CHECKPOINT_20261021.md. Release candidate riêng, KHÔNG ghi đè Release\PlateVision50_V2.dll hay START_DATN.cmd.

## 6. NGHIỆM THU / BÁO CÁO
Milestones: M0 Context+backup+gap; M1 XAML Overview; M2 rest 7 pages; M3 functions preserving existing + safe command adapter; M4 build/tests & GUI visual QA; M5 summary.
Sau mỗi milestone hãy nêu file changed, hashes/backups, build errors, unresolved blockers. Nếu làm dở phải checkpoint để phiên mới tiếp tục. Không dừng sau khi lập plan; thực hiện cho tới khi có GUI/WPF preview thực sự và bằng chứng offline. Nếu thiếu camera2/hợp đồng START, KHÔNG biến nó thành chức năng máy chạy thật.
