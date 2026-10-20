using System;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TwinCAT.Ads;

namespace PlateVision50;

public sealed record TagValueRow(string Group,string Tag,string Value,string Quality,string Access,string Timestamp);

internal sealed class LineStatus
{
    public DateTime ReceivedUtc { get; init; }
    public IReadOnlyDictionary<string,bool> SymbolPresence { get; init; } = new Dictionary<string,bool>();
    public bool EstopOk { get; init; }
    public bool SystemEStopOk { get; init; }
    public bool ServoReady { get; init; }
    public bool GlobalStop { get; init; }
    public bool PlcQConnected { get; init; }
    public bool GantryBusy { get; init; }
    public bool GantryDone { get; init; }
    public bool GantryError { get; init; }
    public short GantryVertex { get; init; }
    public short GantryErrorCode { get; init; }
    public bool GantryPublisherEnabled { get; init; }
    public bool VisionBridgeVerified { get; init; }
    public short QueueCount { get; init; }
    public uint QueueHead { get; init; }
    public uint Accepted { get; init; }
    public uint Missed { get; init; }
    public double BeltSpeed { get; init; }
    public double BeltDistance { get; init; }
    public bool BeltRun { get; init; }
    public bool BeltRequest { get; init; }
    public double HzSet { get; init; }
    public double HzActualReference { get; init; }
    public double AoVoltsReference { get; init; }
    public ushort AoRaw { get; init; }
}

internal static class LineAds
{
    private static T Read<T>(AdsClient c, string name)
    {
        var h=c.CreateVariableHandle(name);
        try { return (T)c.ReadAny(h,typeof(T)); }
        finally { c.DeleteVariableHandle(h); }
    }
    private static void Write<T>(AdsClient c, string name, T value)
    {
        var h=c.CreateVariableHandle(name);
        try { c.WriteAny(h,value!); }
        finally { c.DeleteVariableHandle(h); }
    }
    private static T Optional<T>(AdsClient c, string name, Dictionary<string,bool> presence,T fallback=default!)
    {
        try { var value=Read<T>(c,name); presence[name]=true; return value; }
        catch { presence[name]=false; return fallback; }
    }
    public static LineStatus Poll()
    {
        using var ads=new AdsClient();
        ads.Connect(851);
        if(ads.ReadState().AdsState!=AdsState.Run)
            throw new InvalidOperationException("TwinCAT ADS 851 không ở RUN.");
        // Global stop and conveyor IO are REQUIRED: never infer healthy on failure.
        var presence=new Dictionary<string,bool>(StringComparer.Ordinal);
        return new LineStatus {
            SymbolPresence=presence,
            ReceivedUtc=DateTime.UtcNow,
            EstopOk=Read<bool>(ads,"GVL_MD200_IO.bEStopFeedback"),
            GlobalStop=Read<bool>(ads,"MAIN.bSystemStopRequested"),
            SystemEStopOk=Optional<bool>(ads,"MAIN.bSystemEStopOK",presence),
            ServoReady=Optional<bool>(ads,"MAIN.bServo_Ready",presence),
            PlcQConnected=Optional<bool>(ads,"MAIN.bPlcQConnected",presence),
            GantryBusy=Optional<bool>(ads,"MAIN.bGlueCycleBusy",presence),
            GantryDone=Optional<bool>(ads,"MAIN.bGlueCycleDone",presence),
            GantryError=Optional<bool>(ads,"MAIN.bGlueCycleError",presence),
            GantryVertex=Optional<short>(ads,"MAIN.nGlueCurVertex",presence),
            GantryErrorCode=Optional<short>(ads,"MAIN.nGlueErrorCode",presence),
            GantryPublisherEnabled=Optional<bool>(ads,"MAIN.bLegacyMotionPublisherAllow",presence),
            VisionBridgeVerified=Optional<bool>(ads,"MAIN.bVisionFeedVerified",presence),
            QueueCount=Optional<short>(ads,"MAIN.nQueueCount",presence),
            QueueHead=Optional<uint>(ads,"MAIN.nQueueHeadProductID",presence),
            Accepted=Optional<uint>(ads,"MAIN.nQueueAccepted",presence),
            Missed=Optional<uint>(ads,"MAIN.nQueueMissed",presence),
            BeltSpeed=Optional<double>(ads,"MAIN.rConveyorSpeed",presence),
            BeltDistance=Optional<double>(ads,"MAIN.rConveyorDist",presence),
            BeltRun=Read<bool>(ads,"GVL_MD200_IO.bRUN_DO"),
            BeltRequest=Read<bool>(ads,"MAIN.bMD200_Start"),
            HzSet=Read<double>(ads,"MAIN.rMD200_SetFrequency_Hz"),
            HzActualReference=Read<double>(ads,"MAIN.rMD200_Ramped_Hz"),
            AoVoltsReference=Read<double>(ads,"MAIN.rMD200_AO_VoltsVirtual"),
            AoRaw=Read<ushort>(ads,"GVL_MD200_IO.nAO1_Raw")
        };
    }

    public static void StartBelt(double hz)
    {
        if(!double.IsFinite(hz) || hz<=0 || hz>25)
            throw new ArgumentOutOfRangeException(nameof(hz),"Tần số phải lớn hơn 0 và không quá 25 Hz.");
        using var ads=new AdsClient();
        ads.Connect(851);
        if(ads.ReadState().AdsState!=AdsState.Run)throw new InvalidOperationException("PLC không RUN.");
        if(!Read<bool>(ads,"GVL_MD200_IO.bEStopFeedback")
           || Read<bool>(ads,"MAIN.bSystemStopRequested"))
            throw new InvalidOperationException("E-Stop tổng không OK. Không thể START.");
        if(Read<bool>(ads,"MAIN.bGlueCycleBusy"))
            throw new InvalidOperationException("Gantry đang chạy. Không cho START băng tải từ dashboard trong phiên test.");
        if(Read<bool>(ads,"MAIN.bMD200_Start"))
            throw new InvalidOperationException("Băng tải đang có yêu cầu START. Hãy STOP trước.");
        // Direct HMI command ONLY for explicitly approved MD200 setpoint and start tags.
        // NEVER write EStop flags, motion publisher, M1000, QD75 or camera FIFO.
        Write(ads,"MAIN.rMD200_SetFrequency_Hz",hz);
        if(Math.Abs(Read<double>(ads,"MAIN.rMD200_SetFrequency_Hz")-hz)>0.000001)
            throw new InvalidOperationException("PLC chưa nhận Hz mới, không bật START.");
        if(!Read<bool>(ads,"GVL_MD200_IO.bEStopFeedback")
           || Read<bool>(ads,"MAIN.bSystemStopRequested"))
            throw new InvalidOperationException("E-Stop đổi trạng thái. START bị hủy.");
        try
        {
            Write(ads,"MAIN.bMD200_Start",true);
            if(!Read<bool>(ads,"MAIN.bMD200_Start"))
                throw new InvalidOperationException("PLC từ chối START hoặc E-Stop đang tác động.");
        }
        catch
        {
            // A write-ACK/readback failure can leave the physical start bit high.
            // Request STOP best-effort; operator must verify actual machine state.
            try { Write(ads,"MAIN.bMD200_Start",false); } catch { }
            throw;
        }
    }
    public static void StopBelt()
    {
        using var ads=new AdsClient();
        ads.Connect(851);
        if(ads.ReadState().AdsState!=AdsState.Run)throw new InvalidOperationException("PLC không RUN.");
        Write(ads,"MAIN.bMD200_Start",false);
        if(Read<bool>(ads,"MAIN.bMD200_Start"))
            throw new InvalidOperationException("PLC chưa xác nhận STOP.");
    }
}

public partial class MainWindow
{
    private readonly DispatcherTimer _dashTimer=new() {Interval=TimeSpan.FromMilliseconds(1000)};
    private bool _dashPolling;
    private bool _dashControlArmed;
    private LineStatus? _dashStatus;

    private static SolidColorBrush DashBrush(string code) =>
        new((Color)ColorConverter.ConvertFromString(code));

    private void InitDashboard()
    {
        _dashControlArmed=false;
        DashStartButton.IsEnabled=false;
        DashStopButton.IsEnabled=false;
        DashControlArm.IsChecked=false;
        _dashTimer.Tick+=DashTimer_Tick;
        _dashTimer.Start();
        Closed+=(_,_)=>_dashTimer.Stop();
        _=RefreshDashboardAsync();
    }
    private async void DashTimer_Tick(object? sender,EventArgs e) => await RefreshDashboardAsync();

    private async Task RefreshDashboardAsync()
    {
        if(_dashPolling || _closing)return;
        _dashPolling=true;
        try {
            var s=await Task.Run(LineAds.Poll);
            if(_closing)return;
            _dashStatus=s;
            UpdateCellViews(s);
            UpdateStationOverviewPlc(s);
            DashUpdated.Text="ADS / "+s.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss")+
                "  |  Cập nhật 1 giây";
            DashPlc.Text="ONLINE";
            DashPlc.Foreground=DashBrush("#71DFC4");
            DashEStop.Text=s.EstopOk && !s.GlobalStop?"DI OK (aux)":"DỪNG / LỖI";
            DashEStop.Foreground=DashBrush(s.EstopOk && !s.GlobalStop?"#71DFC4":"#FF978C");
            DashSpeed.Text=$"{s.BeltSpeed:F1} mm/s";
            DashFrequency.Text=$"Tần số đặt: {s.HzSet:F1} Hz";
            DashQueue.Text=$"{s.QueueCount} / 8";
            DashQueueInfo.Text=s.QueueCount>0?$"Sản phẩm #{s.QueueHead}":"Không có sản phẩm";
            DashBeltState.Text=s.BeltRun?"RUN YÊU CẦU":"STOP";
            DashBeltState.Foreground=DashBrush(s.BeltRun?"#71DFC4":"#EAC17E");
            DashDistance.Text=$"{s.BeltDistance:F1} mm";
            DashBeltMmS.Text=$"{s.BeltSpeed:F2} mm/s";
            DashHzReal.Text=$"{s.HzActualReference:F1} Hz";
            DashAnalog.Text=$"{s.AoVoltsReference:F2} V (mã {s.AoRaw})";
            DashGantryPlc.Text="Mitsubishi Q: "+(s.PlcQConnected?"TCP ONLINE":"MẤT KẾT NỐI");
            DashGantryState.Text=s.GantryError?"ROBOT: LỖI, KHÔNG TIẾP TỤC"
                :s.GantryBusy?"ROBOT: ĐANG THỰC HIỆN CHU TRÌNH"
                :s.GantryPublisherEnabled?"PUBLISHER MỞ / CHƯA NGHIỆM THU":"ROBOT: CHƯA CHO PHÉP AUTO";
            DashGantryVertex.Text=$"Đỉnh hiện tại: {s.GantryVertex} / 4 | Lỗi: {s.GantryErrorCode}";
            DashGantryStats.Text=$"FIFO: {s.QueueCount}  |  Nhận: {s.Accepted}  |  Bỏ lỡ: {s.Missed}";
            DashCamera1.Text=(_directHik?.IsOpen==true||_sessions.Count>0?"Camera 1: LIVE":"Camera 1: "+(_video!=null?"VIDEO DEMO":"CHƯA KẾT NỐI"))
                 +(_trackingEnabled?" | DETECT ON":" | DETECT OFF");
            // Camera connection ≠ validated camera-to-PLC calibration.
            if(s.GlobalStop || !s.EstopOk)
                DashMachineMessage.Text="DỪNG TỔNG ĐƯỢC YÊU CẦU · KHÔNG CHO PHÉP START THIẾT BỊ";
            else if(!s.PlcQConnected)
                DashMachineMessage.Text="DI E-Stop AUX OK · PLC Mitsubishi chưa kết nối · Gantry bị khóa";
            else
                DashMachineMessage.Text="DI E-Stop AUX OK · TwinCAT RUN · Gantry vẫn cần nghiệm thu riêng trước khi Auto";
            // Normal STOP is available even if command-arm switch is not selected.
            DashStopButton.IsEnabled=true;
            DashStartButton.IsEnabled=_dashControlArmed && s.EstopOk && !s.GlobalStop
                && !s.GantryBusy && !s.BeltRequest;
        } catch(Exception ex) {
            _dashStatus=null;
            UpdateStationOverviewPlc(null);
            TagMatrixTable.ItemsSource=null;
            CellGantryQuality.Text="OFFLINE / không có dữ liệu PLC";
            CellConveyorQuality.Text="OFFLINE / không có dữ liệu PLC";
            CellGantryLink.Text="Mitsubishi Q: KHÔNG XÁC NHẬN";
            CellConveyorRun.Text="MD200 RUN DO: KHÔNG XÁC NHẬN";
            CellConveyorSpeed.Text="OFFLINE / không có dữ liệu encoder";
            CellConveyorDistance.Text="OFFLINE / không có dữ liệu encoder";
            CellConveyorHz.Text="OFFLINE / không có dữ liệu MD200";
            CellConveyorAo.Text="OFFLINE / không có dữ liệu AO";
            CellGantryCycle.Text="OFFLINE / chu trình không xác nhận";
            CellGantryQueue.Text="OFFLINE / FIFO không xác nhận";
            CellGantryVision.Text="OFFLINE / PLC bridge không xác nhận";
            DashSpeed.Text="-- / STALE";
            DashDistance.Text="-- / STALE";
            DashBeltMmS.Text="-- / STALE";
            DashHzReal.Text="-- / STALE";
            DashAnalog.Text="-- / STALE";
            DashQueue.Text="-- / STALE";
            DashGantryState.Text="PLC OFFLINE / GANTRY KHÔNG XÁC NHẬN";
            DashUpdated.Text="ADS OFFLINE / "+DateTime.Now.ToString("HH:mm:ss");
            DashPlc.Text="MẤT KẾT NỐI";
            DashPlc.Foreground=DashBrush("#FF978C");
            DashEStop.Text="KHÔNG XÁC NHẬN";
            DashEStop.Foreground=DashBrush("#FF978C");
            DashMachineMessage.Text="MẤT DỮ LIỆU TWINCAT: "+ex.Message;
            DashStartButton.IsEnabled=false;
            DashStopButton.IsEnabled=false;
        } finally {_dashPolling=false;}
    }


    private void UpdateCellViews(LineStatus s)
    {
        var time=s.ReceivedUtc.ToLocalTime().ToString("HH:mm:ss");
        string Q(string tag) => s.SymbolPresence.TryGetValue(tag,out var ok) && !ok ? "READ_FAIL" : "LIVE";
        string V(bool v) => v ? "TRUE" : "FALSE";
        CellGantryQuality.Text=$"ADS LIVE / {time} - chỉ giám sát, Gantry chưa cho phép lệnh từ HMI";
        CellGantryLink.Text="Mitsubishi PLC-Q: "+(s.PlcQConnected?"KẾT NỐI":"CHƯA KẾT NỐI");
        CellGantryCycle.Text=$"Chu trình: {(s.GantryError?"LỖI":s.GantryBusy?"ĐANG XỬ LÝ":s.GantryDone?"ĐÃ HOÀN TẤT":"IDLE")} | Vertex {s.GantryVertex}/4 | Error {s.GantryErrorCode}";
        CellGantryQueue.Text=$"FIFO {s.QueueCount}/8 | Head ID {s.QueueHead} | Accepted {s.Accepted} | Missed {s.Missed}";
        CellGantryVision.Text="Camera 1: "+(_directHik?.IsOpen==true||_sessions.Count>0?"ĐANG NHẬN ẢNH":_video!=null?"VIDEO TEST":"CHƯA KẾT NỐI")
            +" | Bridge verified "+V(s.VisionBridgeVerified);
        CellConveyorQuality.Text=$"ADS LIVE / {time} | Encoder chung cho hai robot";
        CellConveyorRun.Text="ST-2318 DO RUN="+V(s.BeltRun)+" | MD200 START request="+V(s.BeltRequest);
        CellConveyorSpeed.Text=$"Encoder velocity: {s.BeltSpeed:F2} mm/s";
        CellConveyorDistance.Text=$"Encoder distance: {s.BeltDistance:F2} mm";
        CellConveyorHz.Text=$"MD200: set {s.HzSet:F2} Hz | ramped {s.HzActualReference:F2} Hz (không phải Hz đo)";
        CellConveyorAo.Text=$"ST-4424 AO raw {s.AoRaw} | volts virtual {s.AoVoltsReference:F2} V (không đo thực)";
        TagValueRow T(string group,string tag,string val,string access="R")
            => new(group,tag,val,Q(tag),access,time);
        TagMatrixTable.ItemsSource=new[] {
            T("TỔNG","GVL_MD200_IO.bEStopFeedback",V(s.EstopOk)),
            T("TỔNG","MAIN.bSystemStopRequested",V(s.GlobalStop)),
            T("TỔNG","MAIN.bSystemEStopOK",V(s.SystemEStopOk)),
            T("TỔNG","MAIN.bServo_Ready",V(s.ServoReady)),
            T("TỔNG","MAIN.bPlcQConnected",V(s.PlcQConnected)),
            T("BĂNG TẢI","MAIN.rConveyorSpeed",$"{s.BeltSpeed:F2} mm/s"),
            T("BĂNG TẢI","MAIN.rConveyorDist",$"{s.BeltDistance:F2} mm"),
            T("BĂNG TẢI","MAIN.bMD200_Start",V(s.BeltRequest),"R/W*"),
            T("BĂNG TẢI","MAIN.rMD200_SetFrequency_Hz",$"{s.HzSet:F2} Hz","R/W*"),
            T("BĂNG TẢI","MAIN.rMD200_Ramped_Hz",$"{s.HzActualReference:F2} Hz"),
            T("BĂNG TẢI","MAIN.rMD200_AO_VoltsVirtual",$"{s.AoVoltsReference:F2} V calc"),
            T("BĂNG TẢI","GVL_MD200_IO.nAO1_Raw",s.AoRaw.ToString()),
            T("BĂNG TẢI","GVL_MD200_IO.bRUN_DO",V(s.BeltRun)),
            T("GANTRY","MAIN.bVisionFeedVerified",V(s.VisionBridgeVerified)),
            T("GANTRY","MAIN.nQueueCount",s.QueueCount.ToString()),
            T("GANTRY","MAIN.nQueueHeadProductID",s.QueueHead.ToString()),
            T("GANTRY","MAIN.nQueueAccepted",s.Accepted.ToString()),
            T("GANTRY","MAIN.nQueueMissed",s.Missed.ToString()),
            T("GANTRY","MAIN.bLegacyMotionPublisherAllow",V(s.GantryPublisherEnabled)),
            T("GANTRY","MAIN.bGlueCycleBusy",V(s.GantryBusy)),
            T("GANTRY","MAIN.bGlueCycleDone",V(s.GantryDone)),
            T("GANTRY","MAIN.bGlueCycleError",V(s.GantryError)),
            T("GANTRY","MAIN.nGlueCurVertex",s.GantryVertex.ToString()),
            T("GANTRY","MAIN.nGlueErrorCode",s.GantryErrorCode.ToString()),
            new("SCARA","GVL_SCARA.*","--","RESERVED","--","--"),
            new("CAMERA 2","GVL_CAMERA2.*","--","RESERVED","--","--")
        };
    }

    private void DashCamera_Click(object sender,RoutedEventArgs e) => MainNavigation.SelectedIndex=1;
    private void DashPLC_Click(object sender,RoutedEventArgs e)
    {
        for(int i=0;i<MainNavigation.Items.Count;i++)
            if(MainNavigation.Items[i] is TabItem t && (t.Header?.ToString()?.Contains("PLC")??false))
            {MainNavigation.SelectedIndex=i;return;}
    }

    private void DashArmChanged(object sender,RoutedEventArgs e)
    {
        if(!IsLoaded)return;
        if(DashControlArm.IsChecked==true)
        {
            var ans=MessageBox.Show(
                "Chỉ mở lệnh HMI khi E-Stop PHẦN CỨNG và băng tải đã được nghiệm thu.\n"+
                "Kích START có thể làm MD200 chuyển động thật. Lệnh STOP ở đây KHÔNG thay thế E-Stop.\n\n"+
                "Bạn đang trực tiếp giám sát máy và muốn mở quyền điều khiển băng tải trong phiên này?",
                "Xác nhận vận hành / DATN",MessageBoxButton.YesNo,MessageBoxImage.Warning);
            if(ans!=MessageBoxResult.Yes){DashControlArm.IsChecked=false;return;}
            _dashControlArmed=true;
            DashControlNote.Text="QUYỀN LỆNH BĂNG TẢI ĐÃ MỞ CHO PHIÊN NÀY. Gantry/SCARA vẫn không cấp lệnh từ HMI.";
        }
        else {
            _dashControlArmed=false;
            DashStartButton.IsEnabled=false;
            DashControlNote.Text="Lệnh HMI khóa. STOP thường khả dụng khi ADS có kết nối; E-Stop phải là mạch cứng.";
        }
        UpdateControlSafetyBanner();
    }

    private async void DashStart_Click(object sender,RoutedEventArgs e)
    {
        if(!_dashControlArmed || _dashStatus is null || !_dashStatus.EstopOk
            || _dashStatus.GlobalStop || _dashStatus.GantryBusy)return;
        if(!double.TryParse(DashHzInput.Text.Trim(),NumberStyles.Float,
                CultureInfo.InvariantCulture,out var hz) &&
           !double.TryParse(DashHzInput.Text.Trim(),NumberStyles.Float,
                CultureInfo.CurrentCulture,out hz))
        {MessageBox.Show("Nhập số Hz hợp lệ (ví dụ 5.0).");return;}
        if(hz<=0 || hz>25){MessageBox.Show("Giá trị cho phép: lớn hơn 0 đến 25 Hz.");return;}
        if(MessageBox.Show($"Xác nhận START BĂNG TẢI THẬT ở {hz:F1} Hz?\n"+
                "Kiểm tra người, sản phẩm, E-Stop và cơ cấu trước khi tiếp tục.",
                "LỆNH CHUYỂN ĐỘNG / DATN",MessageBoxButton.YesNo,MessageBoxImage.Warning)
                !=MessageBoxResult.Yes)return;
        DashStartButton.IsEnabled=false;
        try{
            await Task.Run(()=>LineAds.StartBelt(hz));
            DashControlNote.Text=$"Đã gửi START {hz:F1}Hz đến PLC lúc {DateTime.Now:HH:mm:ss}; theo dõi phản hồi thực tế.";
        } catch(Exception ex) {
            DashControlNote.Text="START KHÔNG XÁC NHẬN: "+ex.Message;
            MessageBox.Show(ex.Message,"DATN: lệnh START bị từ chối",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        await RefreshDashboardAsync();
    }

    private async void DashStop_Click(object sender,RoutedEventArgs e)
    {
        DashStopButton.IsEnabled=false;
        try{
            await Task.Run(LineAds.StopBelt);
            DashControlNote.Text=$"Đã gửi STOP thường tới PLC lúc {DateTime.Now:HH:mm:ss}; cần xác nhận băng tải đứng yên.";
        }catch(Exception ex){
            DashControlNote.Text="STOP KHÔNG XÁC NHẬN: "+ex.Message+" · Sử dụng E-Stop PHẦN CỨNG khi nguy hiểm.";
            MessageBox.Show(ex.Message,"DATN: STOP chưa xác nhận",MessageBoxButton.OK,MessageBoxImage.Error);
        }
        await RefreshDashboardAsync();
    }
}
