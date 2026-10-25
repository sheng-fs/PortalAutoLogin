using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PortalAutoLogin
{
    internal static class Program
    {
        private static EventWaitHandle _showEvent;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            bool tray = HasArg(args, "--tray");
            if (HasArg(args, "--test"))
            {
                Environment.Exit(RunSelfTest());
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                Log.Error("未处理异常：" + (ex != null ? ex.ToString() : Convert.ToString(e.ExceptionObject)));
            };

            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\PortalAutoLogin.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting("Local\\PortalAutoLogin.Show"))
                        {
                            ev.Set();
                        }
                    }
                    catch
                    {
                    }
                    return;
                }

                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppConfig cfg = ConfigStore.Load();
                if (cfg == null)
                {
                    using (var setup = new SettingsForm(null))
                    {
                        if (setup.ShowDialog() != DialogResult.OK) return;
                    }
                    cfg = ConfigStore.Load();
                    if (cfg == null) return;
                }

                if (cfg.AutoStart) AutoStart.Ensure(Application.ExecutablePath);

                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\PortalAutoLogin.Show");
                var form = new MainForm(cfg, tray);

                var waiter = new Thread(delegate ()
                {
                    while (true)
                    {
                        try
                        {
                            _showEvent.WaitOne();
                            if (form.IsDisposed) break;
                            form.ShowFromTray();
                        }
                        catch
                        {
                            break;
                        }
                    }
                });
                waiter.IsBackground = true;
                waiter.Start();

                Log.Info("程序启动 v" + AppVersion + "（" + (tray ? "后台" : "前台") + "），配置文件：" + ConfigStore.ConfigFile);
                Application.Run(form);
            }
        }

        private static string AppVersion
        {
            get { return typeof(Program).Assembly.GetName().Version.ToString(3); }
        }

        private static bool HasArg(string[] args, string value)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int RunSelfTest()
        {
            var sb = new StringBuilder();
            try
            {
                sb.AppendLine("版本: v" + AppVersion);
                AppConfig cfg = ConfigStore.Load();
                Directory.CreateDirectory(ConfigStore.Dir);
                if (cfg == null)
                {
                    sb.AppendLine("结果: 未配置账号密码（请先运行应用完成首次设置）");
                    File.WriteAllText(Path.Combine(ConfigStore.Dir, "selftest.txt"), sb.ToString(), Encoding.UTF8);
                    return 1;
                }

                var client = new PortalClient(cfg);
                sb.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("账号: " + cfg.Account);
                sb.AppendLine("Portal: " + cfg.PortalUrl);
                sb.AppendLine("登录接口: " + cfg.LoginPath);
                sb.AppendLine("本机 IP: " + client.LocalIp());

                bool online = client.IsOnline();
                sb.AppendLine("登录前在线: " + online);

                PortalResult result = null;
                if (!online)
                {
                    result = client.Login();
                    sb.AppendLine("登录结果: " + (result.Success ? "成功" : "失败"));
                    sb.AppendLine("服务器结果码: " + result.Result);
                    sb.AppendLine("服务器消息: " + result.Message);
                    sb.AppendLine("登录后在线: " + client.IsOnline());
                }
                sb.AppendLine("结论: " + ((online || (result != null && (result.Success || client.IsOnline()))) ? "在线可用" : "未登录"));
                File.WriteAllText(Path.Combine(ConfigStore.Dir, "selftest.txt"), sb.ToString(), Encoding.UTF8);
                return online || (result != null && (result.Success || client.IsOnline())) ? 0 : 2;
            }
            catch (Exception ex)
            {
                sb.AppendLine("自检异常: " + ex.Message);
                try { File.WriteAllText(Path.Combine(ConfigStore.Dir, "selftest.txt"), sb.ToString(), Encoding.UTF8); } catch { }
                return 3;
            }
        }
    }
}
