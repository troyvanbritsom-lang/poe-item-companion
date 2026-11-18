using System;
using System.Diagnostics;
using System.IO;

namespace Poe2ItemCompanion
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath = null;

        public static string LogFilePath
        {
            get
            {
                if (_logFilePath == null)
                {
                    string baseDir = CompanionStorage.DataDirectory;
                    string logDir = Path.Combine(baseDir, "logs");
                    try
                    {
                        if (!Directory.Exists(logDir))
                        {
                            Directory.CreateDirectory(logDir);
                        }
                        _logFilePath = Path.Combine(logDir, "companion.log");
                    }
                    catch
                    {
                        // Fallback to base directory
                        _logFilePath = Path.Combine(baseDir, "companion.log");
                    }
                }
                return _logFilePath;
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message, null);
        }

        public static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public static void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        public static void Debug(string message)
        {
            Write("DEBUG", message, null);
        }

        private static void Write(string level, string message, Exception ex)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string line = string.Format("[{0}] [{1}] {2}", timestamp, level, message);

            if (ex != null)
            {
                line += Environment.NewLine + "Exception: " + ex.ToString();
            }

            // Also output to console if present
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
            }

            lock (_lock)
            {
                try
                {
                    string path = LogFilePath;
                    
                    // If log file exceeds 5MB, archive it
                    if (File.Exists(path))
                    {
                        var fi = new FileInfo(path);
                        if (fi.Length > 5 * 1024 * 1024)
                        {
                            string backup = Path.Combine(Path.GetDirectoryName(path), "companion_old.log");
                            try
                            {
                                if (File.Exists(backup)) File.Delete(backup);
                                File.Move(path, backup);
                            }
                            catch
                            {
                            }
                        }
                    }

                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch
                {
                    // Fail silently to never crash the main application because of logging
                }
            }
        }

        public static void OpenLogFile()
        {
            try
            {
                string path = LogFilePath;
                if (!File.Exists(path))
                {
                    Info("Log file initialized by user request.");
                }

                // Prefer our built-in dark-themed log viewer form so we don't depend on external notepad packages
                var form = new LogViewerForm();
                form.Show();
                form.BringToFront();
                form.Activate();
            }
            catch (Exception ex)
            {
                Error("Failed to open built-in log viewer: " + ex.Message, ex);

                // Fallback to classic Windows System32 notepad
                try
                {
                    string classicNotepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
                    if (File.Exists(classicNotepad))
                    {
                        Process.Start(classicNotepad, "\"" + LogFilePath + "\"");
                    }
                    else
                    {
                        Process.Start("explorer.exe", "/select,\"" + LogFilePath + "\"");
                    }
                }
                catch
                {
                }
            }
        }
    }
}
