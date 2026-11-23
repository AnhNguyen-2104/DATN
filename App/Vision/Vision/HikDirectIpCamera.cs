using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using WpfYolo_DotNet.Devices;

namespace PlateVision50.Vision;

/// <summary>
/// Native Hikrobot MVS GigE direct IP path. The installed MVS SDK reports sizeof
/// MV_CC_DEVICE_INFO=636 and SpecialInfo offset=32. The older sample MyCamera.cs
/// uses different union lengths and cannot marshal a fabricated GigE device.
/// Camera is opened in MV_ACCESS_Control mode, configuring continuous acquisition,
/// trigger mode off, and BGR8 format to match HikCameraService.
/// </summary>
public sealed class HikDirectIpCamera : IDisposable
{
    private static class Native
    {
        static Native()
        {
            MvsNativeLoader.Ensure();
        }
        private const string Dll = "MvCameraControl.dll";
        [DllImport(Dll, EntryPoint="MV_CC_Initialize")]
        internal static extern int Initialize();
        [DllImport(Dll, EntryPoint="MV_CC_CreateHandle")]
        internal static extern int CreateHandle(ref IntPtr handle, IntPtr info);
        [DllImport(Dll, EntryPoint="MV_CC_OpenDevice")]
        internal static extern int OpenDevice(IntPtr handle,uint access,ushort key);
        [StructLayout(LayoutKind.Sequential)]
        internal struct MVCC_INTVALUE_EX
        {
            public long CurValue;
            public long Max;
            public long Min;
            public long Inc;
            public long R0, R1, R2, R3, R4, R5, R6, R7;
        }
        [DllImport(Dll, EntryPoint="MV_CC_SetEnumValue", CallingConvention = CallingConvention.StdCall)]
        internal static extern int SetEnumValue(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string key, uint value);
        [DllImport(Dll, EntryPoint="MV_CC_SetEnumValueByString", CallingConvention = CallingConvention.StdCall)]
        internal static extern int SetEnumValueByString(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);
        [DllImport(Dll, EntryPoint="MV_CC_GetIntValueEx", CallingConvention = CallingConvention.StdCall)]
        internal static extern int GetIntValueEx(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string key, ref MVCC_INTVALUE_EX value);
        [DllImport(Dll, EntryPoint="MV_CC_SetIntValueEx", CallingConvention = CallingConvention.StdCall)]
        internal static extern int SetIntValueEx(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string key, long value);
        [DllImport(Dll, EntryPoint="MV_CC_GetOptimalPacketSize", CallingConvention = CallingConvention.StdCall)]
        internal static extern int GetOptimalPacketSize(IntPtr handle);
        [DllImport(Dll, EntryPoint="MV_CC_GetPayloadSize", CallingConvention = CallingConvention.StdCall)]
        internal static extern int GetPayloadSize(IntPtr handle, ref ulong payloadSize, ref uint alignment);
        [DllImport(Dll, EntryPoint="MV_CC_StartGrabbing")]
        internal static extern int StartGrabbing(IntPtr handle);
        [DllImport(Dll, EntryPoint="MV_CC_GetImageForBGR")]
        internal static extern int GetImageForBGR(IntPtr handle,IntPtr buffer,uint bytes,IntPtr info,int timeoutMs);
        [DllImport(Dll, EntryPoint="MV_CC_StopGrabbing")]
        internal static extern int StopGrabbing(IntPtr handle);
        [DllImport(Dll, EntryPoint="MV_CC_CloseDevice")]
        internal static extern int CloseDevice(IntPtr handle);
        [DllImport(Dll, EntryPoint="MV_CC_DestroyHandle")]
        internal static extern int DestroyHandle(IntPtr handle);
    }
    static HikDirectIpCamera()
    {
        MvsNativeLoader.Ensure();
    }
    public const uint TargetAcquisitionMode = 2; // MV_ACQ_MODE_CONTINUOUS = 2
    public const uint TargetTriggerMode = 0; // MV_TRIGGER_MODE_OFF = 0
    public const uint TargetPixelFormat = 0x02180015; // PixelType_Gvsp_BGR8_Packed

    public const string FrameStartSelector = "FrameStart";
    public const uint FrameStartSelectorValue = 3;

    public const string AcquisitionStartSelector = "AcquisitionStart";
    public const uint AcquisitionStartSelectorValue = 0;

    public const string FrameBurstStartSelector = "FrameBurstStart";
    public const uint FrameBurstStartSelectorValue = 6;

    public static bool IsUnsupportedSelectorError(int ret)
    {
        return ret == unchecked((int)0x80000001)  // MV_E_SUPPORT
            || ret == unchecked((int)0x80000021)  // MV_E_DEV_SUPPORT
            || ret == unchecked((int)0x80000109)  // MV_E_GC_NODE_NOT_FOUND
            || ret == unchecked((int)0x80000200)  // MV_E_NOT_IMPLEMENTED
            || ret == unchecked((int)0x80000041); // MV_E_DEV_NOT_IMPLEMENTED
    }

    public static int ConfigureTriggerSelector(IntPtr handle, string selectorName, uint fallbackEnum, uint mode = TargetTriggerMode)
    {
        if (handle == IntPtr.Zero) return unchecked((int)0x80000000); // MV_E_HANDLE
        int ret = Native.SetEnumValueByString(handle, "TriggerSelector", selectorName);
        if (ret != 0)
        {
            ret = Native.SetEnumValue(handle, "TriggerSelector", fallbackEnum);
        }
        if (ret == 0)
        {
            int modeRet = Native.SetEnumValue(handle, "TriggerMode", mode);
            if (modeRet != 0) return modeRet;
        }
        return ret;
    }
    public const int Station01DefaultBytes = 1440 * 1080 * 3; // 4,665,600 bytes
    public const int Station02RequiredBytes = 2592 * 1944 * 3; // 15,116,544 bytes
    public const int DefaultMinBufferSize = Station02RequiredBytes;

    public static int CalculateRequiredBufferSize(long width, long height, long payloadSize = 0)
    {
        long dimBytes = (width > 0 && height > 0) ? width * height * 3 : 0;
        long payloadBytes = 0;
        if (payloadSize > 0)
        {
            // If payloadSize already covers 3 bytes per pixel (BGR), do not triple it.
            // Otherwise (e.g. raw Bayer/Mono sensor payload where payloadSize <= width*height),
            // expand 3x for BGR conversion.
            if (dimBytes > 0 && payloadSize >= dimBytes)
                payloadBytes = payloadSize;
            else if (dimBytes > 0)
                payloadBytes = Math.Max(dimBytes, payloadSize * 3);
            else
                payloadBytes = payloadSize >= DefaultMinBufferSize ? payloadSize : payloadSize * 3;
        }
        long needed = Math.Max(dimBytes, payloadBytes);
        if (needed < DefaultMinBufferSize)
        {
            needed = DefaultMinBufferSize;
        }
        if (needed > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(needed), $"Requested frame buffer size {needed} bytes exceeds int.MaxValue");
        }
        return (int)needed;
    }

    private IntPtr _handle = IntPtr.Zero;
    private CancellationTokenSource? _cancel;
    private Task? _worker;
    private readonly object _gate=new();
    private Mat? _last;
    private bool _opened,_grabbing;
    private long _frameNumber;
    public string CameraIp{get;}
    public string HostIp{get;}
    public string LastError{get;private set;}="";
    public bool IsOpen=>_opened&&_grabbing;
    public long FrameNumber=>Interlocked.Read(ref _frameNumber);
    public HikDirectIpCamera(string cameraIp,string hostIp)
    {
        var cip=IPAddress.Parse(cameraIp);
        var hip=IPAddress.Parse(hostIp);
        if(cip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||
           hip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("Only IPv4 GigE addresses are supported.");
        CameraIp=cip.ToString();HostIp=hip.ToString();
    }
    private static uint UIntNetwork(string ip)
    {
        var b=IPAddress.Parse(ip).GetAddressBytes();
        return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3];
    }
    public void Open()
    {
        if(IsOpen)return;
        if(!MvsNativeLoader.Ensure())
        {
            LastError=MvsNativeLoader.LastError;
            throw new DllNotFoundException(MvsNativeLoader.LastError);
        }
        int ret=Native.Initialize();
        if(ret!=0)throw new InvalidOperationException($"MVS Initialize 0x{ret:X8}");
        // The directly inspected installed MVS Python SDK's sizeof(MV_CC_DEVICE_INFO)
        // is 636, with nTLayerType at 12 and GigE fields at 32+8 and 32+196.
        // Allocated on unmanaged heap to avoid outdated 540-byte C# union marshaling.
        const int bytes=636;
        IntPtr nativeInfo=Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.Copy(new byte[bytes],0,nativeInfo,bytes);
            Marshal.WriteInt32(nativeInfo,12,1); // MV_GIGE_DEVICE
            Marshal.WriteInt32(nativeInfo,40,unchecked((int)UIntNetwork(CameraIp)));
            Marshal.WriteInt32(nativeInfo,228,unchecked((int)UIntNetwork(HostIp)));
            ret=Native.CreateHandle(ref _handle,nativeInfo);
            if(ret!=0)throw new InvalidOperationException($"MVS CreateHandle 0x{ret:X8}");
        }
        finally{Marshal.FreeHGlobal(nativeInfo);}
        ret=Native.OpenDevice(_handle,3,0); // MV_ACCESS_Control; no takeover
        if(ret!=0)throw new InvalidOperationException($"MVS OpenDevice 0x{ret:X8}");
        _opened=true;
        // Configure non-persistent acquisition, trigger, packet size and pixel format matching HikCameraService
        try
        {
            // Negotiate optimal packet size between OpenDevice and StartGrabbing
            try
            {
                int optimalPacketSize = Native.GetOptimalPacketSize(_handle);
                if (optimalPacketSize > 0)
                {
                    int retPacket = Native.SetIntValueEx(_handle, "GevSCPSPacketSize", optimalPacketSize);
                    if (retPacket != 0 && string.IsNullOrEmpty(LastError))
                    {
                        LastError = $"MVS Set GevSCPSPacketSize 0x{retPacket:X8}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(LastError))
                {
                    LastError = $"MVS packet size negotiation: {ex.Message}";
                }
            }

            int retAcq = Native.SetEnumValue(_handle, "AcquisitionMode", TargetAcquisitionMode);
            if (retAcq != 0 && string.IsNullOrEmpty(LastError)) LastError = $"MVS Set AcquisitionMode 0x{retAcq:X8}";

            // Disarm trigger mode across supported selectors:
            // Safely attempt AcquisitionStart (0) and FrameBurstStart (6) Off if supported, ignoring only MV_E_SUPPORT / unsupported selector errors.
            int retAcqStart = ConfigureTriggerSelector(_handle, AcquisitionStartSelector, AcquisitionStartSelectorValue, TargetTriggerMode);
            if (retAcqStart != 0 && !IsUnsupportedSelectorError(retAcqStart) && string.IsNullOrEmpty(LastError))
            {
                LastError = $"MVS Set TriggerSelector AcquisitionStart 0x{retAcqStart:X8}";
            }

            int retBurstStart = ConfigureTriggerSelector(_handle, FrameBurstStartSelector, FrameBurstStartSelectorValue, TargetTriggerMode);
            if (retBurstStart != 0 && !IsUnsupportedSelectorError(retBurstStart) && string.IsNullOrEmpty(LastError))
            {
                LastError = $"MVS Set TriggerSelector FrameBurstStart 0x{retBurstStart:X8}";
            }

            // Explicitly select FrameStart (value 3) and set TriggerMode Off
            int retFrameStart = ConfigureTriggerSelector(_handle, FrameStartSelector, FrameStartSelectorValue, TargetTriggerMode);
            if (retFrameStart != 0)
            {
                int retBase = Native.SetEnumValue(_handle, "TriggerMode", TargetTriggerMode);
                if (retBase != 0 && string.IsNullOrEmpty(LastError)) LastError = $"MVS Set TriggerMode 0x{retBase:X8}";
            }

            int retPix = Native.SetEnumValue(_handle, "PixelFormat", TargetPixelFormat);
            if (retPix != 0 && string.IsNullOrEmpty(LastError)) LastError = $"MVS Set PixelFormat 0x{retPix:X8}";

            var valW = new Native.MVCC_INTVALUE_EX();
            var valH = new Native.MVCC_INTVALUE_EX();
            var valP = new Native.MVCC_INTVALUE_EX();
            long w = Native.GetIntValueEx(_handle, "Width", ref valW) == 0 ? valW.CurValue : 0;
            long h = Native.GetIntValueEx(_handle, "Height", ref valH) == 0 ? valH.CurValue : 0;
            long p = Native.GetIntValueEx(_handle, "PayloadSize", ref valP) == 0 ? valP.CurValue : 0;
            if (p <= 0)
            {
                ulong payloadSize = 0;
                uint alignment = 0;
                if (Native.GetPayloadSize(_handle, ref payloadSize, ref alignment) == 0 && payloadSize > 0 && payloadSize <= long.MaxValue)
                {
                    p = (long)payloadSize;
                }
            }
            int initialBufferSize = CalculateRequiredBufferSize(w, h, p);

            ret=Native.StartGrabbing(_handle);
            if(ret!=0)throw new InvalidOperationException($"MVS StartGrabbing 0x{ret:X8}");
            _grabbing=true;
            _cancel=new CancellationTokenSource();
            _worker=Task.Run(()=>Loop(_cancel.Token, initialBufferSize));
        }
        catch
        {
            if (_grabbing) { try { Native.StopGrabbing(_handle); } catch { } _grabbing = false; }
            if (_opened) { try { Native.CloseDevice(_handle); } catch { } _opened = false; }
            if (_handle != IntPtr.Zero) { try { Native.DestroyHandle(_handle); } catch { } _handle = IntPtr.Zero; }
            throw;
        }
    }
    private void Loop(CancellationToken ct, int initialBufferSize)
    {
        int bufferSize = initialBufferSize > 0 ? initialBufferSize : DefaultMinBufferSize;
        IntPtr pixels = IntPtr.Zero;
        IntPtr frameInfo = IntPtr.Zero;
        try
        {
            pixels = Marshal.AllocHGlobal(bufferSize);
            frameInfo = Marshal.AllocHGlobal(1024);
            while (!ct.IsCancellationRequested)
            {
                // Zero-initialize frame header to prevent reading stale/garbage dimensions on error
                Marshal.WriteInt64(frameInfo, 0, 0);
                Marshal.WriteInt64(frameInfo, 8, 0);
                Marshal.WriteInt64(frameInfo, 16, 0);
                Marshal.WriteInt64(frameInfo, 24, 0);

                int ret = Native.GetImageForBGR(_handle, pixels, (uint)bufferSize, frameInfo, 200);
                if (ret != 0)
                {
                    if (ret == unchecked((int)0x8000000A)) // MV_E_NOENOUGH_BUF
                    {
                        int fw = (ushort)Marshal.ReadInt16(frameInfo, 0);
                        int fh = (ushort)Marshal.ReadInt16(frameInfo, 2);
                        long needed = (fw > 0 && fh > 0) ? (long)fw * fh * 3 : (long)bufferSize * 2;
                        if (needed > bufferSize && needed <= int.MaxValue)
                        {
                            IntPtr newPixels = IntPtr.Zero;
                            try
                            {
                                newPixels = Marshal.AllocHGlobal((int)needed);
                            }
                            catch (Exception allocEx)
                            {
                                LastError = $"MVS GetImageForBGR buffer expansion failed ({needed} bytes): {allocEx.Message}";
                                continue;
                            }
                            Marshal.FreeHGlobal(pixels);
                            pixels = newPixels;
                            bufferSize = (int)needed;
                            continue;
                        }
                        LastError = $"MVS GetImageForBGR buffer insufficient ({bufferSize} bytes)";
                        continue;
                    }
                    if (ret != unchecked((int)0x80000007)) // MV_E_NODATA (timeout)
                    {
                        LastError = $"MVS GetImageForBGR 0x{ret:X8}";
                    }
                    continue;
                }
                int width = (ushort)Marshal.ReadInt16(frameInfo, 0);
                int height = (ushort)Marshal.ReadInt16(frameInfo, 2);
                if (width <= 0 || height <= 0) continue;

                long requiredBytes = (long)width * height * 3;
                if (requiredBytes > int.MaxValue)
                {
                    LastError = $"MVS GetImageForBGR frame dimensions {width}x{height} exceed int.MaxValue ({requiredBytes} bytes)";
                    continue;
                }
                if (requiredBytes > bufferSize)
                {
                    IntPtr newPixels = IntPtr.Zero;
                    try
                    {
                        newPixels = Marshal.AllocHGlobal((int)requiredBytes);
                    }
                    catch (Exception allocEx)
                    {
                        LastError = $"MVS GetImageForBGR buffer reallocation failed ({requiredBytes} bytes): {allocEx.Message}";
                        continue;
                    }
                    Marshal.FreeHGlobal(pixels);
                    pixels = newPixels;
                    bufferSize = (int)requiredBytes;
                    continue;
                }
                using var bgr = Mat.FromPixelData(height, width, MatType.CV_8UC3, pixels, (long)width * 3);
                Mat snapshot = bgr.Clone();
                lock (_gate)
                {
                    Mat? previous = _last;
                    _last = snapshot;
                    previous?.Dispose();
                }
                Interlocked.Increment(ref _frameNumber);
                LastError = "";
            }
        }
        catch (Exception ex)
        {
            LastError = "MVS direct IP: " + ex.Message;
        }
        finally
        {
            if (frameInfo != IntPtr.Zero) Marshal.FreeHGlobal(frameInfo);
            if (pixels != IntPtr.Zero) Marshal.FreeHGlobal(pixels);
        }
    }
    public Mat? CloneLatestFrame()
    {
        lock(_gate)return _last?.Clone();
    }
    public void Dispose()
    {
        _cancel?.Cancel();
        try{_worker?.Wait(TimeSpan.FromSeconds(2));}catch{}
        if(_worker?.IsCompleted==false)return; // don't destroy a busy native handle
        try{if(_grabbing)Native.StopGrabbing(_handle);}catch{}
        try{if(_opened)Native.CloseDevice(_handle);}catch{}
        try{if(_handle!=IntPtr.Zero)Native.DestroyHandle(_handle);}catch{}
        _handle=IntPtr.Zero;_opened=_grabbing=false;
        lock(_gate){_last?.Dispose();_last=null;}
        _cancel?.Dispose();
        _cancel = null;
        _worker = null;
    }
}
