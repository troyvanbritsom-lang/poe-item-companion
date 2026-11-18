using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    // One in-game overlay with its "mark detection spot" hotkey and screen-spot auto-open.
    // Showing/hiding by key goes through the single overlay selector hotkey (TrayApplicationContext).
    public class OverlayFeature : IDisposable
    {
        public string Title;                                // e.g. "Gem overlay"
        public string WindowName;                           // e.g. "gem cutting window"
        public Func<AppConfig, OverlayForm> Create;
        public Func<AppConfig, List<string>> Spots;
        public string SpotPrefix;
        public Func<AppConfig, bool> GetAutoShow;
        public Action<AppConfig, bool> SetAutoShow;
        public Func<AppConfig, int> MarkModifiers, MarkKey;
        public bool GemCutter;

        private AppConfig _config;
        private HotkeyManager _markHotkey;
        private GemCutterDetector _detector;
        private OverlayForm _overlay;
        private int _misses;
        private NotifyIcon _notify;
        private Action _menuChanged;

        public OverlayForm Overlay { get { return _overlay; } }
        public bool IsOpen { get { return _overlay != null && !_overlay.IsDisposed && _overlay.Visible; } }
        public int SpotCount { get { return _detector == null ? 0 : _detector.SpotCount; } }
        public bool AutoShow { get { return (!GemCutter || _config.Game == GameId.Poe2) && GetAutoShow(_config); } }

        public void Start(AppConfig config, int markId, NotifyIcon notify, Action menuChanged)
        {
            _notify = notify;
            _menuChanged = menuChanged;
            _markHotkey = new HotkeyManager(markId);
            _markHotkey.HotkeyPressed += MarkSpot;
            SetConfig(config);
        }

        // Settings replace the config object: rebind everything that reads or saves it.
        public void SetConfig(AppConfig config)
        {
            _config = config;
            if (_overlay != null) { _overlay.Dispose(); _overlay = null; }
            if (_detector != null) _detector.Dispose();
            _detector = new GemCutterDetector(config, () => Spots(config), SpotPrefix);
            _misses = 0;
        }

        // 'taken' holds hotkeys already registered (modifiers << 16 | key) so duplicates are skipped.
        public void RegisterHotkeys(HashSet<int> taken)
        {
            Register(_markHotkey, MarkModifiers(_config), GemCutter && _config.Game == GameId.Poe1 ? 0 : MarkKey(_config), Title + " detection spot", taken, false);
        }

        private void Register(HotkeyManager hotkey, int modifiers, int key, string what, HashSet<int> taken, bool warnUser)
        {
            hotkey.Unregister();
            if (key == 0) return;
            if (!taken.Add((modifiers << 16) | key))
            {
                Logger.Warn(what + " hotkey " + AppConfig.HotkeyText(modifiers, key) + " is already used by another hotkey; not registered.");
                return;
            }
            string err;
            if (!hotkey.Register(modifiers, key, out err))
            {
                Logger.Warn(what + " hotkey not registered: " + err);
                if (warnUser && _notify != null) _notify.ShowBalloonTip(4000, what + " hotkey", "Could not register " + AppConfig.HotkeyText(modifiers, key) + ". Pick another in Settings.", ToolTipIcon.Warning);
            }
        }

        private OverlayForm Ensure()
        {
            if (_overlay == null || _overlay.IsDisposed) _overlay = Create(_config);
            return _overlay;
        }

        public void Toggle()
        {
            Ensure().Toggle();
        }

        public void ClearSpots()
        {
            _detector.ClearSpots();
            _menuChanged();
        }

        // Hotkey with the mouse over a static part of the open in-game window.
        private void MarkSpot()
        {
            if ((GemCutter && _config.Game == GameId.Poe1) || !GameIdentity.IsForeground(_config))
            {
                _notify.ShowBalloonTip(3000, Title, "Open the selected game before marking its detection spot.", ToolTipIcon.Warning);
                return;
            }
            var bounds = GemCutterDetector.SpotAround(Cursor.Position);
            bool hidden = false;
            if (_overlay != null && !_overlay.IsDisposed && _overlay.Visible && _overlay.Bounds.IntersectsWith(bounds))
            {
                _overlay.Hide();
                hidden = true;
                System.Threading.Thread.Sleep(150);
            }
            double detail;
            using (var image = GemCutterDetector.Capture(bounds))
            {
                detail = GemCutterDetector.Detail(image);
                if (detail >= GemCutterDetector.MinDetail) _detector.AddSpot(image, bounds);
            }
            if (hidden) _overlay.Show();
            if (detail < GemCutterDetector.MinDetail)
            {
                Logger.Info(Title + " detection spot rejected: too plain (detail " + detail.ToString("0.0") + ").");
                _notify.ShowBalloonTip(5000, Title + " detection", "Spot not saved: that area is too plain and would also match other screens. " +
                    "Point at text or a frame that only this window shows (e.g. its title or search box) and press the key again.", ToolTipIcon.Warning);
                return;
            }
            _misses = 0;
            _menuChanged();
            _notify.ShowBalloonTip(3000, Title + " detection", "Spot " + _detector.SpotCount + "/" + GemCutterDetector.MaxSpots +
                " saved. The overlay now opens when this part of the screen looks the same (" + WindowName + " open).", ToolTipIcon.Info);
            if (AutoShow) ShowAuto();
        }

        private void ShowAuto()
        {
            var overlay = Ensure();
            if (overlay.Visible) return;
            overlay.AutoShown = true;
            overlay.ShowOverlay();
        }

        public int PlainSpotCount { get { return _detector == null ? 0 : _detector.PlainSpotCount; } }

        public bool AutoVisible { get { return _overlay != null && !_overlay.IsDisposed && _overlay.Visible && _overlay.AutoShown; } }

        public void HideAuto()
        {
            if (!AutoVisible) return;
            _overlay.AutoShown = false;
            _overlay.Hide();
        }

        // Called every ~500 ms while PoE2 is the foreground window. Returns the detection result.
        // 'suppress' (another overlay's window is detected) keeps this overlay from auto-opening.
        public bool? Poll(bool suppress)
        {
            if (suppress) { _misses = 0; HideAuto(); }
            if (!AutoShow || _detector.SpotCount == 0) return null;
            var bounds = _overlay != null && !_overlay.IsDisposed && _overlay.Visible ? _overlay.Bounds : Rectangle.Empty;
            bool? open;
            try { open = _detector.IsGemCutterOpen(bounds); }
            catch (Exception ex) { Logger.Debug(Title + " detection failed: " + ex.Message); return null; }
            if (suppress) return open;
            if (open == true)
            {
                _misses = 0;
                ShowAuto();
            }
            else if (open == false && _overlay != null && !_overlay.IsDisposed && _overlay.Visible && _overlay.AutoShown)
            {
                if (++_misses >= 2) HideAuto();
            }
            return open;
        }

        public bool IsPoeForeground() { return _detector.IsPoeForeground(); }

        public void Dispose()
        {
            if (_markHotkey != null) _markHotkey.Dispose();
            if (_overlay != null) _overlay.Dispose();
            if (_detector != null) _detector.Dispose();
        }
    }
}
