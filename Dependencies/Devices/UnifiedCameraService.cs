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

    public string LastError { get; private set; } = string.Empty;
    public string HikLastError => _hik.LastError;
    public string BaslerLastError => _basler?.LastError ?? _baslerInitError;

    private string _baslerInitError = string.Empty;
    public bool IsBaslerAvailable => _basler != null;

    public UnifiedCameraService()
    {
        try
        {
            PylonNativeLoader.Ensure();
        }
        catch (Exception ex)
        {
            _baslerInitError = ex.GetType().Name + ": " + ex.Message;
        }

        _hik.FrameReceived += frame => FrameReceived?.Invoke(frame);

        try
        {
            _basler = new BaslerCameraService();
            _basler.FrameReceived += frame => FrameReceived?.Invoke(frame);
        }
        catch (Exception ex)
        {
            _basler = null;
            _baslerInitError = ex.GetType().Name + ": " + ex.Message;
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
        LastError = string.Empty;

        try
        {
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
        }
        catch (Exception ex)
        {
            AppendError("Hikvision: " + ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            if (_basler != null)
                _basler.Enumerate(ToBaslerTransport(transport));
            else if (_baslerInitError.Length > 0)
                AppendError("Basler: " + _baslerInitError);

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
        }
        catch (Exception ex)
        {
            AppendError("Basler: " + ex.GetType().Name + ": " + ex.Message);
        }

        return CameraList.Count > 0;
    }

    private void AppendError(string message) =>
        LastError = LastError.Length == 0 ? message : LastError + " | " + message;

    public bool Open(CameraInfo camInfo)
    {
        LastError = string.Empty;
        if (camInfo == null)
        {
            LastError = "Select a camera device";
            return false;
        }

        CloseActiveOnly();

        bool opened;
        try
        {
            opened = camInfo.Vendor switch
            {
                CameraVendor.Basler => OpenBasler(camInfo),
                _ => _hik.Open(camInfo)
            };
        }
        catch (Exception ex)
        {
            LastError = camInfo.Vendor + " open failed: " + ex.GetType().Name + ": " + ex.Message;
            return false;
        }

        if (opened)
        {
            _activeVendor = camInfo.Vendor;
            return true;
        }

        LastError = camInfo.Vendor switch
        {
            CameraVendor.Basler => BaslerLastError.Length > 0 ? BaslerLastError : "Open refused by Basler SDK",
            _ => HikLastError.Length > 0 ? HikLastError : "Open refused by Hikvision SDK"
        };
        return false;
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
