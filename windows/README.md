# Codex TPS for Windows

A privacy-first Windows 11 tray monitor for local Codex token throughput.
The release is a self-contained Portable ZIP: extract it and run the executable;
no installer or separately installed .NET runtime is required.

**What's new in v0.3.2:**

- Reduces redundant theme resource rebuilds for the desktop overlay. When no
  effective theme change has occurred, the Windows application and overlay themes
  are no longer reapplied, so a fixed overlay theme is not repeatedly rebuilt in
  response to unrelated system preference notifications. This improves overlay
  appearance stability.
- Removes the read-only `Metrics` submenu from the tray right-click menu. The
  actionable `Metric Window` menu, tray tooltip, desktop overlay, and detailed
  metrics panel continue to show throughput data.

**What's new in v0.3.1:**

- Fixes a bug where the desktop overlay could lose its always-on-top status and
  be covered by other windows while locked (click-through mode). The overlay now
  reliably stays on top regardless of lock state, after position changes, and
  after quick-position presets. Locking still means click-through and drag
  disabled; unlock restores dragging. Position presets, themes, opacity, and
  Windows/WSL data source selection are unaffected.

**What's new in v0.3.0:**

- Overlay background opacity levels and position presets snapped to the current
  monitor's work area (custom drag positions remain absolute coordinates).
- Data source selection: choose the Windows Codex home or one discovered,
  accessible WSL distribution. Sessions from different sources are not merged.
- Hardened session-scanning cursor continuity.

[English](#english) | [简体中文](#简体中文)

## English

### Requirements

- Windows 11 x64
- Codex session logs under `%USERPROFILE%\.codex\sessions`, or
  `%CODEX_HOME%\sessions` when `CODEX_HOME` is set
- WSL distributions are only available as data sources when discoverable and
  their sessions directory is accessible from Windows
- No API key is required by Codex TPS
- No separately installed .NET runtime is required for the release build

### Install and Run

1. Download `Codex-TPS-Windows-x64-Portable-0.3.2.zip` and its `.sha256` file.
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
- `Overlay > Opacity` to select a background opacity level
- `Overlay > Position` to choose a preset position (Top Left, Top Right,
  Middle Left, Middle Right, Bottom Left, Bottom Right) on the current
  monitor's work area

When unlocked, drag anywhere on the overlay to move it. A manually dragged
position is saved as custom absolute screen coordinates; selecting any preset
recalculates the position against the current monitor's work area, so presets
adapt when monitor layout changes. When locked, the overlay becomes mouse
click-through, so it does not block the window underneath. Unlock it from the
tray menu. The overlay is always on top, does not appear in the taskbar or
Alt+Tab, and is included in normal screenshots and recordings.

The overlay uses the same snapshot and refresh cadence as the detailed panel. It
does not start another scanner or increase the configured polling frequency.

### Data Source

The `Data Source` submenu selects where Codex TPS reads session logs from.
Exactly one source is active at a time:

- **Windows (default)** reads from `%USERPROFILE%\.codex\sessions`, or
  `%CODEX_HOME%\sessions` when the `CODEX_HOME` environment variable is set.
- **WSL distributions** listed in the submenu are discovered automatically. A
  distribution appears only when WSL is installed and its Codex sessions
  directory is accessible from Windows (for example via
  `\\wsl.localhost\<DistroName>\home\<user>\.codex\sessions`). If a listed distribution
  is no longer accessible, switching to it shows a warning and the active
  source does not change.

Sessions from different sources are never merged together. Switching sources
stops reading from the previous source immediately; the next refresh uses the
newly selected source. The selected source is saved across launches. Discovery
of WSL distributions runs once when the submenu opens; open the submenu again
to refresh the list after WSL distributions are started or stopped.

### Language

Use `Language` in the tray menu to switch between English and Simplified
Chinese. The tray menu, detailed panel, overlay, status text, and warnings update
immediately. The selection is saved across launches.

### Themes

The top-level `Theme` menu selects `System`, `Light`, or `Dark` for the detailed
panel and tray menu. `Overlay > Theme` independently selects `Follow Application`,
`System`, `Light`, or `Dark` for the overlay. Theme selections are saved. When
`System` is selected, the appearance updates in real time when the Windows light
or dark mode changes, without restarting the app.

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
$zipPath = ".\Codex-TPS-Windows-x64-Portable-0.3.2.zip"
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
- If a WSL distribution does not appear in the Data Source menu, confirm that
  WSL is installed, the distribution is running, and its Codex sessions
  directory is accessible from Windows (`\\wsl.localhost\<DistroName>\...`).
- If switching to a WSL distribution shows a failure warning, confirm that the
  distribution is still accessible; the previous data source remains active.
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
.\windows\scripts\New-Release.ps1 -Version 0.3.2
```

The script creates:

```text
windows\artifacts\Codex-TPS-Windows-x64-Portable-0.3.2.zip
windows\artifacts\Codex-TPS-Windows-x64-Portable-0.3.2.zip.sha256
```

The ZIP contains exactly:

- `CodexTPSTray.exe`
- `LICENSE`
- `README.md`

## 简体中文

Codex TPS Windows 版是一个仅在本地读取 Codex 会话日志、显示 token
吞吐率的 Windows 11 托盘工具。发布包为自包含 Portable ZIP：解压后即可
运行，不需要安装程序，也不需要单独安装 .NET 运行时。

**v0.3.2 更新内容：**

- 减少桌面悬浮窗的冗余主题资源重建。当未发生有效主题变化时，不再重复应用
  Windows 应用与悬浮窗主题，因此固定的悬浮窗主题不会因无关的系统偏好通知而
  被重复重建，提升悬浮窗外观稳定性。
- 移除托盘右键菜单中只读的 `Metrics` 子菜单。可操作的 `Metric Window` 菜单、
  托盘 tooltip、桌面悬浮窗和详细监控面板的指标展示不受影响。

**v0.3.1 更新内容：**

- 修复桌面悬浮窗在锁定（鼠标点击穿透）状态下可能失去置顶、被其他窗口遮挡的
  问题。悬浮窗现在无论锁定/解锁状态、位置移动或快速定位之后都会可靠地保持
  置顶。锁定仍然代表点击穿透和禁止拖动；解锁后恢复拖动。位置预设、主题、
  透明度以及 Windows/WSL 数据源功能均不受影响。

**v0.3.0 新增内容：**

- 悬浮窗背景透明度档位与位置预设，预设位置按当前显示器工作区计算（自
  定义拖动位置仍保存绝对坐标）。
- 数据源选择：可选择 Windows Codex Home 或一个已探测并可访问的 WSL
  发行版；不同来源的会话不会合并。
- 会话扫描 cursor 连续性加固。

### 系统要求

- Windows 11 x64
- Codex 会话日志位于 `%USERPROFILE%\.codex\sessions`；设置了
  `CODEX_HOME` 时则读取 `%CODEX_HOME%\sessions`
- WSL 发行版仅在可被探测且其 sessions 目录可从 Windows 访问时才会作为
  数据源选项出现
- Codex TPS 本身不需要 API Key

### 安装与启动

1. 下载 `Codex-TPS-Windows-x64-Portable-0.3.2.zip` 和对应的 `.sha256`。
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
- `悬浮窗 > 透明度`：选择背景透明度档位
- `悬浮窗 > 位置`：选择预设位置（左上角、右上角、左侧居中、右侧居中、
  左下角、右下角），按当前显示器工作区计算

未锁定时，可按住悬浮窗任意位置拖动。手动拖动的位置以自定义绝对屏幕坐标
保存；选择任一预设位置会按当前显示器工作区重新计算，显示器布局变化后会
自动适配。锁定后悬浮窗会变为鼠标穿透，不会阻挡下面窗口的操作；需要移动
时从托盘菜单解除锁定。悬浮窗保持置顶，不出现在任务栏和 Alt+Tab 中，普通
截图和录屏会包含它。

悬浮窗与详细面板共用同一份数据快照和刷新频率，不会启动第二个扫描器，
也不会提高当前设置的轮询频率。

### 数据源

通过托盘菜单的“数据源”子菜单选择 Codex TPS 读取会话日志的位置。同一时间
只有一个数据源处于活动状态：

- **Windows（默认）**：从 `%USERPROFILE%\.codex\sessions` 读取；设置了
  `CODEX_HOME` 环境变量时从 `%CODEX_HOME%\sessions` 读取。
- **WSL 发行版**：子菜单中列出的 WSL 发行版会自动探测。仅当已安装 WSL 且
  该发行版的 Codex sessions 目录可从 Windows 访问（例如通过
  `\\wsl.localhost\<发行版名>\home\<用户名>\.codex\sessions`）时，该发行版才会
  出现在列表中。若已列出的发行版变得不可访问，切换时会显示警告，当前
  活动数据源不会改变。

不同来源的会话不会合并。切换数据源后立即停止从旧源读取，下一次刷新将
使用新选择的数据源。数据源选择会在启动间保存。WSL 发行版的探测在子菜单
打开时执行一次；启动或停止 WSL 发行版后重新打开子菜单即可刷新列表。

### 中英文切换

通过托盘菜单的 `语言` 子菜单切换 English 或简体中文。托盘菜单、详细
面板、悬浮窗、状态和警告会立即更新，语言选择会保存到设置中。

### 主题

顶层“主题”菜单可为详细面板和托盘菜单选择“跟随系统”、“浅色”或“深色”。
“悬浮窗 > 主题”可独立选择“跟随应用”、“跟随系统”、“浅色”或“深色”。主题
选择会保存。选择“跟随系统”时，Windows 明暗模式变化后会实时更新，无需
重启。

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
.\windows\scripts\New-Release.ps1 -Version 0.3.2
```

发布脚本会生成 Portable ZIP 和对应的 SHA-256 文件。
