# WinuiTrayIcon

A small library that allow you to add a system tray icon to WinUI 3 applications.

It provides a simple way to display an icon in the notification area, handle mouse interactions, and show a Winui3 context menu.


## Features

* Tray icon support for WinUI 3
* Left and right click events on the icon
* Context menu
* Works in unpackaged apps
* Supports custom icon


## Installation
Nuget package:
```bash
dotnet add package WinuiTrayIcon
```

---

## Usage

### Create the tray icon

```csharp
// MainWindow.xaml.cs
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
var tray = new WinuiTrayIcon(hwnd);
```
OR
```csharp
// MainWindow.xaml.cs
var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
var tray = new WinuiTrayIcon(hwnd, "Application.ico");
```

> On the first scenario, a default system icon is used.


### Add menu items

```csharp
tray.AddMenuItem("Open");
tray.AddMenuItem("Settings");
tray.AddMenuSeparator();
tray.AddMenuItem("Exit");
```

You can also use full WinUI elements:

```csharp
tray.AddMenuItem(new MenuFlyoutItem
{
    Text = "Open"
});
```


### Handle events

```csharp
tray.LeftClicked += (s, e) =>
{
    // handle left click
};

tray.RightClicked += (s, e) =>
{
    // handle right click
};

tray.MenuItemClicked += (s, item) =>
{
    if (item.Text == "Exit")
    {
        Application.Current.Exit();
    }
};
```

---

## Properties

```csharp
tray.IsIconVisible = true;
tray.IconToolTip = "My application";
```

* `IsIconVisible`: shows or hides the icon
* `IconToolTip`: text shown when hovering the icon


## Notes

* Requires a valid window handle (`HWND`)
* Uses Win32 APIs under the hood
* The context menu is rendered in a separate window
