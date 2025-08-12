using System;
using System.IO;
using System.Runtime.InteropServices;
using WindowsMediaController;

static class Program
{
    private static readonly MediaManager mediaManager = new();
    private static MediaManager.MediaSession? lastAllowedSession;

    static void Main()
    {
        mediaManager.Start();

        // Register media keys as global hotkeys so we can intercept them before SMTC
        NativeMethods.RegisterHotKey(IntPtr.Zero, 1, 0, NativeMethods.VK_MEDIA_PLAY_PAUSE);
        NativeMethods.RegisterHotKey(IntPtr.Zero, 2, 0, NativeMethods.VK_MEDIA_NEXT_TRACK);
        NativeMethods.RegisterHotKey(IntPtr.Zero, 3, 0, NativeMethods.VK_MEDIA_PREV_TRACK);

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
        NativeMethods.UnregisterHotKey(IntPtr.Zero, 1);
        NativeMethods.UnregisterHotKey(IntPtr.Zero, 2);
        NativeMethods.UnregisterHotKey(IntPtr.Zero, 3);
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

    private static class NativeMethods
    {
        public const int VK_MEDIA_NEXT_TRACK = 0xB0;
        public const int VK_MEDIA_PREV_TRACK = 0xB1;
        public const int VK_MEDIA_PLAY_PAUSE = 0xB3;
        public const int WM_HOTKEY = 0x0312;

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

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")] public static extern bool TranslateMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int nExitCode);
    }
}
