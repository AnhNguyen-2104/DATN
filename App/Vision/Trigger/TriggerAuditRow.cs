using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlateVision50.Trigger;

/// <summary>One midline event, with separate image-event and ADS-delivery states.</summary>
public sealed class TriggerAuditRow : INotifyPropertyChanged
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Time => Timestamp.ToString("HH:mm:ss.fff");
    public string Source { get; init; } = "";
    public int TrackId { get; init; }
    public double PreviousPx { get; init; }
    public double CurrentPx { get; init; }
    public double MidlinePx { get; init; }
    public string Crossing => $"{PreviousPx:F1} → {CurrentPx:F1} / {MidlinePx:F1}";
    public double Xmm { get; init; }
    public double Ymm { get; init; }
    public double Theta { get; init; }
    public string Position => $"{Xmm:F2} / {Ymm:F2} / {Theta:F2}°";
    private string _delivery = "DETECTED";
    private string _plc = "CHƯA GỬI";
    public string Delivery { get => _delivery; set { _delivery = value; Changed(); } }
    public string Plc { get => _plc; set { _plc = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
