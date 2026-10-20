namespace WpfYolo_DotNet.Devices;

using OpenCvSharp;
using System.Windows.Media.Imaging;

public interface ICameraService : IDisposable
{
    event Action<BitmapSource> FrameReceived;
    event Action<Mat> FrameReceived_;
    bool OpenFirstDevice();
    bool StartGrabbing();
    void StopGrabbing();
    void Close();
    bool IsOpened { get; }
    bool IsGrabbing { get; }
    Mat mat { get; set; }
}
