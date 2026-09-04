# Test nút Nhập hàng → FX5U qua Modbus TCP

## Phạm vi

Nút **+ Nhập hàng** trên Dashboard chỉ gửi một xung Modbus TCP đến PLC. Nó không
tạo inbound queue, không đổi slot, không ghi item hay lịch sử kho.

| Hạng mục | Giá trị |
| --- | --- |
| Bit PLC được WMS ghi | `M210` |
| Modbus coil | `8402` (`8192 + 210`; cùng map với `M202 = 8394`) |
| Giá trị xung WMS ghi | `D100:D101 = 50,000` (holding-register offsets `100:101`) |
| Độ rộng xung từ WMS | 500 ms |
| Số xung chuyển động | 50,000 |
| Tốc độ lệnh | 1,000 pps |
| Pulse/SIGN output | `Y0` / `Y4` |

WMS ghi số xung `50,000` vào cặp `D100:D101` trước, rồi mới ghi `M210 = 1`,
chờ 500 ms và luôn ghi lại `0`. `D100:D101` là một số nguyên 32-bit; không thể
lưu số 50,000 trong vùng `M` vì mỗi `M` chỉ là một bit. PLC giữ tốc độ cố định
`1,000 pps` trong ladder.

## GX Works3 / Ladder cần thêm

Thêm các network sau vào chương trình FX5U. `M212` là busy lock để bỏ qua mọi
request mới khi axis đang chạy. Không cần dùng `M211` hay `PLS` cho move test
này: xung M210 từ WMS dài 500 ms, lớn hơn rất nhiều so với một scan PLC.

```text
Network 1 — nhận M210 và khoá lệnh lặp
|---[ M210 ]---[/ M212 ]------------------------( SET M212 )---|

Network 2 — giữ drive contact trong toàn bộ positioning move
|---[ M212 ]----------------------[ DDRVI D100 K1000 Y0 Y4 ]---|

Network 3 — nhả busy khi axis 1 kết thúc bình thường hoặc lỗi
|---[ SM8029 ]---------------------------------( RST M212 )---|
|---[ SM8329 ]---------------------------------( RST M212 )---|

Network 4 — reset lock khi PLC vừa vào RUN
|---[ SM402 ]----------------------------------( RST M212 )---|
```

`DDRVI` là bắt buộc vì `DRVI` chỉ nhận khoảng -32,768 đến +32,767; 50,000 là
lệnh 32-bit. Tiếp điểm drive của `DDRVI` phải là `M212`: M210 chỉ là tín hiệu
khởi động, còn M212 giữ ON cho đến khi PLC phát đủ xung. `DDRVI D100 K1000 Y0
Y4` đọc số xung từ cặp `D100:D101`, nên PLC phát đúng 50,000 pulse rồi tự dừng
khi web đã ghi giá trị 50,000. Nếu chiều cơ khí bị ngược, web phải gửi `-50000`
(cần thay đổi cấu hình có chủ đích) thay vì sửa logic ladder.

## Tham số phải giữ trong GX Works3

Trong **High Speed I/O → Positioning → Axis 1**:

```text
Pulse Output Mode: PULSE/SIGN
Output Device (PULSE/CW): Y0
Output Device (SIGN/CCW): Y4
Max. Speed: >= 1000 pps
Acceleration / Deceleration: đặt giá trị phù hợp cơ khí
```

Trong Modbus TCP server, cho phép master ghi coil `8402` (M210). Với **FX5
dedicated pattern** mặc định, holding-register offset `100` map tới `D100` và
offset `101` map tới `D101`; không thay đổi Device Assignment này. PLC cần dùng
IP đúng với WMS (`192.168.3.250`) và port TCP `502`.

## An toàn khi test

- `Dừng nhập hàng` hiện hữu không phải nút emergency stop của move test này.
- Chỉ thử khi đường chạy trống, servo ready, E-stop phần cứng hoạt động và có người giám sát.
- 50,000 / 1,000 = 50 giây ở tốc độ ổn định; tổng thời gian thực tế có thể dài hơn do acceleration/deceleration.
- Nếu cần dừng khẩn, dùng mạch E-stop/servo disable phần cứng, không chờ web hoặc Modbus.
