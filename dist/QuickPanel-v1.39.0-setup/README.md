# QuickPanel

Windows 快捷启动面板 — 分组式启动面板 + 屏幕底部的透明 Dock 长条，5 步初始化向导，样式全部可自定义。

**全本地 · 零外联 · 零第三方 NuGet 依赖**

![platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20x64-0078D4)
![.NET](https://img.shields.io/badge/.NET-8.0%20LTS-512BD4)
![version](https://img.shields.io/badge/version-1.31.0-7C6FFF)
![license](https://img.shields.io/badge/license-MIT-green)

**[⬇ 下载安装包（Releases）](https://github.com/AstonKrss/QuickPanel/releases/latest)** —— Setup.exe 双击即装，无需任何运行库

---

## 解决什么问题

| 你遇到的 | QuickPanel 的做法 |
|---|---|
| 桌面图标越堆越多，想用的程序翻半天 | 分组收纳 + 全局搜索（支持拼音首字母），`Alt+Q` 一下直达 |
| 启动器点开「已经在后台」的软件特别慢 | 已运行的软件**直接切到前台**（与任务栏一致），实测 233ms；普通启动器再拉起一个进程要 6 秒+ |
| 想要干净的桌面，又不敢删快捷方式 | **桌面留白**：只隐藏壁纸上的图标，文件一个不删，托盘一键恢复 |
| 启动器花里胡哨，和壁纸糊在一起看不清 | 底色 / 透明度 / 文字颜色 / 圆角 / 间距全部可调，文字按底色自动反差 |
| 初始化向导自作主张给你分一堆没用的组 | 向导**不替你分类**：程序铺成图标墙，自己建组自己拖 |
| 好看的启动器都依赖全家桶 | 单 exe、零第三方依赖、零外联，装完即用 |

一句话：**把桌面和启动这件事收进一个按 `Alt+Q` 就出现的面板，桌面留给你自己。**

> 已知不足、实测基线和版本计划见 [ROADMAP.md](ROADMAP.md)。

---

## 特性

### 主面板
- **分组侧边导航** + 图标网格（高分辨率图标，悬停放大 + 点击弹动）
- 桌面/资源管理器**拖入** `.lnk` / `.exe` / `.url` / 文件夹自动入库；**拖出**到桌面生成副本
- **全局搜索**：各分组 + Dock 常用一起去重合并，支持**拼音首字母**；Enter 直启首个结果
- **已运行的软件直接切到窗口**（与任务栏一致）—— 实测点 Dock 图钉 233ms 切到前台，
  而走"再拉起一个进程"要 6 秒+；Shift+点击强制开新实例
- 右键菜单：启动 / 管理员运行 / 打开所在位置 / 固定到 Dock / 移动到分组 / 重命名 / 移除；中键 = 管理员运行

### Dock 长条
- **背景模式**：毛玻璃（半透明深色板）/ 全透明（自定义底色）/ 完全隐形（无底板）
- **底板样式**：带底板（胶囊）/ 纯图标（无底无框，直接浮在壁纸上，文字颜色可自定义并自动描光）
- 图钉悬停放大 1.32x + 上浮，Z 序置顶不被邻居遮挡
- **自由拖动**：按住 ⠿ 手柄拖到任意位置（多显示器安全夹取），底部居中 / 自由模式可切换
- 时钟 + 日期 + CPU/内存/网速监控组件（可开关、可调字号）
- 默认**不置顶** —— 只在桌面层级显示，最大化程序会盖住它（可开关）

### 面板与外观
- 面板底色 + 面板不透明度独立于 Dock（5%–100%），文字按底板亮度自动黑/白
- 强调色、Dock 文字颜色、图标大小 / 图钉大小 / 间距 / 圆角 / 悬停放大 / 上浮距离全部可调
- 面板缩放、面板常驻、鼠标热区延迟、收回延迟、全屏自动隐藏

### 初始化向导（不替你做决定）
- 5 步：欢迎 → **整理**（桌面程序铺成图标墙，自己建分组、拖进去）→ 呼出方式与启动项 → 外观 → 完成
- 不自动分类：扫描到的程序由你拖进自建分组，右上角「+ 新建分组」
- 可选**桌面留白**：隐藏壁纸上的桌面图标，快捷方式仍在桌面文件夹里，
  从此桌面只剩壁纸；托盘/设置一键恢复，退出 QuickPanel 自动恢复

### 生命周期
- 全局热键（默认 `Alt+Q`，冲突自动回退）、鼠标热区（仅分组按钮上触发）、单实例
- 托盘：显示面板 / 分组快速入口 / **隐藏桌面图标开关** / 备份配置 / 退出

### 数据
- `%AppData%\QuickPanel\config.json` 明文 JSON，可手改/备份/迁移
- 图标缓存 PNG 落盘，二次启动零提取

---

## 安装

到 [Releases](https://github.com/AstonKrss/QuickPanel/releases) 下载，或看下表自己选：

| 形态 | 大小 | 适用 |
|---|---|---|
| `QuickPanel-v1.31.0-win-x64.zip` | 0.4 MB | 本机已装 .NET 8 桌面运行时 |
| `QuickPanel-v1.31.0-standalone-win-x64/` | 68 MB | 任何 Win10/11 x64，**无需运行时**；解压后双击「安装.bat」 |
| `QuickPanel-v1.31.0-Setup.exe` | 61 MB | 同上，但只有一个 exe；双击即装（静默安装并设为开机启动） |

> 为什么有的包要 .NET：经典软件目标 .NET Framework 4.8（Windows 自带），本项目用 .NET 8 ——
> 它不在 Windows 里，要么单独装，要么把运行时一起打包（后两种就是）。

安装器行为：装到 `%LOCALAPPDATA%\QuickPanel`，创建桌面/开始菜单快捷方式，写注册表卸载项
（设置 → 应用 里可卸载），配置保存在 `%APPDATA%\QuickPanel`，卸载时保留。

---

## 使用

| 操作 | 效果 |
|---|---|
| `Alt+Q` | 呼出 / 收回面板 |
| 输入关键字 + Enter | 搜索（含常用），直启首个结果；Esc 清空 |
| 点击 / Enter 启动 | 已在后台的软件直接切窗口；**Shift+点击** 强制开新实例 |
| 悬停 Dock 分组按钮 | 唤出面板并定位该分组（滑到别的分组实时切换） |
| 悬停 Dock 图钉 | 图标放大上浮 |
| 拖文件到面板网格 | 加入当前分组 |
| 拖文件到 Dock | 钉选（上限 14） |
| 拖面板/Dock 图标到桌面 | 生成快捷方式副本 |
| 右键图标 | 增强菜单（编辑 / 排序 / 移动 / 管理员运行…） |
| 中键图标 | 管理员运行 |
| ⠿ 手柄 | 拖动 Dock 到任意位置（只有手柄能拖） |
| Dock 右键空白 | 新建分组 / 设置 |
| 托盘图标 | 显示面板 / 分组入口 / **隐藏桌面图标** / 设置 / 备份 / 退出 |

---

## 从源码构建

```bash
# 需要 .NET 8 SDK
dotnet build -c Release

# 框架依赖单文件（0.4MB，需运行时）
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true

# 自包含单文件（68MB，免运行时，可直接分发）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true
```

---

## 技术栈

- **C# 12 / WPF / .NET 8 LTS**，x64，PerMonitorV2 DPI
- **零 NuGet 第三方依赖**（托盘用 WinForms 内置 NotifyIcon）
- Win32 P/Invoke：`RegisterHotKey` / `SHGetFileInfo` / `SHCreateItemFromParsingName` /
  `DwmSetWindowAttribute` / `SHQueryUserNotificationState` / `WindowFromPoint` / `SetForegroundWindow`
- 分层窗口逐像素 alpha（`AllowsTransparency`），`WM_NCHITTEST` 对空白区返回 `HTTRANSPARENT` 不挡下层点击

### 踩过的坑（完整版见 PRD.md §6.3 / §19–21）

| 坑 | 结论 |
|---|---|
| DWM SystemBackdrop 涂满整个窗口矩形 | 与"悬停余量"无法共存 → 统一分层窗口 + 半透明色板 |
| Win11 24H2 上 `WM_COMMAND 0x7402` 隐藏桌面图标失效 | 改 ShowWindow 隐藏桌面视图 + 同步 HideIcons 注册表 |
| 混合 DPI 多屏下 WPF `CenterScreen` 算到屏幕外 | 按鼠标所在显示器工作区手动居中 |
| 点击空白衬垫拖动 + 静默切自由模式 | 只有 ⠿ 手柄能拖，且必须真的移动过才落盘 |
| Electron 类应用"再启动"要走单实例握手（6 秒+） | 已运行直接切窗口，与任务栏行为一致 |

---

## 项目结构

```
QuickPanel/
├─ QuickPanel.csproj / app.manifest / App.xaml(.cs) / Assets/QuickPanel.ico
├─ Models/      LaunchItem · Group · AppSettings(+AppConfig)
├─ Services/    ConfigStore · ShortcutParser · IconCache · LaunchService · RunningAppActivator
│               HotkeyService · HotZoneService · DesktopIcons · ScreenHelper · SystemMonitor
│               PinyinIndex · ShellIconFactory · FirstRunSeeder · Acrylic · Theme
│               SingleInstanceGuard · Autostart · Links · Win32
├─ ViewModels/  ViewModels.cs（ItemVM/GroupVM/MainViewModel）
├─ Windows/     PanelWindow · DockBarWindow · SettingsWindow · OnboardingWindow
│               ScanAddWindow · GroupDragHelper · DragGhost · 各类对话框
├─ installer/   install.ps1 · uninstall.ps1 · package.ps1
├─ dist/        安装包产物（Setup.exe / standalone / zip）
├─ PRD.md       产品需求文档（完整决策记录与踩坑清单）
├─ TESTING.md   测试手册（97 个用例）
└─ ROADMAP.md   已知不足与未来计划
```

---

## 隐私

- **零外联**：不引用任何 HTTP 客户端库，`netstat -ano | findstr <pid>` 应为空
- **数据本地**：明文 JSON，无混淆、无隐藏写盘
- **无遥测**：无崩溃上报、无统计
- **提权边界**：管理员启动仅通过标准 UAC（`Verb=runas`），不绕过、不静默提权
- GitHub 链接仅在点击时打开浏览器

---

## 作者

**Aston** · 觉得好用的话去 [GitHub](https://github.com/AstonKrss/QuickPanel) 点个 Star ⭐

## License

MIT
