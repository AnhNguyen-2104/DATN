using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PlateVision50;

// Operator station overview: no hardware writes. All data comes from
// existing camera processing and read-only TwinCAT ADS polling.
public partial class MainWindow
{
    private DateTime _stationLastFrameUtc = DateTime.MinValue;
    private LineStatus? _lastSensorSnapshot;
    private readonly System.Collections.ObjectModel.ObservableCollection<string> _stationSensorEvents = new();

    private void SensorHistory(string message)
    {
        _stationSensorEvents.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  •  " + message);
        while (_stationSensorEvents.Count > 80)
            _stationSensorEvents.RemoveAt(_stationSensorEvents.Count - 1);
    }

    private void RecordSensorEdges(LineStatus? now)
    {
        var old = _lastSensorSnapshot;
        _lastSensorSnapshot = now;
        if (now is null)
        {
            if (old is not null) SensorHistory("ADS 851 MẤT KẾT NỐI / dữ liệu PLC chưa xác nhận");
            return;
        }
        if (old is null)
        {
            SensorHistory("ADS 851 LIVE / bắt đầu giám sát các tín hiệu hiện hữu");
            return;
        }
        void Edge(string label,bool oldValue,bool newValue)
        {
            if (oldValue!=newValue) SensorHistory(label + ": " + (newValue ? "ON" : "OFF"));
        }
        Edge("EStop AUX DI0 (không phải Safety)",old.EstopOk,now.EstopOk);
        Edge("STOP mềm PLC",old.GlobalStop,now.GlobalStop);
        Edge("PLC-Q TCP",old.PlcQConnected,now.PlcQConnected);
        Edge("Gantry Busy",old.GantryBusy,now.GantryBusy);
        Edge("Gantry Alarm",old.GantryError,now.GantryError);
        Edge("MD200 RUN DO",old.BeltRun,now.BeltRun);
        bool wasServo = old.SymbolPresence.TryGetValue("MAIN.bServo_Ready",out var oldOk) && oldOk;
        bool isServo = now.SymbolPresence.TryGetValue("MAIN.bServo_Ready",out var newOk) && newOk;
        if (wasServo && isServo) Edge("Servo Ready",old.ServoReady,now.ServoReady);
    }


    private void StationFrameReceived()
    {
        _stationLastFrameUtc = DateTime.UtcNow;
        UpdateStationOverviewVision();
    }

    private void InitStationOverview()
    {
        // The existing ObservableCollection updates the event log when a product
        // crosses the image trigger; no duplicated tracking process.
        OverviewGantryLog.ItemsSource = _auditRows;
        OverviewSensorLog.ItemsSource = _stationSensorEvents;
        UpdateStationOverviewVision();
        OverviewGlobalAuthority.Text = "CHƯA TÍCH HỢP LỆNH START / STOP TỔNG; E-STOP LÀ MẠCH CỨNG";
    }

    private void UpdateStationOverviewVision()
    {
        if (_closing) return;
        bool sourceFresh = Preview.Source != null && HasActiveSource
            && DateTime.UtcNow - _stationLastFrameUtc <= TimeSpan.FromSeconds(3);
        bool sourceVideo = _video != null;
        string source = sourceVideo ? "VIDEO TEST" :
            (_directHik?.IsOpen == true || _sessions.Count > 0) ? "CAMERA 1" : "CHƯA KẾT NỐI";
        string freshness = sourceFresh ? "FRAME LIVE" : "NO FRAME / STALE";
        OverviewCam1Quality.Text = source + " • " + freshness +
            (_trackingEnabled ? " • TRACKING" : " • DETECT OFF");
        OverviewCam1Empty.Visibility = sourceFresh ? Visibility.Collapsed : Visibility.Visible;

        // Camera coordinates belong to the most recently evaluated detection,
        // never to an encoder-predicted robot target or a fake SCARA source.
        bool validPose = sourceFresh && _trackingEnabled && _last.Found &&
            double.IsFinite(_lastBelt.Xmm) && double.IsFinite(_lastBelt.Ymm) &&
            double.IsFinite(_last.AngleDeg);
        string track = _overlayTrackIds.Count > 0
            ? string.Join(", ", _overlayTrackIds.Values.Distinct().OrderBy(x => x).Take(6))
            : "--";
        OverviewCam1Track.Text = validPose ? "Track ID: " + track : "Track ID: --";
        OverviewCam1Pose.Text = validPose
            ? $"X {_lastBelt.Xmm:F1}  Y {_lastBelt.Ymm:F1}  θ {_last.AngleDeg:F1}°"
            : "X --  Y --  θ --";
        StationCam1Status.Text = source + " | " + freshness + (_trackingEnabled ? " | DETECT ON" : " | DETECT OFF")
            + (sourceVideo ? " | Không có encoder đồng bộ" : "");
        StationCam1Coords.Text = validPose
            ? $"Track {track} | X {_lastBelt.Xmm:F2} mm | Y {_lastBelt.Ymm:F2} mm | θ {_last.AngleDeg:F2}°"
            : "Track -- | X -- / Y -- / θ --";
        if (_events.Count > 0)
            OverviewRecentEvent.Text = "CAMERA 1 / " + _events[0];
    }

    private void UpdateStationOverviewPlc(LineStatus? status)
    {
        RecordSensorEdges(status);
        if (status is null)
        {
            OverviewSensors.Text = "ADS OFFLINE / không có phản hồi cảm biến. Không xác nhận máy an toàn.";
            OverviewGantrySensors.Text = "PLC-Q / Gantry / Encoder: OFFLINE";
            OverviewGlobalAuthority.Text = "ADS OFFLINE · START/STOP TỔNG KHÔNG ĐƯỢC LIÊN KẾT PLC";
            UpdateStationOverviewVision();
            return;
        }
        string BoolState(bool v) => v ? "ON" : "OFF";
        string Quality(string tag) => status.SymbolPresence.TryGetValue(tag, out var q) && !q ? "READ_FAIL" : BoolState(true);
        string servo = Quality("MAIN.bServo_Ready") == "READ_FAIL" ? "READ_FAIL" : BoolState(status.ServoReady);
        OverviewSensors.Text =
            $"E-STOP AUX DI0: {BoolState(status.EstopOk)}  |  TwinCAT STOP REQUEST: {BoolState(status.GlobalStop)}  |  " +
            $"SERVO READY: {servo}  |  MD200 RUN DO: {BoolState(status.BeltRun)}  |  ENCODER: {status.BeltSpeed:F2} mm/s";
        OverviewGantrySensors.Text =
            $"PLC-Q TCP: {(status.PlcQConnected ? "CONNECTED" : "DISCONNECTED")}  |  " +
            $"VISION BRIDGE: {BoolState(status.VisionBridgeVerified)}  |  GANTRY BUSY: {BoolState(status.GantryBusy)}" +
            $"  |  ALARM: {BoolState(status.GantryError)}";
        OverviewGlobalAuthority.Text = status.EstopOk && !status.GlobalStop
            ? "ADS CÓ DỮ LIỆU · LỆNH START/STOP TỔNG CHƯA TÍCH HỢP; E-STOP PHẢI LÀ MẠCH CỨNG"
            : "PLC BÁO DỪNG / E-STOP AUX KHÔNG OK · TUYỆT ĐỐI KHÔNG CHẠY MÁY";
        UpdateStationOverviewVision();
    }

    private void DashGlobalCommand_Click(object sender, RoutedEventArgs e)
    {
        var action = (sender as Button)?.Tag?.ToString() ?? "UNKNOWN";
        string details = action switch
        {
            "START" => "START TỔNG chưa được kết nối đến TwinCAT. Cần tag Cmd + Ack, kiểm tra chế độ Auto, tất cả interlock Gantry, SCARA, băng tải và mạch E-Stop được nghiệm thu. KHÔNG có lệnh nào được gửi.",
            "STOP" => "STOP TỔNG phải có hợp đồng dừng tuần tự, hủy các tác vụ robot và băng tải ở PLC. Hiện chưa có tag lệnh tổng đã nghiệm thu. KHÔNG có lệnh nào được gửi.",
            "ESTOP" => "ĐÂY KHÔNG PHẢI NÚT E-STOP THẬT. Khi nguy hiểm, dùng nút E-Stop VẬT LÝ trên tủ máy. Phần mềm chỉ giám sát phản hồi DI0 của mạch phụ trợ, không cắt nguồn nguy hiểm.",
            _ => "Chưa có lệnh."
        };
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        OverviewRecentEvent.Text = $"{stamp} · YÊU CẦU HMI {action}: KHÔNG GỬI PLC";
        MessageBox.Show(details, "DATN / Trạng thái lệnh hệ thống",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DashOpenGantry_Click(object sender, RoutedEventArgs e) =>
        MainNavigation.SelectedItem = GantryDetailTab;
    private void DashOpenScara_Click(object sender, RoutedEventArgs e) =>
        MainNavigation.SelectedItem = ScaraDetailTab;
    private void DashOpenConveyor_Click(object sender, RoutedEventArgs e) =>
        MainNavigation.SelectedItem = ConveyorDetailTab;

    private void StationVisionStart_Click(object sender, RoutedEventArgs e)
    {
        // Existing image-only handler disarms guarded ADS benchmark writes.
        StartTracking_Click(sender, e);
        UpdateStationOverviewVision();
    }

    private void StationVisionStop_Click(object sender, RoutedEventArgs e)
    {
        StopTracking_Click(sender, e);
        UpdateStationOverviewVision();
    }
}
