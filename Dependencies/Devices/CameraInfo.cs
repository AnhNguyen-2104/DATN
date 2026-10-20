namespace WpfYolo_DotNet.Devices;

public class CameraInfo
{
    public int Index { get; set; }
    public int BackendIndex { get; set; }
    public CameraVendor Vendor { get; set; }
    public CameraTransportType Transport { get; set; }
    public string Name { get; set; } = "";
    public string SerialNumber { get; set; } = "";
}
