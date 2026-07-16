<p align="center">
  <img src="Resources/AppIcon.png" width="112" alt="Codex TPS icon">
</p>

<h1 align="center">Codex TPS for Windows</h1>

<p align="center">
  A privacy-first Windows 11 tray monitor for local Codex token throughput.<br>
  Self-contained, portable, and processed entirely on your computer.
</p>

<p align="center">
  <a href="https://github.com/lifelover26/codex-tps/releases/latest"><img src="https://img.shields.io/github/v/release/lifelover26/codex-tps?label=Windows%20release" alt="Latest Windows release"></a>
  <img src="https://img.shields.io/badge/Windows-11%20x64-0078D4.svg" alt="Windows 11 x64">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green.svg" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/data-local%20only-2E7D32.svg" alt="Local data only">
</p>

<p align="center">
  <a href="#english">English</a> | <a href="#简体中文">简体中文</a> | <a href="windows/README.md">Full Windows guide / 完整 Windows 指南</a>
</p>

<p align="center">
  <img src="docs/assets/windows-panel-overlay.png" alt="Codex TPS for Windows detailed metrics panel and desktop overlay">
</p>

<p align="center">
  <sub>Detailed metrics panel and optional desktop overlay</sub>
</p>

> This repository is an unofficial Windows port of
> [gaofeng21cn/codex-tps](https://github.com/gaofeng21cn/codex-tps), the
> original macOS menu bar app created by Feng Gao. See
> [Origin and acknowledgements](#origin-and-acknowledgements) for details.

## English

Codex TPS for Windows reads the usage events already written to local Codex
session logs and turns them into an at-a-glance throughput display. It does not
require an API key and does not upload conversation data.

### Download

Download the latest Portable ZIP and its `.sha256` file from
[GitHub Releases](https://github.com/lifelover26/codex-tps/releases/latest).

1. Verify the ZIP checksum.
2. Extract it to a stable directory such as `%USERPROFILE%\Apps\CodexTPS`.
3. Run `CodexTPSTray.exe`.

No installer, administrator access, or separately installed .NET runtime is
required. The current build is unsigned, so Windows SmartScreen may display a
warning. Do not run the executable directly from inside the ZIP.

### Features

- Native Windows 11 notification-area application
- Detailed panel with rolling `1m`, `5m`, `30m`, and `1h` metrics
- Optional compact desktop overlay that can be dragged or locked click-through
- Total, input, cached-input, output, and reasoning token throughput
- Requests per minute, active sessions, and cache ratio
- English and Simplified Chinese interface
- Configurable `5s`, `15s`, `30s`, or `60s` refresh cadence
- Manual refresh, session-folder shortcut, and optional launch at login
- Self-contained x64 Portable release

### Basic use

- Left-click the tray icon to show or hide the detailed metrics panel.
- Right-click it to change the language, metric window, refresh cadence, login
  behavior, and desktop overlay settings.
- When the overlay is unlocked, drag it to move it. When locked, it becomes
  mouse click-through.

Codex records usage when a model request completes. The displayed TPS is
completion-time throughput over the selected rolling window, not a live
per-streaming-token speedometer.

### Requirements and settings

- Windows 11 x64
- Codex logs under `%USERPROFILE%\.codex\sessions`, or
  `%CODEX_HOME%\sessions` when `CODEX_HOME` is set

Per-user settings are stored at:

```text
%LOCALAPPDATA%\CodexTPS\settings.json
```

The executable is portable; saved preferences and the optional launch-at-login
registry entry intentionally remain outside the application directory.

### Privacy

- Session processing is local only.
- The scanner reads structural metadata and token-count records needed for
  accounting.
- Prompt, response, and tool-content bodies are not displayed, stored, or
  transmitted.
- The Windows application contains no analytics, login, telemetry, or automatic
  update path.

These values are operational estimates based on local logs, not authoritative
billing data.

### Build from source

Building requires the .NET 10 SDK. PowerShell 7 or later is also required for
release packaging.

```powershell
dotnet build .\windows\CodexTPS.slnx
dotnet test .\windows\CodexTPS.slnx
dotnet format .\windows\CodexTPS.slnx --verify-no-changes
```

See the [full Windows guide](windows/README.md) for checksum verification,
troubleshooting, removal, and release packaging.

## 简体中文

Codex TPS for Windows 是一款 Windows 11 托盘工具。它读取 Codex 已写入
本机的会话用量记录，显示不同时间窗口内的 token 吞吐率。程序本身不需要
API Key，也不会上传对话内容。

### 下载与运行

前往 [GitHub Releases](https://github.com/lifelover26/codex-tps/releases/latest)
下载最新的 Portable ZIP 和对应的 `.sha256` 文件。

1. 校验 ZIP 的 SHA-256。
2. 解压到稳定的目录，例如 `%USERPROFILE%\Apps\CodexTPS`。
3. 运行 `CodexTPSTray.exe`。

发布包解压即用，不需要安装程序、管理员权限或另行安装 .NET 运行时。当前
版本尚未进行代码签名，因此 Windows SmartScreen 可能显示警告。请勿直接在
ZIP 压缩包内运行程序。

### 主要功能

- 原生 Windows 11 通知区域应用
- 详细统计面板，支持 `1 分钟 / 5 分钟 / 30 分钟 / 1 小时` 时间窗口
- 可选桌面悬浮窗；未锁定时可拖动，锁定后鼠标可穿透
- 显示总 TPS、输入、缓存输入、输出和推理输出
- 显示请求/分钟、活动会话和缓存比例
- 支持简体中文与英文即时切换
- 自动刷新可选 `5 / 15 / 30 / 60 秒`
- 支持手动刷新、打开会话目录和可选的开机启动
- 自包含 Windows x64 Portable 发布包

### 基本操作

- 左键单击托盘图标：显示或隐藏详细面板。
- 右键单击托盘图标：设置语言、统计窗口、刷新频率、开机启动和悬浮窗。
- 悬浮窗未锁定时可以拖动；锁定后不会拦截鼠标操作。

Codex 会在一次模型请求完成时记录 token 用量，因此这里显示的是所选滚动
时间窗口内的完成时吞吐率，并不是逐个流式 token 的瞬时速度。

### 系统要求与设置

- Windows 11 x64
- Codex 日志位于 `%USERPROFILE%\.codex\sessions`；设置了 `CODEX_HOME`
  时则读取 `%CODEX_HOME%\sessions`

用户设置保存在：

```text
%LOCALAPPDATA%\CodexTPS\settings.json
```

程序文件本身是绿色版；用户偏好和可选的开机启动注册表项会保留在程序目录
之外。

### 隐私说明

- 所有会话处理均在本机完成。
- 扫描器只读取统计所需的结构信息和 token 计数记录。
- 不显示、保存或传输提示词、回复正文和工具调用正文。
- Windows 版没有分析 SDK、账号登录、遥测或自动更新功能。

显示数据来自本机日志，适合观察运行状态，但不等同于官方账单数据。

校验方法、故障排查、卸载和源码构建说明请参阅
[完整 Windows 指南](windows/README.md)。

## Origin and acknowledgements

This Windows port is based on the original
[Codex TPS](https://github.com/gaofeng21cn/codex-tps) project created by
[Feng Gao](https://github.com/gaofeng21cn) as a macOS menu bar application.
Thanks to Feng Gao for the original project, product concept, interface design,
accounting semantics, and test foundation.

The Windows application reimplements the runtime and user interface in C#,
.NET 10, and WPF, with Windows-specific tray integration, a bilingual metrics
panel, an optional desktop overlay, and Portable release packaging. The original
Swift/macOS source is retained in this fork for attribution and upstream
comparison.

This is an independently maintained, unofficial community port. It is not an
official release by, affiliated with, or endorsed by the original author or
OpenAI. OpenAI and Codex are trademarks of their respective owner.

The original project's accounting semantics were informed by the public
[Tokscale](https://github.com/junhoyeo/tokscale) project; Codex TPS remains an
independent implementation and does not embed Tokscale.

## License

This repository is distributed under the [MIT License](LICENSE). The original
copyright and permission notice are retained as required by that license.
