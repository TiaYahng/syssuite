# UI-SPEC.md — SysSuite 界面规范 v1.1

## 1. 全局设计

- 布局形态：左侧 NavigationView 侧边栏 + 内容区 ContentFrame + 全局底部状态栏
- 运行时：WPF (.NET 8) + WPF-UI（Fluent/Mica），侧边栏可折叠（汉堡按钮）
- 主题：深/浅色跟随系统（ThemeService），所有颜色引用 ThemeResource，禁止硬编码
- 窗口：默认 1280×800，最小 1024×680，记住位置与尺寸（%AppData%\SysSuite\ui.state.json）
- 自适应：侧边栏/水平导航与状态栏占用后，内容区必须随窗口最大化、还原、拖拽缩放同比例拉伸或收缩；禁止固定内容宽度、居中限宽或只按初始尺寸计算布局
- 字体：Segoe UI Variable；正文 14 / 小字 12 / 页面标题 28 SemiBold / 卡片标题 18 Medium
- 图标：Segoe Fluent Icons（SymbolIcon），每个侧边栏项配 Symbol + 文本
- DPI：app.manifest 声明 PerMonitorV2
- 所有耗时操作：IProgress<T> 报告进度 +/− CancellationToken 可取消 + 状态栏显示
- 设计令牌：`Radius/Card=8`、`Radius/Dialog=12`、`Spacing/XS=4`、`Spacing/S=8`、`Spacing/M=16`、`Spacing/L=24`、`Duration/Fast=120ms`、`Duration/Normal=200ms`、`Elevation/Dialog=32`。颜色、字体、尺寸一律引用资源 key，不得在控件内散落字面量
- 内容间距（当前验收标准）：页面容器 `Margin=16,4,16,12`；页面根边距 `8,0,8,8`；区块间距 `4px`；内部列表行高 `22px`；空间足够时禁用内部滚动条。后续页面按此基准实现
- 空态规范：每页统一 EmptyState（图标 + 一句解释 + 主操作或帮助链接）；骨架屏只用于首次加载，刷新用 InfoBar/ProgressRing
- 可访问性：最小命中区 32×32；焦点可视化完整；键盘顺序与视觉顺序一致；所有图标按钮配 AutomationProperties.Name；正文对比度 ≥ 4.5:1
- 高危 UI 分级：L0/L1 常规入口；L2 实验入口默认隐藏，需设置页显式开启；L3 只在实验分支/灰度构建显示。所有高危入口统一 `SeverityBanner + ConfirmationInputBox + 预览/备份/执行`
- 状态一致性：任务运行期间同类主命令禁用；页面切换保留运行任务；导航返回不重置筛选条件；所有异步错误可复制诊断 ID

## 2. 主窗口 Shell（MainWindow.xaml）

┌──────────────────────────────────────────────────────────┐
│ [☰] SysSuite        [🔍 全局搜索 Ctrl+K]      [─][□][×]  │ ← 自绘标题栏
├────────┬─────────────────────────────────────────────────┤
│ 🏠仪表盘│  页头区（页面标题 + 主操作按钮，由各 Page 提供）  │
│ 💻系统信息│                                              │
│ 🧹磁盘清理│                                              │
│ 📦卸载器 │              ContentFrame                     │
│ 🛡️安全中心│           （当前 Page 用户控件）               │
│ 🖼️桌面整理│                                              │
│ 🔄软件管家│                                              │
│ 🧰工具箱 │                                                │
├────────┼─────────────────────────────────────────────────┤
│ ⚙️ 设置  │              （footer 固定项）                  │
├────────┴─────────────────────────────────────────────────┤
│ ● 就绪 │ 后台任务: 扫描C盘 43% [取消] │ v1.0.0 │ 📋日志  │ ← 全局状态栏
└──────────────────────────────────────────────────────────┘

- 控件树：
  MainWindow
  ├─ TitleBar（Grid）：Logo + 全局搜索 AutoSuggestBox（范围：软件/清理规则/设置项）
  ├─ NavigationView（PaneDisplayMode=Left, OpenPaneLength=220, SelectionChanged→导航）
  │  ├─ MenuItems：8 项（见 §4 顺序）
  │  └─ FooterMenuItems：设置
  ├─ Frame（ContentFrame，NavigationCacheMode=Enabled，页面切换保留状态）
  └─ StatusBar（Grid）：
     ├─ StatusText（全局状态）
     ├─ TaskProgress（ProgressBar 256px + 取消按钮，绑定 BackgroundTaskManager.Current）
     └─ VersionText / LogButton（打开 %AppData%\SysSuite\logs）

- 导航注册表（App_hosting 中配置，key=页面枚举 NavPage）：
  Dashboard / SystemInfo / Cleaner / Uninstaller / Security / Desktop / SoftwareHub / Toolbox / Settings

- Shell 契约：MainWindow 仅持有 NavigationView、TitleBarSearch、ContentFrame、StatusBar；页面通过 `INavigationService.Navigate(NavPage, parameter?)` 跳转，禁止页面间直接实例化
- 全局任务栏：后台任务以 `BackgroundTaskCard{Id,Title,Progress,State,CancelCommand}` 注入 StatusBar 弹层；主状态栏仅显示最近 1 个前台任务，多任务收进任务中心
- 搜索行为：`Ctrl+K` 打开浮层，Enter 导航或执行第一项，`Esc` 关闭；空结果展示帮助建议；搜索结果记录 `source=Page|Setting|App|Rule|Command`

## 3. 通用页面骨架

每个 Page 统一三段式：
1) 页头：PageHeader（标题/副标题 + 1~2 个主按钮）
2) 主体：内容
3) 若列表数据 >1 万条：ListView + 虚拟化（Recycling）+ 底部分页/计数

每个 ViewModel 必须继承 `PageViewModelBase`，统一包含 `IsBusy / BusyText / HasError / ErrorText / DiagnosticId / RefreshCommand / CancelCommand`；命令使用 `[RelayCommand]`，禁止在 code-behind 写业务逻辑。页面注册 DI 时同时声明 ViewModel 与 View。

组件复用规范：
- `PageHeader`：标题、副标题、主/次按钮、风险徽章、任务状态
- `MetricCard`：标题、值、单位、百分比环、趋势、详情文本
- `FilterToolbar`：搜索、过滤、排序、刷新；筛选状态由 ViewModel 持有
- `RiskBadge`：Safe / Caution / Risky / Experimental
- `EmptyState`、`SkeletonList`、`DiagnosticInfoBar`、`ConfirmationInputBox`
- `SummaryBar`：选中数、总大小、风险计数、主执行按钮

## 4. 页面明细（按侧边栏顺序）

### 4.1 DashboardPage 仪表盘
布局（Grid 3 行 × 3 列）：
┌──────────┬──────────┬──────────┐
│ CPU 环形卡 │ 内存环形卡 │ GPU环形卡 │   行1：四张 HealthCard
├──────────┴──────────┴──────────┤
│ 磁盘环形卡 │  实时监控折线图（Tab: CPU/内存/磁盘/网络）│  行2
├───────────────┬────────────────┤
│ 系统摘要卡     │  快捷操作 4 大按钮           │  行3
└───────────────┴────────────────┘
绑定/命令：
- HealthCard { Title, Percent, DetailText }×4  ← DashboardViewModel.Metrics
- RealtimeChart（自绘 or LiveCharts2）← MetricSeries
- 快捷按钮：StartQuickCleanCommand / CheckUpdatesCommand / StartScanCommand / OpenUninstallerCommand
- 摘要：OS 版本、CPU 型号、内存条数与容量、磁盘总/剩余
- 响应式：≥1440px 显示 3 卡一列；1024~1439px 折为 2 卡一列；低于 1100px 高度时监控图进入 2/3 屏高摘要模式；卡片均支持 `AutomationProperties.Name`
- 数据节流：仪表盘 1s 快照、图表 2s 聚合；页面不可见时暂停 UI 绑定但不停止采集；返回页面时先展示缓存再刷新

### 4.2 SystemInfoPage 系统信息
布局：左 240px 分类树（TreeView）｜右属性表格
- 分类节点：处理器/主板/内存/显卡/存储/网络/声卡/电池/操作系统
- 右侧：DataGrid 两列（项目 / 值），行右键→复制；顶部工具条：刷新、导出报告（TXT/HTML/JSON）
- 底部横条：温度徽章（CPU 包/核/GPU/主板/磁盘），>85℃ 变红
命令：RefreshCommand / ExportReportCommand(format)
- 左树状态持久化（选中节点/展开状态）；导出按钮带下拉菜单；属性表支持搜索、复制单元格、复制 JSON 片段；无传感器显示“不可用”而非 0℃
- 结构契约：`InfoNode{Id,Title,Icon,Properties}`；`InfoProperty{Key,Value,Unit,Risk,Source,IsCopyable}`；只读数据禁止行内编辑

### 4.3 CleanerPage 磁盘清理
单页布局：顶部操作栏 + 盘符芯片 + 扫描进度/剩余时间；左侧分类摘要，右侧按名称分组。
1) 统一扫描：一次勾选固定盘符后扫描临时文件、重复文件与空文件夹；分类摘要只做过滤，不再切换页签
   [扫描] → 进度与 ETA → 按名称分组结果（组头勾选/组内逐项勾选） → [清理选中]
   每项显示盘符徽标、路径、大小与风险说明；盘符按钮打开所在路径
2) 重复文件：同文件名跨盘聚合展示，组头可全选组内副本
3) 临时文件：覆盖用户/系统 Temp 与固定盘常见 Temp 根；`Risky` 项默认禁选
4) 空文件夹：删除前重新校验仍为空；标准程序、便携和含可执行文件目录自动排除
命令：扫描 / 取消 / 全选 / 分组选择 / 清理选中 / 打开所在路径
绑定：`DiskCleanerRow{Item,Group,IsChecked}`、`DiskScanProgress{Phase,Progress,ProcessedFiles,TotalFiles,Elapsed,EstimatedRemaining}`
- 清理前弹确认并显示项数与可释放大小；清理仅允许本次扫描报告中的路径
- 系统盘默认不选；标准程序目录、软件/便携/工具目录及含可执行文件或 portable 标记的目录自动排除
- 结果使用虚拟化 `CollectionView` 分组；进度条显示阶段、文件数、已处理量与估算剩余时间
- 扫描使用低优先级专用后台任务并限制并行度；扫描中盘符与分类区域保持可交互

### 4.4 UninstallerPage 卸载器
布局：上工具条 ｜ 左列表 700px ｜ 右详情 ｜ 底部队列
- 工具条：搜索框、过滤 ComboBox（全部/MSI/Store/大型>500MB/最近30天/有残留）、排序（名称/大小/日期）
- 列表行：图标(32) 名称 大小 安装日期 发布者徽章
- 详情面板：大图标、元信息表、按钮组：
  [卸载] [强制卸载(红)] [静默卸载] [打开目录] [打开注册表项] [扫描残留] [加入卸载队列]
- 底部：卸载队列卡（队列项 + 当前进度 + 跳过按钮）
- 空态 / 正在刷新骨架屏（Skeleton）
命令：UninstallCommand(app, mode) / ForceCommand / ScanLeftoverCommand / RefreshCommand
- `mode=Standard|Quiet|Store`；`Force` 属 L2，默认隐藏，设置页显式开启后进入备份预览 + 确认句 + 分区进度；预留升级链路统一走 SoftwareHub
- 列表选中状态、筛选、排序与滚动位置在刷新后尽量保留；失败行提供“复制诊断/重试/查看日志”
- 详情按钮可用性由能力标志决定：`CanUninstall/CanQuiet/CanForce/CanScanLeftover`；无权限或来源不明时禁用并显示原因

### 4.5 SecurityPage 安全中心
布局：顶部横向大卡（评分圆环 0-100 + 高/中/低计数）｜下方三 Pivot：
1) 体检报告：问题列表（RiskIcon/描述/等级/修复方式），底 [一键修复]
2) 启动项：分组（注册表Run/计划任务/服务/启动文件夹），每行 [启用|禁用] 开关，禁用前记录原值可撤销
3) 防护状态：Defender 实时保护/病毒库日期/防火墙；按钮：[快速扫描][指定目录扫描（FolderPicker）][全盘扫描]
命令：RunHealthCheckCommand / FixCommand(items) / ToggleStartupCommand(item) / StartDefenderScanCommand(mode)
- 防护控制为 L2：MVP0 只显示状态与扫描入口；关闭实时保护、修改防火墙、添加排除项等入口默认隐藏。实验分支开启后使用 SeverityBanner、ConfirmationInputBox、自动恢复倒计时、审计提示和不可撤销警示
- 体检修复队列逐项显示影响范围/备份状态/失败原因；自动修复默认只勾选 Safe 项；`Caution/Risky` 必须单独确认
- 启动项行显示来源路径、签名者、发布者、当前状态与最后启用时间；禁用后可撤销，来源文件缺失显示 EmptyState 而非报错

### 4.6 DesktopPage 桌面整理
布局：左预览画布（按显示器比例缩放的桌面示意，显示分区矩形与图标点，只读预览）｜右侧面板
- 右面板 Tabs：
  1) 分区管理：分区列表（名称/规则/图标数/启用开关）、[新建分区][删除]
  2) 分类规则：规则表（名称/匹配：扩展名·正则·发布者/目标分区），[添加][编辑][上移下移]
  3) 布局方案：已保存方案列表（缩略图+时间），[保存当前][应用][导出JSON][导入]
- 顶部工具条：[立即整理]（下拉：按类型/按频次/按规则）、[启用桌面分区覆盖层] 开关
说明：覆盖层为独立 OverlayWindow（Fences 式），此处仅管理与预览
命令：OrganizeCommand(mode) / SaveLayoutCommand(name) / ApplyLayoutCommand(id) / ToggleOverlayCommand
- 覆盖层为 L2，默认关闭；设置页显式开启并提示独立进程/自动恢复；主页面只做规则和布局配置
- 分区模型：`Zone{Id,Name,MonitorId,Bounds,RuleSetId,Enabled}`；规则模型：`ClassificationRule{Id,Priority,Matchers,TargetZoneId}`；拖拽预览显示目标 Zone 与预计移动数量
- 多显示器预览按缩放比例渲染，标注 DPI/分辨率；布局导入时指纹不匹配提供“按比例适配 / 仅导入规则 / 取消”

### 4.7 SoftwareHubPage 软件管家
Pivot 3 个：
1) 可升级：卡片网格或列表（图标/名称/当前版本→最新版本/大小/发布日期），行尾 [升级] [忽略此版本]
2) 全部软件：与卸载器共用数据源（AppEntry 表），提供 [跳转到卸载器]
3) 更新历史：时间线（时间/应用/操作：升级|安装|卸载|失败）
顶部：[检查更新]（含上次检查时间）、[全部升级]
命令：CheckUpdatesCommand / UpgradeCommand(app) / UpgradeAllCommand / IgnoreVersionCommand(app)
数据：winget（COM 优先）+ 自定义源 JSON；winget 不可用时降级提示
- MVP0 先交付“检查更新 + 单项升级 + 忽略版本 + 更新历史”；`UpgradeAll` 默认串行队列且失败不阻断后续；升级前显示磁盘空间与“保留用户数据”声明
- 每行显示当前版本/目标版本/来源/大小；来源不可信或签名缺失时 RiskBadge；失败页展示 stderr 摘要与诊断 ID

### 4.8 ToolboxPage 工具箱（扩展位）
- 网格卡入口（每卡：图标+名称+描述）：右键菜单管理器 / 环境变量编辑器 / Hosts 编辑器 / 网速测速 / 硬件烤机（转 M1 基准）
- MVP0 仅展示“敬请期待”壳，接口 `IToolboxTool{Id,Title,Description,Icon,Route,IsExperimental}` 预留；v1 后按 T7.8 逐项开放
- 每个工具独立 ViewModel/View；Hosts、环境变量等编辑器保存前显示 diff/备份路径；测速入口默认不联网，需用户点击

### 4.9 SettingsPage 设置（Footer 固定项）
左侧子导航（4 节）：
1) 通用：主题（深/浅/跟随）、语言、开机自启、启动页选择、管理员权限说明
2) 清理规则：安全等级阈值、默认勾选类别、还原点开关、"排除目录"列表编辑
3) 更新控制：显示 F3 当前策略状态（只读上下文提示）、WaaSMedic 实验性开关（红字免责）
4) 关于：版本/检查程序更新/开源许可(WPF-UI、LibreHardwareMonitor)/日志目录打开
- 第 5 节“实验功能”：统一列出 L2/L3 能力；开关默认关闭；开启需选择风险确认复选框，并显示构建通道与生效条件
- 设置模型：`SettingDefinition{Key,Type,Default,Scope,RestartRequired,RiskLevel,VisibilityRule}`；导出/导入前显示变更摘要，禁止带密钥或机器绑定字段
- 每项设置显示状态徽章（默认/自定义/需重启/实验）；搜索定位到节；只读项给出原因

## 5. 交互细则
- 列表统一虚拟化 + UI 线程节流（CollectionChanged 合并刷新）
- 所有确认弹窗：ContentDialog（危险操作按钮红色、需勾选"我已知晓"）
- 全局快捷键：Ctrl+K 搜索 / Ctrl+R 刷新 / Esc 取消当前任务
- 状态机：任何 Page 的长任务运行时，禁止再次触发同类任务（按钮 Loading 态）
- 异常展示：页内 InfoBar（Warning/Error 级），不使用 MessageBox 阻塞刷新线程

## 6. MVP0 页面验收

| 页面 | MVP0 必做 | 后置项 |
|---|---|---|
| 仪表盘 | 4 张健康卡、摘要、快捷入口、1s 监控 | 高级图表、完整基准入口 |
| 系统信息 | 分类树、属性表、温度徽章、导出 TXT/JSON | HTML 模板、SMART 全量视图 |
| 磁盘清理 | 快速清理扫描/确认/执行、回收站可还原、基础规则 | MFT 极速通道、重复文件、深度瘦身 |
| 卸载器 | 三路列表、标准/静默卸载、队列、图标缓存 | 强制卸载、残留深度扫描 |
| 安全中心 | Defender 状态、快速/目录扫描、启动项只读 | 启动项修改、防护控制 |
| 软件管家 | winget 只读升级列表、单项升级、忽略版本、历史 | 全部升级、复杂源 |
| 桌面整理 | 页面骨架与规则说明 | 分类执行、布局、覆盖层 |
| 工具箱 | 卡片占位与接口 | 右键菜单/环境变量/Hosts/测速 |
| 设置 | 通用、清理规则、关于 | 更新控制、实验功能治理 |

验收规则：MVP0 每页通过键盘导航与 Tab 焦点检查；深/浅主题无硬编码色；空态/加载态/错误态齐备；9 页导航无崩溃；所有高危入口默认隐藏或禁用。

## 7. UI 验收清单

- [ ] Design Tokens 全量资源化，无散落颜色/尺寸/动画字面量
- [ ] 9 页均有 PageViewModelBase、Refresh/Cancel、Busy/Error/DiagnosticId
- [ ] 所有列表虚拟化且筛选/排序/选中状态保留
- [ ] 所有破坏性操作满足预览 → 备份 → 执行，且 Risky 默认不勾选
- [ ] L2/L3 入口默认隐藏，实验开关可追溯
- [ ] Ctrl+K、Ctrl+R、Esc 全局可用；焦点环和键盘顺序通过检查
- [ ] 1024×680 / 1280×800 / 1920×1080 / 2560×1440 四档布局冒烟通过
