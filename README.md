# CTTools – Revit API Add-in (Revit 2025)

Bộ công cụ Revit cá nhân, build ra được file cài đặt để người khác dùng.

## Yêu cầu

| Thứ | Ghi chú |
| --- | --- |
| .NET 8 SDK | Revit 2025 chạy trên .NET 8 |
| Visual Studio 2022 / Rider / VS Code (C# Dev Kit) | IDE tùy chọn |
| Revit 2025 | Chỉ cần để **chạy/test**, không cần để build (API lấy từ NuGet `Nice3point.Revit.Api`) |
| Inno Setup 6 | Chỉ cần để tạo `Setup.exe`: `winget install JRSoftware.InnoSetup` |

## Cấu trúc

```
CTTools.sln
Directory.Build.props        # Version, tên sản phẩm, RevitVersion
secrets.props.example        # Mẫu cấu hình chuỗi kết nối license (copy thành secrets.props)
build.ps1                    # Build Release + đóng gói zip + Setup.exe
database/licenses.sql        # Tạo bảng license + role giới hạn trên Neon
installer/CTTools.iss        # Script Inno Setup
src/CTTools/
  CTTools.addin              # Manifest để Revit load add-in
  App.cs                     # Tạo tab + nút trên Ribbon
  Core/                      # CommandBase (kiểm tra license), tạo nút ribbon, icon
  Commands/                  # Mỗi lệnh = 1 file
  Licensing/                 # Kiểm tra tài khoản Autodesk với Neon, lưu trạng thái offline
```

## Phát triển hằng ngày

1. Build **Debug** (`dotnet build` hoặc F5 trong Visual Studio): add-in tự copy vào
   `%APPDATA%\Autodesk\Revit\Addins\2025\` → mở Revit là thấy tab **CTTools**.
2. F5 trong Visual Studio sẽ mở Revit 2025 (xem `Properties/launchSettings.json`) để đặt breakpoint.
3. Phải **tắt Revit trước khi build** – Revit khóa file DLL khi đang chạy.

### Thêm lệnh mới

1. Tạo file trong `Commands/`, kế thừa `CommandBase`:

   ```csharp
   [Transaction(TransactionMode.Manual)]
   public class MyCommand : CommandBase
   {
       protected override Result Run(UIApplication uiApp)
       {
           var doc = uiApp.ActiveUIDocument.Document;
           using var tx = new Transaction(doc, "My Command");
           tx.Start();
           // ... sửa model ...
           tx.Commit();
           return Result.Succeeded;
       }
   }
   ```

   Lệnh chỉ đọc dữ liệu thì dùng `TransactionMode.ReadOnly`.
2. Thêm icon trong `Core/RibbonIcons.cs` (glyph font Segoe MDL2 Assets: `IconFactory.Glyph('\uE8B7', "#00796B")`, hoặc tự vẽ bằng `IconFactory.Drawing(...)`), rồi thêm nút trong `App.cs`: `panel.AddButton<MyCommand>("Tên nút", RibbonIcons.MyIcon, "Tooltip");`

## License theo tài khoản Autodesk (Neon PostgreSQL)

Mọi lệnh kế thừa `CommandBase` đều phải qua bước kiểm tra license. Riêng **About** và **License** được chạy tự do (`RequiresLicense => false`).

**Cách hoạt động:** người dùng **không phải nhập gì**. Khi bấm một tool, add-in lấy tài khoản Autodesk đang đăng nhập trong Revit
(`Application.Username`, chỉ khi `Application.IsLoggedIn`) → gọi `check_license(...)` trên Neon → có thì cho chạy.

- Mỗi phiên Revit chỉ hỏi server **một lần**; đổi tài khoản Autodesk giữa chừng thì kiểm tra lại.
- Chưa đăng nhập Autodesk → bị chặn, báo "Please sign in...".
- Không có trong DB → hộp thoại hiện đúng tài khoản đang đăng nhập để người dùng gửi cho bạn thêm vào DB.
  Sau khi thêm, người dùng bấm nút **License** để kiểm tra lại, không cần khởi động lại Revit.
- Mất mạng: tài khoản đã kiểm tra thành công vẫn dùng được **7 ngày** (lưu ở `%APPDATA%\CTTools\license.json`).

> **Quan trọng:** Revit API không có hàm lấy email. `Username` là **tên đăng nhập Autodesk** – tài khoản mới thường là email,
> tài khoản cũ có username riêng (ví dụ `lechithien26` chứ không phải `lechithien26@gmail.com`).
> Vì vậy bảng có thêm cột `autodesk_username` cho các tài khoản cũ – xem phần *Quản lý email* bên dưới.

### Thiết lập lần đầu

1. Tạo project miễn phí trên [neon.tech](https://neon.tech).
2. Mở **SQL Editor**, đổi mật khẩu `CHANGE_ME...` trong `database/licenses.sql` rồi chạy toàn bộ file.
3. Copy `secrets.props.example` → `secrets.props`, điền host + mật khẩu của role **`cttools_client`**
   (lấy host trong Neon → *Connect*). `secrets.props` đã được git-ignore, không bị đẩy lên GitHub.
4. Build lại. `build.ps1` sẽ từ chối build nếu thiếu `secrets.props`.

### Quản lý email

Chạy trong Neon SQL Editor (hoặc dùng *Tables* để sửa trực tiếp):

Bảng `licenses` có 2 cột định danh, add-in cho dùng nếu username Revit khớp **một trong hai**:

| Cột | Khi nào điền |
| --- | --- |
| `email` | Luôn điền – dùng để bạn quản lý khách |
| `autodesk_username` | Chỉ khi nút **About** của khách hiện giá trị **khác** email (tài khoản Autodesk cũ) |

```sql
-- Tài khoản mới (About hiện đúng email)
insert into licenses (email, note) values ('client@firm.com', 'Trial');
-- Tài khoản cũ (About hiện username riêng)
insert into licenses (email, autodesk_username) values ('someone@gmail.com', 'someone_bim');
-- Có thời hạn
insert into licenses (email, expires_at) values ('client@firm.com', now() + interval '1 year');
-- Thu hồi
update licenses set is_active = false where email = 'someone@gmail.com';
```

Database tạo trước khi có cột `autodesk_username`: chạy `database/migrations/001_autodesk_username.sql` một lần.

### Giới hạn cần biết (nên nâng cấp khi bắt đầu bán)

- **Chuỗi kết nối nằm trong DLL**: ai decompile được đều thấy. Vì vậy add-in chỉ dùng role `cttools_client` – role này
  chỉ gọi được `check_license`, không đọc được danh sách email, không thêm/xóa được gì. **Tuyệt đối không dùng role owner.**
- **Username chỉ bị khóa khi đã đăng nhập Autodesk** – vì vậy add-in bắt buộc `IsLoggedIn`. Muốn chặt hơn nữa có thể lưu thêm
  `Application.LoginUserId` (ID nội bộ của Autodesk, không đổi được) vào DB và ràng buộc với email.
- **Cổng 5432 có thể bị firewall công ty chặn** (hay gặp ở các văn phòng nước ngoài). Hướng nâng cấp: dựng một API HTTPS nhỏ
  (Cloudflare Workers / Vercel, đều free) đứng giữa add-in và Neon – chỉ cần thay `LicenseClient.Check`, phần còn lại giữ nguyên.
- Mọi cơ chế license phía client đều có thể bị crack; mục tiêu ở đây là chặn người dùng thông thường.

## Phát hành cho người khác

1. Tăng `<Version>` trong `Directory.Build.props`.
2. Chạy:

   ```powershell
   .\build.ps1
   ```

3. Kết quả trong `dist/`:
   - `CTTools-Revit2025-x.y.z-Setup.exe` – gửi cho người dùng, bấm cài là xong
     (mặc định cài cho user hiện tại, không cần quyền admin; chạy bằng admin thì chọn được cài cho mọi user).
     Gỡ bằng *Settings → Apps*.
   - `CTTools-Revit2025-x.y.z.zip` – bản cài tay: giải nén vào `%APPDATA%\Autodesk\Revit\Addins\2025\`.

Lần đầu mở Revit, người dùng sẽ thấy cảnh báo "unsigned add-in" → chọn **Always Load**.
Muốn bỏ cảnh báo này cần mua chứng chỉ code signing và ký file DLL.

## Hỗ trợ thêm phiên bản Revit sau này

- **Revit 2026**: cũng dùng .NET 8 → đổi `RevitVersion` là build được gần như ngay.
- **Revit 2021–2024**: dùng .NET Framework 4.8 → cần multi-target (`net48`) và `#if` cho API khác nhau.

## Ý tưởng theo nhu cầu quốc tế

Những nhóm công cụ hay được tìm mua trên Autodesk App Store / các diễn đàn BIM:

- **Quản lý sheet & view**: tạo hàng loạt sheet từ Excel, đổi tên/đánh số lại view/sheet, copy view template giữa các dự án.
- **Dữ liệu & Excel**: xuất/nhập parameter qua Excel (sửa dữ liệu hàng loạt), xuất schedule ra CSV/Excel.
- **Kiểm tra model (QA/QC)**: tìm warning, family không dùng, phần tử trùng, kiểm tra quy tắc đặt tên theo tiêu chuẩn **ISO 19650**.
- **Tự động ghi chú**: tự dim tường/lưới, tag hàng loạt, đánh số phòng/cửa theo thứ tự.
- **MEP**: tính toán kích thước ống/ống gió, đánh số thiết bị, kiểm tra kết nối hở.
- **Kết cấu**: bố trí thép tự động, xuất bảng thống kê khối lượng.
- **Quản lý family**: load/rename/purge family hàng loạt, thư viện family có preview.
- **Xuất file**: xuất PDF/DWG/IFC hàng loạt với quy tắc đặt tên tùy chỉnh.

Kênh kiểm chứng nhu cầu: [Autodesk App Store](https://apps.autodesk.com/RVT/en/Home/Index),
[Revit API Forum](https://forums.autodesk.com/t5/revit-api-forum/bd-p/160), r/Revit, nhóm BIM trên LinkedIn.

## Tài liệu học

- [Revit API Docs (tra cứu class)](https://www.revitapidocs.com/2025/)
- [The Building Coder – Jeremy Tammik](https://thebuildingcoder.typepad.com/)
- [RevitLookup](https://github.com/jeremytammik/RevitLookup) – công cụ bắt buộc để xem dữ liệu bên trong model
