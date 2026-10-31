namespace NVKProject.DotNet.BaslerCamera;

public sealed class BaslerCameraDeviceInfo
{
    public int Index { get; set; }
    public int DeviceListIndex { get; set; }
    public string Name { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string ModelName { get; set; } = "";
    public CameraTransportType Transport { get; set; }
}
