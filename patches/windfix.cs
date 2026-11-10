using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Win+D 兜底：UE 的 WS_POPUP 全屏窗口会吞掉系统的 Win+D，
// 这里挂低级键盘钩子，检测到 Win+D 后把「前台窗口 + 所有铺满显示器的窗口」一起最小化。
// 构建：csc /target:winexe /r:System.Windows.Forms.dll windfix.cs
class WinDFix {
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_KEYUP = 0x0101;
    const int WM_SYSKEYDOWN = 0x0104;
    const int WM_SYSKEYUP = 0x0105;
    const int VK_LWIN = 0x5B;
    const int VK_RWIN = 0x5C;
    const int VK_D = 0x44;
    const int SW_MINIMIZE = 6;
    const int GWL_EXSTYLE = -20;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int DWMWA_CLOAKED = 14;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int idx);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "windfix.log");
    static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.Ordinal) {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "MultitaskingViewFrame", "ForegroundStaging"
    };
    static long lastTrigger = 0;
    static LowLevelKeyboardProc proc;

    static void Log(string s) {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
    }

    static IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam) {
        try {
            if (nCode >= 0) {
                int msg = (int)wParam;
                int vk = Marshal.ReadInt32(lParam);
                bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

                // 锁屏或 UAC 安全桌面期间收不到 Win 键抬起，不能自己记录按下状态，只能实时查询
                if (isDown && vk == VK_D && WinHeld()) {
                    long now = Environment.TickCount;
                    if (now - lastTrigger > 400) {
                        lastTrigger = now;
                        ShowDesktop();
                    }
                }
            }
        } catch (Exception ex) {
            // 钩子里绝对不能抛异常：一抛出系统会摘钩、进程直接退出（2026-11-01 就是这样掉了）
            Log("HOOK ERROR " + ex.GetType().Name + ": " + ex.Message);
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    static bool WinHeld() {
        return ((GetAsyncKeyState(VK_LWIN) | GetAsyncKeyState(VK_RWIN)) & 0x8000) != 0;
    }

    static bool IsCloaked(IntPtr h) {
        try {
            int cloaked;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0) return cloaked != 0;
        } catch { }
        return false;
    }

    static bool CoversMonitor(IntPtr h) {
        RECT r;
        if (!GetWindowRect(h, out r)) return false;
        IntPtr mon = MonitorFromWindow(h, 2);
        var mi = new MONITORINFO();
        mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetMonitorInfo(mon, ref mi)) return false;
        int mw = mi.rcMonitor.Right - mi.rcMonitor.Left;
        int mh = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
        int ww = r.Right - r.Left;
        int wh = r.Bottom - r.Top;
        if (mw <= 0 || mh <= 0) return false;
        return ww >= mw * 95 / 100 && wh >= mh * 95 / 100;
    }

    // 回到桌面：前台窗口 + 所有铺满显示器的窗口一起最小化
    static void ShowDesktop() {
        var targets = new List<IntPtr>();
        IntPtr fg = GetForegroundWindow();

        EnumWindows((h, l) => {
            try {
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
                var cls = new StringBuilder(128);
                GetClassNameW(h, cls, 128);
                if (ShellClasses.Contains(cls.ToString())) return true;
                if (IsCloaked(h)) return true;
                bool isFore = h == fg;
                if (!isFore && !CoversMonitor(h)) return true;
                targets.Add(h);
            } catch { }
            return true;
        }, IntPtr.Zero);

        int done = 0;
        foreach (IntPtr h in targets) {
            try {
                ShowWindow(h, SW_MINIMIZE);
                done++;
            } catch { }
        }
        Log("Win+D -> minimized " + done + " window(s) [foreground=" + fg.ToInt64() + "]");
    }

    // 枚举窗口会累计系统 DLL 页面，定期回收工作集（私有内存不受影响）
    static void TrimLoop() {
        while (true) {
            Thread.Sleep(60000);
            try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
        }
    }

    static void Main() {
        proc = Hook;
        IntPtr hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
        Log("start hook=" + hook.ToInt64() + " err=" + Marshal.GetLastWin32Error());
        if (hook == IntPtr.Zero) {
            MessageBox.Show("键盘钩子安装失败，Win+D 兜底未生效。", "Win+D 兜底");
            return;
        }
        var t = new Thread(TrimLoop);
        t.IsBackground = true;
        t.Start();
        Application.Run();
        UnhookWindowsHookEx(hook);
    }
}
