# Changelog

All notable changes to the HYC Framework package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [2.0.5] - Unreleased

### Added
- **行为树打包支持（补齐缺口）**：`BTTreeAsset` / `BTBlobBuilder` 位于 Editor 程序集，且 `BTAutoRegisterOnPlay`（进 Play 自动注册）是编辑器钩子——打包后两者都不存在，真机拿不到任何树。现补上与配置管线（`StreamingAssets/ConfigBlob` + `TryRead`）同构的打包路径：
  - `Editor/BT/BTBlobExporter.cs`：菜单 `Tools/HYC/BT/导出 Blob(打包用)`，把全工程树资产导出为 `Assets/StreamingAssets/BTBlob/<treeId>.blob` + `manifest.txt`（导出前清空旧产物，避免删树/改名后残留）；`ExportAll(dir)` 可供构建流程直接调用。
  - `Runtime/BT/BTBlobLoader.cs`：`LoadAll()` / `Load(treeId)`，`BlobAssetReference<BTRootBlob>.TryRead` → `BTManager.Register`；清单缺失时回退扫描目录；检测到 `jar:`（Android APK 内）路径时给出明确报错而不是静默失败。
  - `BTBlobBuilder.BuildToFile(asset, path, version)`：把树资产直接写成 Blob 文件，与 `Build` 共用同一段构建逻辑（`Fill`），两条路产物字节一致。
- 文档：`82-BT-运行时与Blob` 新增「七、打包：导出文件 → 运行时读回」（含格式版本、Android StreamingAssets 限制与绕法）；`80-行为树-总览` 补打包链路图、菜单入口与「编辑器能跑 ≠ 真机能跑」注意事项。

### Changed
- `BTBlobBuilder` 内部把「结构校验」与「构建填充」抽成 `Validate` / `Fill` 两个私有方法，供 `Build` 与 `BuildToFile` 共用。**对外 API、校验规则与产物行为完全不变**（纯内部重构，非破坏性变更）。

### Fixed
- **行为树编辑器在零资产时打不开**：`BTGraphWindow` 原先用 `ShowEmptyState()` 只显示一行「没有行为树资产，用工具栏 File → 新建行为树 创建」，但工具栏（File 菜单）本身由 `BTGraphIMGUI.DrawGUI()` 绘制，而 `DrawGUI` 第一行就是 `if (_asset == null) return;`——空状态下工具栏根本不渲染，提示指向一个不存在的入口，是死胡同。修复：`BuildUI` 在零资产时改为建立一棵**仅存于内存的临时树**并直接打开完整编辑器（工具栏 / 画布 / 左资源面板都正常），左面板此时为空列表；新增 File → **保存** 菜单项（`OnRequestSave` 回调），首次保存才把临时树落盘到 `Assets/BTTrees/`。不再自动写文件，避免「只是打开看一眼就多一个 NewTree_xxx.asset」。
- **删除当前打开的树后窗口全空白**：左面板「删除」或 Project 窗口删除当前树后，`_current` 成为已销毁引用（Unity 重载 `==` 判 null），`DrawGUI` 首行拦截整窗绘制，而 `BuildUI` 只在 OnEnable 跑一次，无人恢复。修复：窗口新增 `Update()` 守卫，检测到 `_current` 被销毁即自动重开——还有其它树就开第一棵，一棵都没有就回内存临时树（与零资产启动行为一致）。
- **新建的空树开局就是 2 条校验错误**（缺少 Root 入口节点 / 树为空）：校验器与导出器都正确地要求 Root，但新建树没有入口、也没人告诉用户从哪开始。修复分两层：①校验与引导解耦——**零节点树（临时树/新建树/清空画布后重开）视为"尚未开始编辑"的初始态**：画布中央出引导文案（`DrawEmptyCanvasHint`），校验静默、底栏零报错；画布上有任何节点后恢复完整校验。②`BTTreeAsset` 新增 `EnsureRootNode()` 供**程序化建树**调用——编辑器 UI 特意不预置 Root：孤立的入口节点会立刻触发「Root 未连线」报错，与引导态哲学冲突。
- **节点端口点不到、拖不了线**：输出端口圆心画在节点矩形**外**（`xMax + 6`），而按下判定先要求命中节点矩形（`NodeAt >= 0`）才进入端口检查——点在可见圆点上时 `NodeAt == -1`，端口永远点不到。修复：按下时先对全部可视节点做端口距离命中（`PORT_HIT=12`），端口命中优先于节点选中/拖动；松手落点（`EndPendingConnect`）本就是纯距离判定，无需改。
- **节点圆角显示成直角**：`BTEditorStyles` 的 9-slice 圆角纹理（圆角外透明）下面垫了两层方形 `EditorGUI.DrawRect`（身体打底 + 头部色条），透明圆角处透出方底。修复：撤掉方形垫底——身体纹理自带 `NodeBody` 填充 + 描边，头部白底纹理由 `GUI.color` 染成分类色，圆角 6px 正常呈现。
- **行为树连线方向与 NodeCanvas 不符（端口在左/右、横线）**：原端口几何照搬 FlowCanvas 的**水平流**（输入在左缘、输出在右缘、贝塞尔切线沿 X 轴），但 NodeCanvas 行为树是**垂直流**（输入在顶边居中、输出在底边居中、连线自上而下）。修复：端口位置改为 `InputPortPos=(center.x, yMin - 6)` / `OutputPortPos=(center.x, yMax + 6)`，贝塞尔切线改沿 Y 轴（`ty = |Δy|·rigidity`）；端口命中、连线预览、落点判定全部跟随新几何；`DecorateNode` 的装饰节点由「放在左侧」改为「放在被装饰节点正上方」（垂直流里装饰节点是父）。节点边框描边加厚到 2px，深色画布上更立挺。
- **从端口拖出连线特别卡、不跟手**：节点拖动每帧走 `MarkDirty`（内部 `OnRepaint`）所以平滑；**连线拖动在 `MouseDrag` 事件里既不 `Use()` 也不请求重绘**，预览线只靠偶发的窗口重绘刷新。修复：`_pendingSrcId` 存在时 `MouseDrag` → `e.Use() + OnRepaint`。拖拽端帽同时改为实心小圆（原 16px 半透明大圆发虚）。
- **节点底色/边框和 NodeCanvas 原版不一致**：三个来源——①画布底 `BG=(0.24,0.24,0.27)` 比节点体 `NodeBody=(0.21,0.21,0.25)` 还亮，节点整个「隐」进背景只剩个框，看起来像半透明（原版是**暗底 + 明显更亮的节点面板**）→ BG 压暗到 0.15、节点体提到 0.23、边框提到 0.40；②`DrawNodeShadow` 用**方形 `DrawRect`** 垫在圆角节点后，四角露出方角 → 改用 9-slice 圆角阴影纹理（`NodeShadowStyle`，此前定义了从未用上）；③头部纹理四个角全圆（注释写「仅顶部」但实现不是）且渐变把分类色压暗 22%（0.78 起步）→ 底部直角、顶部圆角、纯白均匀纹理（去掉渐变）。
- **节点「一个色块一个色块」的块状瑕疵**：手搓纹理 `MakeRounded` 节点体内部带「顶部 40% 5% 白高光」、头部带渐变，被 9-slice 拉伸后变成横跨整节点的大色带；描边当时被加粗到 2px 让四角出现小方块。修复：**节点体/头部内部改为纯色均匀**（任何内部渐变经 9-slice 拉伸都会成色块），描边回到 1px。纹理为懒生成单例，域重载/重开窗口后生效。
- **节点/连线样式与 NodeCanvas 不完全一致（终版方案：直接用原版纹理）**：程序化手搓纹理无论怎么调都无法与原版逐像素一致（本轮仍出现块状瑕疵）。`BTEditorStyles` 彻底废弃全部程序化纹理（`MakeRounded`/`MakeHeader`/`MakeShadow`/`SignedRound` 等全删），改用 NodeCanvas 原版纹理（用户自有资产，拷贝至 `Editor/BT/Styles/Textures/`：Window/WindowShadow/WindowHighlight/WindowHeader/simpleBox/NodePort/NodePortHover/NodePortConnectedHover/Bezier/Circle 共 10 张），GUIStyle 的 border/overflow/padding 逐项取自 `StyleSheetDark.asset`。绘制逻辑严格对齐 `Editor.Node.cs` / `Editor.Connection.cs`：
  - 节点绘制顺序 = 原版：选中整体染色 `(0.9,0.9,1)` → 身体(Window.psd) → 阴影(windowShadow，直接用 node.rect，外扩由 overflow(9,7,10,10) 负责，不再手工偏移) → 头部(windowHeader 染分类色) → 标题(windowTitle 白字)；
  - 端口 = 原版：**只有底部一个输出端口**——simpleBox 容器条画在 `Rect(x, yMax-2, width, 12)` + 12×12 端口圆（`nodePortConnected`=NodePortHover / `nodePortEmpty`=NodePort，hover 态由 GUIStyle 自动切换）；**顶部不再画输入端口**（原版如此）；输入端点改为 `(center.x, yMin)`（原版连线落点即顶边，无 6px 外凸）；连线落点判定改为原版的「松手落在目标节点矩形内即连接」；
  - 连线 = 原版 `DrawConnection`：阴影贝塞尔控制点写法逐字对齐（`from + shadow + fromT + shadow`）、末端 16×16 Circle.png 圆头（`color.WithAlpha(1)`）；拖拽预览 = 原版 997-998：单条贝塞尔（Resting a0.8, size 3），去掉自创的阴影与端帽；
  - minimap = 原版 `DrawMinimap`：`Grey(0.5)·a0.85` 染 windowShadow 直接画容器矩形（去掉 -4 外扩）。
  - 旧的选中「四边描边矩形」删除，选中表现 = 原版整体染色。
- **画布节点图与 NodeCanvas 一模一样（Option B 收尾）**：在「终版原版纹理」基础上补齐原版 play 态的两处视觉：①节点**状态高亮**（`windowHighlight`，WindowHighlight.psd）——选中（编辑态）染 Resting 浅蓝 `(0.7,0.7,1,0.8)`、play 态且节点有运行结果时染对应状态色（严格对齐 `Editor.Node.cs` 287-297 的原版 `if/else` 分支）；②节点左上角 **16×16 状态图标**（StatusSuccess/Failure/Running.png，原版 `ShowStatusIcons` 443-459）。为此在 `BTManager` 新增编辑器调试快照（`UNITY_EDITOR` 守卫的 `EditorDebugEnabled` + `TryGetEditorLiveStates`，按 blob 节点索引 = 资产 `Nodes` 列表序 暴露每节点最新 `BTNodeState`），并在 `BTGraphWindow.Update` 于 play 模式置 `EditorDebugEnabled=true` 并持续 `Repaint`，使 play 模式下画布实时反映节点成功/失败/运行中状态（与 NodeCanvas 行为一致）。新增的 3 张状态图标纹理同样拷贝自用户自有 NodeCanvas 资产。

## [2.0.4]

### Added
- 行为树（BT）编辑器与运行时解释器：可视化 `BTTreeAsset` 编辑、`BTRootBlob` 序列化、`BTManager` 注册、`BTInterpreterSystem` 逐帧解释执行、运行时黑板（`BTBlackboardRuntime`）、自定义节点（`BTCustomNode` + `BTNodeRuntimeRegistry` 反射扫描）、断点调试。
- 配置管线扩展：新增配置字段类型（行为树引用、Addressable、多语言 Key、引用、枚举等）。

### Changed
- 热键系统改用 action-name 字符串（`HotkeyActionNames`）分发，取代原 `HotkeyID` 枚举。
- 升级为可发布的 Unity 包：完善 `package.json` 发布字段（documentationUrl / changelogUrl / licensesUrl），补充 README / LICENSE / CHANGELOG。

## [2.0.3] 及更早

- DOTS / ECS 运行时基建（`Bootstrap` / `Simulation` / 分层 `UpdateGroup`）、消息系统。
- 配置管线（Excel / 模板 → Blob）、多语言本地化（含敏感词）。
- ECS UI 系统（`UIManager` / `AbsUISystem` / `ComponentBinderTable`）。
- 输入 / 热键、Timeline 过场。
- 编辑器工具链（配置生成、组件绑定代码生成、本地化 Key 管理）。
