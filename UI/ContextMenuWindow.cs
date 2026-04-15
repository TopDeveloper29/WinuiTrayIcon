using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinuiTrayIcon.UI;

/// <summary>
/// Lightweight popup window used as a system tray context menu.
/// This mimics native tray menus using a WinUI window + Win32 styling.
/// </summary>
internal class ContextMenuWindow
{ 
    #region Events

    /// <summary>
    /// Fired when a menu item is clicked.
    /// Sender = MenuFlyoutItem
    /// </summary>
    internal event EventHandler<MenuFlyoutItem>? ItemClicked;

    /// <summary>
    /// Fired when the menu is closed (lost focus or item click).
    /// </summary>
    public event EventHandler? MenuClosed;

    #endregion

    #region Constants (Win32 / DWM)

    private const uint SPI_GETWORKAREA = 0x0030;

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    private const int SW_SHOW = 5;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    #endregion

    #region Private Fields

    private readonly Window window;
    private bool ShowContextMenu => (window.Content as ItemsControl).Items.Count > 0;

    #endregion

    #region Public Properties

    /// <summary>
    /// Indicates whether the menu is currently visible.
    /// </summary>
    internal bool IsVisible { get; private set; } = false;

    #endregion

    #region Constructor

    internal ContextMenuWindow()
    {
        // Create base window
        window = new Window();

        // Root container (acts like a vertical menu list)
        window.Content = new ItemsControl()
        {
            IsTabStop = false,
            MinWidth = 125,
            Margin = new Thickness(4)
        };

        var hWnd = GetHwnd();

        // Apply native window styling
        UseRoundCorners(hWnd);
        SetTopMost(hWnd);

        SetWindowStyle(hWnd, WindowStyle.PopupWindow);
        AddWindowStyleEx(hWnd, WindowStyleEx.Layered | WindowStyleEx.TopMost | WindowStyleEx.AppWindow);

        // Visual styling (WinUI)
        window.SystemBackdrop = new DesktopAcrylicBackdrop();

        // Event hooks
        window.Activated += OnWindowActivated;
        window.AppWindow.Closing += OnWindowClosing;
        window.AppWindow.IsShownInSwitchers = false;

        // Apply theme based on system
        UpdateTheme(ShouldSystemUseDarkMode());
    }

    #endregion

    #region Internal API

    /// <summary>
    /// Displays the menu at the specified screen coordinates.
    /// </summary>
    internal void Show(int x, int y)
    {
        if (!ShowContextMenu)
            return;

        var hWnd = GetHwnd();
        var workArea = GetPrimaryWorkArea();

        // DPI scaling
        var scale = GetDpiForWindow(hWnd) / 96f;

        if (window.Content is not FrameworkElement root)
            return;

        // Measure content to determine size
        root.UpdateLayout();
        root.Measure(new Size(workArea.Width, workArea.Height));

        // Resize window based on measured size
        window.AppWindow?.Resize(new Windows.Graphics.SizeInt32(
            (int)(root.DesiredSize.Width * scale),
            (int)(root.DesiredSize.Height * scale)));

        var size = window.AppWindow?.Size;

        // Position: try right of cursor, fallback to left if overflow
        window.AppWindow?.Move(new Windows.Graphics.PointInt32
        {
            X = (x + size?.Width < workArea.Width ? x : x - size?.Width) ?? x,
            Y = y - size?.Height ?? y
        });

        // Bring to front and show
        SetForegroundWindow(hWnd);
        IsVisible = true;
        ShowWindow(hWnd, SW_SHOW);
    }

    /// <summary>
    /// Adds a clickable menu item.
    /// </summary>
    internal void AddItem(MenuFlyoutItemBase item)
    {
        if (window.Content is not ItemsControl items)
            return;

        items.Items.Add(item);
    }

    /// <summary>
    /// Adds a clickable menu item.
    /// </summary>
    internal void AddItem(MenuFlyoutItem item)
    {
        if (window.Content is not ItemsControl items)
            return;

        item.PreviewKeyDown += OnPreviewKeyDown;
        item.Click += OnItemClick;
        items.Items.Add(item);
    }

    /// <summary>
    /// Adds a clickable menu item.
    /// </summary>
    internal MenuFlyoutItem? AddItem(string Text, IconElement Icon = null )
    {
        var item = new MenuFlyoutItem
        {
            Padding = new Thickness(12, 6, 12, 6),
            Text = Text
        };

        if (Icon != null)
            item.Icon = Icon;

        AddItem(item);
        return item;
    }

    /// <summary>
    /// Adds a separator line.
    /// </summary>
    internal void AddSeparator()
    {
        var separator = new MenuFlyoutSeparator
        {
            Padding = new Thickness(12, 6, 12, 6),
            IsTabStop = false
        };

        AddItem(separator);
    }



    #endregion

    #region Window Event Handlers

    /// <summary>
    /// Prevent actual window destruction → just hide instead.
    /// </summary>
    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        window.AppWindow.Hide();
        IsVisible = false;
    }

    /// <summary>
    /// Auto-close when losing focus.
    /// </summary>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            window.AppWindow.Hide();
            IsVisible = false;
            MenuClosed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Handles keyboard navigation (Up/Down arrows).
    /// </summary>
    private void OnPreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (window.Content is not ItemsControl items)
            return;

        var index = items.Items.IndexOf(sender);
        if (index < 0) return;

        int direction = e.Key switch
        {
            Windows.System.VirtualKey.Up => -1,
            Windows.System.VirtualKey.Down => +1,
            _ => 0
        };

        if (direction == 0) return;

        // Find next visible item
        while (index + direction >= 0 && index + direction < items.Items.Count)
        {
            index += direction;

            if (items.Items[index] is MenuFlyoutItem item &&
                item.Visibility == Visibility.Visible)
            {
                item.Focus(FocusState.Programmatic);
                return;
            }
        }
    }

    /// <summary>
    /// Handles item click → closes menu + raises event.
    /// </summary>
    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        window.AppWindow.Hide();
        IsVisible = false;

        MenuClosed?.Invoke(this, EventArgs.Empty);

        if (sender is MenuFlyoutItem item)
        {
            ItemClicked?.Invoke(this, item);
        }
    }

    #endregion

    #region Theme / Appearance

    /// <summary>
    /// Applies dark/light mode to both Win32 and WinUI layers.
    /// </summary>
    private void UpdateTheme(bool isDarkTheme)
    {
        var hwnd = GetHwnd();
        int value = isDarkTheme ? 1 : 0;

        IntPtr ptr = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(ptr, value);
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ptr, sizeof(int));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        if (window.Content is FrameworkElement element)
        {
            element.RequestedTheme = isDarkTheme
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }
    }

    #endregion

    #region Native Helpers

    private HWND GetHwnd()
        => new HWND(WinRT.Interop.WindowNative.GetWindowHandle(window));

    private void SetWindowStyle(HWND hWnd, WindowStyle style)
        => SetWindowLongPtr(hWnd, GWL_STYLE, (nint)style);

    private void AddWindowStyleEx(HWND hWnd, WindowStyleEx style)
    {
        var current = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, current | (nint)style);
    }

    private static void SetTopMost(HWND hWnd)
    {
        const int HWND_TOPMOST = -1;
        SetWindowPos(hWnd, new HWND(HWND_TOPMOST), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    /// <summary>
    /// Enables Windows 11 rounded corners.
    /// </summary>
    private static void UseRoundCorners(HWND hWnd)
    {
        const uint DWMWCP_ROUND = 2;

        IntPtr ptr = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(ptr, (int)DWMWCP_ROUND);
            DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ptr, sizeof(uint));
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Gets usable screen area (excluding taskbar).
    /// </summary>
    private static Rect GetPrimaryWorkArea()
    {
        RECT rect = new();
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());

        try
        {
            Marshal.StructureToPtr(rect, ptr, false);
            SystemParametersInfo(SPI_GETWORKAREA, 0, ptr, 0);
            rect = Marshal.PtrToStructure<RECT>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        return new Rect(rect.left, rect.top,
                        rect.right - rect.left,
                        rect.bottom - rect.top);
    }

    #endregion

    #region Win32 Imports

    [DllImport("UxTheme.dll", EntryPoint = "#138", SetLastError = true)]
    private static extern bool ShouldSystemUseDarkMode();

    [DllImport("user32.dll")] private static extern bool SetWindowPos(HWND hWnd, HWND hWndInsertAfter, int X, int Y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtr(HWND hWnd, int index, nint value);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(HWND hWnd, int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(HWND hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(HWND hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(HWND hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, IntPtr data, uint winIni);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(HWND hwnd, int attr, IntPtr value, int size);

    #endregion

    #region Native Structs / Enums

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HWND
    {
        public nint Value;
        public HWND(nint value) => Value = value;

        public static implicit operator nint(HWND h) => h.Value;
        public static implicit operator HWND(nint h) => new HWND(h);
    }

    [Flags]
    private enum WindowStyle : long
    {
        PopupWindow = 0x80880000,
    }

    [Flags]
    private enum WindowStyleEx : long
    {
        Layered = 0x00080000,
        TopMost = 0x00000008,
        AppWindow = 0x00040000,
    }

    #endregion
}