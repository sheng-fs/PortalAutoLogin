using System;
using System.IO;

namespace PortalAutoLogin
{
    public static class AutoStart
    {
        private const string TaskName = "PortalAutoLogin";
        private const string StartupLnkName = "PortalAutoLogin.lnk";

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
            try
            {
                dynamic service = Connect();
                dynamic root = service.GetFolder("\\");
                dynamic task = root.GetTask(TaskName);
                return task != null;
            }
            catch
            {
                return File.Exists(StartupLink);
            }
        }

        private static string CurrentTaskPath()
        {
            try
            {
                dynamic service = Connect();
                dynamic root = service.GetFolder("\\");
                dynamic task = root.GetTask(TaskName);
                dynamic actions = task.Definition.Actions;
                if (actions.Count >= 1) return Convert.ToString(actions.Item(1).Path);
            }
            catch
            {
            }
            return null;
        }

        public static bool Enable(string exePath)
        {
            try
            {
                dynamic service = Connect();
                dynamic root = service.GetFolder("\\");
                dynamic def = service.NewTask(0);
                def.RegistrationInfo.Description = "PortalAutoLogin 门户自动登录（登录 Windows 后后台运行）";
                def.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
                def.Settings.Enabled = true;
                def.Settings.ExecutionTimeLimit = "PT0S";
                def.Settings.DisallowStartIfOnBatteries = false;
                def.Settings.StopIfGoingOnBatteries = false;
                def.Settings.StartWhenAvailable = true;
                def.Settings.MultipleInstances = 2; // TASK_INSTANCES_IGNORE_NEW
                def.Settings.RestartCount = 3;
                def.Settings.RestartInterval = "PT1M";

                dynamic trigger = def.Triggers.Create(9); // TASK_TRIGGER_LOGON
                trigger.UserId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                trigger.Delay = "PT5S";

                dynamic action = def.Actions.Create(0); // TASK_ACTION_EXEC
                action.Path = exePath;
                action.Arguments = "--tray";
                action.WorkingDirectory = Path.GetDirectoryName(exePath);

                root.RegisterTaskDefinition(TaskName, def, 6, null, null, 3);
                Log.Info("已注册开机自启计划任务：" + exePath);
                if (File.Exists(StartupLink)) File.Delete(StartupLink);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("注册计划任务失败：" + ex.Message + "，改用启动文件夹。");
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
                catch (Exception ex2)
                {
                    Log.Error("创建启动项失败：" + ex2.Message);
                    return false;
                }
            }
        }

        public static void Disable()
        {
            try
            {
                dynamic service = Connect();
                dynamic root = service.GetFolder("\\");
                root.DeleteTask(TaskName, 0);
                Log.Info("已删除开机自启计划任务。");
            }
            catch
            {
            }
            try
            {
                if (File.Exists(StartupLink)) File.Delete(StartupLink);
            }
            catch
            {
            }
        }

        public static void Ensure(string exePath)
        {
            string current = CurrentTaskPath();
            if (current == null || !string.Equals(current, exePath, StringComparison.OrdinalIgnoreCase))
            {
                Enable(exePath);
            }
        }
    }
}
