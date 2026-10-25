using System;
using System.IO;
using System.Text;

namespace PortalAutoLogin
{
    public static class Log
    {
        private static readonly object Gate = new object();

        public static string Dir
        {
            get { return Path.Combine(ConfigStore.Dir, "logs"); }
        }

        public static string FilePath
        {
            get { return Path.Combine(Dir, "portalautologin.log"); }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Write(string level, string message)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1024 * 1024)
                    {
                        string old = FilePath + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    string line = string.Format("{0} [{1}] {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), level, message);
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }
}
