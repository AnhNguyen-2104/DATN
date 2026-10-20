using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace PlateVision50.Vision;

/// <summary>
/// Native Hikrobot MVS GigE direct IP path. The installed MVS SDK reports sizeof
/// MV_CC_DEVICE_INFO=636 and SpecialInfo offset=32. The older sample MyCamera.cs
/// uses different union lengths and cannot marshal a fabricated GigE device.
/// Camera is opened for image read in MV_ACCESS_Control mode, with no feature writes.
/// </summary>
public sealed class HikDirectIpCamera : IDisposable
{
    private static class Native
    {
        private const string Dll = "MvCameraControl.dll";
        [DllImport(Dll, EntryPoint="MV_CC_Initialize")]
        internal static extern int Initialize();
        [DllImport(Dll, EntryPoint="MV_CC_CreateHandle")]
        internal static extern int CreateHandle(ref IntPtr handle, IntPtr info);
        [DllImport(Dll, EntryPoint="MV_CC_OpenDevice")]
        internal static extern int OpenDevice(IntPtr handle,uint access,ushort key);
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
        ret=Native.StartGrabbing(_handle);
        if(ret!=0)throw new InvalidOperationException($"MVS StartGrabbing 0x{ret:X8}");
        _grabbing=true;
        _cancel=new CancellationTokenSource();
        _worker=Task.Run(()=>Loop(_cancel.Token));
    }
    private void Loop(CancellationToken ct)
    {
        const int bufferSize=1440*1080*3*2;
        IntPtr pixels=Marshal.AllocHGlobal(bufferSize);
        IntPtr frameInfo=Marshal.AllocHGlobal(1024);
        try
        {
            while(!ct.IsCancellationRequested)
            {
                int ret=Native.GetImageForBGR(_handle,pixels,bufferSize,frameInfo,200);
                if(ret!=0)
                {
                    if(ret!=unchecked((int)0x80000007))LastError=$"MVS GetImageForBGR 0x{ret:X8}";
                    continue;
                }
                int width=(ushort)Marshal.ReadInt16(frameInfo,0);
                int height=(ushort)Marshal.ReadInt16(frameInfo,2);
                if(width<=0||height<=0||(long)width*height*3>bufferSize)continue;
                using var bgr=Mat.FromPixelData(height,width,MatType.CV_8UC3,pixels,(long)width*3);
                Mat snapshot=bgr.Clone();
                lock(_gate){Mat? previous=_last;_last=snapshot;previous?.Dispose();}
                Interlocked.Increment(ref _frameNumber);
                LastError="";
            }
        }
        catch(Exception ex){LastError="MVS direct IP: "+ex.Message;}
        finally{Marshal.FreeHGlobal(frameInfo);Marshal.FreeHGlobal(pixels);}
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
    }
}
