using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenOMSI.SimHub
{
    internal sealed class TabletCommandResult
    {
        public TabletCommandResult(bool ok, string message)
        {
            Ok = ok;
            Message = message;
        }

        public bool Ok { get; private set; }
        public string Message { get; private set; }
    }

    // Bridges same-origin tablet commands into openOMSI's documented Lua key event.
    // NumpadAdd delimits frames; Numpad0-Numpad9 encode decimal digits.
    internal sealed class TabletCommandBridge
    {
        private const byte FrameKey = 0x6B; // VK_ADD (numpad +), unused as a game action
        private const byte FirstDigitKey = 0x60; // VK_NUMPAD0
        private static readonly object SendLock = new object();
        private static DateTime lastDispatchUtc = DateTime.MinValue;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT32
        {
            public uint Type;
            public InputUnion32 Data;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct InputUnion32
        {
            [FieldOffset(0)] public KEYBDINPUT32 Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT32
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public uint ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT64
        {
            public uint Type;
            public InputUnion64 Data;
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct InputUnion64
        {
            [FieldOffset(0)] public KEYBDINPUT64 Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT64
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
        private static extern uint SendInput32(uint inputCount, INPUT32[] inputs, int inputSize);

        [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
        private static extern uint SendInput64(uint inputCount, INPUT64[] inputs, int inputSize);

        private const uint KeyUp = 0x0002;

        public TabletCommandResult Dispatch(string json, string profile)
        {
            if (string.IsNullOrEmpty(profile) || string.Equals(profile, "none", StringComparison.OrdinalIgnoreCase))
                return new TabletCommandResult(false, "Dieser Bus hat noch kein Bedienprofil.");

            string frame;
            string action;
            string value;
            string normalizedProfile = profile.ToLowerInvariant();
            try
            {
                JObject request = JObject.Parse(json ?? "{}");
                action = ((string)request["action"] ?? "").Trim().ToLowerInvariant();
                value = ((string)request["value"] ?? "").Trim();
            }
            catch
            {
                return new TabletCommandResult(false, "Ungültiger Befehl.");
            }

            switch (action)
            {
                case "line": frame = "0"; break;
                case "umlauf": frame = "1"; break;
                case "confirm": frame = "2"; break;
                case "cancel": frame = "3"; break;
                case "digit":
                    if (!IsSingleDigit(value)) return new TabletCommandResult(false, "Ziffer muss 0 bis 9 sein.");
                    frame = "4" + value;
                    break;
                case "ticket":
                    if (!IsSingleDigit(value) || value == "0") return new TabletCommandResult(false, "Ticketnummer muss 1 bis 9 sein.");
                    frame = "5" + value;
                    break;
                case "sell": frame = "6"; break;
                default: return new TabletCommandResult(false, "Dieser Befehl ist nicht freigegeben.");
            }

            bool hasAlmex = normalizedProfile == "almex";
            bool hasAtron = normalizedProfile == "atron";
            bool hasAfr4 = normalizedProfile == "afr4";
            bool hasMvgCopilot = normalizedProfile == "mvg-copilot";
            bool hasIbox = normalizedProfile == "ibox" || normalizedProfile == "ibox-ibis";
            bool hasIbis = normalizedProfile == "ibis" || normalizedProfile == "ibis-ticketprinter";
            if (normalizedProfile == "ibox-ibis") hasIbis = true;
            bool hasTicketPrinter = hasAlmex || hasAtron || hasAfr4 || hasIbox || hasMvgCopilot || normalizedProfile == "ticketprinter" || normalizedProfile == "ibis-ticketprinter";
            if ((action == "line" || action == "umlauf" || action == "digit") && !hasAlmex && !hasIbis && !hasAtron && !hasAfr4 && !hasIbox && !hasMvgCopilot)
                return new TabletCommandResult(false, "Dieses Fahrzeugprofil unterstützt keine Routeneingabe.");
            if ((action == "ticket" || action == "sell") && !hasTicketPrinter)
                return new TabletCommandResult(false, "Dieses Fahrzeugprofil unterstützt keinen Ticketverkauf.");
            if ((action == "confirm" || action == "cancel") && !hasAlmex && !hasIbis && !hasAtron && !hasAfr4 && !hasIbox && !hasMvgCopilot && normalizedProfile != "ticketprinter")
                return new TabletCommandResult(false, "Dieser Befehl wird von diesem Fahrzeugprofil nicht unterstützt.");

            lock (SendLock)
            {
                if (!IsOpenOmsiForeground())
                    return new TabletCommandResult(false, "Bitte openOMSI am PC in den Vordergrund holen.");
                if ((DateTime.UtcNow - lastDispatchUtc).TotalMilliseconds < 70)
                    return new TabletCommandResult(false, "Einen Moment warten und erneut tippen.");

                try
                {
                    Press(FrameKey);
                    foreach (char digit in frame)
                    {
                        if (digit < '0' || digit > '9') continue;
                        Press((byte)(FirstDigitKey + (digit - '0')));
                    }
                    Press(FrameKey);
                    lastDispatchUtc = DateTime.UtcNow;
                    return new TabletCommandResult(true, "Befehl an openOMSI gesendet.");
                }
                catch
                {
                    return new TabletCommandResult(false, "Befehl konnte nicht gesendet werden.");
                }
            }
        }

        private static bool IsOpenOmsiForeground()
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            uint processId;
            if (GetWindowThreadProcessId(window, out processId) == 0 || processId == 0) return false;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                    return process.ProcessName.IndexOf("openomsi", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static void Press(byte key)
        {
            bool sent = false;
            if (IntPtr.Size == 8)
            {
                INPUT64[] inputs = new INPUT64[2];
                inputs[0].Type = inputs[1].Type = 1; // INPUT_KEYBOARD
                inputs[0].Data.Keyboard.VirtualKey = key;
                inputs[1].Data.Keyboard.VirtualKey = key;
                inputs[1].Data.Keyboard.Flags = KeyUp;
                sent = SendInput64(2, inputs, Marshal.SizeOf(typeof(INPUT64))) == 2;
            }
            else
            {
                INPUT32[] inputs = new INPUT32[2];
                inputs[0].Type = inputs[1].Type = 1;
                inputs[0].Data.Keyboard.VirtualKey = key;
                inputs[1].Data.Keyboard.VirtualKey = key;
                inputs[1].Data.Keyboard.Flags = KeyUp;
                sent = SendInput32(2, inputs, Marshal.SizeOf(typeof(INPUT32))) == 2;
            }
            if (!sent)
            {
                keybd_event(key, 0, 0, UIntPtr.Zero);
                Thread.Sleep(8);
                keybd_event(key, 0, KeyUp, UIntPtr.Zero);
            }
            Thread.Sleep(8);
        }

        private static bool IsSingleDigit(string value)
        {
            return value != null && value.Length == 1 && value[0] >= '0' && value[0] <= '9';
        }
    }
}
