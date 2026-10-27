using System;
using System.IO;
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

        private static dynamic Connect()
        {
            Type type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) throw new InvalidOperationException("当前系统不支持计划任务服务");
            dynamic service = Activator.CreateInstance(type);
            service.Connect();
            return service;
        }

        public static bool IsEnabled()
        {
            string value = CurrentRunValue();
            if (!string.IsNullOrEmpty(value)) return true;
            return File.Exists(StartupLink);
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
            bool runKeyOk = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    key.SetValue(RunValue, Quote(exePath) + " --tray", RegistryValueKind.String);
                    runKeyOk = true;
                }
                Log.Info("已写入开机自启注册表：" + exePath);
            }
            catch (Exception ex)
            {
                Log.Warn("写入开机自启注册表失败：" + ex.Message + "，改用启动文件夹。");
            }

            if (runKeyOk)
            {
                TryDelete(StartupLink);
                RemoveLegacyTask();
                return true;
            }

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
                RemoveLegacyTask();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("创建启动项失败：" + ex.Message);
                return false;
            }
        }

        public static void Disable()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null) key.DeleteValue(RunValue, false);
                }
                Log.Info("已删除开机自启注册表项。");
            }
            catch
            {
            }
            TryDelete(StartupLink);
            RemoveLegacyTask();
        }

        public static void Ensure(string exePath)
        {
            // 每次启动都按当前 exe 路径重写自启项，避免程序目录移动后仍启动旧版本。
            Enable(exePath);
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

        private static void RemoveLegacyTask()
        {
            try
            {
                dynamic service = Connect();
                dynamic root = service.GetFolder("\\");
                root.DeleteTask(TaskName, 0);
                Log.Info("已移除旧版开机自启计划任务。");
            }
            catch
            {
            }
        }
    }
}
