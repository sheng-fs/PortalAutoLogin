using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace PortalAutoLogin
{
    public static class AutoStart
    {
        private const string TaskName = "PortalAutoLogin";
        private const string StartupLnkName = "PortalAutoLogin.lnk";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "PortalAutoLogin";

        private static string StartupLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLnkName); }
        }

        public static bool IsEnabled()
        {
            if (TaskExists()) return true;
            return !string.IsNullOrEmpty(CurrentRunValue()) || File.Exists(StartupLink);
        }

        private static string CurrentRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return null;
                    return key.GetValue(RunValue) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string Quote(string path)
        {
            return "\"" + path + "\"";
        }

        public static bool Enable(string exePath)
        {
            // 优先用计划任务：除了登录，系统从睡眠唤醒并解锁时也会拉起，
            // 而注册表 Run 项只在真正登录时执行，睡眠唤醒不会触发。
            if (CreateTask(exePath))
            {
                TryDelete(StartupLink);
                RemoveRunValue();
                return true;
            }

            if (WriteRunValue(exePath))
            {
                TryDelete(StartupLink);
                return true;
            }

            return CreateStartupLink(exePath);
        }

        public static void Disable()
        {
            RemoveRunValue();
            TryDelete(StartupLink);
            DeleteTask();
            Log.Info("已关闭开机自启。");
        }

        public static void Ensure(string exePath)
        {
            // 每次启动都按当前 exe 路径重写自启项，避免程序目录移动后仍启动旧版本。
            Enable(exePath);
        }

        private static bool WriteRunValue(string exePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    key.SetValue(RunValue, Quote(exePath) + " --tray", RegistryValueKind.String);
                }
                Log.Info("已写入开机自启注册表：" + exePath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("写入开机自启注册表失败：" + ex.Message);
                return false;
            }
        }

        private static void RemoveRunValue()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null) key.DeleteValue(RunValue, false);
                }
            }
            catch
            {
            }
        }

        private static bool CreateStartupLink(string exePath)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic lnk = shell.CreateShortcut(StartupLink);
                lnk.TargetPath = exePath;
                lnk.Arguments = "--tray";
                lnk.WorkingDirectory = Path.GetDirectoryName(exePath);
                lnk.WindowStyle = 7;
                lnk.Save();
                Log.Info("已创建启动文件夹快捷方式：" + StartupLink);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("创建启动项失败：" + ex.Message);
                return false;
            }
        }

        private static bool CreateTask(string exePath)
        {
            string temp = null;
            try
            {
                string sid = WindowsIdentity.GetCurrent().User.Value;
                temp = Path.Combine(Path.GetTempPath(), "PortalAutoLogin.task.xml");
                File.WriteAllText(temp, BuildTaskXml(exePath, sid), Encoding.Unicode);

                if (!RunSchTasks("/create /tn \"" + TaskName + "\" /xml \"" + temp + "\" /f", false)) return false;
                if (!TaskExists()) return false;

                Log.Info("已注册登录/解锁自启计划任务：" + exePath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("注册计划任务失败：" + ex.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (temp != null && File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                }
            }
        }

        private static bool TaskExists()
        {
            return RunSchTasks("/query /tn \"" + TaskName + "\"", true);
        }

        private static void DeleteTask()
        {
            RunSchTasks("/delete /tn \"" + TaskName + "\" /f", true);
        }

        private static bool RunSchTasks(string arguments, bool quiet)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", arguments);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit(15000);
                    if (process.ExitCode != 0)
                    {
                        if (!quiet) Log.Warn("schtasks 执行失败（" + process.ExitCode + "）：" + output.Trim());
                        return false;
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (!quiet) Log.Warn("调用 schtasks 失败：" + ex.Message);
                return false;
            }
        }

        private static string BuildTaskXml(string exePath, string sid)
        {
            string user = XmlEscape(sid);
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                   "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                   "  <RegistrationInfo>\r\n" +
                   "    <Description>PortalAutoLogin 登录或解锁后在后台自动运行</Description>\r\n" +
                   "  </RegistrationInfo>\r\n" +
                   "  <Triggers>\r\n" +
                   "    <LogonTrigger>\r\n" +
                   "      <Enabled>true</Enabled>\r\n" +
                   "      <UserId>" + user + "</UserId>\r\n" +
                   "      <Delay>PT15S</Delay>\r\n" +
                   "    </LogonTrigger>\r\n" +
                   "    <SessionStateChangeTrigger>\r\n" +
                   "      <Enabled>true</Enabled>\r\n" +
                   "      <StateChange>SessionUnlock</StateChange>\r\n" +
                   "      <UserId>" + user + "</UserId>\r\n" +
                   "    </SessionStateChangeTrigger>\r\n" +
                   "  </Triggers>\r\n" +
                   "  <Principals>\r\n" +
                   "    <Principal id=\"Author\">\r\n" +
                   "      <UserId>" + user + "</UserId>\r\n" +
                   "      <LogonType>InteractiveToken</LogonType>\r\n" +
                   "      <RunLevel>LeastPrivilege</RunLevel>\r\n" +
                   "    </Principal>\r\n" +
                   "  </Principals>\r\n" +
                   "  <Settings>\r\n" +
                   "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                   "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                   "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                   "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                   "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                   "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                   "    <IdleSettings>\r\n" +
                   "      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n" +
                   "      <RestartOnIdle>false</RestartOnIdle>\r\n" +
                   "    </IdleSettings>\r\n" +
                   "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                   "    <Enabled>true</Enabled>\r\n" +
                   "    <Hidden>false</Hidden>\r\n" +
                   "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                   "    <WakeToRun>false</WakeToRun>\r\n" +
                   "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                   "    <Priority>7</Priority>\r\n" +
                   "  </Settings>\r\n" +
                   "  <Actions Context=\"Author\">\r\n" +
                   "    <Exec>\r\n" +
                   "      <Command>" + XmlEscape(exePath) + "</Command>\r\n" +
                   "      <Arguments>--tray</Arguments>\r\n" +
                   "    </Exec>\r\n" +
                   "  </Actions>\r\n" +
                   "</Task>\r\n";
        }

        private static string XmlEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;")
                       .Replace("<", "&lt;")
                       .Replace(">", "&gt;")
                       .Replace("\"", "&quot;")
                       .Replace("'", "&apos;");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
