using System;
using Windows.Foundation;

namespace WinuiTrayIcon.Models;

internal class SystemTrayEventArgs : EventArgs
{
    public Rect Rect { get; init; }
}