using OpenCvSharp;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        public bool Enumerate(CameraTransportType transport = CameraTransportType.GigE)
        {
            MyCamera.MV_CC_DEVICE_INFO_LIST list = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int ret = MyCamera.MV_CC_EnumDevices_NET(GetHikTransportMask(transport), ref list);
            if (ret != MyCamera.MV_OK) return false;
            CameraList.Clear();

            for (int i = 0; i < list.nDeviceNum; i++)
            {
                var dev = (MyCamera.MV_CC_DEVICE_INFO)
                    Marshal.PtrToStructure(list.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));
                var (modelName, serialNumber, deviceTransport) = ReadHikDeviceIdentity(dev);
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

        public bool Open(CameraInfo camInfo)
        {
            if (camInfo == null) return false;
            MyCamera.MV_CC_DEVICE_INFO_LIST list = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            MyCamera.MV_CC_EnumDevices_NET(GetHikTransportMask(camInfo.Transport), ref list);
            if (list.nDeviceNum <= camInfo.BackendIndex) return false;

            var pDevInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(list.pDeviceInfo[camInfo.BackendIndex], typeof(MyCamera.MV_CC_DEVICE_INFO));
            _cam.MV_CC_CreateDevice_NET(ref pDevInfo);
            if (_cam.MV_CC_OpenDevice_NET() != MyCamera.MV_OK) return false;

            _cam.MV_CC_SetEnumValue_NET("AcquisitionMode", (int)MyCamera.MV_CAM_ACQUISITION_MODE.MV_ACQ_MODE_CONTINUOUS);
            _cam.MV_CC_SetEnumValue_NET("TriggerMode", (int)MyCamera.MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
            _cam.MV_CC_SetEnumValue_NET("PixelFormat", (uint)MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed);
            _callback = new MyCamera.cbOutputExdelegate(OnFrame);
            _opened = true;
            return true;
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

        private static (string ModelName, string SerialNumber, CameraTransportType Transport) ReadHikDeviceIdentity(
            MyCamera.MV_CC_DEVICE_INFO dev)
        {
            if (dev.nTLayerType == MyCamera.MV_USB_DEVICE)
            {
                var usb = (MyCamera.MV_USB3_DEVICE_INFO_EX)MyCamera.ByteToStruct(
                    dev.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO_EX));
                return (usb.chModelName, usb.chSerialNumber, CameraTransportType.Usb);
            }

            var gige = (MyCamera.MV_GIGE_DEVICE_INFO_EX)MyCamera.ByteToStruct(
                dev.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO_EX));
            return (gige.chModelName, gige.chSerialNumber, CameraTransportType.GigE);
        }
    }
}
