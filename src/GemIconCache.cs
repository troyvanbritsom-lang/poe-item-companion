using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Poe2ItemCompanion
{
    internal sealed class GemIconCache : IDisposable
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Bitmap> _images = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly bool _poe1;

        internal static string DiscoverDirectory(string configured, GameId game, string applicationDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configured))
                return Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(applicationDirectory, configured));
            string folder = game == GameId.Poe1 ? "poe 1 gem" : "poe 2 gem";
            var parent = Directory.GetParent(applicationDirectory);
            string[] names = { folder, game == GameId.Poe1 ? "poe1_gems" : "poe2_gems" };
            foreach (string root in parent == null ? new[] { applicationDirectory } : new[] { applicationDirectory, parent.FullName })
                foreach (string name in names)
                {
                    string candidate = Path.Combine(root, name);
                    if (Directory.Exists(candidate)) return candidate;
                }
            return Path.Combine(parent == null ? applicationDirectory : parent.FullName, folder);
        }

        public GemIconCache(AppConfig config)
        {
            _poe1 = config.Game == GameId.Poe1;
            try
            {
                string directory = DiscoverDirectory(config.GemIconDirectory, config.Game, AppDomain.CurrentDomain.BaseDirectory);
                if (!Directory.Exists(directory))
                {
                    if (!string.IsNullOrWhiteSpace(config.GemIconDirectory))
                        Logger.Warn("Gem icon directory not found: " + directory + ". Gem rows will show text only.");
                    return;
                }
                foreach (var group in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(f => string.Equals(Path.GetExtension(f), ".png", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(f => Key(Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase))
                {
                    var files = group.ToArray();
                    if (files.Length == 1) _files.Add(group.Key, files[0]);
                    else Logger.Warn("Ambiguous gem icon filename: " + group.Key + ". Gem row will show text only.");
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)) throw;
                Logger.Warn("Cannot index local gem icons: " + ex.Message + ". Gem rows will show text only.");
            }
        }

        private static string Key(string name)
        {
            string text = (name ?? "").Replace('_', ' ').Trim();
            const string suffix = " inventory icon";
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - suffix.Length);
            return text;
        }

        public Bitmap Get(string name, string kind = null)
        {
            string key = Key(name);
            if (_poe1 && kind == "Support" && !key.EndsWith(" Support", StringComparison.OrdinalIgnoreCase))
                key += " Support";
            Bitmap image;
            if (_images.TryGetValue(key, out image)) return image;
            string file;
            if (!_files.TryGetValue(key, out file)) return null;
            image = null;
            try
            {
                using (var stream = File.OpenRead(file))
                using (var source = Image.FromStream(stream))
                    image = new Bitmap(source);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException ||
                    ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException)) throw;
                Logger.Warn("Cannot load gem icon " + file + ": " + ex.Message + ". Gem row will show text only.");
            }
            _images[key] = image;
            return image;
        }

        public void Dispose()
        {
            foreach (var image in _images.Values) if (image != null) image.Dispose();
            _images.Clear();
            _files.Clear();
        }
    }
}
