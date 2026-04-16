using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using WinuiTrayIcon.Interfaces;
using WinuiTrayIcon.UI;

namespace WinuiTrayIcon;

/// <summary>
/// High-level manager for system tray icon + context menu.
/// Handles:
/// - Tray icon lifecycle
/// - Left/right click behavior
/// - Context menu display
/// </summary>
public class TrayIcon : IDisposable
{
    #region Events

    /// <summary>
    /// Fired when the tray icon is left-clicked.
    /// </summary>
    public event EventHandler? LeftClicked;

    /// <summary>
    /// Fired when the tray icon is right-clicked.
    /// </summary>
    public event EventHandler? RightClicked;

    /// <summary>
    /// Fired when a menu item is clicked.
    /// Sender = MenuFlyoutItem
    /// </summary>
    public event EventHandler<MenuFlyoutItem>? MenuItemClicked;

    #endregion

    #region Private Fields

    private SystemTrayIcon? systemTrayIcon;
    private ContextMenuWindow? contextMenuWindow;

    private string iconToolTip = "";

    #endregion

    #region Public Properties


    /// <summary>
    /// Gets or sets tray icon visibility.
    /// </summary>
    public bool IsIconVisible
    {
        get => systemTrayIcon?.IsVisible ?? false;
        set
        {
            if (systemTrayIcon == null) return;

            if (value)
                systemTrayIcon.Show();
            else
                systemTrayIcon.Hide();
        }
    }

    /// <summary>
    /// Tooltip displayed when hovering the tray icon.
    /// </summary>
    public string IconToolTip
    {
        get => iconToolTip;
        set
        {
            iconToolTip = value;

            if (systemTrayIcon != null)
                systemTrayIcon.Text = iconToolTip;
        }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes the tray manager.
    /// </summary>
    /// <param name="hwnd">Owner window handle</param>
    /// <param name="iconPath">Optional .ico file path</param>
    public TrayIcon(nint hwnd, string? iconPath = null)
    {
        // Create tray icon
        systemTrayIcon = new SystemTrayIcon(hwnd)
        {
            Id = Guid.NewGuid(),
            Icon = iconPath == null
                ? new LibIcon("shell32.dll", 2)
                : new IcoIcon(iconPath),
            Text = iconToolTip
        };

        // Create context menu window
        contextMenuWindow = new ContextMenuWindow();

        // Wire events
        systemTrayIcon.LeftClick += (_, _) =>
            LeftClicked?.Invoke(this, EventArgs.Empty);

        contextMenuWindow.ItemClicked += (s, e) =>
            MenuItemClicked?.Invoke(this, e);

        systemTrayIcon.RightClick += (s, e) =>
        {
            RightClicked?.Invoke(this, EventArgs.Empty);
            var position = GetClickPosition(e.Rect);
            contextMenuWindow?.Show((int)position.X, (int)position.Y);
        };

        // Show icon immediately
        systemTrayIcon.Show();
    }

    #endregion

    #region Public Methods
    public void AddMenuItem(MenuFlyoutItemBase Item) => contextMenuWindow?.AddItem(Item);

    public void AddMenuItem(MenuFlyoutItem Item) => contextMenuWindow?.AddItem(Item);
    public MenuFlyoutItem? AddMenuItem(string Text) => contextMenuWindow?.AddItem(Text);

    public void AddMenuSeparator() => contextMenuWindow?.AddSeparator();
    
    /// <summary>
    /// Releases tray icon and menu resources.
    /// </summary>
    public void Dispose()
    {
        systemTrayIcon?.Dispose();
        systemTrayIcon = null;

        contextMenuWindow = null;
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Determines where to show the context menu.
    /// Falls back to cursor position if invalid.
    /// </summary>
    private static Point GetClickPosition(Rect rect)
    {
        if (double.IsInfinity(rect.X) || double.IsInfinity(rect.Y))
        {
            return GetMousePosition();
        }

        return new Point(rect.X, rect.Y);
    }

    /// <summary>
    /// Gets current cursor position using Win32.
    /// </summary>
    private static Point GetMousePosition()
    {
        return GetCursorPos(out POINT p)
            ? new Point(p.X, p.Y)
            : new Point(100, 100); // fallback
    }

    #endregion

    #region Win32 Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    #endregion

    #region Icon Implementations

    /// <summary>
    /// Loads an icon from a system DLL (e.g. shell32.dll).
    /// </summary>
    public sealed class LibIcon : IIconFile, IDisposable
    {
        private SafeIconHandle iconHandle;

        public LibIcon(string fileName, uint iconIndex)
        {
            var hIcon = ExtractIcon(nint.Zero, fileName, iconIndex);
            iconHandle = new SafeIconHandle(hIcon, true);

            if (iconHandle.IsInvalid)
                throw new InvalidOperationException("Cannot extract icon.");
        }

        public nint Handle => iconHandle.DangerousGetHandle();

        public void Dispose() => iconHandle.Dispose();

        #region Native

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern nint ExtractIcon(nint hInst, string file, uint index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(nint hIcon);

        private sealed class SafeIconHandle : SafeHandle
        {
            public SafeIconHandle(nint handle, bool ownsHandle)
                : base(nint.Zero, ownsHandle)
            {
                SetHandle(handle);
            }

            public override bool IsInvalid => handle == nint.Zero;

            protected override bool ReleaseHandle()
                => DestroyIcon(handle);
        }

        #endregion
    }

    /// <summary>
    /// Loads an icon from a .ico file.
    /// </summary>
    public sealed class IcoIcon : IIconFile, IDisposable
    {
        private SafeIconHandle iconHandle;

        public IcoIcon(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Icon file not found.", path);

            var hIcon = LoadImageIcon(path);
            iconHandle = new SafeIconHandle(hIcon, true);

            if (iconHandle.IsInvalid)
                throw new InvalidOperationException("Cannot load .ico file.");
        }

        public nint Handle => iconHandle.DangerousGetHandle();

        public void Dispose() => iconHandle.Dispose();

        #region Native

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const uint LR_DEFAULTSIZE = 0x00000040;

        private static nint LoadImageIcon(string path)
        {
            return LoadImage(IntPtr.Zero, path, IMAGE_ICON, 0, 0,
                LR_LOADFROMFILE | LR_DEFAULTSIZE);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(nint hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern nint LoadImage(
            IntPtr hInst,
            string name,
            uint type,
            int cx,
            int cy,
            uint flags);

        private sealed class SafeIconHandle : SafeHandle
        {
            public SafeIconHandle(nint handle, bool ownsHandle)
                : base(nint.Zero, ownsHandle)
            {
                SetHandle(handle);
            }

            public override bool IsInvalid => handle == nint.Zero;

            protected override bool ReleaseHandle()
                => DestroyIcon(handle);
        }

        #endregion
    }

    #endregion
}