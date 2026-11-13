using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Poe2ItemCompanion
{
    // Detects an in-game window (gem cutting, passive tree) by comparing small screen spots the user marked once
    // (mouse over a static part of the window + detection hotkey). Read-only screen capture, no OCR, no game memory.
    public class GemCutterDetector : IDisposable
    {
        public const int MaxSpots = 3;
        public const int SpotWidth = 140, SpotHeight = 36;
        // Minimum luminance standard deviation of a spot; plain dark areas (std ~5) also match other screens.
        public const double MinDetail = 12;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private class Spot
        {
            public Rectangle Bounds;
            public Bitmap Reference;
        }

        private readonly AppConfig _config;
        private readonly List<Spot> _spots = new List<Spot>();
        private string _loadedKey;
        private int _plain;
        private readonly List<string> _plainEntries = new List<string>();

        private readonly Func<List<string>> _spotList;
        private readonly string _prefix;

        public GemCutterDetector(AppConfig config) : this(config, () => config.GemDetectSpots, "spot-") { }

        // Same detection for another in-game window (e.g. the passive tree) with its own spot list and file prefix.
        public GemCutterDetector(AppConfig config, Func<List<string>> spotList, string filePrefix)
        {
            _config = config;
            _spotList = spotList;
            _prefix = filePrefix;
        }

        private List<string> Spots
        {
            get
            {
                var list = _spotList();
                return list ?? new List<string>();
            }
        }

        public static string GetFolder()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gemdetect");
        }

        public int SpotCount { get { EnsureLoaded(); return _spots.Count; } }

        // Saved spots that are too plain to recognise anything; they are ignored.
        public int PlainSpotCount { get { EnsureLoaded(); return _plain; } }

        public IEnumerable<Rectangle> SpotBounds
        {
            get { EnsureLoaded(); foreach (var s in _spots) yield return s.Bounds; }
        }

        private void EnsureLoaded()
        {
            string key = string.Join(";", Spots.ToArray());
            if (key == _loadedKey) return;
            ClearLoaded();
            _loadedKey = key;
            foreach (var entry in Spots)
            {
                var parts = entry.Split('|');
                if (parts.Length != 5) continue;
                int x, y, w, h;
                if (!int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y) || !int.TryParse(parts[2], out w) || !int.TryParse(parts[3], out h)) continue;
                string file = Path.Combine(GetFolder(), Path.GetFileName(parts[4]));
                if (!File.Exists(file)) continue;
                try
                {
                    using (var img = Image.FromFile(file))
                    {
                        var bmp = new Bitmap(img);
                        if (Detail(bmp) < MinDetail) { bmp.Dispose(); _plain++; _plainEntries.Add(entry); continue; }
                        _spots.Add(new Spot { Bounds = new Rectangle(x, y, w, h), Reference = bmp });
                    }
                }
                catch (Exception ex) { Logger.Warn("Gem cutter spot unreadable: " + ex.Message); }
            }
        }

        private void ClearLoaded()
        {
            _plain = 0;
            _plainEntries.Clear();
            foreach (var s in _spots) s.Reference.Dispose();
            _spots.Clear();
        }

        public static Rectangle SpotAround(Point center)
        {
            var area = System.Windows.Forms.Screen.FromPoint(center).Bounds;
            int x = Math.Max(area.Left, Math.Min(area.Right - SpotWidth, center.X - SpotWidth / 2));
            int y = Math.Max(area.Top, Math.Min(area.Bottom - SpotHeight, center.Y - SpotHeight / 2));
            return new Rectangle(x, y, SpotWidth, SpotHeight);
        }

        public static Bitmap Capture(Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
            return bmp;
        }

        // Stores a new reference spot (oldest is dropped beyond MaxSpots). Returns the saved bounds.
        public Rectangle AddSpot(Bitmap image, Rectangle bounds)
        {
            Directory.CreateDirectory(GetFolder());
            string name = _config.Game.ToString() + "-" + _prefix + Guid.NewGuid().ToString("N") + ".png";
            image.Save(Path.Combine(GetFolder(), name), ImageFormat.Png);
            var list = Spots;
            EnsureLoaded();
            foreach (var plain in _plainEntries.ToArray())
            {
                DeleteSpotFile(plain);
                list.Remove(plain);
            }
            list.Add(bounds.X + "|" + bounds.Y + "|" + bounds.Width + "|" + bounds.Height + "|" + name);
            while (list.Count > MaxSpots)
            {
                DeleteSpotFile(list[0]);
                list.RemoveAt(0);
            }
            _config.Save();
            return bounds;
        }

        public void ClearSpots()
        {
            foreach (var entry in Spots) DeleteSpotFile(entry);
            Spots.Clear();
            _config.Save();
            ClearLoaded();
            _loadedKey = null;
        }

        private static void DeleteSpotFile(string entry)
        {
            var parts = entry.Split('|');
            if (parts.Length != 5) return;
            try { File.Delete(Path.Combine(GetFolder(), Path.GetFileName(parts[4]))); } catch (Exception) { }
        }

        // Mean absolute RGB difference, 0 (identical) .. 255.
        public static double Difference(Bitmap a, Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height) return 255;
            if (ReferenceEquals(a, b)) return 0;
            var rect = new Rectangle(0, 0, a.Width, a.Height);
            var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(da.Stride) * a.Height;
                var pa = new byte[bytes];
                var pb = new byte[bytes];
                Marshal.Copy(da.Scan0, pa, 0, bytes);
                Marshal.Copy(db.Scan0, pb, 0, bytes);
                long sum = 0;
                for (int i = 0; i < bytes; i += 4)
                    sum += Math.Abs(pa[i] - pb[i]) + Math.Abs(pa[i + 1] - pb[i + 1]) + Math.Abs(pa[i + 2] - pb[i + 2]);
                return sum / (double)(bytes / 4 * 3);
            }
            finally
            {
                a.UnlockBits(da);
                b.UnlockBits(db);
            }
        }

        // Luminance standard deviation: how much detail (text, frames) the spot contains.
        public static double Detail(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * bmp.Height;
                var px = new byte[bytes];
                Marshal.Copy(data.Scan0, px, 0, bytes);
                double sum = 0, sum2 = 0;
                int n = 0;
                for (int i = 0; i + 3 < bytes; i += 4)
                {
                    double l = 0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2];
                    sum += l; sum2 += l * l; n++;
                }
                if (n == 0) return 0;
                double m = sum / n;
                return Math.Sqrt(Math.Max(0, sum2 / n - m * m));
            }
            finally { bmp.UnlockBits(data); }
        }

        public bool IsPoeForeground()
        {
            return GameIdentity.IsForeground(_config);
        }

        // Open only when EVERY checkable spot matches (one similar-looking spot must not trigger it).
        // null = cannot tell (no spots, or every spot is covered by 'exclude', e.g. the overlay itself).
        public bool? IsGemCutterOpen(Rectangle exclude)
        {
            EnsureLoaded();
            bool any = false;
            foreach (var s in _spots)
            {
                if (!exclude.IsEmpty && exclude.IntersectsWith(s.Bounds)) continue;
                any = true;
                using (var now = Capture(s.Bounds))
                    if (Difference(now, s.Reference) > _config.GemDetectThreshold) return false;
            }
            return any ? (bool?)true : null;
        }

        public void Dispose() { ClearLoaded(); }
    }
}
