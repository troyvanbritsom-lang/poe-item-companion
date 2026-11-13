using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace Poe2ItemCompanion
{
    public class HotkeyManager : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private readonly int HOTKEY_ID;

        public event Action HotkeyPressed;

        private bool _isRegistered = false;
        private int _currentModifiers = 0;
        private int _currentKey = 0;

        public bool IsRegistered { get { return _isRegistered; } }
        public int CurrentModifiers { get { return _currentModifiers; } }
        public int CurrentKey { get { return _currentKey; } }

        public HotkeyManager() : this(9001) { }

        public HotkeyManager(int id)
        {
            HOTKEY_ID = id;
            // Create an invisible message-only window for receiving WM_HOTKEY messages
            var cp = new CreateParams
            {
                Caption = "Poe2ItemCompanionHotkeyWindow",
                Parent = (IntPtr)(-3) // HWND_MESSAGE
            };
            CreateHandle(cp);
        }

        public bool Register(int modifiers, int key, out string error)
        {
            error = null;
            if (_isRegistered)
            {
                Unregister();
            }

            if (Handle == IntPtr.Zero)
            {
                error = "Message window handle not created";
                return false;
            }

            // MOD_NOREPEAT = 0x4000 (prevents repeated hotkey events if key held down)
            uint mod = (uint)modifiers | 0x4000;
            bool success = RegisterHotKey(Handle, HOTKEY_ID, mod, (uint)key);
            if (!success)
            {
                // Try without MOD_NOREPEAT on older Windows
                success = RegisterHotKey(Handle, HOTKEY_ID, (uint)modifiers, (uint)key);
            }

            if (!success)
            {
                int errCode = Marshal.GetLastWin32Error();
                error = string.Format("Failed to register hotkey (Win32 error {0}). Hotkey may already be in use by another application.", errCode);
                Logger.Error(error);
                return false;
            }

            _isRegistered = true;
            _currentModifiers = modifiers;
            _currentKey = key;
            Logger.Info(string.Format("Global hotkey registered successfully (Modifiers: {0}, Key: {1})", modifiers, (Keys)key));
            return true;
        }

        public void Unregister()
        {
            if (_isRegistered && Handle != IntPtr.Zero)
            {
                UnregisterHotKey(Handle, HOTKEY_ID);
                _isRegistered = false;
                Logger.Info("Global hotkey unregistered.");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                Logger.Debug("Global hotkey pressed.");
                var handler = HotkeyPressed;
                if (handler != null)
                {
                    handler();
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Unregister();
            DestroyHandle();
        }
    }
}
