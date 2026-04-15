using System;
using Windows.Foundation;

namespace WinuiTrayIcon.Models;

public class SystemTrayEventArgs : EventArgs
{
    public Rect Rect { get; init; }
}