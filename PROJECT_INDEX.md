# DATN — THƯ MỤC DỰ ÁN TẬP TRUNG

**Đường dẫn chính:** C:\Users\aml\Desktop\DATN

Đây là khu vực TẬP HỢP MÃ NGUỒN / BACKUP trên AML-IPC. Không xóa, không di chuyển bản gốc đang chạy trong Documents. Việc sao chép không nạp chương trình vào PLC.

## 1. Bảng đường dẫn quan trọng

| Công việc | Mở ở đâu |
| --- | --- |
| Chạy giao diện DATN | START_DATN.cmd |
| Source C# Hikrobot/Basler/Vision | App\Vision\PlateVision50.csproj |
| Thư viện/bộ điều khiển camera | Dependencies\ |
| Dự án TwinCAT 3 tích hợp gần nhất (bản sao) | PLC\TwinCAT\DATN.sln |
| Mã PLC TwinCAT | PLC\TwinCAT\DATN\PLC1\ |
| Bản TwinCAT gốc trước thay đổi | D:\DATN_ARCHIVE_20261020\DATN_Excluded_Builds_and_Backups\PLC\TwinCAT_Original_20261015\DATN.sln |
| Các bản TwinCAT theo từng giai đoạn | D:\DATN_ARCHIVE_20261020\DATN_Excluded_Builds_and_Backups\PLC\Backups\TwinCAT_Stages\ (12 ZIP) |
| PLC Mitsubishi GX Works2: Test2 gốc | PLC\Mitsubishi\Sources_20261019\GXWorks2\Candidates\Test2_D_DATN.gxw |
| PLC Mitsubishi GX Works2: bản Session thử | PLC\Mitsubishi\Sources_20261019\GXWorks2\Candidates\Test2_FROM_SESSION_20261017.gxw |
| Bản Mitsubishi gốc / tham khảo | PLC\Mitsubishi\Sources_20261019\GXWorks2\Backups và GXWorks2\References |
| Mã ST + tài liệu PLC Mitsubishi | PLC\Mitsubishi\Sources_20261019\Source\ và Docs\ |
| Tài liệu tích hợp ADS / EStop / UI | Docs\ |
| Bản C# đã publish | Release\ |
| Runtime .NET đóng gói | Runtime\DotNet8\ |

## 2. Dự án nào là dự án chính?

- **TwinCAT 3:** PLC\TwinCAT\DATN.sln là COPY trùng với bản C:\Users\aml\Documents\DATN_GLOBAL_STOP_STAGE_20261019\DATN.sln tại thời điểm gom. Runtime TwinCAT là hệ thống riêng; không suy luận bản copy đã được nạp, và không tự Activate/Download.
- **PLC Mitsubishi:** Có nhiều bản .gxw từ máy DESKTOP-B090KN3. Chưa đối chiếu / Read from PLC-Q nên CHƯA THỂ khẳng định file nào là bản chạy thực tế. Không Write to PLC nếu chưa so sánh online.
- **C# Dashboard:** App\Vision là source và Release\PlateVision50_V2.dll là ứng dụng từ START_DATN.cmd. Không di chuyển Release hay Runtime vì có thể gián đoạn chương trình đang chạy.
- **Bản TwinCAT gốc:** Đã chuyển vào D:/DATN_ARCHIVE_20261020/DATN_Excluded_Builds_and_Backups/PLC/TwinCAT_Original_20261015; không dùng để thay thế bản hiện hành.
- **12 ZIP lịch sử:** Đã chuyển vào D:/DATN_ARCHIVE_20261020/DATN_Excluded_Builds_and_Backups/PLC/Backups.

## 3. Các giới hạn / phần chưa được gom vật lý

- Thư mục D:\DATN_ARCHIVE_20261020\DATN_LEGACY_ARCHIVE_20261019 chứa khoảng **6,48 GB** bản C# lịch sử. Chúng vẫn an toàn ở vị trí cũ, KHÔNG nhân đôi vì chứa nhiều bản build nặng và trùng lặp. Có thể đọc Docs\legacy_folder_inventory.csv và Docs\legacy_folder_moves.csv để truy vết.
- Hai dự án PlateVision50 cũ (ứng dụng và tests) đã được kiểm tra rồi lưu tại D:/DATN_ARCHIVE_20261020/Desktop_Legacy_Projects; Desktop/WpfYolo_Detect_RealTime-master và Desktop/DEMO vẫn giữ nguyên vì là nguồn phụ thuộc/dữ liệu thử.
- File GX Works2 được chép từ máy DESKTOP-B090KN3 qua Tailscale, gói backup tại PLC\Mitsubishi\DATN_Mitsubishi_Sources_20261019.zip. Tất cả 10 tập tin nguồn đều khớp SHA256 của SOURCE_MANIFEST.json.
- Chương trình Mitsubishi PLC-Q, cấu hình TwinCAT đang RUN, route, license và driver KHÔNG được di chuyển hoặc nạp trong thao tác gom file.
- Hướng dẫn này không xác nhận tính đúng của chương trình PLC, điều kiện an toàn cơ khí hay mạch E-Stop.

## 4. Cách mở

- Double click **START_DATN.cmd** để chạy giao diện đang dùng.
- Double click **OPEN_TWINCAT3.cmd** để mở bản sao dự án TwinCAT (không Activate).
- Double click **OPEN_MITSUBISHI_FOLDER.cmd** để tới thư mục các dự án GX Works2 và mở file cần thiết trong GX Works2.
- Cần làm việc trực tiếp với chương trình Mitsubishi: so sánh với PLC đang chạy trước khi chọn file làm bản gốc chính thức.

## 5. Quy tắc cập nhật từ nay

Đặt source chính và bản phát hành mới trong DATN; trước khi ghi đè tạo ZIP hoặc thư mục phiên bản mới. Không thay PLC đang chạy chỉ bằng thao tác copy source. Giữ hợp đồng giao tiếp M1000/M1001 và bảo vệ E-Stop phần cứng.

## 6. CẬP NHẬT 2026-10-20
- Tất cả source hiện tại nằm trong DATN; lịch sử/backup chuyển về D:/DATN_ARCHIVE_20261020 sau kiểm tra hash.
- Có thêm App/Tests/Tests.csproj, build pass.
- Thư mục Visual Studio cũ Desktop/PlateVision50_V2 đã chuyển vào D sau khi xác minh VS Insiders mở chính DATN/App/PlateVision50.sln.
- Các thư mục TwinCAT_AI_OFFLINE và TwinCAT_Original rỗng sau lưu trữ đã được dọn khi Windows giải phóng handle; tất cả dữ liệu đều trên ổ D.
