using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace PortalAutoLogin
{
    public enum NetworkKind
    {
        None,
        Wifi,
        Other
    }

    public static class NetworkGuard
    {
        [DllImport("iphlpapi.dll")]
        private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

        public static NetworkKind ActiveKind()
        {
            try
            {
                uint bestIndex;
                uint dest = BitConverter.ToUInt32(IPAddress.Parse("8.8.8.8").GetAddressBytes(), 0);
                if (GetBestInterface(dest, out bestIndex) == 0)
                {
                    foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (ni.OperationalStatus != OperationalStatus.Up) continue;
                        IPv4InterfaceProperties ipv4 = null;
                        try { ipv4 = ni.GetIPProperties().GetIPv4Properties(); } catch { }
                        if (ipv4 == null || ipv4.Index != (int)bestIndex) continue;
                        return ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                            ? NetworkKind.Wifi
                            : NetworkKind.Other;
                    }
                }
            }
            catch
            {
            }

            // 兜底：如果没有取到默认路由，只要存在已连接的 Wi-Fi 就按 Wi-Fi 处理。
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) continue;
                    foreach (UnicastIPAddressInformation addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(addr.Address))
                        {
                            return NetworkKind.Wifi;
                        }
                    }
                }
            }
            catch
            {
            }

            return HasAnyUpInterface() ? NetworkKind.Other : NetworkKind.None;
        }

        public static string Describe(NetworkKind kind)
        {
            if (kind == NetworkKind.Wifi) return "Wi-Fi";
            if (kind == NetworkKind.Other) return "有线/其它网络";
            return "无网络";
        }

        private static bool HasAnyUpInterface()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }
    }
}
