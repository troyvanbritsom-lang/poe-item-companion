using System;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace Poe2ItemCompanion
{
    public static class InputSimulator
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_LCONTROL = 0xA2;
        private const ushort VK_C = 0x43;
        private const ushort VK_MENU = 0x12;  // Alt
        private const ushort VK_SHIFT = 0x10;
        private const ushort VK_D = 0x44;
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_RWIN = 0x5C;

        // Must match the native INPUT size (union includes MOUSEINPUT), otherwise SendInput fails with error 87
        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public int type;
            [FieldOffset(8)] public KEYBDINPUT ki;
            [FieldOffset(8)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static string CopyHoveredItem(AppConfig config = null)
        {
            Logger.Debug("Beginning hovered item copy sequence...");

            // Release any currently pressed modifier keys (Ctrl, Alt, Shift) or configured hotkey trigger key
            int triggerKey = config != null && config.HotkeyKey != 0 ? config.HotkeyKey : VK_D;
            int waitRelease = 0;
            while (((GetAsyncKeyState(triggerKey) & 0x8000) != 0 ||
                    (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 ||
                    (GetAsyncKeyState(VK_MENU) & 0x8000) != 0 ||
                    (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) && waitRelease < 75)
            {
                Thread.Sleep(20);
                waitRelease++;
            }
            Logger.Debug("Hotkey keys released after " + (waitRelease * 20) + "ms" + (waitRelease >= 75 ? " (TIMED OUT, keys still held)" : ""));
            LogForegroundWindow();

            ReleaseModifiers();
            Thread.Sleep(60);

            // Remember what was on the clipboard so we only accept a fresh capture
            uint initialSeq = GetClipboardSequenceNumber();
            string result = null;

            for (int attempt = 1; attempt <= 3 && result == null; attempt++)
            {
                SendCtrlC();

                // Poll for a clipboard update (up to ~700ms per attempt)
                for (int i = 0; i < 14; i++)
                {
                    Thread.Sleep(50);
                    if (GetClipboardSequenceNumber() != initialSeq)
                    {
                        string text = GetClipboardTextSafe();
                        if (IsPoeItemText(text))
                        {
                            result = text;
                            break;
                        }
                    }
                }

                if (result == null)
                {
                    Logger.Debug("Attempt " + attempt + ": clipboard did not change after Ctrl+C (sequence " + initialSeq + " -> " + GetClipboardSequenceNumber() + ")");
                }
            }

            // Fallback: check current clipboard text even if sequence didn't change
            if (string.IsNullOrEmpty(result))
            {
                string text = GetClipboardTextSafe();
                if (IsPoeItemText(text))
                {
                    result = text;
                }
            }

            if (!string.IsNullOrEmpty(result))
            {
                string firstLine = result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                Logger.Info("Captured item from clipboard: " + firstLine);
            }
            else
            {
                string currentClip = GetClipboardTextSafe();
                string snippet = string.IsNullOrEmpty(currentClip) ? "(clipboard empty)" : (currentClip.Length > 60 ? currentClip.Substring(0, 60) + "..." : currentClip);
                Logger.Warn("No Path of Exile item text found on clipboard after Ctrl+C. Current clipboard: " + snippet);
            }

            return result;
        }

        public static void ReleaseModifiers()
        {
            ushort[] keysToRelease = new ushort[] { VK_MENU, VK_SHIFT, VK_LWIN, VK_RWIN };
            INPUT[] inputs = new INPUT[keysToRelease.Length];
            for (int i = 0; i < keysToRelease.Length; i++)
            {
                inputs[i] = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT
                    {
                        wVk = keysToRelease[i],
                        wScan = (ushort)MapVirtualKey(keysToRelease[i], 0),
                        dwFlags = KEYEVENTF_KEYUP
                    }
                };
            }
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        // Scan-code-only events: games reading raw input / DirectInput respond to these
        // where virtual-key events are ignored.
        private static void SendScanKey(ushort vk, bool up)
        {
            ushort scan = (ushort)MapVirtualKey(vk, 0);
            INPUT[] input = new INPUT[1];
            input[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0) }
            };
            uint sent = SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
            if (sent != 1)
            {
                Logger.Warn("SendInput failed for vk 0x" + vk.ToString("X") + " (Win32 error " + Marshal.GetLastWin32Error() + ")");
            }
        }

        public static void SendCtrlC()
        {
            SendScanKey(VK_CONTROL, false);
            Thread.Sleep(40);
            SendScanKey(VK_C, false);
            Thread.Sleep(40);
            SendScanKey(VK_C, true);
            Thread.Sleep(40);
            SendScanKey(VK_CONTROL, true);
            Thread.Sleep(20);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private static void LogForegroundWindow()
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                var p = System.Diagnostics.Process.GetProcessById((int)pid);
                Logger.Debug("Foreground window at hotkey: " + p.ProcessName + " (\"" + p.MainWindowTitle + "\")");
            }
            catch (Exception ex)
            {
                Logger.Debug("Could not determine foreground window: " + ex.Message);
            }
        }

        public static bool IsPoeItemText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Contains("Rarity:") || text.Contains("Item Class:") || text.Contains("--------");
        }

        public static string GetClipboardTextSafe()
        {
            string text = null;
            // Clipboard access on Windows can occasionally fail with CLIPBRD_E_CANT_OPEN if another app has it open
            for (int retry = 0; retry < 6; retry++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        text = Clipboard.GetText();
                        break;
                    }
                }
                catch (ExternalException)
                {
                    Thread.Sleep(25);
                }
                catch
                {
                    break;
                }
            }
            return text;
        }
    }
}
