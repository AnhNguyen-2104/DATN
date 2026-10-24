# SỬA LỖI BUILD MSB3030 - NVKProject.DotNet.BaslerCamera.dll

**Thời điểm:** 2026-10-21  
**Môi trường:** Visual Studio 2022 / .NET SDK 8 / 11 preview trên AML-IPC  

---

## 1. Hiện tượng lỗi
- Khi build project `PlateVision50` trong Visual Studio 2022 hoặc chạy build từ Solution `App\PlateVision50.sln`, IDE báo lỗi:
  ```
  MSB3030: Could not copy the file "E:\Orca\DATN\Dependencies\BaslerCamera\bin\Debug\net8.0-windows\NVKProject.DotNet.BaslerCamera.dll" because it was not found.
  Project: PlateVision50
  File: Microsoft.Common.CurrentVersion.targets
  Line: 5131
  ```

## 2. Nguyên nhân gốc rễ
- File `App\Vision\PlateVision50.csproj` có tham chiếu dự án:
  ```xml
  <ProjectReference Include="..\..\Dependencies\BaslerCamera\NVKProject.DotNet.BaslerCamera.csproj"/>
  ```
- Tuy nhiên trong commit trước (`bb6502c`), dự án `NVKProject.DotNet.BaslerCamera.csproj` đã bị xóa khỏi file solution `App\PlateVision50.sln`.
- Visual Studio IDE khi build Solution chỉ lên lịch build (schedule) các project có mặt trong file `.sln`. Khi build `PlateVision50.csproj`, Visual Studio truyền thuộc tính `BuildProjectReferences=false`, dẫn đến project phụ thuộc `NVKProject.DotNet.BaslerCamera.csproj` không được build trước. File output DLL trong thư mục `bin` không tồn tại, gây lỗi `MSB3030`.

## 3. Giải pháp đã thực hiện
1. **Sao lưu trước khi chỉnh sửa:**
   - File gốc đã lưu tại: `Docs/BUILD_FIX_20261021/App_PlateVision50.sln.bak`
2. **Cập nhật Solution `App\PlateVision50.sln`:**
   - Thêm lại `NVKProject.DotNet.BaslerCamera.csproj` vào Solution.
   - Gom gọn gàng vào Solution Folder `Dependencies` để Solution Explorer trong Visual Studio hiển thị cấu trúc rõ ràng:
     - 📁 **Vision**: `PlateVision50`
     - 📁 **Dependencies**: `NVKProject.DotNet.BaslerCamera`
3. **Build kiểm chứng offline:**
   - Đã xóa sạch các thư mục `bin` và `obj` để kiểm tra build từ đầu (clean build).
   - Chạy lệnh build:
     ```cmd
     dotnet build App/PlateVision50.sln -v:m
     dotnet build App/PlateVision50.sln -c Release -v:m
     ```
   - Kết quả: Build thành công 100% (0 Error), cả hai project biên dịch đúng thứ tự phụ thuộc và tự động copy `NVKProject.DotNet.BaslerCamera.dll` cùng các thư viện runtime Basler Pylon.
   - Đã build thử nghiệm vào thư mục: `ReleaseCandidate\20261021_BuildFix` (không ghi đè thư mục `Release` đang hoạt động; thư mục RC đã được `.gitignore` bỏ qua).
