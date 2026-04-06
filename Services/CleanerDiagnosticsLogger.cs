using System;
using System.IO;
using System.Text;

namespace BlueSapphire.Services
{
    internal static class CleanerDiagnosticsLogger
    {
        private static readonly object SyncRoot = new();

        public static void Trace(string source, string message)
        {
            try
            {
                string rootPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BlueSapphire",
                    "Diagnostics");
                Directory.CreateDirectory(rootPath);

                string logPath = Path.Combine(rootPath, "cleaner-trace.log");
                StringBuilder builder = new();
                builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
                builder.Append(" [");
                builder.Append(source);
                builder.Append("] ");
                builder.AppendLine(message);

                lock (SyncRoot)
                {
                    File.AppendAllText(logPath, builder.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 诊断日志不能影响主流程。
            }
        }
    }
}
