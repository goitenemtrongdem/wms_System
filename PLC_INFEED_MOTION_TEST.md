# Test nút Nhập hàng → FX5U qua Modbus TCP

## Phạm vi

Nút **+ Nhập hàng** trên Dashboard chỉ gửi một xung Modbus TCP đến PLC. Nó không
tạo inbound queue, không đổi slot, không ghi item hay lịch sử kho.

| Hạng mục | Giá trị |
| --- | --- |
| Bit PLC được WMS ghi | `M210` |
| Modbus coil | `8402` (`8192 + 210`; cùng map với `M202 = 8394`) |
| Độ rộng xung từ WMS | 500 ms |
| Số xung chuyển động | 50,000 |
| Tốc độ lệnh | 10,000 pps |
| Pulse/SIGN output | `Y0` / `Y4` |

WMS chỉ ghi `M210 = 1`, chờ 500 ms rồi luôn ghi lại `0`. PLC là nơi duy nhất
đặt số xung và tốc độ; web không thể sửa các giá trị này.

## GX Works3 / Ladder cần thêm

Thêm các network sau vào chương trình FX5U. `M211` là xung một scan, `M212` là
busy lock để bỏ qua mọi request mới khi axis đang chạy.

```text
Network 1 — tạo xung chạy một scan từ lệnh Modbus
|---[ M210 ]---[/ M212 ]------------------------( PLS M211 )---|

Network 2 — khoá lệnh lặp và chạy relative positioning
|---[ M211 ]-----------------------------------( SET M212 )---|
|---[ M211 ]-------------------[ DDRVI K50000 K10000 Y0 Y4 ]---|

Network 3 — nhả busy khi axis 1 kết thúc bình thường hoặc lỗi
|---[ SM8029 ]---------------------------------( RST M212 )---|
|---[ SM8329 ]---------------------------------( RST M212 )---|

Network 4 — reset lock khi PLC vừa vào RUN
|---[ SM402 ]----------------------------------( RST M212 )---|
```

`DDRVI` là bắt buộc vì `DRVI` chỉ nhận khoảng -32,768 đến +32,767; 50,000 là
lệnh 32-bit. `DDRVI K50000 K10000 Y0 Y4` là relative positioning, nên PLC phát
50,000 pulse rồi tự dừng. Dấu `K50000` cho chiều tiến; nếu chiều cơ khí bị ngược,
đổi thành `K-50000` chỉ sau khi thử không tải.

## Tham số phải giữ trong GX Works3

Trong **High Speed I/O → Positioning → Axis 1**:

```text
Pulse Output Mode: PULSE/SIGN
Output Device (PULSE/CW): Y0
Output Device (SIGN/CCW): Y4
Max. Speed: >= 10000 pps
Acceleration / Deceleration: đặt giá trị phù hợp cơ khí
```

Trong Modbus TCP server, cho phép master ghi coil `8402` (M210). PLC cần dùng IP
đúng với WMS (`192.168.3.250`) và port TCP `502`.

## An toàn khi test

- `Dừng nhập hàng` hiện hữu không phải nút emergency stop của move test này.
- Chỉ thử khi đường chạy trống, servo ready, E-stop phần cứng hoạt động và có người giám sát.
- 50,000 / 10,000 = 5 giây ở tốc độ ổn định; tổng thời gian thực tế có thể dài hơn do acceleration/deceleration.
- Nếu cần dừng khẩn, dùng mạch E-stop/servo disable phần cứng, không chờ web hoặc Modbus.
