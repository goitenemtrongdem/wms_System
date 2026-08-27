# AS/RS WMS

WMS cho hệ AS/RS: quét QR qua Basler, điều phối nhập theo tải trọng, xuất kho FEFO và báo cáo tồn kho.

## Chạy lần đầu

1. Mở `Database/upgrade_infeed_v2.sql` trong SQL Server/VS Code và chạy toàn bộ file. Script an toàn, giữ nguyên dữ liệu có sẵn trong `ASRS_Warehouse`.
2. Kiểm tra chuỗi kết nối trong `appsettings.json`.
3. Chạy:

   ```powershell
   dotnet restore
   dotnet run
   ```

`Database/init.sql` cũ đã được vô hiệu hoá để không thể xoá dữ liệu. Không dùng file đó để khởi tạo.

## Chạy bằng Docker (web cloud + SQL Server)

Dockerfile ở thư mục gốc build phiên bản **cloud** của WMS để chạy trên Linux/Railway.
Nó giữ Dashboard, quét QR thủ công, SignalR, workflow và SQL Server; camera GigE,
PLC Modbus và đọc QR từ ảnh không chạy trong container cloud vì các thiết bị đó chỉ
được nhìn thấy từ mạng nội bộ kho và camera đang dùng SDK Windows.

1. Cài và mở Docker Desktop ở chế độ Linux containers.
2. Tạo file cấu hình local (file này đã được `.gitignore`):

   ```powershell
   Copy-Item .env.example .env
   ```

3. Đổi `MSSQL_SA_PASSWORD` trong `.env` thành mật khẩu mạnh của riêng bạn.
4. Khởi động web và SQL Server:

   ```powershell
   docker compose up --build -d
   ```

5. Khi `db` đã healthy, chạy script nâng cấp schema một lần. Script có thể chạy lại
   mà không xoá dữ liệu:

   ```powershell
   $password = (Get-Content .env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' }) -replace '^MSSQL_SA_PASSWORD=', ''
   docker compose exec db /opt/mssql-tools18/bin/sqlcmd `
     -S localhost -U sa -P $password -C `
     -i /scripts/upgrade_infeed_v2.sql
   ```

Mở `http://127.0.0.1:8080` và kiểm tra `http://127.0.0.1:8080/health`.
Trên máy này Docker Desktop không chuyển tiếp ổn định qua IPv6 `localhost`, nên
dùng `127.0.0.1` để truy cập local. SQL Server chỉ mở trong mạng Docker (web kết
nối qua `db:1433`), tránh xung đột với SQL Server đã cài sẵn trên Windows.
Để xem log, dùng `docker compose logs -f web`; để dừng, dùng `docker compose down`.
Dữ liệu SQL Server nằm trong Docker volume `mssql_data`, nên `down` không xoá dữ liệu.

`appsettings.json` không chứa connection string hay mật khẩu nữa. Khi chạy không qua
Docker, sao chép `appsettings.Development.example.json` thành
`appsettings.Development.json`, rồi điền chuỗi kết nối local của bạn.

### Triển khai Railway

Railway tự nhận `Dockerfile` ở thư mục gốc. Tạo service web từ repository này và đặt
biến `ConnectionStrings__WarehouseDb` tại runtime. App đã tự đọc biến `PORT` mà
Railway cấp và có health check tại `/health`.

Vì project sử dụng SQL Server, hãy triển khai SQL Server thành một service/container
riêng có volume bền vững, hoặc dùng SQL Server managed bên ngoài Railway; không dùng
`localhost` trong connection string của service web. Nên cấu hình health check
Railway là `/health`.

Muốn WMS điều khiển PLC/camera sau khi lên cloud thì cần một **edge agent Windows**
chạy trong mạng kho, giữ SDK MV Viewer và kết nối PLC, sau đó trao đổi với API cloud
qua HTTPS. Không thể để Railway truy cập trực tiếp IP `192.168.x.x` hay USB/GigE
camera trong kho.

## Luồng nhập QR

1. PLC đưa `M202 = 1` (Modbus coil `8394`). Ứng dụng nhận cạnh lên một lần.
2. Sau đúng 60 giây (`Camera:TriggerDelaySeconds`), ứng dụng chụp một frame từ Basler Pylon và đọc QR.
3. QR được đối chiếu với `INFEED_ITEMS.QRCodeValue`, `QRCode` hoặc `ItemCode`; có thể dùng QR JSON đầy đủ để tự tạo item.
4. Item được lưu trong `inbound_queue` với trạng thái `READY`.
5. Bấm **Nhập hàng**. Hàng nặng (mặc định từ 20 kg) được ưu tiên tầng dưới, hàng nhẹ tầng trên. Có thể đổi ngưỡng và chiều cao `RowNo` trong `StoragePolicy`.
6. Khi cảm biến của ô báo có pallet, item mới được gán `CurrentSlotId`, trạng thái chuyển `STORED`, `Item ID` hiện trên Dashboard và History.

Ô “Quét thủ công” trên Dashboard phục vụ nghiệm thu hoặc máy scan cầm tay; nó dùng đúng cùng hàng đợi với camera.

## Basler và PLC

- Cài **Basler pylon Runtime** trên máy chạy WMS. Service sẽ tự tìm `Basler.Pylon.dll`; nếu cài ở vị trí khác, đặt đường dẫn tuyệt đối vào `Camera:PylonAssemblyPath`.
- M200/M201 là hai cảm biến R01/R02, M202 là trigger camera. Các địa chỉ có thể đổi trong phần `Plc` của `appsettings.json`.
- `InboundRequestCoil` và `OutboundRequestCoil` đang để `null` để không ghi nhầm PLC. Sau khi xác nhận mapping trong GX Works3, điền Modbus coil tương ứng. Khi được cấu hình, mỗi lệnh sẽ bật coil trong `CommandPulseMilliseconds` rồi tự tắt.

## Xuất kho và report

- Transfer nhận **tên mặt hàng** và **số lượng**. Hệ thống chỉ lấy các lô `STORED`, sắp theo hạn dùng gần nhất (FEFO).
- Nếu số lượng yêu cầu cắt giữa một lô pallet, hệ thống yêu cầu lấy toàn lô; sau khi cảm biến xác nhận pallet đã rời kệ, phần dư được tạo thành item/lô mới trong `inbound_queue` để nhập lại.
- Report có các khoảng tuần/tháng/năm, thống kê lượt nhập/xuất, lô/số lượng tồn và lô sắp hết hạn.

## Test cảm biến không cần PLC

```powershell
Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5000/api/sensors/update" `
  -ContentType "application/json" `
  -Body '{"slotName":"R01","occupied":true}'
```

Đổi cổng theo URL do `dotnet run` in ra. Chỉ dùng lệnh này trên môi trường test vì nó mô phỏng cảm biến thật.
