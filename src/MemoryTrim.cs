using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PortalAutoLogin
{
    internal static class MemoryTrim
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, int minSize, int maxSize);

        public static void Trim()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                using (Process process = Process.GetCurrentProcess())
                {
                    SetProcessWorkingSetSize(process.Handle, -1, -1);
                    Log.Info("内存整理完成，工作集约 " + (process.WorkingSet64 / 1024 / 1024) + " MB");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("内存整理失败：" + ex.Message);
            }
        }
    }
}
