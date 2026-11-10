# QuickPanel 产品需求文档（PRD）

| | |
|---|---|
| **产品名** | QuickPanel — Windows 快捷启动面板 |
| **版本** | v0.4（当前实现基线）· 本文档覆盖至 v1.0 |
| **平台** | Windows 10 (1903+) / Windows 11（含 24H2）x64 |
| **技术栈** | C# 12 / WPF / .NET 8 LTS / 零第三方 UI 依赖 |
| **文档状态** | 需求基线 + 当前实现状态标注 |

---

## 1. 产品概述

### 1.1 一句话定位

**QuickPanel 是一个常驻屏幕底部、由全局热键与鼠标热区唤出的分组式快捷启动面板，配套一条可钉选常用应用的透明 Dock 长条；全本地、零外联、可深度自定义，且内存占用仅为 Electron 同类产品的 1/2～1/3。**

### 1.2 产品愿景

把 Windows 桌面的"找应用"成本压到最低：

- 键盘党：`热键 → 看到所有分组 → 单击启动`，全程 < 2 秒；
- 鼠标党：`鼠标碰 Dock → 图标浮起 → 点图标`，全程 < 1 秒；
- 审美党：毛玻璃透明、iOS 式弹出动画、可调透明度，**样式代码级可定制**（这是相比商业软件的核心优势——不满意的地方直接改源码）。

### 1.3 目标用户

| 用户群 | 场景 |
|---|---|
| 安全研究者/白帽（主要）| 工具链庞杂（Burp/Yakit/蚁剑/010…），需要按任务分组管理；对"零外联、本地数据"有洁癖 |
| 开发者 | 多 IDE/终端/数据库客户端切换，热键呼出比 Win+搜索快 |
| 重度游戏玩家 | 游戏平台 + 加速器 + 启动器集中管理 |
| 桌面美化爱好者 | 不满足商业软件的固定样式，想要可改源码的面板 |

### 1.4 核心设计原则

1. **零外联**：不引用任何 HTTP 客户端库，不发一个网络请求（可用 netstat 审计验证）；
2. **本地优先**：全部数据为 `%AppData%\QuickPanel` 下的明文 JSON，用户可手改、可备份、可版本管理；
3. **性能预算制**：每项功能必须满足量化性能指标（见 §5），不达标不上线；
4. **克制**：不做云端同步、不做账号体系、不做插件市场——所有能力本地闭环。

---

## 2. 对标软件分析

### 2.1 竞品全景

| 产品 | 类型 | 价格 | 技术栈 | 内存实测* | 核心优势 | 核心短板 |
|---|---|---|---|---|---|---|
| **Stardock Fences** | 桌面围栏 | $9.99 买断（Steam 有售）| C++ | ~60 MB | 围栏交互成熟、卷起/悬停展开、试用了 10 年+的打磨 | 闭源；样式不可深度定制；试用绑定机器指纹，重装即失效；无 Dock |
| **腾讯桌面整理** | 桌面围栏 | 免费 | C++/Qt | ~120 MB | 免费、格子自动归类 | 腾讯系组件常驻、广告位、不可定制 |
| **MyDockFinder** | Dock | ~¥35 买断 | C++ | ~80 MB | macOS 还原度最高（Dock+菜单栏+启动台）、动效精致 | 闭源；深度接管任务栏，大版本 Windows 更新后偶发崩坏；不可编程 |
| **Winstep Nexus** | Dock | 免费/Ultimate $19.99 | C++ | ~150 MB | 老牌、功能全 | 界面风格陈旧、内存偏重 |
| **DAWN Launcher** | 快捷启动面板 | 免费 | C++/Qt | ~40 MB | 国产精品：分组面板+拖拽+备份，颜值在线 | 开源程度有限、更新节奏慢、无 Dock |
| **Rolan / Aless** | 快捷启动面板 | 免费 | C++ | ~20 MB | 极轻量 | 功能太简（无图标缓存优化、无动画）|
| **Flow Launcher** | 热键搜索启动器 | 免费开源 | C#/.NET | ~100 MB | 插件生态丰富、Everything 联动 | 纯键盘流，无可视化分组面板 |
| **PowerToys Run** | 热键搜索启动器 | 免费（微软）| C#/.NET | ~80-150 MB | 官方、稳定 | 同上，无面板形态 |
| **uTools** | 插件平台启动器 | 免费+付费插件 | Electron | ~200-400 MB | 插件生态最强 | Electron 重；会员付费墙 |
| **Raycast**（macOS 对标）| 启动器 | 免费+Pro | 原生 | ~80 MB | macOS 神器、动效标杆 | 无 Windows 版 |
| **Windows 任务栏固定** | 系统自带 | 免费 | — | 0 | 官方 | 无分组、图标小、无法按任务域隔离 |

*内存为公开社区数据与同配置估算，仅供量级参考。

### 2.2 QuickPanel 的差异化定位

```
              分组可视化面板   Dock 长条   热键呼出   毛玻璃动效   零外联   样式可编程
Fences           ✗(围栏近似)      ✗          ✗          部分        ✗         ✗
MyDockFinder        ✗            ✓           ✗          ✓           ✗         ✗
DAWN Launcher       ✓            ✗           部分       部分        ✓         ✗
Flow Launcher       ✗            ✗           ✓          ✗           ✓         ✓(开源)
uTools              ✗            ✗           ✓          ✗           ✗         ✗
─────────────────────────────────────────────────────────────────────────────────
QuickPanel          ✓            ✓           ✓          ✓           ✓         ✓
```

**一句话：QuickPanel = Fences 的分组 + MyDockFinder 的 Dock + Flow Launcher 的热键，且是唯一"源码级可定制 + 零外联"的方案。**

### 2.3 竞争策略

1. 对个人免费（MIT 开源路线候选）；
2. 卖点不是功能数量，是 **"你的启动器你写"**——面向安全/开发人群的定制自由度；
3. 性能承诺量化公开（内存/CPU/启动时间），每版本附实测数据。

---

## 3. 用户画像与核心场景

### 3.1 用户画像：Aston（主用户，即需求来源）

- Windows 11 24H2 + WSL2 双环境；桌面 52+ 快捷方式；
- 工具域：渗透测试（Burp/Yakit/蚁剑/010）、开发（VS Code/Cursor/IDEA/DataGrip）、日常（微信/网盘/会议）、游戏（Steam/EPIC/MC）；
- 已有 Flow Launcher（键盘流）与 Everything（文件索引）；
- 痛点：桌面 52 个图标找起来乱；Fences 样式不合审美且试用机制烦人；Electron 类软件内存重。

### 3.2 核心场景（用户故事）

| # | 场景 | 验收 |
|---|---|---|
| S1 | 正在渗透测试，需要快速打开 Yakit | `Alt+Q` → 面板弹出 → 网安分组（默认或单击切换）→ 单击 Yakit → 启动 |
| S2 | 看着桌面乱，想把 52 个快捷方式按任务域收纳 | 首次运行自动按关键词播种五分组；后续拖拽添加 |
| S3 | 全屏看视频/打游戏，不想被悬浮条遮挡 | Dock 与面板自动隐藏，退出全屏自动恢复 |
| S4 | 想给最常用的 5 个软件一条"点一下就开"的栏 | 拖快捷方式到 Dock 钉选；划过时图标弹钢琴放大 |
| S5 | 觉得面板丑 | 改源码：颜色/圆角/动画参数都在代码里显式可调 |
| S6 | 换电脑/重装系统 | 拷贝 config.json + icons/ 目录即完整迁移 |

---

## 4. 功能需求

> 状态标注：✅ v0.4 已实现 · 🔨 v0.5-0.8 计划 · 🧭 远期

### 4.1 主面板（PanelWindow）

| ID | 功能 | 优先级 | 状态 | 需求说明 / 验收标准 |
|---|---|---|---|---|
| F1 | 分组侧边导航 | P0 | ✅ | 左侧导航栏列出全部分组，单击切换右侧网格；选中态高亮（#3D7C6FFF）；分组切换不重建窗口（<16ms）|
| F2 | 图标网格 | P0 | ✅ | 右侧 WrapPanel 网格（86×96 单元），图标 44px + 名称换行截断 |
| F3 | 拖拽添加 | P0 | ✅ | 从桌面/资源管理器拖 `.lnk`/`.exe`/`.url`/文件夹 进面板自动解析入库；Drop 后立即持久化；v0.5 支持从面板/Dock 拖出到桌面（FileDrop 落地生成副本）|
| F4 | lnk 启动代理解析 | P0 | ✅ | `.lnk` 不做内部二进制解析，Target 保留 lnk 路径，由 Shell（UseShellExecute）代为解析启动——兼容性覆盖 UWP/带参/提权快捷方式 |
| F5 | 图标提取与缓存 | P0 | ✅ | `SHGetFileInfo` 提取 → PNG 落盘（目标路径 MD5 键名）→ `Freeze()` 位图；提取失败回退首字母生成图标 |
| F6 | 单击启动 | P0 | ✅ | 正确工作目录；目标不存在给出提示 |
| F7 | 右键菜单 | P0 | ✅ | v0.6 增强：启动 / 以管理员运行（正规 UAC）/ 固定到 Dock / 打开所在位置 / 重命名 / 移动到分组 / 从分组移除；中键=管理员运行 |
| F8 | iOS 式弹出动画 | P0 | ✅ | 呼出：上浮 30px→0 (220ms, CubicEase) + 缩放 0.94→1 (260ms, BackEase 回弹) + 淡入 (180ms)；隐藏反向 150ms |
| F9 | 分组管理 UI | P1 | 🔨 | 设置窗口内新建/重命名/删除分组（v0.3 已有基础版）|
| F10 | 拖拽排序 | P1 | 🔨 | 图标拖拽交换位置；分组拖拽排序 |
| F11 | 分组图标自定义 | P1 | 🔨 | 每个分组可选图标/emoji |
| F12 | 启动动效 | P1 | ✅ v0.5 | 图标点击弹一下（scale 1→0.9→1）；面板图标悬停放大 1.12x |

### 4.2 Dock 长条（DockBarWindow）

| ID | 功能 | 优先级 | 状态 | 需求说明 |
|---|---|---|---|---|
| F20 | 常驻透明长条 | P0 | ✅ | 屏幕底部居中，真亚克力（DWM Backdrop）+ Win11 圆角 + 深色模式；v0.7 Dock 默认**不置顶**（设置可开启）；v0.9 **Dock 底板样式**：带底板（胶囊亚克力）/ **纯图标**（整条 Dock 无底无框，图标直接悬浮于桌面，缝隙鼠标穿透）|
| F21 | 分组快进按钮 | P0 | ✅ | 每分组一个按钮，单击唤出面板并定位分组；v0.5 悬停分组按钮热切换面板分组，热区唤出时定位光标所在分组 |
| F22 | 钉选应用 | P0 | ✅ | 拖拽 .lnk/.exe 到 Dock 钉选（上限 14，防重复）；点击启动；右键取消钉选 |
| F23 | 图钉悬停放大 | P0 | ✅ | v0.8 改为悬停即动效（不再依赖移动）：悬停放大 1.32x（140ms BackEase）+ 上浮 8px，离开回弹归位；v0.4-v0.7 的移动式高斯波峰因"静止无反馈"被用户反馈替换 |
| F24 | 实时居中 | P0 | ✅ | 放大变宽时逐帧重算 `Left` 保持水平居中 |
| F25 | 设置入口 | P0 | ✅ | ⚙ 打开设置窗口 |
| F26 | 位置自定义 | P1 | ✅ v0.5 | 底部居中（默认）/ 自由拖动（⠿ 手柄拖放，位置持久化）；图标缩放 0.6–1.6 |
| F27 | 自动隐藏模式 | P1 | 🔨 | 贴边只留 4px，鼠标热区唤出（与面板热区共用）|

### 4.3 唤出与生命周期

| ID | 功能 | 优先级 | 状态 | 需求说明 |
|---|---|---|---|---|
| F30 | 全局热键 | P0 | ✅ | 默认 Alt+Q（可改，格式如 Ctrl+Alt+Space）；冲突自动回退并提示；ReRegister 支持热修改 |
| F31 | 鼠标热区 | P0 | ✅ | 悬停 Dock ≥HotZoneMs 唤出面板；v0.7 热区 `WindowFromPoint` 真实命中检测；v0.8 **仅在分组按钮上悬停才唤出**；v1.0 离开面板+Dock 默认 300ms 自动收回（**0=立即**，0–2000ms 可调，轮询 50ms），**点击其他窗口立即收回**（Deactivated） |
| F32 | 默认隐藏 | P0 | ✅ | 面板随启动隐藏；隐藏=窗口隐藏（非销毁），呼出 <16ms |
| F33 | 单实例 | P0 | ✅ | Global Mutex；第二实例触发既有实例唤出后退出 |
| F34 | 托盘 | P0 | ✅ | v0.6：显示面板/设置/**快速分组入口（列出全部分组，点击即定位唤出）**/备份配置/打开配置目录/退出 |
| F35 | 全屏自动隐藏 | P0 | ✅ | v0.6 优化：`SHQueryUserNotificationState` 官方 API 判定（D3D 独占全屏/演示模式即时隐藏；软全屏 BUSY 结合矩形覆盖确认），无边框最大化不再误判；可在设置关闭 |
| F36 | 开机自启 | P1 | 🔨 | 设置窗口 CheckBox → HKCU Run 键（UI 已有，逻辑已通）|

### 4.4 数据与设置

| ID | 功能 | 优先级 | 状态 | 需求说明 |
|---|---|---|---|---|
| F40 | JSON 持久化 | P0 | ✅ | `%AppData%\QuickPanel\config.json`，明文可手改 |
| F41 | 一键备份 | P0 | ✅ | 托盘/设置窗口触发，时间戳文件落 backup/ |
| F42 | 设置窗口 | P0 | ✅ v1.0 | **左侧分类导航**（常规/外观/Dock/面板/分组/数据），右侧仅显示当前分类；热键 / 自启 / 全屏隐藏 / 热区与收回延迟 / 外观 / Dock / 面板 / 分组 / 数据；设置主题深色/浅色，**标题栏与输入对话框跟随主题**；首运引导显示桌面扫描统计；Dock 右键新建分组 |
| F43 | 首次运行播种 | P0 | ✅ | 按名称关键词自动分类桌面快捷方式进五分组（网安/开发/日常/游戏/工具）；Dock 自动播种常用 6 项 |
| F44 | 配置迁移 | P0 | ✅ | 热键 Alt+Space→Alt+Q 自动迁移；Dock 钉选为空自动播种 |

### 4.5 视觉与动效

| ID | 功能 | 优先级 | 状态 | 需求说明 |
|---|---|---|---|---|
| F50 | 毛玻璃/透明背景 | P0 | ✅ v1.1 | 背景模式三选一（对齐同类产品皮肤模型）：毛玻璃（DWM SystemBackdrop 三级降级）/ 全透明（自定义底色 #AARRGGBB）/ **完全隐形（无底板，一键，仅内容浮于壁纸）**；分层窗口逐像素 alpha（alpha=0 穿透、≥1 可点，与 MyDockFinder/Rolan 同路线）|
| F51 | 悬停反馈 | P0 | ✅ | 图标/按钮悬停背景高亮，按下变色 |
| F52 | 透明度滑杆 | P1 | ✅ v0.3 | 0.55–1.0，实时预览面板 |
| F53 | 动效参数可调 | P1 | 🔨 | 峰值放大/波及半径/上浮高度/动画时长 → 设置窗口暴露（热区延迟 v0.6 已暴露）|
| F54 | 主题（明/暗）| P1 | 🔨 | 颜色令牌集中定义，一键切换（强调色/底色已支持运行时自定义，v0.5）|
| F55 | 图标停留浮动感 | P1 | ✅ v0.6 | Dock 图钉静止悬停时轻微上下浮动（呼吸动效）；面板图标悬停放大 + 点击弹动 |

### 4.6 远期（P2/P3）

| ID | 功能 | 说明 |
|---|---|---|
| F60 | 面板搜索框 | P2 | ✅ v0.6 | 面板顶部搜索框跨分组过滤全部条目（名称包含匹配，Enter 启动首个结果，Esc 清空）；Everything es.exe 联动仍列远期 |
| F61 | 自定义命令启动 | 支持 cmd/PowerShell 命令行条目 |
| F62 | 多显示器/多屏停靠 | 当前 v0.4 以主屏工作区为基准 |
| F63 | 皮肤系统 | 主题包（颜色+圆角+动画参数打包）|
| F64 | 全局热键自定义录制 | 按键捕获 UI 替代手输文本 |
| F65 | 拖拽时自动分组建议 | 根据目标特征推荐分组 |

---

## 5. 非功能需求

### 5.1 性能（当前实测 v0.4 Release+ReadyToRun）

| 指标 | 目标 | 实测 | 达标 |
|---|---|---|---|
| 常驻内存（Private）| 尽量低 | **81 MB**（WPF 框架底噪 ~60MB；Electron 同类 200-400 MB）| ✅（相对目标）|
| 冷启动 | < 500 ms | ReadyToRun 发布，体感即时 | ✅ |
| 呼出响应 | < 16 ms（单帧）| 窗口常驻不重建，动画 GPU 合成 | ✅ |
| 空闲 CPU | ≈ 0% | 唯一周期任务为 200ms 光标/全屏轮询（单次 <1μs 级）| ✅ |
| 图标首次加载 | 53 个 < 300 ms | SHGetFileInfo + PNG 落盘缓存，二次启动零提取 | ✅ |

> 注：WPF 框架底噪（约 60-70 MB）是运行时固有开销。若未来需要 <50 MB，迁移路径为 WinUI3/NativeAOT（v2 再议）。

### 5.2 安全与隐私

| 项 | 承诺 | 验证方式 |
|---|---|---|
| 零外联 | 不引用任何 HTTP 客户端；进程级零网络连接 | `netstat -ano \| findstr <pid>` 应为空 |
| 数据本地 | 明文 JSON，无混淆、无隐藏写盘 | 目录可审计 |
| 提权边界 | 管理员启动仅通过标准 UAC（Verb=runas），**不绕过、不静默提权** | 代码审计 |
| 无遥测 | 无崩溃上报、无统计 | 代码审计 |

### 5.3 兼容性

| 项 | 说明 |
|---|---|
| 系统 | Windows 10 1903+（毛玻璃降级为模糊/纯色）、Windows 11 全系（含 24H2）|
| DPI | PerMonitorV2，多屏缩放不模糊 |
| 已知限制 | 多显示器场景以主屏工作区为基准（F62 规划中）；Win11 24H2 AccentPolicy 亚克力失效已规避（改 DWM Backdrop）|

### 5.4 可靠性

| 项 | 说明 |
|---|---|
| 崩溃日志 | 全局 Dispatcher/AppDomain 异常 → `%AppData%\QuickPanel\error.log` |
| 配置自愈 | config.json 损坏自动重建默认并播种 |
| 毛玻璃降级链 | DWM Backdrop → AccentPolicy BLURBEHIND → 纯色（`acrylic.log` 记录每级结果）|
| 单实例竞态 | Mutex + EventWaitHandle 双保险 |

---

## 6. 技术栈与选型论证

### 6.1 选型对比（决策记录）

| 维度 | C#+WPF ⭐ | C#+WinUI3 | Electron | Tauri | C++/Qt |
|---|---|---|---|---|---|
| 内存 | 81MB 实测 | ~70MB | 200-400MB | 30-50MB | ~30MB |
| 启动 | <500ms | 中 | 慢(>1s) | 快 | 最快 |
| 毛玻璃/系统融合 | DWM API 直接可用 | 原生最好 | Web 模拟 | WebView2 受限 | 原生好 |
| lnk/图标/热键/托盘 | 全系统 API | 部分需 P/Invoke | npm 生态参差 | Rust 生态 | 全自研 |
| 开发效率（AI 协作）| 最高 | 中（模板坑多）| 高 | 低 | 低 |
| 部署 | 单 exe + 框架依赖 | 需 AppSDK runtime | 巨大 | 单 exe+WebView2 | 单 exe |
| **结论** | **✅ 采用** | ✗ 坑 | ✗ 重 | ✗ 学习成本 | ✗ 成本 |

**决策：C# 12 + WPF + .NET 8 LTS。** 性能瓶颈不存在的本地工具场景下，决定性因素是开发效率与系统 API 覆盖度；毛玻璃经 DWM Backdrop 在 24H2 上实测可达。

### 6.2 依赖清单（刻意保持极简）

| 依赖 | 用途 | 来源 |
|---|---|---|
| .NET 8 Desktop Runtime | 运行时 | 微软官方 |
| `Hardcodet.NotifyIcon.Wpf`（候选）或 WinForms NotifyIcon | 托盘 | **当前用 WinForms 内置，零 NuGet** |
| System.Text.Json | 配置 | .NET 内置 |
| Win32 P/Invoke（user32/dwmapi/shell32/gdi32）| 热键/热区/毛玻璃/图标/DPI | 系统自带头文件 |

> 当前 **NuGet 第三方依赖 = 0**。

### 6.3 已规避的技术坑（决策记录）

| 坑 | 规避方式 |
|---|---|
| WPF Window 不支持 RenderTransform | 滑动动画改驱动 `Window.Left`；弹出动画放内容层 Border |
| 对 Hidden 窗口调 Activate() 抛异常 | `Show()` 替代 + 延迟激活，Topmost 兜底 |
| dotnet CLI 不支持 COMReference | 放弃 WScript COM，lnk 启动/图标全走 Shell 与 SHGetFileInfo |
| WinForms+WPF 隐式 using 冲突 | csproj `<Using Remove>` 显式裁剪 |
| Win11 24H2 AccentPolicy 亚克力失效 | 改 DWMWA_SYSTEMBACKDROP_TYPE（Windows 11 官方机制）|
| 附加属性不能进对象初始化器 | 后置 `RenderOptions.SetBitmapScalingMode` |
| ItemsSource 绑定晚于 Window.Loaded | 依赖 `ItemContainerGenerator.StatusChanged` 重建引用，勿在 Loaded 直取容器 |
| DataTemplate 匿名 Freezable 被密封冻结 | 动画前检测 `IsFrozen`，冻结则替换为新实例（v0.5）|
| 矩形判定把无边框最大化当全屏（误隐藏 Dock）| 改用 `SHQueryUserNotificationState` 官方 API + BUSY 才做矩形确认（v0.6）|
| 纯几何热区在 Dock 被其他窗口覆盖时误触发 | 悬停判定叠加 `WindowFromPoint`+`GA_ROOT` 真实命中检测（v0.7）|
| 移动式高斯放大在鼠标静止时无反馈 | 改为 Enter/Leave 悬停缩放动画，与移动无关（v0.8）|
| 点击其他应用后面板滞留 | 面板 `Deactivated` 即隐藏 + 收回延迟降为 600ms 可调（v0.8）|
| DropShadowEffect 中间位图裁切 RenderTransform 溢出（图钉上浮被"削掉"，加窗口余量无效）| Dock 一律不挂 Effect；悬浮可读性由图标本身保证（v1.0）|
| MV3 扩展默认"点击时"授权 | 桌面应用无此问题（对照：浏览器扩展方案）|

---

## 7. 系统架构

### 7.1 架构图

```
┌──────────────────────────── QuickPanel（单进程 · Mutex 单实例）─────────────────────────────┐
│                                                                                            │
│  ┌─────────── UI 表面层（双窗口常驻）───────────┐      ┌────────────── 服务层 ──────────────┐│
│  │                                              │      │                                    ││
│  │  DockBarWindow   底部居中透明长条             │      │  HotkeyService   全局热键(ReRegister)││
│  │   ├─ 分组快进按钮(×5)                        │      │  HotZoneService  热区/收回/全屏检测  ││
│  │   ├─ DockPins 钉选图标(弹钢琴放大)            │◄────►│  LaunchService   启动/UAC 提权      ││
│  │   └─ ⚙ 设置入口                              │ 事件 │  ShortcutParser  lnk/url/目录解析   ││
│  │                                              │      │  IconCache       图标提取+PNG缓存   ││
│  │  PanelWindow     热键/热区唤出的横版面板      │      │  ConfigStore     JSON 持久化+备份   ││
│  │   ├─ Sidebar 分组导航(168px)                  │      │  FirstRunSeeder  首次运行播种       ││
│  │   └─ ItemsGrid 图标网格(拖拽目标)             │      │  Acrylic          毛玻璃三级降级    ││
│  │                                              │      │                                    ││
│  └──────────────────────────────────────────────┘      └────────────────┬───────────────────┘│
│                                                                         ▼                    │
│                                            ┌──────────────────────────────────────┐         │
│                                            │ %AppData%\QuickPanel\                │         │
│                                            │   config.json   icons/*.png          │         │
│                                            │   backup/*.json error.log acrylic.log│         │
│                                            └──────────────────────────────────────┘         │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 7.2 模块职责表

| 模块 | 职责 | 关键实现 |
|---|---|---|
| HotkeyService | 全局热键注册/重注册/回退 | RegisterHotKey + WM_HOTKEY 钩子（防重复挂钩）|
| HotZoneService | 热区呼出、离开收回、全屏检测 | 200ms DispatcherTimer + GetCursorPos + GetForegroundWindow 全屏判定 |
| LaunchService | 三类目标启动 + UAC | ProcessStartInfo(UseShellExecute) |
| ShortcutParser | lnk/url/目录/文件归一化为 LaunchItem | Shell 代理解析策略（见 §4.1 F4）|
| IconCache | 图标提取→PNG 缓存→冻结位图 | SHGetFileInfo + MD5 键名 + 失败兜底生成 |
| ConfigStore | 加载/保存/备份/迁移 | System.Text.Json + 版本迁移（热键/钉选播种）|
| Acrylic | 三级降级毛玻璃 | DWM Backdrop → AccentBLURBEHIND → 纯色，全程日志 |
| FirstRunSeeder | 桌面扫描 + 关键词分类 | 五分组规则表 |

### 7.3 代码规模（v0.4）

21 文件 / 约 1400 行 C#+XAML / NuGet 第三方依赖 0。

---

## 8. 数据模型

```json
{
  "settings": {
    "hotkey": "Alt+Q",
    "panelEdge": "bottom",
    "hotZoneMs": 300,
    "dockAutoHide": false,
    "theme": "dark",
    "opacity": 0.92,
    "backgroundMode": "transparent",
    "surfaceColor": "#3C151518",
    "accentColor": "#7C6FFF",
    "dockPositionMode": "bottomCenter",
    "dockX": 0,
    "dockY": 0,
    "dockTopmost": false,
    "dockIconPlate": true,
    "panelHideMs": 300,
    "settingsTheme": "dark",
    "iconScale": 1.0
  },
  "groups": [
    {
      "id": "sec",
      "name": "网安",
      "dockPinned": true,
      "items": [
        {
          "name": "Burp Suite",
          "target": "D:\\...\\Burp.lnk",
          "args": "",
          "workDir": "D:\\...\\",
          "kind": "App",
          "runAsAdmin": false,
          "iconCache": "a3f8c2.png"
        }
      ]
    }
  ],
  "dockPins": [
    { "name": "微信", "target": "D:\\Program Files\\Tencent\\Weixin\\Weixin.lnk", "kind": "App" }
  ]
}
```

目录约定：

```
%AppData%\QuickPanel\
├─ config.json        主配置（明文）
├─ icons\*.png        图标缓存（目标 MD5 键名）
├─ backup\*.json      一键备份
├─ acrylic.log        毛玻璃降级链日志
└─ error.log          崩溃日志
```

---

## 9. UI / UX 规范

### 9.1 动效参数表（代码级可调）

| 动效 | 参数 | 当前值 |
|---|---|---|
| 面板弹出-缩放 | 0.94→1，BackEase(EaseOut, amp 0.45) | 260ms |
| 面板弹出-上浮 | Y 30→0，CubicEase(EaseOut) | 220ms |
| 面板弹出-淡入 | Opacity 0→Settings.Opacity | 180ms |
| 面板隐藏 | 反向（缩 0.94 / 沉 26 / 透明）| 150ms |
| Dock 图标峰值放大 | 1 + 0.55·Gauss(d, σ=70) | 峰值 1.55x |
| Dock 图标上浮 | (scale-1)×22px | 峰值 ~12px |
| Dock 回位 | 缩放/浮起归一 | 160ms CubicEase |
| 全屏隐藏触发 | 前台窗口覆盖整显示器 | 200ms 轮询 |

### 9.2 视觉令牌（当前）

| 令牌 | 值 |
|---|---|
| 面板/DOCK 底色 | 亚克力(暗) + 叠加 #C0151518 |
| 卡片底 | #2EFFFFFF / #1FFFFFFF |
| 主文字 | #FFDCDDE2 |
| 次级文字 | #FFB9BDC7 |
| 主题强调色 | #7C6FFF（紫）|
| 选中态 | #3D7C6FFF |
| 圆角 | 面板 12 / 卡片 10 / Dock 16 / 图标 12 |

---

## 10. 里程碑与状态

| 里程碑 | 内容 | 状态 |
|---|---|---|
| M0 | 环境搭建（.NET 8 SDK 8.0.427）| ✅ |
| M1 | 双窗口骨架 + 分组播种 + lnk/图标 + 启动 | ✅ v0.1 |
| M2 | 热键/热区唤出 + iOS 动画 + 横版 + 毛玻璃 + 崩溃日志 | ✅ v0.2 |
| M2.5 | 真透明修复（DWM Backdrop）+ 设置窗口 + 钉选 + 弹钢琴 + 实时居中 + 全屏隐藏 | ✅ v0.3-v0.4 |
| M3.5 | 自测反馈迭代：热区分组定位 · 图标动效（悬停/点击）· 拖出 · 背景/颜色自定义 · Dock 自由位置/缩放 · 设置重构+首运引导 | ✅ v0.5 |
| M3.6 | 用户反馈迭代：面板搜索框 · 右键菜单增强 · 托盘分组入口 · 图钉呼吸浮动 · 全屏判定 API 化 + 设置美化（卡片式）| ✅ v0.6 |
| M3.7 | 用户反馈迭代：Dock 默认桌面层级（不置顶，可配置）· 热区真实命中检测 | ✅ v0.7 |
| M3.8 | 用户反馈迭代：收回 600ms+点击即收 · 图钉悬停放大 · 热区仅分组按钮触发 · 底板样式 · 设置深/浅主题 · Dock 右键建组 | ✅ v0.8 |
| M3.9 | 用户反馈迭代：设置分区导航重构 · 收回 300ms+100ms 轮询 · 纯图标=整条 Dock 无底悬浮 | ✅ v0.9 |
| M3.10 | 用户反馈迭代：近隐形底板（AA≤20 去阴影描边）· 图钉浮动防裁切 · 对话框/标题栏跟随主题 · 收回 0=立即 + 50ms 轮询 | ✅ v1.0 |
| M3.11 | 用户反馈迭代：背景模式三选一 +「完全隐形」一键无底板（调研同类产品后对齐皮肤模型）· Dock 移除 DropShadowEffect（Effect 位图裁切根因） | ✅ v1.1 |
| M3 | 拖拽排序 · 分组重命名 UI 精修 · Dock 位置可选 · Everything 搜索 | 🔨 v0.5-0.6 |
| M4 | 主题令牌化（明/暗）· 动效参数设置化 · 启动动效 | 🔨 v0.7 |
| M5 | ReadyToRun 发布流水线 · 图标资源 · 安装包 · 零外联审计自动化 | 🔨 v1.0 |

---

## 11. 风险与已知限制

| # | 风险/限制 | 影响 | 缓解 |
|---|---|---|---|
| R1 | WPF 内存底噪 ~60MB | 与 <50MB 原始预期有差距 | 已诚实标注实测值；v2 可选 WinUI3/AOT 迁移 |
| R2 | 24H2 DWM Backdrop 依赖系统 DWM | 个别 GPU/驱动组合可能异常 | 三级降级链 + acrylic.log 可诊断 |
| R3 | 全屏检测按"窗口覆盖显示器"判定 | 伪全屏（无边框窗口化游戏）可能漏判 | 后续可加 WDA_EXCLUDEFROMCAPTURE 探测 |
| R4 | 单显示器假设 | 多屏下面板出现在主屏 | F62 规划 |
| R5 | lnk 不解析内部目标 | 图标带系统小箭头覆盖 | Shell 官方行为；可通过系统注册表全局去箭头 |
| R6 | 单实例 EventWaitHandle 跨会话 | 多用户会话场景未验证 | 个人使用场景低风险 |
| R7 | 配置无版本号字段 | 未来 schema 变更需迁移 | v1.0 引入 schemaVersion |

---

## 12. 测试清单（人工验收脚本）

1. 启动 → Dock 出现在底部居中、半透明毛玻璃、五分组 + 钉选图标 + ⚙；
2. `Alt+Q` → 面板弹出动画（上浮+放大+淡入），再按收回；
3. 鼠标悬停 Dock >300ms → 面板唤出；鼠标离开面板+Dock 1.5s → 自动收回；
4. 鼠标划过 Dock 图标 → 弹钢琴放大 + 推挤 + 上浮；离开回弹；
5. 拖桌面 .lnk 进面板网格 → 图标出现且点击可启动；
6. 拖 .lnk 到 Dock → 钉选；右键 Dock 图标 → 取消钉选；
7. 右键面板图标 → 管理员运行（弹 UAC）/ 移除；
8. 设置：透明度滑杆实时预览；热键改 Ctrl+Alt+Space 立即生效；新建/重命名/删除分组；开机自启勾选后注册表出现 Run 键；
9. 浏览器 F11 全屏 → Dock 与面板自动隐藏；退出全屏 → Dock 恢复；
10. 托盘备份 → backup 目录出现时间戳文件；
11. `netstat -ano | findstr <QuickPanel pid>` → 零网络连接；
12. 重启应用 → 配置/图标完整恢复（二次启动图标零提取耗时）；
13. 悬停 Dock 分组按钮 → 面板弹出并定位该分组；滑到另一分组按钮 → 面板实时切换；
14. 面板图标悬停放大、点击弹动；从面板/Dock 拖图标到桌面 → 落地生成快捷方式副本；
15. 设置：背景模式毛玻璃/全透明切换、底色/强调色修改实时生效、Dock ⠿ 拖动定位与图标缩放。

---

## 13. 附录

### 13.1 Win32 API 清单（全部系统调用，零第三方）

| API | 模块 | 用途 |
|---|---|---|
| RegisterHotKey / UnregisterHotKey | user32 | 全局热键 |
| GetCursorPos | user32 | 热区轮询 |
| GetForegroundWindow / GetWindowRect | user32 | 全屏检测 |
| MonitorFromWindow / GetMonitorInfo | user32 | 显示器边界 |
| DwmSetWindowAttribute | dwmapi | 毛玻璃/圆角/深色 |
| SetWindowCompositionAttribute | user32 | AccentPolicy 兜底 |
| SHGetFileInfo | shell32 | 图标提取 |
| IWshShell（仅在需要解析 lnk 内部时备用）| COM | 当前已弃用（Shell 代理解析）|

### 13.2 文件清单（v0.4）

```
QuickPanel/
├─ QuickPanel.csproj / app.manifest / App.xaml(.cs)
├─ Models/  LaunchItem.cs · Group.cs · AppSettings.cs
├─ Services/  ConfigStore · ShortcutParser · IconCache · LaunchService
│             HotkeyService · HotZoneService · FirstRunSeeder · Acrylic
├─ ViewModels/  ViewModels.cs（ItemVM/GroupVM/MainViewModel）
├─ Windows/  PanelWindow · DockBarWindow · SettingsWindow（xaml+cs）
└─ docs/  PRD.md（本文档）
```

### 13.3 发布产物

```
bin/Release/net8.0-windows/win-x64/publish/QuickPanel.exe
（框架依赖模式 + ReadyToRun；安装 .NET 8 Desktop Runtime 的机器直接运行）
```

---

*文档结束。本 PRD 与代码同步演进：每次功能迭代先更新本文档对应章节，再动代码。*

---

## 19. 初始化向导（Onboarding）

### 19.1 设计目标

首次安装后用户面对一个空面板会不知所措。向导负责把"装完就能用"落地。
**不替用户分类**：只把扫描到的程序铺成图标墙，分组由用户自己建、自己命名、自己拖。

### 19.2 流程（5 步，v1.25 起）

| 步骤 | 内容 | 可跳过 |
|---|---|---|
| 1 欢迎 | 产品定位 + 核心用法（热键/Dock/分组自由/本地数据），末尾预告"下一步列出桌面程序" | 否 |
| 2 整理 | 左侧「桌面上的程序」图标墙（可筛选、刷新、加开始菜单）；右侧「我的分组」：常用（Dock）+ 用户自建分组。拖拽归组，拖错可在分组间互拖或拖回左侧；右上角「+ 新建分组」 | 是 |
| 3 选项 | 全局热键、开机自启动、Dock 常驻显示 | 是 |
| 4 外观 | 背景样式（毛玻璃/半透明/完全透明）、强调色、Dock 文字颜色，改动即时生效 | 是 |
| 5 完成 | 统计（分组数/条目数/常用数）+ 4 条使用提示 | 否 |

### 19.3 关键实现

- **主题**：浅色（窗口 #F8F8FB、卡片纯白、正文 #1B1F2A、次要 #5A6172）。深色向导在浅色桌面上显脏，v1.27 起统一改浅色
- **文案**：短句、说结果不说功能（"按 Alt+Q 呼出面板，点图标即启动"），参考成熟软件的首次引导
- `Services/AppScanner`：默认只扫桌面（用户+公共），可手动追加开始菜单；**去重键用显示名**（`.lnk` 的 Target 是 lnk 自身路径，桌面版与开始菜单版路径不同会漏判重复）
- 拖拽用私有格式 `QuickPanel.OnboardApp`，左侧 ↔ 分组 ↔ 常用 三区互通；`RemoveEverywhere` 保证一个程序只在一个位置
- `Windows/OnboardingWindow`：单窗口 5 页 + 步骤点；`IsDefault` 让 Enter 推进
- 无边框窗口的 `DragMove` 必须避开可交互控件（按钮/输入框/下拉框），否则会吞掉点击；色板因此用 `Button` 而非 `Border`
- 色板选择环：选中 2px 强调紫 + 与色块留 2px 间隙（浅色卡片上白色描边看不见）；白色色块也带 1px 淡描边

### 19.4 后续再次导入

设置 → 分组 → 「扫描添加程序…」打开 `ScanAddWindow`，同样的扫描列表，勾选后加入指定分组。

### 19.5 首运与播种的关系

`MainViewModel.LoadFromConfig(seedOnFirstRun)`：首运时**不**自动播种，交给向导让用户自己整理；非首运仍保留原有的空配置兜底播种。

## 20. v1.28 透明与可读性定稿

### 20.1 毛玻璃模式的取舍

DWM `SystemBackdrop`（DWMSBT_TRANSIENTWINDOW）会**涂满整个窗口矩形**。Dock 为了悬停放大不被裁切，
必须在胶囊上方保留一段透明余量 —— 这段余量同样被涂成玻璃，视觉上就是"胶囊上下多出一大块没用的灰膜"。
真毛玻璃与"窗口内留余量"在 DWM 下无法共存。

结论：**所有背景模式统一使用分层窗口**（`AllowsTransparency=true`），不调用 DWM 背板；
"毛玻璃"= 固定半透明深色板 `#D81B1B20`，视觉接近且不会有多余矩形。

### 20.2 文字可读性规则

- 底板较实（`SurfaceColor.A ≥ 0x60`，含毛玻璃）：文字用主题浅色，靠深色底板保证对比
- 底板很淡（完全透明 / 隐形，`A < 0x60`）：文字改用用户设置的 **Dock 文字色**（默认黑），
  因为该颜色本来就是为"浮在任意壁纸上"挑选的；`TextPrimaryBrush` / `TextSecondaryBrush` /
  `CardSubtleBrush` / `HairlineBrush` 四个令牌一起跟随，避免"分组名看得见、条目名看不见"
- Dock 侧另有外发光兜底：`MakeReadabilityGlow()` 按文字色明暗自动在黑字外描白、白字外描黑
