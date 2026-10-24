using NVKProject.DotNet.BaslerCamera;
using OpenCvSharp;
using System.Windows.Media.Imaging;
using BaslerTransport = NVKProject.DotNet.BaslerCamera.CameraTransportType;

namespace WpfYolo_DotNet.Devices;

/// <summary>
/// Merges HIK (GigE/USB) and Basler (GigE/USB3) into one scan/connect flow.
/// </summary>
public sealed class UnifiedCameraService : IDisposable
{
    private readonly HikCameraService _hik = new();
    private BaslerCameraService? _basler;
    private CameraVendor? _activeVendor;

    public List<CameraInfo> CameraList { get; } = new();
    public event Action<BitmapSource>? FrameReceived;

    public bool IsBaslerAvailable => _basler != null;

    public UnifiedCameraService()
    {
        PylonNativeLoader.Ensure();

        _hik.FrameReceived += frame => FrameReceived?.Invoke(frame);

        try
        {
            _basler = new BaslerCameraService();
            _basler.FrameReceived += frame => FrameReceived?.Invoke(frame);
        }
        catch
        {
            // Basler optional when pylon runtime is unavailable.
        }
    }

    public bool HasFrame => _activeVendor switch
    {
        CameraVendor.Hikvision => _hik.HasFrame,
        CameraVendor.Basler => _basler?.HasFrame ?? false,
        _ => false
    };

    public Mat? CloneLatestFrame() => _activeVendor switch
    {
        CameraVendor.Hikvision => _hik.CloneLatestFrame(),
        CameraVendor.Basler => _basler?.CloneLatestFrame(),
        _ => null
    };

    public (int Width, int Height)? GetFrameSize() => _activeVendor switch
    {
        CameraVendor.Hikvision => _hik.GetFrameSize(),
        CameraVendor.Basler => _basler?.GetFrameSize(),
        _ => null
    };

    public bool Enumerate(CameraTransportType transport)
    {
        CameraList.Clear();

        _hik.Enumerate(transport);
        foreach (var cam in _hik.CameraList)
        {
            CameraList.Add(new CameraInfo
            {
                Index = CameraList.Count,
                BackendIndex = cam.BackendIndex,
                Vendor = cam.Vendor,
                Transport = cam.Transport,
                Name = cam.Name,
                SerialNumber = cam.SerialNumber
            });
        }

        if (_basler != null)
            _basler.Enumerate(ToBaslerTransport(transport));

        foreach (var cam in _basler?.CameraList ?? [])
        {
            CameraList.Add(new CameraInfo
            {
                Index = CameraList.Count,
                BackendIndex = cam.DeviceListIndex,
                Vendor = CameraVendor.Basler,
                Transport = cam.Transport == BaslerTransport.Usb
                    ? CameraTransportType.Usb
                    : CameraTransportType.GigE,
                Name = cam.Name,
                SerialNumber = cam.SerialNumber
            });
        }

        return CameraList.Count > 0;
    }

    public bool Open(CameraInfo camInfo)
    {
        if (camInfo == null) return false;

        CloseActiveOnly();

        var opened = camInfo.Vendor switch
        {
            CameraVendor.Basler => OpenBasler(camInfo),
            _ => _hik.Open(camInfo)
        };

        if (opened)
            _activeVendor = camInfo.Vendor;

        return opened;
    }

    public void Start()
    {
        switch (_activeVendor)
        {
            case CameraVendor.Hikvision:
                _hik.Start();
                break;
            case CameraVendor.Basler:
                _basler?.Start();
                break;
        }
    }

    public void Stop()
    {
        switch (_activeVendor)
        {
            case CameraVendor.Hikvision:
                _hik.Stop();
                break;
            case CameraVendor.Basler:
                _basler?.Stop();
                break;
        }
    }

    public void Close()
    {
        _hik.Stop();
        _basler?.Stop();
        _hik.Close();
        _basler?.Close();
        _activeVendor = null;
    }

    public void Dispose()
    {
        Close();
        _hik.Dispose();
        _basler?.Dispose();
    }

    private bool OpenBasler(CameraInfo camInfo)
    {
        if (_basler == null) return false;

        var baslerInfo = _basler.CameraList.FirstOrDefault(c => c.DeviceListIndex == camInfo.BackendIndex);
        if (baslerInfo == null)
        {
            baslerInfo = new BaslerCameraDeviceInfo
            {
                DeviceListIndex = camInfo.BackendIndex,
                SerialNumber = camInfo.SerialNumber,
                Name = camInfo.Name,
                Transport = camInfo.Transport == CameraTransportType.Usb
                    ? BaslerTransport.Usb
                    : BaslerTransport.GigE
            };
        }

        return _basler.Open(baslerInfo);
    }

    private void CloseActiveOnly()
    {
        _hik.Stop();
        _basler?.Stop();
        _hik.Close();
        _basler?.Close();
        _activeVendor = null;
    }

    private static BaslerTransport ToBaslerTransport(CameraTransportType transport) =>
        transport == CameraTransportType.Usb ? BaslerTransport.Usb : BaslerTransport.GigE;
}
