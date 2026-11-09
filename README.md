# Photo Frame

Turns an idle Windows PC into a photo frame. After a few idle minutes, photos from a
folder drop onto the screen one at a time, like prints tossed onto a pile. Each lands a
little off square, and the pile always covers the whole screen. While the slideshow
runs, the monitors dim. Move the mouse or press a key to end it.

Runs on Windows 10 and 11, x64 and Arm64. One exe, no admin rights, nothing else to
install.

## Install

Download `PhotoFrame.exe` from [Releases](../../releases) and run it. It installs for
your user only, adds a desktop and Start menu shortcut, starts with Windows, and puts an
icon in the system tray.

The exe is unsigned, so the first run may show a SmartScreen warning. Choose
**More info**, then **Run anyway**.

To update, run a newer `PhotoFrame.exe`. To remove it, use **Settings > Apps**. Your
photos are never touched.

## Settings

Open them from the desktop shortcut or the tray icon.

**General**
- Photo folder
- Minutes idle before the slideshow starts
- Seconds per photo
- Monitor brightness during the slideshow, and the rest of the time (or leave it alone
  the rest of the time)
- Start with Windows

**Advanced**
- Include subfolders
- Prints on the pile, print size, tilt, and how far prints may run past the screen edge
- Keep Windows' own screensaver off

The tray icon can also start the slideshow now, or pause it for an hour.

## How it works

- **Brightness** changes over DDC/CI, the same control as the monitor's own buttons.
  Monitors without DDC/CI (most laptop screens, some TVs and docks) keep their
  brightness. The settings window says how many of yours respond.
- **Idle start.** The app watches for idle time itself and keeps Windows' own
  screensaver off. A screensaver that Windows starts can lock the PC when it ends, even
  with the lock option off.
- **No interruptions.** It does not start over a full-screen app or presentation, while
  a video holds the display on, or while the PC is locked.
- **Photos.** JPEG, PNG, BMP, GIF and TIFF. WebP and HEIC work once their codecs are
  installed from the Microsoft Store. Phone photos turn upright from their EXIF
  orientation.
- **Multiple monitors.** Each monitor gets its own pile.

Settings are stored in `HKEY_CURRENT_USER\Software\PhotoFrame`. The program lives in
`%LOCALAPPDATA%\Programs\PhotoFrame`, with its log beside it.

## Build

```
build.cmd
```

This uses the C# compiler that ships with Windows (.NET Framework 4.8), so no SDK is
needed. The result is `out\PhotoFrame.exe`. `tools\make-icon.ps1` redraws the icon.

## License

MIT
