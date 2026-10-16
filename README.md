# WinAirPlay · Mirror your iPad / iPhone to Windows

A Windows desktop app that works the moment you open it: your PC becomes an AirPlay
receiver, and your iPad / iPhone shows up in **Screen Mirroring** — pick it and the
screen (with audio) appears on Windows.

> No Start/Stop button. Launch the app and it immediately starts advertising over mDNS.
> On the iPad: Control Center → Screen Mirroring → choose your PC.

---

## Features

- **Screen mirroring** — iPad / iPhone / Mac screen to Windows (H.264 video + AAC-ELD audio)
- **Open-and-go** — auto-starts receiving on launch; your PC appears in Screen Mirroring instantly
- **Video embedded in the window** — the mirroring view is hosted inside the app, no extra pop-up
- **Audio** — AAC-ELD decoded via a built-in FFmpeg subprocess (no extra dependency required);
  optionally `libfdk-aac` for better quality
- **Renameable device** — change the name shown on the iPad in Settings; applies after auto-restart
- **Live log panel** — real-time receiver status for troubleshooting

---

## Credits

The receiver engine is derived from open-source projects (all MIT):

- [YimingZhanshen/Airplay2OnWindows](https://github.com/YimingZhanshen/Airplay2OnWindows) — the C# / .NET 8 AirPlay 2 receiver this engine is based on
- [SteeBono/airplayreceiver](https://github.com/SteeBono/airplayreceiver) — original C# AirPlay implementation (see `LICENSE`, © 2020 Stefano Bono)
- [FDH2/UxPlay](https://github.com/FDH2/UxPlay) — AirPlay protocol reference

Video is rendered with `ffplay` (FFmpeg). AAC-ELD audio falls back to an FFmpeg
subprocess decoder when the native FDK-AAC library is unavailable.

---

## Requirements

- Windows 10 / 11 (64-bit)
- iPad / iPhone on the **same Wi-Fi** as the PC
- Allow the firewall on first run (see below)

---

## Quick start

### Prebuilt (recommended)

1. Download `WinAirPlay-Setup-x.x.x.exe` from [Releases](https://github.com/Aiden6652/WinAirPlay/releases) (or a ZIP) and install / extract.
2. Run `WinAirPlay.exe`.
3. On the iPad: Control Center → Screen Mirroring → choose your PC.

### Build from source (Windows)

Prereq: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# from repo root — builds and downloads the FFmpeg dependency automatically
./tools/build.ps1
```

Output is in `dist\WinAirPlay\`; run `WinAirPlay.exe`.

> Smaller build without the bundled .NET runtime (requires .NET 8 Desktop Runtime installed):
> `./tools/build.ps1 -FrameworkDependent`

---

## First run: allow the firewall

The receiver listens on ports `5000 / 7000` and advertises mDNS (UDP 5353). Allow
`WinAirPlay.exe` and `Engine\AirPlay.exe` through the firewall (Private/Public).

Admin PowerShell:

```powershell
New-NetFirewallRule -DisplayName "WinAirPlay" -Direction Inbound -Program "$PWD\WinAirPlay.exe" -Action Allow
New-NetFirewallRule -DisplayName "WinAirPlay-Engine" -Direction Inbound -Program "$PWD\Engine\AirPlay.exe" -Action Allow
```

---

## Optional: better audio with libfdk-aac

The FFmpeg fallback already produces sound. For higher quality, build
`libfdk-aac-2.dll` and drop it into the `Engine\` folder:

```bash
# MSYS2 MinGW 64-bit terminal
pacman -S mingw-w64-x86_64-gcc mingw-w64-x86_64-cmake make
git clone https://github.com/mstorsjo/fdk-aac.git && cd fdk-aac
mkdir build && cd build
cmake -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON ..
cmake --build .
```

Copy the resulting `libfdk-aac-2.dll` into `Engine\`, restart the app.

---

## Build an installer (optional)

After building, compile `installer\WinAirPlay.iss` with [Inno Setup](https://jrsoftware.org/isdl.php)
to produce `WinAirPlay-Setup-x.x.x.exe`.

---

## License

MIT. See [`LICENSE`](LICENSE) (© 2020 Stefano Bono, from SteeBono/airplayreceiver).
This repository is a derivative work under the same license.

> Disclaimer: built only from open-source components, for educational/research use.
