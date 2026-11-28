# ABS_Home compact — đọc ABS và preset Md.20 (2026-11-27)

## ĐƯỜNG DẪN CHUẨN

- Workspace GX Works2 đang mở: `E:\Orca\DATN\PLC\Mitsubishi\DATN_LINE\Robot_SCARA`
- Tệp dự án GX Works2: `E:\Orca\DATN\PLC\Mitsubishi\DATN_PLC.gxw`
- Nguồn compact chuẩn: `E:\Orca\DATN\PLC\Mitsubishi\ABS_Home.st`
- POU: `Robot_SCARA > POU > Program > ABS_Home > Program`
- CPU/đích vật lý: `Q06UDEH`, `Host 192.168.3.40`
- Không sử dụng checkout Desktop/`C:\Users\aml\orca` hoặc host `192.168.3.39`.

## 1. Chức năng hiện tại

`PLC/Mitsubishi/ABS_Home.st` chỉ thực hiện một chu kỳ đồng bộ khi PLC khởi động hoặc sau xung reset lỗi `M131`:

1. Chờ 3,5 giây và kiểm tra liên động an toàn.
2. Đọc encoder tuyệt đối của từng trục đã lắp qua engine RS-422 CH2 hiện hữu. Mỗi trục phải có hai lần đọc lệnh `02/91` liên tiếp và bằng nhau.
3. Chuyển đổi theo công thức:
   `diQ = HomeValue + (AbsPosition - ZeroPulse) * ScaleNum / ScaleDen`.
   Nếu `mode = 0`, kết quả được chuẩn hoá bằng `MOD 36000000`.
4. Đọc Pr.12/Pr.13 bằng `FROM` với `wBfm` và từ chối giá trị ngoài dải.
5. Ghi Da.6, tạm đặt Cd.3 = `9003`, phát xung start `M111..M114`, chờ start-done và not-busy, đọc lại Md.20, so sánh tuyệt đối rồi phục hồi Cd.3 cũ.
6. Kết thúc ở `SyncState = 50` nếu toàn bộ quá trình thành công.

Chương trình không ra lệnh chuyển động. Origin/home cơ khí chỉ được thiết lập trong **MR Configurator2**, không được thiết lập trong PLC.

Cấu hình được viết lại ở first scan dù `D3000` là vùng latch:

| Trục | Mode | Station | ScaleNum | ScaleDen | ZeroPulse | HomeValue |
|---|---:|---:|---:|---:|---:|---:|
| J1 | 2 (rotary) | 0 | 30 | 1 | 0 | 0 |
| J2 | 2 (rotary) | 1 | 36 | 1 | 0 | 0 |

`AxisMax = 2`; J3/J4 bằng 0 và `PositionValid3/4` luôn FALSE. `bAxisFresh` chỉ được set tại state 20 sau hai mẫu bằng nhau cho `AxisIndex`, mà `AxisIndex` chạy từ 1 đến `AxisMax`; vì vậy không thể set cho trục lớn hơn `AxisMax`. Cờ fresh được xoá trước mỗi lần đọc, khi reset `M131` và khi vào state 90 để không công bố vị trí cũ.

### Liên động QD75 theo `AxisMax`

Các bit lỗi (`X8`/`X9`/`XA`/`XB`) và busy (`XC`/`XD`/`XE`/`XF`) chỉ tham gia liên động đến trục được cấu hình bởi `AxisMax`; J1/J2 vẫn luôn được kiểm tra khi `AxisMax = 2`. Vì J3/J4 chưa đấu dây, QD75 có thể tiếp tục giữ lỗi axis 3 mã `102` (`XA = 1`), nhưng tín hiệu này được bỏ qua khi `AxisMax = 2`; nếu tăng `AxisMax`, liên động của trục tương ứng tự động có hiệu lực và vẫn fail-closed.

## 2. Chức năng đã loại bỏ

- Đường CR set-zero `M123`, state 70 và toàn bộ nhánh command kind 1/2/3; chỉ còn đọc `02/91`.
- Set-Home `M125/M126/M127/M128`.
- Luồng nạp/áp dụng cấu hình vận hành `M129/M130`.
- Toàn bộ GoHome (`GoHomeState`, `GoHome*`, state 91).
- Logic stop-pulse `Y4..Y7`.
- Ghi Cd.19; chương trình không ghi Cd.19.
- Mọi logic chuyển động và origin setting trong PLC.

## 3. State và lỗi

| SyncState | Ý nghĩa |
|---:|---|
| 0 | Khởi tạo/autostart; tất cả xung start OFF |
| 10 | Chờ Startup3s 3,5 s, kiểm tra Y0, X0, lỗi/busy QD75 đến `AxisMax`, M20/M21/M1000/M105 và timeout StartupFault 10 s |
| 20 | Đọc hai mẫu ABS bằng nhau cho từng trục; timeout 15 s |
| 30 | Chuyển đổi và kiểm tra Pr.12/Pr.13 |
| 40 | Preset Da.6/Md.20 bằng start No.9003; đọc lại Cd.3 phải bằng 9003 và kiểm tra lại M20/M21 cùng lỗi/busy QD75 đến `AxisMax` ngay trước khi bật M111..M114; timeout 2 s, settle 100 ms, verify Md.20 và phục hồi Cd.3. Lỗi sau khi ghi 9003 phục hồi Cd.3 trước state 90 |
| 50 | Ready: `M120=TRUE`, `PositionValid=TRUE` |
| 90 | Fault fail-closed: tắt xung start, phục hồi Cd.3 đã chụp nếu đã ghi 9003, xoá fresh/valid, không ghi QD75 thêm |

| AbsError | Ý nghĩa |
|---:|---|
| 901 | Liên động hoặc QD75 không sẵn sàng / xuất hiện lỗi |
| 902 | Lỗi truyền thông ABS, alarm servo, timeout hoặc hai mẫu không ổn định |
| 903 | Cấu hình chuyển đổi không hợp lệ |
| 904 | Tràn phép tính, Pr.12/13 không hợp lệ hoặc kết quả ngoài dải |
| 905 | Không nhận start-done/not-busy trong thời gian cho phép |
| 906 | Timeout verify hoặc Md.20 đọc lại khác giá trị yêu cầu |
| 908 | Frame, độ dài, ký tự hex hoặc checksum phản hồi không hợp lệ |

`ErrorCode` là mã chẩn đoán nội bộ của engine RS-422 trong lúc đang đọc. Khi engine idle (`bReadEnable=FALSE`, gồm `SyncState=50`), chương trình giữ `ErrorCode=0`; lỗi nghiệm thu chu kỳ chỉ đọc qua `AbsError`, `SyncState` và `M121`. Trạng thái PASS là `SyncState=50`, `AbsError=0`, `M120=1`, `M121=0`.

Fault cleanup: trong state 40, sau khi bước 1 đã chụp `wPrevStart` và ghi `9003`, mọi lỗi ở bước 2/3 (901, 905, 906) đều tắt `M111..M114`, ghi lại `Cd.3 := wPrevStart` bằng `TO` với `wBfm`, rồi mới sang state 90. Lỗi trước bước 1 (chưa ghi 9003) không restore.

## 4. Local label được dùng

Tất cả identifier local đều nằm trong `_scratch/20261125/work/labels_after_AxisMax_wBfm.csv`; bốn shadow được phép là `bM111`, `bM112`, `bM113`, `bM114`. Không dùng shadow `bY4..bY7`.

- Scheduler/config: `AbsAlarm`, `AbsAttempt`, `AbsError`, `AbsFresh`, `AbsPosition`, `AbsStation`, `AbsValid`, `AxisIndex`, `AxisMax`, `CfgIdx`, `CfgOfs`, `Confirm`, `FaultAxis`, `PrevM131`, `PrevSample`, `ReadReq`, `ReadTimeout`, `Startup3s`, `StartupFault`, `StartupReady`, `SyncState`, `SyncStep`, `SyncTimeoutQ`, `bAutoStart`, `bAxisBusy`, `bAxisFresh`, `bAxisStartDone`, `bConfigOk`, `bConvFault`, `bInit`, `bReadEnable`, `bResetPulse`.
- Timer: `AckTimer`, `AckTimerQ`, `ReadyTimer`, `ReadTimer`, `RetryTimer`, `StartupFaultTimer`, `StartupTimer`, `SyncTimer`, `WaitTimer`, cùng các cờ run/restart tương ứng đang dùng.
- Vị trí/chuyển đổi/QD75: `Position`, `Position1..4`, `PositionValid`, `PositionValid1..4`, `PrevReadReq`, `ReadSuccess`, `ServoAlarm`, `awCfg`, `diAbsPulse`, `diDelta`, `diHiS`, `diHome`, `diMag`, `diPrLower`, `diPrUpper`, `diQ`, `diQcalc`, `diRead`, `diScaleD`, `diScaleN`, `diTmp2`, `diZero`, `wBfm`, `wHiInt`, `wLoInt`, `wPrevStart`.
- RS-422: `Accum`, `Attempt`, `Busy`, `ByteIndex`, `Ch`, `ChecksumSum`, `Count`, `Elapsed`, `ErrorCode`, `Expected`, `LastError`, `Nibble`, `PreviousState`, `Raw`, `RetryElapsed`, `RetryExpired`, `RxControl`, `RxData`, `RxDone`, `RxFire`, `StartEdge`, `State`, `TimerExpired`, `TxControl`, `TxData`, `TxDone`, `TxFire`, `WaitPreset`, `iAxis`, `jLoop`.

## 5. Triển khai có kiểm soát

> Tài liệu này không cho phép tự thao tác GX Works2, MR Configurator2 hoặc PLC. Người có thẩm quyền thực hiện theo quy trình an toàn của máy.

1. Xác nhận backup cũ `PLC/Mitsubishi/_backup_20261127_pre_compact/ABS_Home.st`, md5 `bc773b4806b83452abdcd19c89d44346`, 1740 dòng.
2. Import/replace riêng POU `ABS_Home` và dùng đúng Local Label CSV nêu trên; không thêm label mới.
3. Xác nhận chỉ POU này sở hữu CH2 U8 và MAIN vẫn ánh xạ `M111..M114` tới start input tương ứng.
4. Rebuild offline: yêu cầu 0 error; kiểm tra không có literal `U0\G`, mọi QD75 `FROM/TO` đều qua `wBfm`, không có ghi Cd.19.
5. Trước khi triển khai thật: servo OFF, vùng làm việc an toàn, không có yêu cầu chuyển động, xác nhận origin đã được đặt đúng bằng MR Configurator2.
6. Việc download/verify/RUN chỉ do người vận hành được uỷ quyền thực hiện theo checklist của hệ thống.

## 6. Acceptance test

1. Servo OFF và bảo đảm không có chuyển động được phép.
2. Phát một xung `M131` để reset fault và chạy lại chu kỳ.
3. Chờ hoàn tất, yêu cầu đồng thời:
   - `SyncState = 50`;
   - `AbsError = 0`;
   - `M120 = TRUE`;
   - `M121 = FALSE`, `M122 = FALSE`;
   - `PositionValid = TRUE`;
   - `PositionValid1 = TRUE`, `PositionValid2 = TRUE`;
   - `PositionValid3 = FALSE`, `PositionValid4 = FALSE`.
4. Với servo vẫn OFF, quay tay J1 khoảng 1 độ; chạy lại bằng `M131` và xác nhận Md.20 trục J1 thay đổi phù hợp.
5. Lặp lại cho J2 khoảng 1 độ và xác nhận Md.20 trục J2 thay đổi.
6. Trong toàn bộ thử nghiệm không được có chuyển động. Không dùng PLC để set origin; origin chỉ set trong MR Configurator2.
7. Thử một lỗi có kiểm soát theo quy trình offline/commissioning: yêu cầu state 90, `M121=TRUE`, `M120=FALSE`, `PositionValid` và `PositionValid1..4` đều FALSE, không có lần ghi QD75 tiếp theo.

## 7. Rủi ro còn lại

- Chưa kiểm chứng trên PLC/phần cứng trong thay đổi offline này; offset BFM, tín hiệu start-done/not-busy và timing phải được xác nhận trong commissioning có kiểm soát.
- Không còn rủi ro Cd.3 bị giữ ở `9003` trên lỗi sau start: các nhánh lỗi state 40 phục hồi `wPrevStart` trước state 90. Trước khi bật `M111..M114`, chương trình đọc lại `Cd.3 = 9003` và kiểm tra lại `M20/M21` cùng lỗi/busy QD75 đến `AxisMax`; nếu sai hoặc interlock bật, chương trình phục hồi Cd.3 và fault mà không phát start.
- Chương trình không thể xác nhận tầng vật lý ngoài readback BFM; acceptance test vẫn phải xác nhận hành vi fail-closed và Cd.3 thực tế theo quy trình an toàn.

## TRIỂN KHAI THỰC TẾ 2026-11-28

- Đích quan sát và xác nhận trong GX Works2: CPU `Q06UDEH`, `Host 192.168.3.40`; sử dụng đúng project/workspace chuẩn đã liệt kê tại mục **ĐƯỜNG DẪN CHUẨN**.
- PLC đã được chuyển sang STOP có kiểm soát; GX Works2 báo `The operation(STOP) has been completed.`
- Phạm vi ghi duy nhất: `Program (Program File) -> MAIN1`, kích thước `16,584 bytes`. `MAIN`, Parameter, Device Memory, Global Device Comment, Symbolic Information, Boot File, Remote Password và các mục khác đều không được chọn.
- Kết quả ghi: `Program (MAIN1) Write : Completed`.
- Kết quả Verify: `MAIN1 -> MAIN1: Match`, `0 parts not matched`; không quan sát thấy khác biệt hoặc lỗi.
- Sau khi ghi và Verify thành công, PLC đã được chuyển lại RUN; GX Works2 báo `The operation(RUN) has been completed.`, các chỉ thị MODE và RUN hiển thị màu xanh.
- Không thực hiện Reset, force/Device Test, kích `M131`, hoặc kiểm thử chuyển động.
- Bằng chứng ảnh: `_scratch/20261128_main1_deploy/01_preflight.png`, `_scratch/20261128_main1_deploy/02_stopped.png`, `_scratch/20261128_main1_deploy/03_write_complete.png`, `_scratch/20261128_main1_deploy/04_verify.png`, `_scratch/20261128_main1_deploy/05_running.png`.
- Nguồn compact có `476` dòng, MD5 `aaf668d3998f495fa214f2e93af0756f`; kiểm tra embedded xác nhận normalized match và build hoàn tất với `Error: 0, Warning: 0`.
- Kết quả runtime của chu kỳ đồng bộ ABS vẫn **chưa được kiểm thử/giám sát**. Chưa có bằng chứng runtime để kết luận trạng thái `M120`, `AbsError` hoặc các tín hiệu acceptance khác.
