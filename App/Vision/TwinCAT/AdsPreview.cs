using TwinCAT.Ads;
namespace PlateVision50.TwinCAT;
public sealed class AdsPreview
{
    // Writes only three camera center/angle LREAL data symbols upon explicit operator action.
    // Does NOT trigger camera exposure, tracking, M1000, gantry or glue valve.
    public string Check()
    {
        using var ads=new AdsClient();
        ads.Connect(851);
        var state=ads.ReadState();
        return "ADS port 851: "+state.AdsState;
    }
    [Obsolete("Unsafe direct ADS writes are disabled. Use the guarded FIFO BENCH bridge.")]
    public string WritePose(double x,double y,double theta)
        =>throw new NotSupportedException(
            "ADS direct pose write disabled: use supervised, interlocked TwinCAT FIFO BENCH.");
}
