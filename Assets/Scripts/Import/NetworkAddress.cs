using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace StageReadyVR.Import
{
    /// <summary>Finds the address other devices on the same Wi-Fi can use to reach the headset.</summary>
    public static class NetworkAddress
    {
        /// <summary>The LAN IPv4 address as text, or null when the device has no usable network.</summary>
        public static string GetLanIPv4()
        {
            return FromRoute() ?? FromInterfaces();
        }

        // connecting a udp socket sends nothing, it only makes the os pick the
        // outgoing interface. the address is a reserved documentation range.
        // this path is preferred because interface enumeration is restricted on newer android.
        static string FromRoute()
        {
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 9));
                    var local = socket.LocalEndPoint as IPEndPoint;
                    return Usable(local?.Address) ? local.Address.ToString() : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string FromInterfaces()
        {
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (Usable(info.Address)) return info.Address.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // some android versions refuse enumeration, which just means no address
            }
            return null;
        }

        static bool Usable(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any)) return false;

            byte[] b = address.GetAddressBytes();
            return !(b[0] == 169 && b[1] == 254);   // link local means no dhcp lease
        }
    }
}
