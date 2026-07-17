# Codex TPS for Windows

A privacy-first Windows 11 tray monitor for local Codex token throughput.
The release is a self-contained Portable ZIP: extract it and run the executable;
no installer or separately installed .NET runtime is required.

[English](#english) | [简体中文](#简体中文)

## English

### Requirements

- Windows 11 x64
- Codex session logs under `%USERPROFILE%\.codex\sessions`, or
  `%CODEX_HOME%\sessions` when `CODEX_HOME` is set
- No API key is required by Codex TPS
- No separately installed .NET runtime is required for the release build

### Install and Run

1. Download `Codex-TPS-Windows-x64-Portable-0.1.1.zip` and its `.sha256` file.
2. Verify the checksum before extracting the ZIP.
3. Extract the ZIP to a stable writable directory, for example:

   ```text
   %USERPROFILE%\Apps\CodexTPS
   ```

4. Run `CodexTPSTray.exe`. The app starts in the notification area and does not
   open a normal taskbar window.

Do not run the executable directly from inside the ZIP or leave it in a temporary
directory. Windows SmartScreen may warn because the current build is unsigned.

### Metrics Panel

- Left-click the tray icon to show the detailed metrics panel.
- Left-click the tray icon again to hide it.
- The panel remains visible when focus moves elsewhere.
- Press `Escape` while the panel has focus to hide it.
- Right-click the tray icon for settings and commands.

The panel includes:

- Total, input, cached-input, output, and reasoning token throughput
- Requests per minute, active sessions, and cache ratio
- Rolling windows of 1 minute, 5 minutes, 30 minutes, and 1 hour
- Automatic refresh intervals of 5, 15, 30, or 60 seconds
- Manual refresh and a shortcut to the Codex sessions folder

Codex records token usage when a model request completes. The displayed TPS is
completion-time throughput over the selected rolling window, not a live
per-streaming-token speedometer.

### Desktop Overlay

The optional desktop overlay is disabled by default. Open the tray menu and use:

- `Overlay > Show Overlay` to show or hide it
- `Overlay > Lock Overlay` to lock or unlock it
- `Overlay > Reset Overlay Position` to return it to the primary monitor's
  top-right area

When unlocked, drag anywhere on the overlay to move it. Its position is saved.
When locked, the overlay becomes mouse click-through, so it does not block the
window underneath. Unlock it from the tray menu. The overlay is always on top,
does not appear in the taskbar or Alt+Tab, and is included in normal screenshots
and recordings.

The overlay uses the same snapshot and refresh cadence as the detailed panel. It
does not start another scanner or increase the configured polling frequency.

### Language

Use `Language` in the tray menu to switch between English and Simplified
Chinese. The tray menu, detailed panel, overlay, status text, and warnings update
immediately. The selection is saved across launches.

### Launch at Login

`Launch at Login` writes the current executable path to the current user's
Windows Run registry key. Disable it before moving or deleting the Portable
directory; otherwise the registry entry will point to the old path.

### Portable Behavior and Settings

The executable distribution is portable, but the application intentionally
stores per-user settings outside its directory:

```text
%LOCALAPPDATA%\CodexTPS\settings.json
```

This keeps settings when the executable is replaced during a manual update.
Enabling Launch at Login also creates a per-user registry value. The application
does not require administrator privileges.

### Privacy and Data Handling

- Session data is processed locally.
- The scanner reads only relevant metadata and token-count records.
- Prompt, response, and tool-content bodies are not decoded, displayed, stored,
  or transmitted.
- The Windows app has no analytics, login, automatic update, or telemetry path.
- The displayed values are operational estimates, not billing authority.

### Manual Update

1. Disable `Launch at Login` if the new version will use a different directory.
2. Exit Codex TPS from the tray menu.
3. Verify and extract the new Portable ZIP.
4. Replace the old application files.
5. Run the new `CodexTPSTray.exe`.

Existing settings remain in `%LOCALAPPDATA%\CodexTPS`.

### Remove

1. Disable `Launch at Login` while the app is running.
2. Exit Codex TPS from the tray menu.
3. Delete the Portable directory.
4. Optionally delete `%LOCALAPPDATA%\CodexTPS` to remove saved settings.

### Checksum Verification

Do not run or extract the package if verification fails.

```powershell
$zipPath = ".\Codex-TPS-Windows-x64-Portable-0.1.1.zip"
$checksumPath = "$zipPath.sha256"

$expectedHash = (Get-Content $checksumPath -Raw).Split('  ')[0].Trim()
$actualHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

if ($expectedHash -ne $actualHash) {
    throw "Checksum verification failed"
}

Write-Host "Checksum verified successfully"
```

### Troubleshooting

- If the icon is not visible, check the notification-area overflow menu.
- If the app reports no sessions, confirm that Codex has created the sessions
  directory or that `CODEX_HOME` points to the intended Codex home.
- If a locked overlay needs to move, unlock it from the tray menu first.
- If an old login entry stops working after moving the app, run the app from its
  new location, disable `Launch at Login`, and enable it again.
- Only one instance runs at a time. Starting another copy exits immediately.

### Build from Source

Requirements:

- .NET 10 SDK
- PowerShell 7 or later for release packaging

```powershell
dotnet build .\windows\CodexTPS.slnx
dotnet test .\windows\CodexTPS.slnx
dotnet format .\windows\CodexTPS.slnx --verify-no-changes
```

Create the x64 Portable release:

```powershell
.\windows\scripts\New-Release.ps1 -Version 0.1.1
```

The script creates:

```text
windows\artifacts\Codex-TPS-Windows-x64-Portable-0.1.1.zip
windows\artifacts\Codex-TPS-Windows-x64-Portable-0.1.1.zip.sha256
```

The ZIP contains exactly:

- `CodexTPSTray.exe`
- `LICENSE`
- `README.md`

## 简体中文

Codex TPS Windows 版是一个仅在本地读取 Codex 会话日志、显示 token
吞吐率的 Windows 11 托盘工具。发布包为自包含 Portable ZIP：解压后即可
运行，不需要安装程序，也不需要单独安装 .NET 运行时。

### 系统要求

- Windows 11 x64
- Codex 会话日志位于 `%USERPROFILE%\.codex\sessions`；设置了
  `CODEX_HOME` 时则读取 `%CODEX_HOME%\sessions`
- Codex TPS 本身不需要 API Key

### 安装与启动

1. 下载 `Codex-TPS-Windows-x64-Portable-0.1.1.zip` 和对应的 `.sha256`。
2. 校验 SHA-256 后再解压。
3. 解压到稳定且可写的目录，例如 `%USERPROFILE%\Apps\CodexTPS`。
4. 运行 `CodexTPSTray.exe`。

程序启动后常驻通知区域，不会显示普通任务栏窗口。请勿直接在 ZIP 内
运行，也不要长期放在临时目录。由于当前版本没有代码签名，Windows
SmartScreen 可能显示安全警告。

### 详细面板

- 左键单击托盘图标：显示详细面板
- 再次左键单击：隐藏详细面板
- 面板不会因为失去焦点而自动隐藏
- 面板获得焦点时可按 `Escape` 隐藏
- 右键单击托盘图标：打开设置与命令菜单

面板显示总 TPS、输入、缓存输入、输出、推理输出、请求/分钟、活动会话
和缓存比例，并支持 1 分钟、5 分钟、30 分钟和 1 小时统计窗口。自动刷新
可选择 5、15、30 或 60 秒。

Codex 在一次模型请求完成时记录 token 用量。因此这里显示的是所选滚动
窗口内的完成时吞吐率，并不是逐个流式 token 更新的实时速度计。

### 桌面悬浮窗

悬浮窗默认关闭，可在托盘菜单中使用：

- `悬浮窗 > 显示悬浮窗`：显示或隐藏
- `悬浮窗 > 锁定悬浮窗`：锁定或解锁
- `悬浮窗 > 重置悬浮窗位置`：恢复到主显示器右上区域

未锁定时，可按住悬浮窗任意位置拖动，位置会自动保存。锁定后悬浮窗会
变为鼠标穿透，不会阻挡下面窗口的操作；需要移动时从托盘菜单解除锁定。
悬浮窗保持置顶，不出现在任务栏和 Alt+Tab 中，普通截图和录屏会包含它。

悬浮窗与详细面板共用同一份数据快照和刷新频率，不会启动第二个扫描器，
也不会提高当前设置的轮询频率。

### 中英文切换

通过托盘菜单的 `语言` 子菜单切换 English 或简体中文。托盘菜单、详细
面板、悬浮窗、状态和警告会立即更新，语言选择会保存到设置中。

### 登录时启动

`登录时启动` 会把当前 EXE 路径写入当前用户的 Windows Run 注册表项。
移动或删除 Portable 目录前应先关闭此选项，否则注册表仍会指向旧路径。

### Portable 与设置位置

发布包中的程序免安装、自包含，但并非“零写入”模式。每用户设置保存在：

```text
%LOCALAPPDATA%\CodexTPS\settings.json
```

因此替换 EXE 后设置仍会保留。启用登录时启动还会写入当前用户注册表。
程序不需要管理员权限。

### 隐私

- 所有会话数据只在本机处理
- 扫描器只解析必要的元数据和 token_count 记录
- 不解析、不显示、不保存、不上传提示词、回复和工具内容正文
- Windows 版没有分析统计、登录、遥测或自动更新网络请求
- 显示结果是运行状态估算，不是账单依据

### 手动更新与删除

更新时先从托盘菜单退出程序，再校验并解压新版本、替换旧文件后重新运行。
如果更换目录，应先关闭登录时启动。设置会继续保留在 `%LOCALAPPDATA%`。

删除时依次关闭登录时启动、退出程序、删除 Portable 目录；如需清除设置，
再删除 `%LOCALAPPDATA%\CodexTPS`。

### 从源码构建

需要 .NET 10 SDK；打包还需要 PowerShell 7 或更高版本。

```powershell
dotnet build .\windows\CodexTPS.slnx
dotnet test .\windows\CodexTPS.slnx
dotnet format .\windows\CodexTPS.slnx --verify-no-changes
.\windows\scripts\New-Release.ps1 -Version 0.1.1
```

发布脚本会生成 Portable ZIP 和对应的 SHA-256 文件。
