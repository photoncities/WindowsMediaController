using System;
using System.IO;
using System.Runtime.InteropServices;
using WindowsMediaController;

static class Program
{
    private static readonly MediaManager mediaManager = new();
    private static MediaManager.MediaSession? lastAllowedSession;
    private static IntPtr hwnd;

    static void Main()
    {
        mediaManager.Start();

        hwnd = NativeMethods.GetConsoleWindow();

        // Register media keys as global hotkeys so we can intercept them before SMTC
        NativeMethods.RegisterHotKey(hwnd, 1, 0, NativeMethods.VK_MEDIA_PLAY_PAUSE);
        NativeMethods.RegisterHotKey(hwnd, 2, 0, NativeMethods.VK_MEDIA_NEXT_TRACK);
        NativeMethods.RegisterHotKey(hwnd, 3, 0, NativeMethods.VK_MEDIA_PREV_TRACK);

        // Register for raw HID input (e.g. Bluetooth remotes) and suppress legacy handling
        var rid = new NativeMethods.RAWINPUTDEVICE[]
        {
            new NativeMethods.RAWINPUTDEVICE
            {
                usUsagePage = NativeMethods.HID_USAGE_PAGE_CONSUMER,
                usUsage = NativeMethods.HID_USAGE_CONSUMER_CONTROL,
                dwFlags = NativeMethods.RIDEV_INPUTSINK | NativeMethods.RIDEV_NOLEGACY,
                hwndTarget = hwnd
            }
        };
        NativeMethods.RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; NativeMethods.PostQuitMessage(0); };
        AppDomain.CurrentDomain.ProcessExit += (_, __) => Cleanup();

        Console.WriteLine("MediaKeyFilter running. Press Ctrl+C to exit.");

        NativeMethods.MSG msg;
        while (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            if (msg.message == NativeMethods.WM_HOTKEY)
            {
                HandleKey(msg.wParam.ToInt32());
            }
            else if (msg.message == NativeMethods.WM_INPUT)
            {
                HandleRawInput(msg.lParam);
            }
            else
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }

        Cleanup();
    }

    private static void HandleKey(int id)
    {
        int vk = id switch
        {
            1 => NativeMethods.VK_MEDIA_PLAY_PAUSE,
            2 => NativeMethods.VK_MEDIA_NEXT_TRACK,
            3 => NativeMethods.VK_MEDIA_PREV_TRACK,
            _ => 0
        };
        if (vk == 0)
            return;

        var focused = mediaManager.GetFocusedSession();
        var exeName = focused != null ? Path.GetFileName(focused.Id) : "<none>";

        if (focused != null && exeName.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Blocked {exeName} {vk}");
            if (lastAllowedSession != null)
            {
                SendCommand(lastAllowedSession, vk);
            }
        }
        else if (focused != null)
        {
            lastAllowedSession = focused;
            Console.WriteLine($"Passed {exeName} {vk}");
            SendCommand(focused, vk);
        }
        else if (lastAllowedSession != null)
        {
            Console.WriteLine($"No focused session, forwarding {vk}");
            SendCommand(lastAllowedSession, vk);
        }
    }

    private static void Cleanup()
    {
        NativeMethods.UnregisterHotKey(hwnd, 1);
        NativeMethods.UnregisterHotKey(hwnd, 2);
        NativeMethods.UnregisterHotKey(hwnd, 3);
        mediaManager.Dispose();
    }

    private static void SendCommand(MediaManager.MediaSession session, int vk)
    {
        switch (vk)
        {
            case NativeMethods.VK_MEDIA_PLAY_PAUSE:
                var controls = session.ControlSession.GetPlaybackInfo().Controls;
                if (controls.IsPauseEnabled == true)
                    _ = session.ControlSession.TryPauseAsync();
                else if (controls.IsPlayEnabled == true)
                    _ = session.ControlSession.TryPlayAsync();
                break;
            case NativeMethods.VK_MEDIA_NEXT_TRACK:
                _ = session.ControlSession.TrySkipNextAsync();
                break;
            case NativeMethods.VK_MEDIA_PREV_TRACK:
                _ = session.ControlSession.TrySkipPreviousAsync();
                break;
        }
    }

    private static void HandleRawInput(IntPtr hRawInput)
    {
        uint dwSize = 0;
        NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>());
        IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
        try
        {
            if (NativeMethods.GetRawInputData(hRawInput, NativeMethods.RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>()) != dwSize)
                return;

            var header = Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer);
            if (header.dwType != NativeMethods.RIM_TYPEHID)
                return;

            IntPtr pHid = buffer + Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
            uint sizeHid = (uint)Marshal.ReadInt32(pHid);
            uint count = (uint)Marshal.ReadInt32(pHid, 4);
            IntPtr pData = pHid + 8;

            byte[] raw = new byte[sizeHid * count];
            Marshal.Copy(pData, raw, 0, raw.Length);

            for (int i = 0; i < count; i++)
            {
                ushort usage = BitConverter.ToUInt16(raw, i * (int)sizeHid);
                switch (usage)
                {
                    case NativeMethods.HID_USAGE_PLAY_PAUSE:
                        HandleKey(1);
                        break;
                    case NativeMethods.HID_USAGE_NEXT:
                        HandleKey(2);
                        break;
                    case NativeMethods.HID_USAGE_PREV:
                        HandleKey(3);
                        break;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static class NativeMethods
    {
        public const int VK_MEDIA_NEXT_TRACK = 0xB0;
        public const int VK_MEDIA_PREV_TRACK = 0xB1;
        public const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_INPUT = 0x00FF;
        public const uint RID_INPUT = 0x10000003;
        public const uint RIM_TYPEHID = 2;
        public const ushort HID_USAGE_PAGE_CONSUMER = 0x0C;
        public const ushort HID_USAGE_CONSUMER_CONTROL = 0x01;
        public const ushort HID_USAGE_PLAY_PAUSE = 0xCD;
        public const ushort HID_USAGE_NEXT = 0xB5;
        public const ushort HID_USAGE_PREV = 0xB6;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RIDEV_NOLEGACY = 0x00000030;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] public static extern bool TranslateMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int nExitCode);
        [DllImport("user32.dll")] public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);
        [DllImport("user32.dll")] public static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
    }
}
