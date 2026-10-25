using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;

namespace PortalAutoLogin
{
    public class PortalResult
    {
        public bool Success;
        public bool AlreadyOnline;
        public string Result = "";
        public string Message = "";
        public string Raw = "";
        public string MaskedUrl = "";
    }

    internal class PageInfo
    {
        public string ProgramIndex = "";
        public string PageIndex = "";
        public int AccountPrefix = 0;
    }

    public class PortalClient
    {
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
        private readonly AppConfig _cfg;

        public PortalClient(AppConfig cfg)
        {
            _cfg = cfg;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public static string NormalizePortal(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            string s = input.Trim();
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                s = "http://" + s;
            }
            return s.TrimEnd('/');
        }

        public string LocalIp()
        {
            try
            {
                Uri uri = new Uri(_cfg.PortalUrl);
                using (var udp = new UdpClient())
                {
                    udp.Connect(uri.Host, 80);
                    string ip = ((IPEndPoint)udp.Client.LocalEndPoint).Address.ToString();
                    if (!string.IsNullOrEmpty(ip) && ip != "0.0.0.0" && !ip.StartsWith("127.")) return ip;
                }
            }
            catch
            {
            }
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = ua.Address.ToString();
                            if (!ip.StartsWith("127.") && !ip.StartsWith("169.254.")) return ip;
                        }
                    }
                }
            }
            catch
            {
            }
            return "0.0.0.0";
        }

        private static HttpWebRequest CreateRequest(string url, string method, int timeoutMs, bool allowRedirect)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = allowRedirect;
            req.Proxy = null;
            req.UserAgent = UserAgent;
            req.KeepAlive = false;
            req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9");
            return req;
        }

        public bool IsOnline()
        {
            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(_cfg.CheckUrl)) urls.Add(_cfg.CheckUrl.Trim());
            urls.Add("http://connect.rom.miui.com/generate_204");
            urls.Add("http://www.gstatic.com/generate_204");

            foreach (string url in urls)
            {
                try
                {
                    var req = CreateRequest(url, "GET", 4000, false);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        if ((int)resp.StatusCode == 204) return true;
                    }
                }
                catch
                {
                }
            }

            try
            {
                var req = CreateRequest("http://www.baidu.com/", "HEAD", 4000, false);
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode == 200) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private string GetPortalRedirect()
        {
            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(_cfg.CheckUrl)) urls.Add(_cfg.CheckUrl.Trim());
            urls.Add("http://connect.rom.miui.com/generate_204");

            foreach (string url in urls)
            {
                try
                {
                    var req = CreateRequest(url, "GET", 4000, false);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        string loc = resp.Headers["Location"];
                        if (!string.IsNullOrEmpty(loc) && loc.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return loc;
                    }
                }
                catch (WebException wex)
                {
                    var resp = wex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        string loc = resp.Headers["Location"];
                        resp.Close();
                        if (!string.IsNullOrEmpty(loc) && loc.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return loc;
                    }
                }
                catch
                {
                }
            }
            return null;
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

        private static string GetJson(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            int i = body.IndexOf('(');
            int j = body.LastIndexOf(')');
            if (i >= 0 && j > i) return body.Substring(i + 1, j - i - 1);
            return body;
        }

        private PageInfo LoadPageInfo(string userIp)
        {
            try
            {
                var ub = new UriBuilder(_cfg.PortalUrl);
                ub.Port = _cfg.EportalPort;
                ub.Path = "/eportal/portal/page/loadConfig";
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(userIp));
                ub.Query = "callback=dr1&program_index=&wlan_vlan_id=1&wlan_user_ip=" + Uri.EscapeDataString(b64) +
                           "&wlan_user_ipv6=Ojo%3D&wlan_user_ssid=&wlan_user_areaid=&wlan_ac_ip=&wlan_ap_mac=000000000000" +
                           "&gw_id=000000000000&page_index=&jsVersion=4.X&v=1234&lang=zh";

                var req = CreateRequest(ub.Uri.ToString(), "GET", 5000, false);
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    string body = GetJson(ReadBody(resp));
                    var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body);
                    if (data == null || !data.ContainsKey("data")) return null;
                    var inner = data["data"] as Dictionary<string, object>;
                    if (inner == null) return null;
                    var info = new PageInfo();
                    if (inner.ContainsKey("program_index")) info.ProgramIndex = Convert.ToString(inner["program_index"]);
                    if (inner.ContainsKey("page_index")) info.PageIndex = Convert.ToString(inner["page_index"]);
                    if (inner.ContainsKey("account_prefix")) info.AccountPrefix = ToInt(inner["account_prefix"]);
                    return info;
                }
            }
            catch
            {
                return null;
            }
        }

        private static int ToInt(object value)
        {
            if (value == null) return 0;
            int n;
            if (int.TryParse(Convert.ToString(value), out n)) return n;
            return 0;
        }

        public PortalResult Login()
        {
            var result = new PortalResult();
            string userIp = LocalIp();
            string mac = "000000000000";
            string acName = "";

            string redirect = GetPortalRedirect();
            if (!string.IsNullOrEmpty(redirect))
            {
                try
                {
                    var query = HttpUtility.ParseQueryString(new Uri(redirect).Query);
                    if (!string.IsNullOrEmpty(query["wlanuserip"])) userIp = query["wlanuserip"];
                    if (!string.IsNullOrEmpty(query["mac"])) mac = query["mac"];
                    if (!string.IsNullOrEmpty(query["wlanacname"])) acName = query["wlanacname"];
                }
                catch
                {
                }
            }

            PageInfo page = LoadPageInfo(userIp);
            string prefix = (page != null && page.AccountPrefix == 1) ? ",0," : "";
            string password = ConfigStore.UnprotectPassword(_cfg.PasswordProtected);

            var ps = new List<KeyValuePair<string, string>>();
            ps.Add(new KeyValuePair<string, string>("callback", "dr" + new Random().Next(1000, 9999).ToString(CultureInfo.InvariantCulture)));
            ps.Add(new KeyValuePair<string, string>("DDDDD", _cfg.Account));
            ps.Add(new KeyValuePair<string, string>("upass", password));
            ps.Add(new KeyValuePair<string, string>("0MKKey", "123456"));
            ps.Add(new KeyValuePair<string, string>("R1", "0"));
            ps.Add(new KeyValuePair<string, string>("R2", ""));
            ps.Add(new KeyValuePair<string, string>("R3", "0"));
            ps.Add(new KeyValuePair<string, string>("R6", "0"));
            ps.Add(new KeyValuePair<string, string>("para", "00"));
            ps.Add(new KeyValuePair<string, string>("v6ip", ""));
            ps.Add(new KeyValuePair<string, string>("R7", "0"));
            ps.Add(new KeyValuePair<string, string>("user_account", prefix + _cfg.Account));
            ps.Add(new KeyValuePair<string, string>("user_password", password));
            ps.Add(new KeyValuePair<string, string>("wlan_user_ip", userIp));
            ps.Add(new KeyValuePair<string, string>("wlan_user_ipv6", ""));
            ps.Add(new KeyValuePair<string, string>("authex_enable", ""));
            ps.Add(new KeyValuePair<string, string>("wlan_user_mac", mac));
            ps.Add(new KeyValuePair<string, string>("wlan_ac_name", acName));
            ps.Add(new KeyValuePair<string, string>("jsVersion", "4.X"));
            ps.Add(new KeyValuePair<string, string>("terminal_type", "1"));
            ps.Add(new KeyValuePair<string, string>("lang", "zh"));
            ps.Add(new KeyValuePair<string, string>("user_agent", UserAgent));
            ps.Add(new KeyValuePair<string, string>("enable_r3", "0"));
            ps.Add(new KeyValuePair<string, string>("mac_type", "0"));
            ps.Add(new KeyValuePair<string, string>("rcn", ""));
            ps.Add(new KeyValuePair<string, string>("operate", "portal_login"));
            ps.Add(new KeyValuePair<string, string>("business_type", ""));
            ps.Add(new KeyValuePair<string, string>("v", new Random().Next(500, 9999).ToString(CultureInfo.InvariantCulture)));
            if (page != null)
            {
                ps.Add(new KeyValuePair<string, string>("program_index", page.ProgramIndex));
                ps.Add(new KeyValuePair<string, string>("page_index", page.PageIndex));
            }

            var sb = new StringBuilder();
            var masked = new StringBuilder();
            foreach (var kv in ps)
            {
                if (sb.Length > 0) { sb.Append('&'); masked.Append('&'); }
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
                bool secret = kv.Key == "upass" || kv.Key == "user_password";
                masked.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(secret ? "***" : Uri.EscapeDataString(kv.Value));
            }

            string path = _cfg.LoginPath;
            if (!path.StartsWith("/")) path = "/" + path;
            string url = _cfg.PortalUrl.TrimEnd('/') + path + "?" + sb;
            string maskedUrl = _cfg.PortalUrl.TrimEnd('/') + path + "?" + masked;

            result.MaskedUrl = maskedUrl;
            Log.Info("本机IP=" + userIp + " MAC=" + mac + " AC=" + acName);
            Log.Info("登录请求：" + maskedUrl);

            try
            {
                var req = CreateRequest(url, "GET", 8000, false);
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    string body = ReadBody(resp);
                    result.Raw = body;
                    Log.Info("登录响应：" + body.Trim());

                    string json = GetJson(body);
                    Dictionary<string, object> data = null;
                    try
                    {
                        data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                    }
                    catch
                    {
                    }

                    if (data != null)
                    {
                        if (data.ContainsKey("result")) result.Result = Convert.ToString(data["result"]);
                        if (data.ContainsKey("msg")) result.Message = Convert.ToString(data["msg"]);
                        if (data.ContainsKey("msga")) result.Message = string.IsNullOrEmpty(result.Message) ? Convert.ToString(data["msga"]) : result.Message + " / " + Convert.ToString(data["msga"]);

                        string r = result.Result;
                        if (r == "1" || string.Equals(r, "ok", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Success = true;
                        }
                        else if (!string.IsNullOrEmpty(result.Message) &&
                                 result.Message.IndexOf("online", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Success = true;
                            result.AlreadyOnline = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                Log.Warn("登录请求异常：" + ex.Message);
            }

            return result;
        }
    }
}
