# QuickPanel

Windows 快捷启动面板 — 常驻屏幕底部的分组式启动器 + 可钉选应用的透明 Dock 长条。

**全本地 · 零外联 · 零第三方 NuGet 依赖 · 样式代码级可定制**

![platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20x64-0078D4)
![.NET](https://img.shields.io/badge/.NET-8.0%20LTS-512BD4)
![license](https://img.shields.io/badge/license-MIT-green)

---

## 特性

### 主面板
- **分组侧边导航** + WrapPanel 图标网格（86×96 单元）
- 从桌面/资源管理器**拖入** `.lnk` / `.exe` / `.url` / 文件夹自动入库
- 从面板**拖出**到桌面（OLE FileDrop 生成副本）
- **跨分组搜索**：Enter 直启首个结果，Esc 清空
- 图标悬停放大 1.12x + 点击弹动；iOS 式弹出动画（上浮 + 缩放回弹 + 淡入）
- 右键菜单：启动 / 管理员运行 / 打开所在位置 / 固定到 Dock / 移动到分组 / 重命名 / 移除
- 中键 = 直接管理员运行（标准 UAC，不绕过）

### Dock 长条
- **三种背景模式**：毛玻璃（DWM Backdrop 三级降级）/ 全透明（自定义底色）/ **完全隐形（无底板）**
- **图标底板可选**：带底板（胶囊亚克力）/ 纯图标（无底无框，直接悬浮于壁纸）
- 图钉悬停放大 1.32x + 上浮，**Z 序置顶**不被邻居遮挡
- **自由拖动**（⠿ 手柄）+ 图标缩放 60%–160% + 位置持久化
- 默认**不置顶**——只在桌面层级显示，其他软件自然覆盖（可开关）
- Dock 右键：新建分组 / 设置 / 置顶开关

### 唤出与生命周期
- 全局热键（默认 `Alt+Q`，冲突自动回退并提示）
- 鼠标热区：**仅悬停在分组按钮上**才唤出面板（`WindowFromPoint` 真实命中检测）
- 离开自动收回（默认 300ms，**0 = 立即**）+ 点击其他窗口**立即**收回
- 全屏自动隐藏（`SHQueryUserNotificationState` 官方 API，无边框最大化不误判）
- 单实例（Global Mutex + EventWaitHandle）、托盘快速分组入口、开机自启

### 数据
- `%AppData%\QuickPanel\config.json` 明文 JSON，可手改/备份/迁移
- 图标缓存 PNG 落盘（目标 MD5 键名），二次启动零提取
- 首次运行扫描桌面快捷方式，按关键词播种五分组（网安/开发/日常/游戏/工具）

---

## 快速开始

```bash
# 需要 .NET 8 SDK
dotnet build -c Release

# 发布单文件（框架依赖 + ReadyToRun）
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishReadyToRun=true -p:PublishSingleFile=true
```

产物：`bin/Release/net8.0-windows/win-x64/publish/QuickPanel.exe`

运行需安装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。

---

## 使用

| 操作 | 效果 |
|---|---|
| `Alt+Q` | 呼出 / 收回面板 |
| 悬停 Dock 分组按钮 | 唤出面板并定位该分组（滑到别的分组实时切换） |
| 悬停 Dock 图钉 | 图标放大上浮 |
| 拖文件到面板网格 | 加入当前分组 |
| 拖文件到 Dock | 钉选（上限 14） |
| 拖面板/Dock 图标到桌面 | 生成快捷方式副本 |
| 右键图标 | 增强菜单 |
| 中键图标 | 管理员运行 |
| ⠿ 手柄 | 拖动 Dock 到任意位置 |
| Dock 右键空白 | 新建分组 / 设置 |
| 托盘图标 | 显示面板 / 分组快速入口 / 设置 / 备份 / 退出 |

---

## 技术栈

- **C# 12 / WPF / .NET 8 LTS**，x64，PerMonitorV2 DPI
- **零 NuGet 第三方依赖**（托盘用 WinForms 内置 NotifyIcon）
- Win32 P/Invoke：`RegisterHotKey` / `SHGetFileInfo` / `DwmSetWindowAttribute` / `SHQueryUserNotificationState` / `WindowFromPoint`
- 分层窗口逐像素 alpha（`AllowsTransparency`），alpha=0 穿透、≥1 可点击

### 已规避的技术坑

| 坑 | 规避方式 |
|---|---|
| WPF Window 不支持 RenderTransform | 滑动动画驱动 `Window.Left`；弹出动画放内容层 Border |
| 对 Hidden 窗口调 `Activate()` 抛异常 | `Show()` + 延迟激活 + Topmost 兜底 |
| `dotnet` CLI 不支持 COMReference | 放弃 WScript COM，lnk 走 Shell 代理解析 |
| Win11 24H2 AccentPolicy 亚克力失效 | 改 `DWMWA_SYSTEMBACKDROP_TYPE` |
| ItemsSource 绑定晚于 `Window.Loaded` | 依赖 `ItemContainerGenerator.StatusChanged` |
| DataTemplate 匿名 Freezable 被密封冻结 | 动画前检测 `IsFrozen`，冻结则换新实例 |
| **`DropShadowEffect` 中间位图裁切** | Effect 以元素边界生成位图，会削掉 RenderTransform 溢出 → Dock 不挂 Effect |
| **`DWMWA_CAPTION_COLOR` 在分层窗口上留色块** | 分层透明窗口完全不碰 DWM 属性（含 WindowChrome） |
| **分组按钮自带底板** | 纯图标模式需连 Style 一起清空，否则触发器重画底板 |

---

## 项目结构

```
QuickPanel/
├─ QuickPanel.csproj / app.manifest / App.xaml(.cs)
├─ Models/      LaunchItem · Group · AppSettings(+AppConfig)
├─ Services/    ConfigStore · ShortcutParser · IconCache · LaunchService
│               HotkeyService · HotZoneService · FirstRunSeeder · Acrylic
│               SingleInstanceGuard · Autostart · Theme · Win32
├─ ViewModels/  ViewModels.cs（ItemVM/GroupVM/MainViewModel）
├─ Windows/     PanelWindow · DockBarWindow · SettingsWindow · InputDialog
└─ PRD.md       产品需求文档（含完整决策记录与踩坑清单）
```

---

## 隐私

- **零外联**：不引用任何 HTTP 客户端库，`netstat -ano | findstr <pid>` 应为空
- **数据本地**：明文 JSON，无混淆、无隐藏写盘
- **无遥测**：无崩溃上报、无统计
- **提权边界**：管理员启动仅通过标准 UAC（`Verb=runas`），不绕过、不静默提权

---

## License

MIT
