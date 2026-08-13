# AS/RS Warehouse Starter

Mini WMS cho 8 ô kệ, gồm 3 màn hình Dashboard / History / Transfer và đúng 3 bảng SQL Server.

## Chạy nhanh
1. Mở `Database/init.sql` bằng extension **SQL Server (mssql)** trong VS Code và chạy toàn bộ script lên SQL Server local.
2. Kiểm tra `appsettings.json`. Mặc định dùng Windows Authentication:
   `Server=localhost;Database=ASRS_Warehouse;Trusted_Connection=True;TrustServerCertificate=True;`
3. Tại terminal trong thư mục `AsrsWarehouse`:
   ```powershell
   dotnet restore
   dotnet run
   ```
4. Mở URL mà terminal hiển thị.

## Test cảm biến
Ví dụ PowerShell:
```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5000/api/sensors/update" `
  -ContentType "application/json" `
  -Body '{"slotName":"R01","occupied":true}'
```
Thay URL/port theo terminal.

## Luồng trạng thái
- EMPTY -> bấm Nhập hàng -> REQUEST(INBOUND) -> sensor=true -> OCCUPIED + ghi History INBOUND.
- OCCUPIED -> Transfer/Gửi đi -> REQUEST(OUTBOUND) -> sensor=false -> EMPTY + ghi History OUTBOUND.
