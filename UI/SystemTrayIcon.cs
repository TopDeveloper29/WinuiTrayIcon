using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using WinuiTrayIcon.Interfaces;
using WinuiTrayIcon.Models;


namespace WinuiTrayIcon.UI;

internal class SystemTrayIcon : IDisposable
{
    private readonly WndProc windowProc, nativeWindowProc;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;

    private const uint MESSAGE_ID = 5800;
    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

    private string? text;
    private IIconFile? icon;
    private nint hwnd;
    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        ProcessMessage(msg, (nuint)wParam, lParam);
        return CallWindowProc(nativeWindowProc, hWnd, msg, wParam, lParam);
    }

    internal SystemTrayIcon(nint hwnd)
    {
        this.hwnd = hwnd;
        windowProc = new WndProc(WindowProc);

        if (hwnd == nint.Zero) return;
        hwnd = hwnd;

        var hWnd = new HWND(hwnd);
        var prevPtr = SetWindowLongPtr(hWnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(windowProc));
        if (prevPtr == nint.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SetWindowLongPtr failed: {err}");
        }
        nativeWindowProc = Marshal.GetDelegateForFunctionPointer<WndProc>(prevPtr);
       
    }

    ~SystemTrayIcon() => Dispose(false);

    internal bool IsVisible { get; private set; }

    internal Guid Id { get; init; }

    internal string Text
    {
        get => text ?? string.Empty;
        set
        {
            if (text == value) return;
            text = value;
            Update();
        }
    }

    internal IIconFile Icon
    {
        get => icon;
        set
        {
            if (icon != null)
                icon.Dispose();

            icon = value;
            Update();
        }
    }

    public event EventHandler<SystemTrayEventArgs>? LeftClick;
    public event EventHandler<SystemTrayEventArgs>? RightClick;

    internal void Show()
    {
        var data = GetData();

        if (Shell_NotifyIcon(NIM_ADD, ref data))
        {
            IsVisible = true;
        }
        else
        {
            var err = Marshal.GetLastWin32Error();
            Debug.WriteLine($"Shell_NotifyIcon failed: {err} - {new System.ComponentModel.Win32Exception(err).Message}");
        }
    }

    internal void Hide()
    {
        var data = GetData();

        if (Shell_NotifyIcon(NIM_DELETE, ref data))
            IsVisible = false;
    }

    private void Update()
    {
        if (IsVisible)
        {
            var data = GetData();
            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }
    }

    private NOTIFYICONDATAW GetData()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = hwnd != nint.Zero ? hwnd : Process.GetCurrentProcess().MainWindowHandle,
            uID = 0,
            uFlags = NIF_TIP | NIF_MESSAGE | NIF_GUID | NIF_ICON,
            uCallbackMessage = MESSAGE_ID,
            hIcon = Icon?.Handle ?? nint.Zero,
            guidItem = Id,
            szTip = Text
        };
        data.uVersion = 5;
        return data;
    }
    private void ProcessMessage(uint messageId, nuint wParam, nint lParam)
    {
        if (messageId == WM_TASKBARCREATED)
        {
            Show();
            return;
        }

        if (messageId != MESSAGE_ID)
            return;

        switch ((int)lParam)
        {
            case WM_LBUTTONUP:
                OnLeftClick(new SystemTrayEventArgs { Rect = GetIconRectangle() });
                break;

            case WM_CONTEXTMENU:
            case WM_RBUTTONUP:
                    OnRightClick(new SystemTrayEventArgs { Rect = GetIconRectangle() });
                break;
        }
    }
    private void OnLeftClick(SystemTrayEventArgs e) => LeftClick?.Invoke(this, e);
    private void OnRightClick(SystemTrayEventArgs e) => RightClick?.Invoke(this, e);

    private void Dispose(bool disposing)
    {
        Hide();
        if (icon != null)
            icon.Dispose();
    }

    internal void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private Rect GetIconRectangle()
    {
        var systemTray = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = hwnd != nint.Zero ? hwnd : Process.GetCurrentProcess().MainWindowHandle,
            guidItem = Id
        };

        return Shell_NotifyIconGetRect(ref systemTray, out RECT rect) != 0
            ? Rect.Empty
            : new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }

    // ============ Interop section ============

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_GUID = 0x00000020;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;

        // helper field
        public uint uVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private const int GWL_WNDPROC = -4;
    private const uint WM_CLOSE = 0x0010;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct HWND
    {
        public readonly nint Value;
        public HWND(nint value) => Value = value;
        public static implicit operator nint(HWND h) => h.Value;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(HWND hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW", SetLastError = true)]
    private static extern nint CallWindowProc(WndProc lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);
    void IDisposable.Dispose() => Dispose();
}
