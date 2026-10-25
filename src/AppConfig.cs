using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PortalAutoLogin
{
    public class AppConfig
    {
        public string Account = "";
        public string PasswordProtected = "";
        public string PortalUrl = "http://portal.example.com";
        public string LoginPath = "/drcom/login";
        public int EportalPort = 803;
        public string CheckUrl = "http://connect.rom.miui.com/generate_204";
        public string OpenUrl = "";
        public int IntervalSeconds = 15;
        public bool AutoStart = true;
        public bool Paused = false;
        public string LastLogin = "";
        public int TodayCount = 0;
        public string CountDate = "";
    }

    public static class ConfigStore
    {
        private static readonly object Gate = new object();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PortalAutoLogin.V1.2026");

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortalAutoLogin"); }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(Dir, "config.json"); }
        }

        public static bool Exists
        {
            get { return File.Exists(ConfigFile); }
        }

        public static AppConfig Load()
        {
            lock (Gate)
            {
                if (!File.Exists(ConfigFile)) return null;
                try
                {
                    string json = File.ReadAllText(ConfigFile, Encoding.UTF8);
                    var cfg = new JavaScriptSerializer().Deserialize<AppConfig>(json);
                    if (cfg == null) return null;
                    if (string.IsNullOrEmpty(cfg.PortalUrl)) cfg.PortalUrl = "http://portal.example.com";
                    if (string.IsNullOrEmpty(cfg.LoginPath)) cfg.LoginPath = "/drcom/login";
                    if (cfg.EportalPort <= 0) cfg.EportalPort = 803;
                    if (cfg.IntervalSeconds < 5) cfg.IntervalSeconds = 15;
                    if (string.IsNullOrEmpty(cfg.CheckUrl)) cfg.CheckUrl = "http://connect.rom.miui.com/generate_204";
                    return cfg;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool Save(AppConfig cfg)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    string json = new JavaScriptSerializer().Serialize(cfg);
                    File.WriteAllText(ConfigFile, json, Encoding.UTF8);
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("保存配置失败：" + ex.Message);
                    return false;
                }
            }
        }

        public static string ProtectPassword(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            byte[] raw = Encoding.UTF8.GetBytes(plain);
            byte[] enc = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        public static string UnprotectPassword(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText)) return "";
            try
            {
                byte[] enc = Convert.FromBase64String(protectedText);
                byte[] raw = ProtectedData.Unprotect(enc, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(raw);
            }
            catch
            {
                return "";
            }
        }

        public static string EffectiveOpenUrl(AppConfig cfg)
        {
            return string.IsNullOrWhiteSpace(cfg.OpenUrl) ? cfg.PortalUrl : cfg.OpenUrl;
        }
    }
}
