# WinAirPlay · 把 iPad / iPhone 投到 Windows

一个 Windows 桌面应用，让你**打开就能用**：Windows 电脑变成一台 AirPlay 接收器，iPad / iPhone 在「屏幕镜像」里直接选它，画面就投上来了（带声音）。

> 打开应用即自动开始接收，无需点击「开始 / 停止」。iPad 上：控制中心 → 屏幕镜像 → 选你的电脑，即可投屏。

---

## 功能特性

- **屏幕镜像**：将 iPad / iPhone / Mac 的屏幕投到 Windows，支持 H.264 视频 + AAC-ELD 音频
- **打开即用**：启动后自动通过 mDNS（Bonjour）广播，iPad 立刻能在「屏幕镜像」列表中看到你的电脑
- **画面自动嵌入主窗口**：投屏窗口直接嵌在应用里，无需额外弹窗
- **音频**：镜像音频默认用内置的 FFmpeg 子进程解码（**无需额外依赖**）；也可选配 `libfdk-aac` 获得更好音质
- **设备名可改**：在「设置」里改设备名（iPad 上显示的名字），应用自动重启生效
- **日志面板**：实时查看接收器状态，便于排查

---

## 原理与溯源

本仓库的接收引擎基于以下开源项目（均为 MIT 协议），并套了一层独立开发的 WPF GUI：

- [YimingZhanshen/Airplay2OnWindows](https://github.com/YimingZhanshen/Airplay2OnWindows) — C# / .NET 8 的 AirPlay 2 接收端（本引擎直接来源）
- [SteeBono/airplayreceiver](https://github.com/SteeBono/airplayreceiver) — 原始 C# AirPlay 接收实现（许可证见 `LICENSE`，© 2020 Stefano Bono）
- [FDH2/UxPlay](https://github.com/FDH2/UxPlay) — AirPlay 协议参考

视频渲染复用 `ffplay`（FFmpeg），音频 AAC-ELD 解码在原生 FDK-AAC 不可用时自动回退到 FFmpeg 子进程解码。

---

## 环境要求

- Windows 10 / 11（64 位）
- iPad / iPhone 与电脑在**同一 Wi-Fi 网络**
- 首次使用需放行防火墙（见下文）

---

## 快速开始

### 方式一：下载预编译包（推荐）

1. 到 [Releases](https://github.com/Aiden6652/WinAirPlay/releases) 下载 `WinAirPlay-vX.X.X.zip` 并解压，**解压后只有一个 `WinAirPlay.exe`**（.NET 运行时、WPF 原生库、引擎、FFmpeg 已全部内嵌）。
2. 双击 `WinAirPlay.exe`。
3. 在 iPad 上：控制中心 → 屏幕镜像 → 选择你的电脑。

### 方式二：从源码构建（Windows）

前置：[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# 在项目根目录执行（会自动编译并下载 FFmpeg 依赖）
./tools/build.ps1
```

构建产物在 `dist\WinAirPlay\`，直接运行其中的 `WinAirPlay.exe` 即可。

> 想生成更小的包（不内置 .NET 运行时，需本机已装 .NET 8 桌面运行时）：
> `./tools/build.ps1 -FrameworkDependent`

---

## 首次使用：放行防火墙

接收器会监听 `5000 / 7000` 等端口并广播 mDNS（UDP 5353）。**首次运行时 Windows 防火墙可能拦截**。本程序的所有组件（含自动解压出的引擎）都打包在单个 `WinAirPlay.exe` 内，推荐直接按端口放行，省去逐个程序配置的麻烦（管理员 PowerShell）：

```powershell
New-NetFirewallRule -DisplayName "WinAirPlay" -Direction Inbound -Program "$PWD\WinAirPlay.exe" -Action Allow
New-NetFirewallRule -DisplayName "WinAirPlay-mDNS" -Direction Inbound -Protocol UDP -LocalPort 5353 -Action Allow
New-NetFirewallRule -DisplayName "WinAirPlay-Mirror" -Direction Inbound -Protocol TCP -LocalPort 5000,7000 -Action Allow
```

> 若首次投屏失败、iPad 搜不到设备，多半是防火墙拦了引擎子进程（它在临时目录运行）。按上面端口规则放行后即可。

---

## 可选：用 libfdk-aac 提升音质

默认 FFmpeg 回退解码已能出声。若想要更高音质，可编译 `libfdk-aac-2.dll` 放到引擎解压目录（默认 `%TEMP%\WinAirPlay\engine\`），重启应用即可：

1. 安装 [MSYS2](https://www.msys2.org/)，打开 **MSYS2 MinGW 64-bit** 终端：
   ```bash
   pacman -S mingw-w64-x86_64-gcc mingw-w64-x86_64-cmake make
   git clone https://github.com/mstorsjo/fdk-aac.git && cd fdk-aac
   mkdir build && cd build
   cmake -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON ..
   cmake --build .
   ```
2. 把生成的 `libfdk-aac-2.dll` 复制到 `%TEMP%\WinAirPlay\engine\` 目录，重启应用即可。

---

## 制作安装包（可选）

构建完成后用 [Inno Setup](https://jrsoftware.org/isdl.php) 编译 `installer\WinAirPlay.iss` 即可生成 `WinAirPlay-Setup-x.x.x.exe`。

---

## 许可证

基于 MIT 协议。原始许可证与版权声明见 [`LICENSE`](LICENSE)（© 2020 Stefano Bono，源自 SteeBono/airplayreceiver）。本仓库为衍生作品，遵循相同协议。

> 免责声明：本项目仅使用开源组件实现，仅供学习研究之用。
