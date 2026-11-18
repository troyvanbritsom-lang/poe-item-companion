using System;
using System.IO;

namespace Poe2ItemCompanion
{
    internal static class CompanionStorage
    {
        internal const string InstalledMutex = "PoeItemCompanion.Installed";
        internal static readonly bool Installed = File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installed.mode"));

        internal static string DataDirectory
        {
            get
            {
                if (!Installed) return AppDomain.CurrentDomain.BaseDirectory;
                string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (string.IsNullOrEmpty(local) || !Path.IsPathRooted(local))
                    local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) throw new IOException("Windows did not provide a local application data directory.");
                return Path.Combine(local, "PoeItemCompanion", "UserData");
            }
        }
    }
}
