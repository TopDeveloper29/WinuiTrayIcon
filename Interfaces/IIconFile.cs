using System;

namespace WinuiTrayIcon.Interfaces;

internal interface IIconFile : IDisposable
{
    nint Handle { get; }
}