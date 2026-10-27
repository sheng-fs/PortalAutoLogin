using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PortalAutoLogin
{
    public class PortalDetection
    {
        public bool Online;
        public string PortalUrl = "";
        public string Evidence = "";
    }

    public static class PortalDetector
    {
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

        private class Probe
        {
            public string Url;
            public string Marker;
        }

        private static readonly Probe[] DefaultProbes = new[]
        {
            new Probe { Url = "http://connect.rom.miui.com/generate_204" },
            new Probe { Url = "http://www.gstatic.com/generate_204" },
            new Probe { Url = "http://www.apple.com/library/test/success.html", Marker = "Success" },
            new Probe { Url = "http://www.msftconnecttest.com/connecttest.txt", Marker = "Microsoft Connect Test" },
            new Probe { Url = "http://detectportal.firefox.com/success.txt", Marker = "success" }
        };

        private static readonly Regex StatusRegex = new Regex(@"^(https?):\/\/([^\/:?#]+)(?::(\d+))?", RegexOptions.IgnoreCase);
        private static readonly Regex MetaRefreshRegex = new Regex(
            @"<meta[^>]+http-equiv\s*=\s*[""']?refresh[""']?[^>]+content\s*=\s*[""']?[^""'>]*url\s*=\s*(?<url>[^""'>\s]+)",
            RegexOptions.IgnoreCase);
        private static readonly Regex ScriptRedirectRegex = new Regex(
            @"(?:window\.|document\.)?location(?:\.href)?\s*(?:=|\.replace\(|\.assign\()\s*[""'](?<url>https?:\/\/[^""']+)[""']",
            RegexOptions.IgnoreCase);
        private static readonly Regex BaseHrefRegex = new Regex(
            @"<base[^>]+href\s*=\s*[""'](?<url>https?:\/\/[^""']+)[""']",
            RegexOptions.IgnoreCase);
        private static readonly Regex FormActionRegex = new Regex(
            @"<form[^>]+action\s*=\s*[""'](?<url>https?:\/\/[^""']+)[""']",
            RegexOptions.IgnoreCase);

        static PortalDetector()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public static PortalDetection Detect(string preferredCheckUrl)
        {
            var probes = new List<Probe>();
            if (!string.IsNullOrWhiteSpace(preferredCheckUrl))
            {
                probes.Add(new Probe { Url = preferredCheckUrl.Trim() });
            }
            probes.AddRange(DefaultProbes);

            foreach (Probe probe in probes)
            {
                PortalDetection result = DetectFromProbe(probe);
                if (result == null) continue;
                if (result.Online) return result;
                if (!string.IsNullOrEmpty(result.PortalUrl)) return result;
            }
            return new PortalDetection();
        }

        private static PortalDetection DetectFromProbe(Probe probe)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(probe.Url);
                req.Method = "GET";
                req.Timeout = 4000;
                req.ReadWriteTimeout = 4000;
                req.AllowAutoRedirect = false;
                req.Proxy = null;
                req.UserAgent = UserAgent;
                req.KeepAlive = false;
                req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9");
                req.Headers.Add("Cache-Control", "no-cache");

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    return Interpret(probe, (int)resp.StatusCode, resp.Headers["Location"], ReadBody(resp));
                }
            }
            catch (WebException wex)
            {
                var resp = wex.Response as HttpWebResponse;
                if (resp == null) return null;
                using (resp)
                {
                    return Interpret(probe, (int)resp.StatusCode, resp.Headers["Location"], ReadBody(resp));
                }
            }
            catch
            {
                return null;
            }
        }

        private static PortalDetection Interpret(Probe probe, int status, string location, string body)
        {
            if (status == 204)
            {
                return Online("HTTP 204");
            }
            if (status == 200)
            {
                if (!string.IsNullOrEmpty(probe.Marker) &&
                    body.IndexOf(probe.Marker, StringComparison.Ordinal) >= 0)
                {
                    return Online("HTTP 200 " + probe.Marker);
                }
                if (string.IsNullOrWhiteSpace(body))
                {
                    return Online("HTTP 200");
                }
            }

            if (status >= 300 && status < 400 && !string.IsNullOrEmpty(location))
            {
                string portal = BaseFrom(location, probe.Url);
                if (portal.Length > 0)
                {
                    return Portal(portal, "HTTP " + status + " → " + location);
                }
            }

            if (LooksLikeHtml(body))
            {
                string redirect = ExtractRedirect(body);
                if (redirect.Length > 0)
                {
                    string portal = BaseFrom(redirect, probe.Url);
                    if (portal.Length > 0)
                    {
                        return Portal(portal, "页面跳转 → " + redirect);
                    }
                }

                string dnsPortal = PortalFromDns(probe.Url);
                if (dnsPortal.Length > 0)
                {
                    return Portal(dnsPortal, "DNS 指向内网 " + dnsPortal);
                }
            }
            return null;
        }

        private static string PortalFromDns(string probeUrl)
        {
            try
            {
                var uri = new Uri(probeUrl);
                foreach (var address in Dns.GetHostAddresses(uri.Host))
                {
                    string ip = address.ToString();
                    if (IsPrivateIpv4(ip)) return "http://" + ip;
                }
            }
            catch
            {
            }
            return "";
        }

        private static bool IsPrivateIpv4(string ip)
        {
            IPAddress address;
            if (!IPAddress.TryParse(ip, out address)) return false;
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length != 4) return false;
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        private static PortalDetection Online(string evidence)
        {
            var result = new PortalDetection();
            result.Online = true;
            result.Evidence = evidence;
            return result;
        }

        private static PortalDetection Portal(string url, string evidence)
        {
            var result = new PortalDetection();
            result.PortalUrl = url;
            result.Evidence = evidence;
            return result;
        }

        private static bool LooksLikeHtml(string body)
        {
            if (string.IsNullOrEmpty(body)) return false;
            return body.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("<script", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("<meta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   body.IndexOf("<form", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractRedirect(string body)
        {
            Match match = MetaRefreshRegex.Match(body);
            if (!match.Success) match = ScriptRedirectRegex.Match(body);
            if (!match.Success) match = BaseHrefRegex.Match(body);
            if (!match.Success) match = FormActionRegex.Match(body);
            return match.Success ? match.Groups["url"].Value.Trim() : "";
        }

        public static string BaseFrom(string target, string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(target)) return "";
            string url = target.Trim();
            if (url.StartsWith("//", StringComparison.Ordinal)) url = "http:" + url;

            Match match = StatusRegex.Match(url);
            if (!match.Success) return "";

            string scheme = match.Groups[1].Value.ToLowerInvariant();
            string host = match.Groups[2].Value.ToLowerInvariant();
            string port = match.Groups[3].Success ? match.Groups[3].Value : "";
            if (host.Length == 0) return "";

            Match baseMatch = StatusRegex.Match(baseUrl ?? "");
            if (baseMatch.Success)
            {
                string baseHost = baseMatch.Groups[2].Value.ToLowerInvariant();
                if (string.Equals(host, baseHost, StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }
            }

            return scheme + "://" + host + (port.Length > 0 ? ":" + port : "");
        }

        private static string ReadBody(HttpWebResponse resp)
        {
            try
            {
                using (var stream = resp.GetResponseStream())
                {
                    if (stream == null) return "";
                    using (var reader = new StreamReader(stream, Encoding.GetEncoding("gb2312")))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                return "";
            }
        }
    }
}
