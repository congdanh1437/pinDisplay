# PinDisplay

App nhỏ chạy trên khay hệ thống (taskbar) Windows, hiển thị % pin của các thiết bị Bluetooth đang kết nối: tai nghe, chuột, bàn phím, tay cầm…

## Tính năng

- Mỗi thiết bị đang kết nối có một icon riêng trên taskbar, kiểu **số % + thanh pin** hoặc **hình cục pin**.
- Bấm vào icon để mở popup chi tiết, với 2 giao diện:
  - **Kính (Liquid Glass)**: kính dày dựng theo mô hình quang học (khúc xạ Snell, tán sắc, Fresnel, highlight, bóng đổ, caustic) trên nền chất lỏng 3D.
  - **Hiện đại (Tối)**: nền xanh đen, thẻ bo lớn, thẻ chuyển xanh khi hover.
- Cập nhật ngay khi cắm/ngắt thiết bị, nhờ sự kiện `WM_DEVICECHANGE` của Windows. % pin được quét lại mỗi 90 giây.
- Cảnh báo pin yếu, có thể đặt ngưỡng riêng cho từng thiết bị (chuột phải vào thẻ thiết bị).
- Khởi động cùng Windows. Cài vào Start Menu cho riêng người dùng, không cần quyền admin.

## Yêu cầu

- Windows 10/11 x64
- `PinDisplay.exe` cần [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Bản `PinDisplay-standalone.exe` thì không cần.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Kết quả nằm trong `dist\`:

| File | Kích thước | Ghi chú |
|---|---|---|
| `PinDisplay.exe` | ~0.7 MB | cần .NET 10 Desktop Runtime |
| `PinDisplay-standalone.exe` | ~47 MB | chạy được mọi nơi |

## Lệnh hỗ trợ

```powershell
PinDisplay.exe --list               # in danh sách thiết bị và % pin
PinDisplay.exe --preview .\preview  # xuất ảnh popup + icon để kiểm tra giao diện
PinDisplay.exe --uninstall          # gỡ cài đặt
```

## Cách đọc pin

App đọc thuộc tính PnP `DEVPKEY_Bluetooth_Battery` (`{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`) mà Windows tự lấy từ thiết bị, qua SetupAPI, không cần thư viện ngoài. Trạng thái kết nối được gom theo địa chỉ Bluetooth từ mọi nút của thiết bị, vì với tai nghe Bluetooth thường, % pin và trạng thái "đã kết nối" nằm ở hai nút khác nhau.

Thiết bị dùng receiver riêng (Logitech Unifying…) hoặc tay cầm Xbox không báo pin qua thuộc tính này nên sẽ không hiện.

## Cấu trúc

| File | Vai trò |
|---|---|
| `BatteryReader.cs` | đọc % pin và trạng thái kết nối qua SetupAPI |
| `DeviceChangeWatcher.cs` | nhận sự kiện cắm/ngắt thiết bị, gộp các sự kiện liên tiếp (debounce) |
| `TrayApp.cs` | icon trên khay, thông báo, cài đặt |
| `Flyout.cs` | popup |
| `Skin.cs` | giao diện Kính và Hiện đại |
| `GlassRenderer.cs` | dựng kính: khúc xạ, Fresnel, thành kính, bóng đổ |
| `Installer.cs` | cài/gỡ vào `%LOCALAPPDATA%\Programs\PinDisplay` |
| `tools/make-icon.ps1` | tạo `app.ico` |
