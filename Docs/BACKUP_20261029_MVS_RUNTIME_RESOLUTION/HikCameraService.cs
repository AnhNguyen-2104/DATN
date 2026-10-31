using OpenCvSharp;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using System.Collections.Generic;
namespace WpfYolo_DotNet.Devices
{
    public class HikCameraService : IDisposable
    {
        private MyCamera _cam = new MyCamera();
        private bool _opened, _grabbing;
        private MyCamera.cbOutputExdelegate _callback;
        private readonly object _matLock = new();
        private Mat _latestMat;
        public bool HasFrame
        {
            get
            {
                lock (_matLock)
                    return _latestMat != null && !_latestMat.Empty();
            }
        }

        public Mat CloneLatestFrame()
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
        public event Action<BitmapSource> FrameReceived;
        public List<CameraInfo> CameraList { get; private set; } = new List<CameraInfo>();
        public string LastError { get; private set; } = string.Empty;
        public IReadOnlyDictionary<int, string> DeviceAddresses => _addresses;
        private readonly Dictionary<int, string> _addresses = new();


        public bool Enumerate(CameraTransportType transport = CameraTransportType.GigE)
        {
            LastError = string.Empty;
            try
            {
                MyCamera.MV_CC_DEVICE_INFO_LIST list = new MyCamera.MV_CC_DEVICE_INFO_LIST();
                int ret = MyCamera.MV_CC_EnumDevices_NET(GetHikTransportMask(transport), ref list);
                if (ret != MyCamera.MV_OK)
                {
                    LastError = FormatHikError("MV_CC_EnumDevices_NET", ret);
                    return false;
                }
                CameraList.Clear();
                _addresses.Clear();

                for (int i = 0; i < list.nDeviceNum; i++)
                {
                    var dev = (MyCamera.MV_CC_DEVICE_INFO)
                        Marshal.PtrToStructure(list.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                    var (modelName, serialNumber, deviceTransport, address) = ReadHikDeviceIdentity(dev);
                    if (!string.IsNullOrWhiteSpace(address)) _addresses[i] = address;
                    var label = string.IsNullOrWhiteSpace(modelName) ? $"Cam{i + 1}" : modelName;
                    var display = string.IsNullOrWhiteSpace(serialNumber) ? label : $"{label} - {serialNumber}";

                    CameraList.Add(new CameraInfo
                    {
                        Index = CameraList.Count,
                        BackendIndex = i,
                        Vendor = CameraVendor.Hikvision,
                        Transport = deviceTransport,
                        Name = $"HIK [{deviceTransport}]: {display}",
                        SerialNumber = serialNumber
                    });
                }
                return CameraList.Count > 0;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool Open(CameraInfo camInfo)
        {
            LastError = string.Empty;
            if (camInfo == null)
            {
                LastError = "CameraInfo is null";
                return false;
            }

            try
            {
                MyCamera.MV_CC_DEVICE_INFO_LIST list = new MyCamera.MV_CC_DEVICE_INFO_LIST();
                int enumRet = MyCamera.MV_CC_EnumDevices_NET(GetHikTransportMask(camInfo.Transport), ref list);
                if (enumRet != MyCamera.MV_OK)
                {
                    LastError = FormatHikError("MV_CC_EnumDevices_NET", enumRet);
                    return false;
                }

                if (list.nDeviceNum <= camInfo.BackendIndex)
                {
                    LastError = $"Device index {camInfo.BackendIndex} out of range ({list.nDeviceNum} found)";
                    return false;
                }

                var pDevInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(
                    list.pDeviceInfo[camInfo.BackendIndex], typeof(MyCamera.MV_CC_DEVICE_INFO));
                int ret = _cam.MV_CC_CreateDevice_NET(ref pDevInfo);
                if (ret != MyCamera.MV_OK)
                {
                    LastError = FormatHikError("MV_CC_CreateDevice_NET", ret);
                    return false;
                }

                ret = _cam.MV_CC_OpenDevice_NET();
                if (ret != MyCamera.MV_OK)
                {
                    LastError = FormatHikError("MV_CC_OpenDevice_NET", ret);
                    return false;
                }

                _cam.MV_CC_SetEnumValue_NET("AcquisitionMode", (int)MyCamera.MV_CAM_ACQUISITION_MODE.MV_ACQ_MODE_CONTINUOUS);
                _cam.MV_CC_SetEnumValue_NET("TriggerMode", (int)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
                _cam.MV_CC_SetEnumValue_NET("PixelFormat", (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed);
                _callback = new MyCamera.cbOutputExdelegate(OnFrame);
                _opened = true;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public void Start()
        {
            if (!_opened) return;
            _cam.MV_CC_RegisterImageCallBackEx_NET(_callback, IntPtr.Zero);
            _cam.MV_CC_StartGrabbing_NET();
            _grabbing = true;
        }

        public void Stop()
        {
            if (_grabbing)
            {
                _cam.MV_CC_StopGrabbing_NET();
                _grabbing = false;
            }
        }

        public void Close()
        {
            Stop();
            if (_opened)
            {
                _cam.MV_CC_CloseDevice_NET();
                _cam.MV_CC_DestroyDevice_NET();
                _opened = false;
            }
        }

        private void OnFrame(IntPtr pData, ref MyCamera.MV_FRAME_OUT_INFO_EX frameInfo, IntPtr pUser)
        {
            try
            {
                var pt = (MyCamera.MvGvspPixelType)frameInfo.enPixelType;

                BitmapSource bmpSrc;

                if (pt == MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed)
                {
                    int width = (int)frameInfo.nWidth;
                    int height = (int)frameInfo.nHeight;
                    int stride = width * 3;

                    byte[] buffer = new byte[stride * height];
                    Marshal.Copy(pData, buffer, 0, buffer.Length);

                    bmpSrc = BitmapSource.Create(
                        width, height, 96, 96,
                        PixelFormats.Bgr24,
                        null,
                        buffer, stride);
                    SetLatestMat(Mat.FromPixelData(height, width, MatType.CV_8UC3, pData, stride).Clone());
                }
                else if (pt == MyCamera.MvGvspPixelType.PixelType_Gvsp_Mono8)
                {
                    int width = (int)frameInfo.nWidth;
                    int height = (int)frameInfo.nHeight;
                    int stride = width;

                    byte[] buffer = new byte[stride * height];
                    Marshal.Copy(pData, buffer, 0, buffer.Length);

                    bmpSrc = BitmapSource.Create(
                        width, height, 96, 96,
                        PixelFormats.Gray8,
                        null,
                        buffer, stride);
                    SetLatestMat(Mat.FromPixelData(height, width, MatType.CV_8UC1, pData, stride).Clone());
                }
                else
                {
                    bmpSrc = ConvertToBgr24(pData, frameInfo);
                    SetLatestMat(ConvertToBGR8(pData, frameInfo));
                }

                if (bmpSrc != null)
                {
                    bmpSrc.Freeze();
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        FrameReceived?.Invoke(bmpSrc);
                    }));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error in OnImageCallback_ {ex.Message}");
            }
        }
        private BitmapSource ConvertToBgr24(IntPtr pData, MyCamera.MV_FRAME_OUT_INFO_EX info)
        {
            int w = (int)info.nWidth;
            int h = (int)info.nHeight;

            var dstSize = w * h * 3;
            IntPtr dstBuf = Marshal.AllocHGlobal(dstSize);

            try
            {
                MyCamera.MV_PIXEL_CONVERT_PARAM c = new MyCamera.MV_PIXEL_CONVERT_PARAM
                {
                    nWidth = info.nWidth,
                    nHeight = info.nHeight,
                    pSrcData = pData,
                    nSrcDataLen = info.nFrameLen,
                    enSrcPixelType = info.enPixelType,
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed,
                    pDstBuffer = dstBuf,
                    nDstBufferSize = (uint)dstSize
                };

                int nRet = _cam.MV_CC_ConvertPixelType_NET(ref c);
                if (nRet != MyCamera.MV_OK) return null;

                byte[] managed = new byte[dstSize];
                Marshal.Copy(dstBuf, managed, 0, dstSize);

                return BitmapSource.Create(
                    w, h, 96, 96,
                    PixelFormats.Bgr24,
                    null,
                    managed, w * 3);
            }
            finally
            {
                Marshal.FreeHGlobal(dstBuf);
            }
        }
        private Mat ConvertToBGR8(IntPtr pData, MyCamera.MV_FRAME_OUT_INFO_EX info)
        {
            int w = (int)info.nWidth;
            int h = (int)info.nHeight;
            int dstSize = w * h * 3;

            IntPtr dstBuf = Marshal.AllocHGlobal(dstSize);
            try
            {
                MyCamera.MV_PIXEL_CONVERT_PARAM param = new MyCamera.MV_PIXEL_CONVERT_PARAM
                {
                    nWidth = info.nWidth,
                    nHeight = info.nHeight,
                    pSrcData = pData,
                    nSrcDataLen = info.nFrameLen,
                    enSrcPixelType = info.enPixelType,
                    enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed,
                    pDstBuffer = dstBuf,
                    nDstBufferSize = (uint)dstSize
                };
                _cam.MV_CC_ConvertPixelType_NET(ref param);

                return Mat.FromPixelData(h, w, MatType.CV_8UC3, dstBuf, dstSize).Clone();
                //return new Mat(h, w, MatType.CV_8UC3, dstBuf, dstSize);
            }
            finally
            {
                Marshal.FreeHGlobal(dstBuf);
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

        public void Dispose()
        {
            Close();
            lock (_matLock)
            {
                _latestMat?.Dispose();
                _latestMat = null;
            }
        }

        private static uint GetHikTransportMask(CameraTransportType transport) =>
            transport == CameraTransportType.Usb
                ? (uint)MyCamera.MV_USB_DEVICE
                : (uint)MyCamera.MV_GIGE_DEVICE;

        private static (string ModelName, string SerialNumber, CameraTransportType Transport, string Address)
            ReadHikDeviceIdentity(MyCamera.MV_CC_DEVICE_INFO dev)
        {
            if (dev.nTLayerType == MyCamera.MV_USB_DEVICE)
            {
                var usb = (MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(
                    dev.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
                return (usb.chModelName, usb.chSerialNumber, CameraTransportType.Usb, string.Empty);
            }

            var gige = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(
                dev.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
            return (gige.chModelName, gige.chSerialNumber, CameraTransportType.GigE, FormatHikIp(gige.nCurrentIp));
        }

        private static string FormatHikIp(uint value)
        {
            if (value == 0) return string.Empty;

            string ip = string.Format("{0}.{1}.{2}.{3}",
                (value >> 24) & 0xFF, (value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);

            return ip == "0.0.0.0" ? string.Empty : ip;
        }

        private static string FormatHikError(string api, int code) =>
            $"{api} returned 0x{code:X8} ({HikErrorName(code)})";

        private static string HikErrorName(int code) => code switch
        {
            MyCamera.MV_E_HANDLE => "MV_E_HANDLE",
            MyCamera.MV_E_SUPPORT => "MV_E_SUPPORT",
            MyCamera.MV_E_BUFOVER => "MV_E_BUFOVER",
            MyCamera.MV_E_CALLORDER => "MV_E_CALLORDER",
            MyCamera.MV_E_PARAMETER => "MV_E_PARAMETER",
            MyCamera.MV_E_RESOURCE => "MV_E_RESOURCE",
            MyCamera.MV_E_NODATA => "MV_E_NODATA",
            MyCamera.MV_E_PRECONDITION => "MV_E_PRECONDITION",
            MyCamera.MV_E_VERSION => "MV_E_VERSION",
            MyCamera.MV_E_NOENOUGH_BUF => "MV_E_NOENOUGH_BUF",
            MyCamera.MV_E_LOAD_LIBRARY => "MV_E_LOAD_LIBRARY",
            MyCamera.MV_E_ACCESS_DENIED => "MV_E_ACCESS_DENIED",
            MyCamera.MV_E_BUSY => "MV_E_BUSY",
            MyCamera.MV_E_NETER => "MV_E_NETER",
            MyCamera.MV_E_IP_CONFLICT => "MV_E_IP_CONFLICT",
            MyCamera.MV_E_USB_READ => "MV_E_USB_READ",
            MyCamera.MV_E_USB_WRITE => "MV_E_USB_WRITE",
            MyCamera.MV_E_USB_DEVICE => "MV_E_USB_DEVICE",
            _ => "HIK_ERROR"
        };
    }
}
