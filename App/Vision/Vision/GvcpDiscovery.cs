using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlateVision50.Vision;

/// <summary>Read-only GigE Vision Discovery, using UDP GVCP 3956 on a chosen local NIC.</summary>
public static class GvcpDiscovery
{
    public static async Task<IReadOnlyList<string>> ScanAsync(string hostIp, int timeoutMs=1000)
    {
        var host=IPAddress.Parse(hostIp);
        if(host.AddressFamily!=AddressFamily.InterNetwork)
            throw new ArgumentException("GVCP requires IPv4.");
        using var client=new UdpClient(new IPEndPoint(host,0));
        client.EnableBroadcast=true;
        // GVCP DISCOVERY_CMD 0x0002, request ID 1. No camera settings change.
        byte[] frame={0x42,0x11,0x00,0x02,0x00,0x00,0x00,0x01};
        await client.SendAsync(frame,frame.Length,new IPEndPoint(IPAddress.Broadcast,3956));
        using var stop=new CancellationTokenSource(timeoutMs);
        var ips=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(!stop.IsCancellationRequested)
        {
            try
            {
                var response=await client.ReceiveAsync(stop.Token);
                // GVCP DISCOVERY_ACK 0x0003, valid packet header and sender.
                if(response.Buffer.Length>=8 &&
                   response.Buffer[2]==0x00 && response.Buffer[3]==0x03)
                    ips.Add(response.RemoteEndPoint.Address.ToString());
            }
            catch(OperationCanceledException){break;}
            catch(SocketException){break;}
        }
        return new List<string>(ips);
    }
}
