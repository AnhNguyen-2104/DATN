using Basler.Pylon;
using OpenCvSharp;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NVKProject.DotNet.BaslerCamera;

/// <summary>
/// Basler pylon camera service (GigE + USB3) aligned with HikCameraService usage.
/// Based on the legacy CameraModel / GigE_Basler save-image flow.
/// </summary>
public sealed class BaslerCameraService : IDisposable
{
    private readonly PixelDataConverter _converter = new();
    private readonly object _matLock = new();
    private readonly List<ICameraInfo> _enumeratedDevices = new();

    private Camera? _camera;
    private Mat? _latestMat;
    private bool _opened;
    private bool _grabbing;

    public List<BaslerCameraDeviceInfo> CameraList { get; } = new();
    public string LastError { get; private set; } = string.Empty;
    public string LastErrorDetails { get; private set; } = string.Empty;

    private Exception? _enumerationError;

    public event Action<BitmapSource>? FrameReceived;

    public bool HasFrame
    {
        get
        {
            lock (_matLock)
                return _latestMat != null && !_latestMat.Empty();
        }
    }

    public Mat? CloneLatestFrame()
    {
        lock (_matLock)
        {
            if (_latestMat == null || _latestMat.Empty()) return null;
            return _latestMat.Clone();
        }
    }

    public (int Width, int Height)? GetFrameSize()
    {
        lock (_matLock)
        {
            if (_latestMat == null || _latestMat.Empty()) return null;
            return (_latestMat.Width, _latestMat.Height);
        }
    }

    public bool Enumerate(CameraTransportType transport)
    {
        LastError = string.Empty;
        LastErrorDetails = string.Empty;
        _enumerationError = null;
        PylonNativeLoader.Ensure();

        CameraList.Clear();
        _enumeratedDevices.Clear();

        try
        {
            Environment.SetEnvironmentVariable("PYLON_GIGE_HEARTBEAT", "500");

            var devices = EnumerateForTransport(transport);

            foreach (var device in devices)
            {
                _enumeratedDevices.Add(device);
                var deviceType = device[CameraInfoKey.DeviceType];
                var model = device[CameraInfoKey.ModelName];
                var serial = device[CameraInfoKey.SerialNumber];
                var label = string.IsNullOrWhiteSpace(model) ? $"Basler {_enumeratedDevices.Count}" : model;
                var display = string.IsNullOrWhiteSpace(serial) ? label : $"{label} - {serial}";

                CameraList.Add(new BaslerCameraDeviceInfo
                {
                    Index = CameraList.Count,
                    DeviceListIndex = _enumeratedDevices.Count - 1,
                    Name = $"Basler [{transport}]: {display}",
                    ModelName = model,
                    SerialNumber = serial,
                    Transport = MapTransport(deviceType)
                });
            }

            if (CameraList.Count == 0 && _enumerationError is not null)
            {
                LastError = "pylon enumeration failed: " + _enumerationError.Message;
                LastErrorDetails = _enumerationError.ToString();
            }

            return CameraList.Count > 0;
        }
        catch (Exception ex)
        {
            LastError = "pylon enumeration failed: " + ex.Message;
            LastErrorDetails = ex.ToString();
            return false;
        }
    }

    private List<ICameraInfo> EnumerateForTransport(CameraTransportType transport)
    {
        var result = new List<ICameraInfo>();
        var seenSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var deviceType in GetDeviceTypesForTransport(transport))
        {
            List<ICameraInfo> batch;
            try
            {
                batch = CameraFinder.Enumerate(deviceType);
            }
            catch (Exception ex)
            {
                _enumerationError ??= ex;
                continue;
            }

            foreach (var device in batch)
            {
                var serial = device[CameraInfoKey.SerialNumber] ?? "";
                if (!seenSerials.Add(serial))
                    continue;
                result.Add(device);
            }
        }

        return result;
    }

    private static IEnumerable<string> GetDeviceTypesForTransport(CameraTransportType transport) =>
        transport switch
        {
            CameraTransportType.Usb => new[]
            {
                DeviceType.Usb,
                DeviceType.BaslerGenTlU3vDeviceClass
            },
            _ => new[]
            {
                DeviceType.GigE,
                DeviceType.BaslerGenTlGevDeviceClass
            }
        };

    public bool Open(BaslerCameraDeviceInfo camInfo)
    {
        LastError = string.Empty;
        LastErrorDetails = string.Empty;

        if (camInfo == null)
        {
            LastError = "Select a camera device";
            return false;
        }

        if (camInfo.DeviceListIndex < 0 || camInfo.DeviceListIndex >= _enumeratedDevices.Count)
        {
            LastError = $"Device index {camInfo.DeviceListIndex} is not in the pylon enumeration";
            return false;
        }

        CloseInternal();

        try
        {
            _camera = new Camera(_enumeratedDevices[camInfo.DeviceListIndex]);
            _camera.ConnectionLost += OnConnectionLost;
            _camera.StreamGrabber.ImageGrabbed += OnImageGrabbed;
            _camera.Open();

            ConfigureContinuousGrab();
            _opened = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastErrorDetails = ex.ToString();
            CloseInternal();
            return false;
        }
    }

    public void Start()
    {
        if (!_opened || _camera == null || _grabbing) return;

        _camera.StreamGrabber.Start(GrabStrategy.LatestImages, GrabLoop.ProvidedByStreamGrabber);
        _grabbing = true;
    }

    public void Stop()
    {
        if (!_grabbing || _camera == null) return;

        if (_camera.StreamGrabber.IsGrabbing)
            _camera.StreamGrabber.Stop();
        _grabbing = false;
    }

    public void Close()
    {
        Stop();
        CloseInternal();
    }

    public void Dispose()
    {
        Close();
        lock (_matLock)
        {
            _latestMat?.Dispose();
            _latestMat = null;
        }
    }

    private void ConfigureContinuousGrab()
    {
        if (_camera == null) return;

        if (_camera.Parameters.Contains(PLCamera.AcquisitionMode))
            _camera.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.Continuous);

        if (_camera.Parameters.Contains(PLCamera.TriggerMode))
            _camera.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.Off);

        if (_camera.Parameters.Contains(PLCamera.PixelFormat))
        {
            if (_camera.Parameters[PLCamera.PixelFormat].CanSetValue(PLCamera.PixelFormat.BGR8))
                _camera.Parameters[PLCamera.PixelFormat].SetValue(PLCamera.PixelFormat.BGR8);
            else if (_camera.Parameters[PLCamera.PixelFormat].CanSetValue(PLCamera.PixelFormat.Mono8))
                _camera.Parameters[PLCamera.PixelFormat].SetValue(PLCamera.PixelFormat.Mono8);
        }
    }

    private void OnImageGrabbed(object? sender, ImageGrabbedEventArgs e)
    {
        try
        {
            using var grabResult = e.GrabResult;
            if (!grabResult.IsValid) return;

            Mat? mat;
            BitmapSource? bmpSrc;

            if (grabResult.PixelTypeValue == PixelType.Mono8)
            {
                var buffer = grabResult.PixelData as byte[];
                if (buffer == null) return;

                mat = new Mat(grabResult.Height, grabResult.Width, MatType.CV_8UC1);
                mat.SetArray(buffer);

                bmpSrc = BitmapSource.Create(
                    grabResult.Width, grabResult.Height, 96, 96,
                    PixelFormats.Gray8, null, buffer, grabResult.Width);
            }
            else
            {
                var stride = grabResult.Width * 3;
                var buffer = new byte[stride * grabResult.Height];
                _converter.OutputPixelFormat = PixelType.BGR8packed;
                _converter.Convert(buffer, grabResult);

                mat = Mat.FromPixelData(grabResult.Height, grabResult.Width, MatType.CV_8UC3, buffer, stride).Clone();
                bmpSrc = BitmapSource.Create(
                    grabResult.Width, grabResult.Height, 96, 96,
                    PixelFormats.Bgr24, null, buffer, stride);
            }

            SetLatestMat(mat);
            if (bmpSrc == null) return;

            bmpSrc.Freeze();
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                FrameReceived?.Invoke(bmpSrc);
            }));
        }
        catch
        {
            // Ignore frame errors during live preview.
        }
    }

    private void OnConnectionLost(object? sender, EventArgs e) => CloseInternal();

    private void CloseInternal()
    {
        if (_camera == null) return;

        try
        {
            _camera.StreamGrabber.ImageGrabbed -= OnImageGrabbed;
            _camera.ConnectionLost -= OnConnectionLost;

            if (_camera.StreamGrabber.IsGrabbing)
                _camera.StreamGrabber.Stop();

            if (_camera.IsOpen)
                _camera.Close();

            _camera.Dispose();
        }
        catch
        {
            // Best effort cleanup.
        }
        finally
        {
            _camera = null;
            _opened = false;
            _grabbing = false;
        }
    }

    private void SetLatestMat(Mat newMat)
    {
        lock (_matLock)
        {
            _latestMat?.Dispose();
            _latestMat = newMat;
        }
    }

    private static bool MatchesTransport(string deviceType, CameraTransportType transport)
    {
        if (string.IsNullOrWhiteSpace(deviceType)) return false;

        var isUsb = deviceType.Equals(DeviceType.Usb, StringComparison.OrdinalIgnoreCase)
                    || deviceType.Equals(DeviceType.BaslerGenTlU3vDeviceClass, StringComparison.OrdinalIgnoreCase)
                    || deviceType.Contains("U3V", StringComparison.OrdinalIgnoreCase)
                    || deviceType.Contains("Usb", StringComparison.OrdinalIgnoreCase);

        var isGigE = deviceType.Equals(DeviceType.GigE, StringComparison.OrdinalIgnoreCase)
                     || deviceType.Equals(DeviceType.BaslerGenTlGevDeviceClass, StringComparison.OrdinalIgnoreCase)
                     || deviceType.Contains("GigE", StringComparison.OrdinalIgnoreCase)
                     || deviceType.Contains("GEV", StringComparison.OrdinalIgnoreCase);

        return transport switch
        {
            CameraTransportType.Usb => isUsb,
            CameraTransportType.GigE => isGigE,
            _ => isUsb || isGigE
        };
    }

    private static CameraTransportType MapTransport(string deviceType) =>
        MatchesTransport(deviceType, CameraTransportType.Usb)
            ? CameraTransportType.Usb
            : CameraTransportType.GigE;
}
