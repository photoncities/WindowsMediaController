using System;
using System.IO;
using System.Runtime.InteropServices;
using WindowsMediaController;

static class Program
{
    private static readonly MediaManager mediaManager = new();
    private static MediaManager.MediaSession? lastAllowedSession;
    private static readonly NativeMethods.LowLevelKeyboardProc hookProc = HookCallback;
    private static IntPtr hookId = IntPtr.Zero;
    private static int threadId;

    static void Main()
    {
        mediaManager.Start();
        hookId = NativeMethods.SetHook(hookProc);
        threadId = NativeMethods.GetCurrentThreadId();

        if (hookId == IntPtr.Zero)
        {
            Console.Error.WriteLine($"Failed to install hook: {Marshal.GetLastWin32Error()}");
            return;
        }

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, __) => Cleanup();

        Console.WriteLine("MediaKeyFilter running. Press Ctrl+C to exit.");
        NativeMethods.MSG msg;
        while (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0) != 0)
        {
        }
        Cleanup();
    }

    private static void Cleanup()
    {
        if (hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }
        mediaManager.Dispose();
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN ||
                           wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == NativeMethods.VK_MEDIA_PLAY_PAUSE ||
                vkCode == NativeMethods.VK_MEDIA_NEXT_TRACK ||
                vkCode == NativeMethods.VK_MEDIA_PREV_TRACK)
            {
                var focused = mediaManager.GetFocusedSession();
                var exeName = focused != null ? Path.GetFileName(focused.Id) : "<none>";

                if (focused != null && exeName.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Blocked {exeName} {vkCode}");
                    if (lastAllowedSession != null)
                    {
                        SendCommand(lastAllowedSession, vkCode);
                    }
                    return (IntPtr)1;
                }
                else
                {
                    if (focused != null)
                        lastAllowedSession = focused;
                    Console.WriteLine($"Passed {exeName} {vkCode}");
                }
            }
        }
        return NativeMethods.CallNextHookEx(hookId, nCode, wParam, lParam);
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
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_QUIT = 0x0012;
        private const int WH_KEYBOARD_LL = 13;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public static IntPtr SetHook(LowLevelKeyboardProc proc)
            => SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool PostThreadMessage(int idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern int GetCurrentThreadId();

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }
    }
}

