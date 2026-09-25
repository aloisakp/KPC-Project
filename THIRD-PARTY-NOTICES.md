# Third-party notices

KPC Launcher uses these components under their respective licenses:

- [Velopack](https://github.com/velopack/velopack), MIT: installation and updates.
- [.NET runtime](https://github.com/dotnet/runtime) and
  [Windows Desktop](https://github.com/dotnet/wpf), MIT: application runtime and UI.
- [Avalonia](https://github.com/AvaloniaUI/Avalonia), MIT: native Linux interface and installer.
- [Inter](https://github.com/rsms/inter), SIL Open Font License: Linux interface font.
- [Skia](https://skia.org/), BSD-style, and [HarfBuzz](https://harfbuzz.github.io/), MIT-style: rendering and text shaping via Avalonia.
- System.Security.Cryptography.ProtectedData, MIT: Windows DPAPI protection of the
  remembered public Steam ID, verification date and community-server session.
  It stores no Steam login token.

Steam, Proton and Wine are installed separately. No game files, Steam credentials, or Steam client
binaries are included in the launcher or installer.
