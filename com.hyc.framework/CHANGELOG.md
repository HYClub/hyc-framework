# Changelog

All notable changes to the HYC Framework package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- **遗留 Excel→IComponentData 生成链的硬编码命名空间修正（`Editor/ConfigGenerator.cs`，ns `HYC.Framework.Config.Editor`）**：命名空间统一收尾时发现 `ConfigWindow`（菜单 `HYC Framework/Config/Config Window`）经 `ConfigGenerator.GenerateStruct` 产出的 `[BlobGenerate]` blittable 行结构仍写死 `namespace HYC.Framework.Config.Generated`——该链虽不参与配置资产系统（无资产、不过滤器），但同一工程里两套配置命名空间并存，用户一生成就出现「规则说改了、实际没改」的货不对板。现改读 `ConfigDataSettings.Namespace`（root 派生），与 Config 主链单点切换、自动跟随；生成文件内 `[HYC.Framework.Config.BlobGenerate]` 本为全限定引用，不受影响。同步 `Runtime/ConfigRowRef.cs` XML 注释与 `README.md` 两处示例表述（`HYC.Framework.Config.Generated` → `{命名空间root}.config`）。**破坏性变更**：若曾用过该遗留链生成过代码，重生成后旧文件命名空间变迁，需删除旧产物重生成。

- **生成代码命名空间统一到「root」派生（HYC Settings 新增 `CodeGen` 分组「生成代码命名空间 root」，`Editor/HYCSettings.cs` + `Editor/HYCEditorSettingsWindow.cs` + `Editor/DataEditor/ConfigDataSettings.cs` + `Editor/DataEditor/ConfigDataSettingsWindow.cs` + `Editor/BT/BTNodeCreatorWindow.cs`）**：此前配置生成命名空间是数据编辑器设置窗里的独立字段（默认 `HYC.Sample.Generated`），BT 自定义节点命名空间更是**硬编码** `GameBT.Nodes`——两处各配各的、改一边忘一边。现统一为一个 root：HYC Settings 窗口新增 `CodeGen` 分组（输入框 + 「生效 root / 配置代码 / BT 节点」三行预览，控件集合恒定），值存 `HYCSettings` 资产（`GeneratedNamespaceRoot`）；**留空 = 自动取项目公司名**（`PlayerSettings.companyName`，按点号分段净化：只保留字母/数字/下划线、数字开头的段整段丢弃），公司名不可用回落 `Default`。配置代码 → `{root}.config`：`ConfigDataSettings.Namespace` 改为**派生只读**（`HYCSettings.ConfigCodeNamespace`），资产过滤器（`ConfigDataTree`/`ConfigExportService`/`ConfigIdService` 的 `type.Namespace.StartsWith(...)`）、`FindExportType` 的 `{ns}.Editor.*Export` 前缀、枚举/模板/BlobBuilder/导出等全部生成点因共用同一属性而**单点切换自动跟随**；BT 自定义节点 → `{root}.BT.Nodes`（替硬编码，创建窗实时显示目标命名空间）。数据编辑器设置窗的「命名空间」字段、合法性校验与保存写入一并移除，改为只读展示派生值并指向 CodeGen 分组。**破坏性变更**：默认命名空间从 `HYC.Sample.Generated` 变为 `{公司名}.config`（公司名不可用时 `Default.config`），升级后需重新生成配置代码——旧资产仍在旧命名空间下，而过滤器/导出类查找按新命名空间走，重生成后归一；`ConfigTemplateCodeGen.LegacyDefaultNamespace` 常量保留作历史参照。

- **HYC Settings 与数据编辑器设置改存纯 SO 资产 `Assets/HYC/HYCSettings.asset`（新增 `Editor/HYCSettings.cs`，ns `HYC.Framework.Editor`）**：EditorPrefs 只存本机（Windows 注册表 / macOS plist）——不进版本控制、多人协作不同步、换机即丢；现全部设置值改落工程内 ScriptableObject 资产，随提交全团队共享。加载顺序：约定路径 `LoadAssetAtPath` → 全工程搜 `t:HYCSettings`（用户把资产移走也能用）→ 都没有则就地创建（逐级建 `Assets/HYC`）；**不做迁移**（首次打开用代码默认值建资产，迁移只会把本机值带进团队仓库）。`ConfigDataSettings`（数据编辑器全项：配置根目录 / 生成目录 / 命名空间 / 编辑器生成目录 / 客户端·服务器导出目录与格式 / Blob 输出目录 / struct 开关 / ID 服务器类型·IP·端口）与 HYC Settings 窗口项（Verbose / AutoGen 经注册表字段条目、音频后端为手写下拉）统一改为委托该资产读写，公开 API 与调用方零改动；`[HYCSettingsGroup]` 字段的持久化从 EditorPrefs 键值改为资产内**键值条目表**（平行 `List<string>` 存取，bool/int/float/string/枚举统一转字符串、不变区域性往返，写入与解析严格配对），字段 `Key` 语义不变。所有写操作（属性 setter / `SetEntry`）立即 `SetDirty + SaveAssetIfDirty` 落盘，杜绝「改了没保存、域重载后还原」；默认值 / 钳制 / 空值回落语义与原 EditorPrefs 版完全一致（端口 1~65535 钳制、IP 空值回落 127.0.0.1、目录空值回落默认路径），`BlobOutputDir` 的一次性 legacy 迁移随 EditorPrefs 存储一并移除。运行时不受影响（`FrameworkSettings` 运行时资产与音频音量分组保留不动）；本地化设置（`LocalizationSettings`，存 EditorPrefs）不在本次范围。

- **配置模板编辑器「基础」页紧凑化（`Editor/DataEditor/ConfigTemplateEditor.cs` + `Editor/DataEditor/ConfigIconPicker.cs`，ns `HYC.Framework.Config.Editor`）**：原图标区（区块标题 + 32px 预览 + 「选择内置图标…」/「浏览项目图标…」/「清除」三个按钮 + 「当前: xxx」说明行，竖排 5 行约 90px）改为面板最左 **88×88 可点击缩略图列**——面上零按钮，点击即弹「选择图标」窗口（内置网格搜索 + 项目图标浏览不变，窗口内新增「清除」按钮回归默认图标，「当前」状态看缩略图本身）；同步删除「配置模板 / 基类 / 图标」三个区块标题（页签与字段名已表意）、字段标签列 200→112 给左列让宽、原常驻两行 Info HelpBox 移入「字段」页签 tooltip（hover 展开）。基础页总高 ~330px → ~120px；显示名净化、Class Name 生成后只读 + 改名流程、基类保护（清空回归 ConfigBase、未生成红底）均原样保留。缩略图内「无图标」标签每帧恒定绘制、仅按有无图标切换文案（IMGUI 控件集合一致性）。

- **枚举命名空间从「配置类命名空间」中独立为平级的 `{root}.Enum`（续命名空间统一到 root 派生）**：此前枚举与配置主类共用 `{root}.config`，用户希望枚举命名空间带 `.Enum` 以明确区分、且不在宿主代码里出现 `Generated`。现枚举命名空间改为 `{root}.Enum`（与 `{root}.config` 平级，均由「生成代码命名空间 root」派生），生成端 `ConfigEnumCodeGen` 与引用端（`ConfigTemplateCodeGen.ResolveTypeName` / `CsTypeName` 的 `using` 注入）一并切换；Settings 的 CodeGen 分组新增「枚举」一行单独显示。特意平级而非 `{root}.config.Enum` 子级——配置资产过滤器（`ConfigDataTree` / `ConfigExportService` / `ConfigIdService` 的 `type.Namespace.StartsWith(ConfigCodeNamespace)`）用前缀匹配识别配置类，子级会被误当成配置类去导出/收集。`CleanStaleNamespaceFiles` 的保留判定同步纳入枚举命名空间。

### Added

- **模板图标传播到 Unity 原生 Project 窗口（新增 `Editor/DataEditor/ConfigIconApplier.cs`，ns `HYC.Framework.Config.Editor`）**：模板 icon 此前只被数据编辑器自己的界面消费（检查器缩略图 / 数据树行 / 创建窗预览），Unity 原生 Project 窗口无感知——该模板生成的配置资产在 Project 窗口仍是默认 ScriptableObject 图标。现新增统一传播入口，把模板图标应用到**生成类型的 MonoScript（类级）**——该类型全部配置资产（Boss/Flyer/Player/Walker…）经类图标继承显示。**为什么只落 MonoScript**：`EditorGUIUtility.SetIconForObject` 官方语义只接受 GameObject / MonoScript，传 ScriptableObject 实例时图标会记到该实例的类上、同类全部资产共享（见 Fixed「图标泄漏到整个类」条）——不存在「单个模板资产自身的原生窗口图标」通道；模板自身的图标仅由框架自绘界面消费（数据树行 / 检查器缩略图读模板字段，逐模板生效）。持久化分两层——**自定义纹理**经 `MonoImporter.SetIcon` + `SaveAndReimport` 写进生成类 `.cs.meta`（`GetIcon()` 已是目标图标则跳过，不白触发重导入；清除图标以 null 恢复默认），跨会话 / 换机 / 进 VCS 有效；**内置图标**（内置纹理无 GUID，写不进 meta）为 session 级，由 `[InitializeOnLoad]` + `delayCall` 的验证式同步兜底（打开工程时 `GetIconForObject` 读回比对，不一致才设，已生效零操作）。即时生效点：保存模板 / 选完图标（`ConfigTemplateEditor`）、工厂创建 / 批量创建（`ConfigAssetFactory`）、复制粘贴落地（`DuplicateIdGuard.Flush`）、生成代码（`WriteFile` 末尾，持久化 + 应用一并做）。手动兜底菜单 `HYC Framework/Config/Apply Template Icons`（pri 5）——MonoScript 一步独立 try/catch 容错（生成脚本未编译时取 MonoScript 抛异常只记日志、不中断），应用后 `EditorApplication.RepaintProjectWindow()` 刷新显示，命中统计（模板数 / MonoScript 数）以对话框报告，便于定位「没设置」与「设置了不显示」两类问题。

- **模板字段「关联参数」条件显示（`Runtime/ConfigTemplate.cs` + `Runtime/ConfigVisibilityAttribute.cs`，ns `HYC.Framework.Config`）**：模板字段小三角展开区新增「关联参数」块——每条条件 = 左下拉选本模板（含继承链）一个 **bool/enum 标量字段** + 右侧控件（bool→勾选「要求真/假」；enum→`MaskField` 多选枚举值，≤32 个），可加多条、× 删除。语义：**多条 AND，全部满足该字段才在数据编辑器的配置面板绘制**；目标字段丢失/类型不符**视为满足（保持显示）**并在模板编辑器给黄字警告（配置面板不能因为目标字段被改名/删除就把字段吞掉）；不级联、不影响反射赋值与导出。数据流复用既有「模板标记→生成特性→编辑器读取」范式：`ConfigTemplateField.visibility`（`List<ConfigVisibilityCondition>`，新序列化类，带 `MatchesBool`/`MatchesEnum` 纯判断）→ 生成字段上的 `[CfgVisibleWhen(...)]`（bool/enum 两个构造重载，`AllowMultiple`）→ `GUITypeField.VisibleWhen` 缓存 → `GUIDrawer.CheckVisible` 求值（反射读目标对象当前值，命中即跳过 `DrawField`）。模板校验覆盖六类失效条件（未选目标/指向自身/目标不存在/目标为列表/目标非 bool-enum/enum 条件未选值），均只警告不阻断生成；无效条件在生成时不发射特性。enum 多选掩码按 Unity 官方语义实现（bit i ↔ 选项 i；全选返回 -1 写回全部值名，`MaskFieldGUI` 源码实证）。`AddEmptyField` 同步复位 `visibility`（防 `arraySize++` 复制上一条条件）。

- **配置资产「类型文件夹」自动归置 + 右键菜单动态化（`Editor/DataEditor/ConfigAssetLayout.cs`，ns `HYC.Framework.Config.Editor`）**：新建的配置/枚举/模板一律落到 `配置根目录/<类型短名>/`（`[CfgAsset("角色/角色配置")]` 的最后一段，即数据树 `[角色]` 那个前缀；模板→`配置模板`、枚举定义→`枚举`），文件夹不存在则逐级创建。`ConfigAssetFactory.Create/CreateMany`、`ConfigCreateWindow`（新增「创建位置」标签实时显示落点）、`ConfigTemplateCodeGen.CreateTemplateAsset`、`ConfigEnumCodeGen.CreateEnumAsset`、数据树与工具栏的全部创建入口统一收口；显式传 folder 的既有调用方式仍然尊重（程序化/AI 入口不受影响）。用户自建子文件夹、手动拖入一律不干预；**已归置资产被移出自己类型文件夹时，`ConfigAssetMoveGuard`（`AssetModificationProcessor.OnWillMoveAsset`）拦截并给出「不建议」提示**（类型文件夹内部移动/重命名/建子文件夹放行；Project 窗口与数据树拖拽两条路径都覆盖，数据树内拦截时弹窗展示提示语）。数据树文件夹右键菜单重写：模板文件夹不再出现不适用的「保存并导出内部所有」，改为按文件夹内容动态列出——「导出」**真子菜单**（GenericMenu 路径按 `/` 拆子菜单）内每种可导出配置类型一项「短名（N 个）」（N = 该文件夹内资产数，与 `ExportFolder` 同一过滤规则实时统计）+ 子菜单内分隔线 + 「导出全部」；枚举 / 模板作为顶级动态项单列（模板是生成代码不是导出，不进导出子菜单），三类皆无时列禁用提示。按类型导出走 `ExportAll` 整类型导出（单资产 `ExportCore` 对 Blob 只写一行，循环调会互相覆盖）。新增 4 个 EditMode 用例钉住布局契约（模板/枚举固定文件夹名、非配置类型不归置、类型文件夹拼在根目录下一层、空路径判定不抛）。

- **生成代码统一写盘 + 「自动生成」头壳 `GeneratedCodeFile`（`Editor/GeneratedCodeFile.cs`，ns `HYC.Framework.Editor`）**：包内所有代码生成器写 `.cs` 的**最底层入口**，两个职责——① 每个产出文件顶部套四行壳：IDE 识别的 `// <auto-generated>` 标签 + 中英文「请勿手工修改」声明 + 变更指引（改生成源后重新生成）；壳内**刻意不含时间戳/路径等易变信息**，否则「内容未变不重写」优化失效、每次生成都白触发重编译。② 内容未变则不重写、不触发 `ImportAsset`。此前 Config 模板生成链（主类/侧类型/Check/Editor/BlobBuilder/BlobSystem/Export 八个产物）与枚举生成器（生成 + 导出两处）**完全没套头**——人手改后下次生成静默覆盖，本次收口；Attr 三件套与 ComponentBinder 原有的三种不同风格 ad-hoc 头移除，禁改声明统一由壳承担（ComponentBinder 的过期文件清理识别同步兼容新旧两种文件头，过渡期不清不掉自家产物）。Talent 生成器（`#region USER` 区可手改，语义不同）与 BT 自定义节点脚手架（本就给用户填逻辑）保持原样。`ConfigTemplateCodeGen.WriteIfChanged` 实现下沉到本类、保留为兼容 shim（纯比对、不套壳）。新增 4 个 EditMode 用例钉住壳契约（含「壳内无当前年份/绝对路径」守卫）。

- **菜单 `HYC Framework/Config/Regenerate All Config Code`（pri 4）**：一键重生成全部配置代码。遍历 `LoadAllTemplates()` 调 `WriteFile`（覆盖写，保留各 `.cs` 的 `.meta` GUID，配置资产不掉脚本）；框架级基类（`generateCode=false`，如 `ConfigBase`）自动跳过并单列计数。带进度条 + 结果汇总对话框（生成/跳过/失败），单个模板异常不中断整批。用于字段类型变更、`ConfigBase` 移入框架等需全量重生成的场景，省去逐个模板右键。

- **配置侧类型/导出基类三件套（框架级手写，随包预编译）**：`ConfigBaseClient`（`Runtime/ConfigBaseClient.cs`，ns `HYC.Framework.Config.Client`）、`ConfigBaseServer`（`Runtime/ConfigBaseServer.cs`，ns `HYC.Framework.Config.Server`） 各承载 ID/GUID + `From(ConfigBase)`；`ConfigBaseExport`（`Editor/DataEditor/ConfigBaseExport.cs`，ns `HYC.Framework.Config.Editor`）为导出管线抽象基类。生成的 `XxxClient`/`XxxServer`/`XxxExport` 分别继承三者——与主类链构成对称的三条继承链，ID/GUID 经侧类型继承自动进入 JSON 导出产物。

- **模板导出可行性静态校验（生成前硬拦截，不捏样例，`Editor/DataEditor/ConfigTemplateCodeGen.cs`）**：解决「资源/对象字段（如 Sprite）用默认『直接导出』又选了客户端+服务器，服务器 JSON 无法序列化、客户端 Blob 落到 `default: int` 兜底编译报错」的坏代码问题。判定直接复用代码生成器自身的类型解析，永不漂移——客户端侧复用 `BlobElemInfoOf` 识别「落到 `default: int` 兜底」的不可 blittable 组合；服务器侧判定「直接导出」+ Unity 对象/不可 JSON 序列化类型（`IsJsonUnsupportedElementType`：资源/对象引用族 + Gradient/AnimationCurve，须随 `UnityTypeName` 同步维护）。两条都进 `Validate` 作为**硬错误直接阻断「生成代码」**，并在模板编辑器顶部红字 `tplErrors` + 导出页不可导侧 Popup 红底高亮 + 表下汇总红字提前暴露（带「改资源名/路径/AA地址/转字符串」的建议）；「保存模板」不受影响（草稿可存）。`GetExportFeasibility` / `ExportFeasibilityMessage` / `CollectExportFeasibilityErrors` 为唯一判定入口，返回用元组不新增类型。覆盖资产/对象/Gradient/曲线/Rect 系/层遮罩/配置引用整对象/Decimal/Char 等 Direct 组合；合法 int/enum、`Vector*`/`Color*`/`Quaternion` 等 blittable 结构、以及一切转 string/取成员/资源名路径地址模式  均判为可导，无误伤。

### Fixed

- **命名空间 root 切换后「一生成就报 CS0246、且重新生成也好不了」死循环（`Editor/DataEditor/ConfigTemplateCodeGen.cs` + `Editor/DataEditor/ConfigDataWindow.cs` + `Editor/DataEditor/ConfigTemplateEditor.cs`，2026-10-15 线上事故根治）**：改「生成代码命名空间 root」后重新生成，产物里混入失效命名空间（`using HYClub.config;` 之类）→ CS0246 → 编译不过 → Unity 里加载的仍是上一次成功编译的旧程序集 → 再生成依旧反射到旧命名空间类型 → 永远出不来。根因是四处「漏生成/乱反射」：① Reference 字段按模板资产里**存储的 pick 时 Assembly-qualified 全名**反射（`Type.GetType` → `usings.Add(type.Namespace)`），root 切换后反射命中的是旧程序集的旧命名空间，失效 using 直接写进新文件；② 「Regenerate All Config Code」只遍历模板、**不含枚举**，枚举文件永远停在旧命名空间（`ActorKind` 等批量 CS0246 的来源）；③ Blob 三件套被 `ClientFormat == 1` 门挡住，非 Blob 格式下旧 Blob 文件不重生成了也不删；④ `XxxConfigEditor.cs` 「已存在则不覆盖」，命名空间切换后永久钉在旧值、`[CfgEditor]` 指向失效类型。修复：① Reference 字段改**确定性解析**——短名命中某 `generateCode` 模板（= 生成类）时按「短名 + 当前命名空间」零反射构造（侧类型所需 using 一并派生），编译失败期间也能正确生成，直接破死循环；手写 `[CfgAsset]` 类（无对应模板）仍按真实命名空间（当前 ns 重建优先、存储全名兜底），不误伤；`CsTypeName`/`MemberTypeOf` 同步不信旧命名空间；`Validate` 补引用存在性校验（生成期清晰报错，替代「编译期 50 个错」）；② Regenerate All 增加枚举批量重生成（`ConfigEnumCodeGen.WriteFile`），汇总计数；③ 非 Blob 格式（或无客户端导出字段）时删除残留 Blob 三件套（含旧位置文件），Blob 格式正常重生成；④ `XxxConfigEditor.cs` 改**始终覆盖**（文件头壳本就禁手工改，「不覆盖」与头壳自相矛盾），自定义引导改为另建编辑器文件；⑤ Regenerate All 末尾**清理旧命名空间残留生成文件**（`CleanStaleNamespaceFiles`：扫输出目录下带「自动生成」头、命名空间不在当前 root 派生范围的 `.cs`，连 `.meta` 删除；手写文件一律不碰）；⑥ 模板编辑器 UI 的引用解析统一走同一 helper，root 切换后不再误显「引用:未选择」。新增 6 个 EditMode 用例钉住契约（旧命名空间不泄漏 / 短名剥离 / 全名重建 / 手写类不误伤 / 引用缺失报错）。**破坏性**：`XxxConfigEditor.cs` 曾被手工扩展过的工程，重生成会以标准内容覆盖（头壳早已声明禁改）；切换过 root 的工程请跑一次 Regenerate All 自愈。

- **HYC Settings 未显示全部生成产物命名空间 + 旧版枚举残留清不掉（本轮线上事故收尾，`Editor/HYCEditorSettingsWindow.cs` + `Editor/DataEditor/ConfigTemplateCodeGen.cs` + `Editor/DataEditor/ConfigDataWindow.cs`）**：用户重生成后仍见旧命名空间相关编译错，且 HYC Settings 的 CodeGen 分组只显示了「配置代码 / BT 节点」两行，看不到全部生成产物的真实命名空间，无法核对宿主引用是否对齐。修复：① Settings 的 `DrawGeneratedNamespaceRootSection` 补全全部 6 个真实落点——`{root}.config`（主类/枚举）、`{root}.config.Client`、`{root}.config.Server`、`{root}.config.Editor`、`{root}.config.Blob`、`{root}.BT.Nodes`，并加提示「宿主手写代码引用须与此一致」；控件集合全无条件绘制（IMGUI 一致性铁律）。② `CleanStaleNamespaceFiles` 新增 `relaxHeaderForEnums` 参数：`Regenerate All` 调用传 `true`，对框架独占的枚举输出目录（`ConfigEnumCodeGen.EnumsOutputDir`）放宽为「不依赖 `<auto-generated>` 头壳」——旧版生成器产出的枚举（无头壳、命名空间停在旧 root，如 `使用方工程.Generated`）此前会被头壳判定漏清，现一律清理；其余子目录仍保守只看头壳、不碰手写文件。**分工**：用户截图里命名空间为 `使用方工程.Generated`、且无头壳的枚举位于宿主 `Assets/Scripts/...` 内，属宿主手写枚举而非框架 OutputDir 产物，按用户「宿主被污染代码自己改回」的分工，不归框架清理——框架只负责自身 OutputDir 下的生成物。

- **「选择图标」窗口图标网格无滚动条 / 列宽不随窗口自适应（`Editor/DataEditor/ConfigIconPicker.cs`，ns `HYC.Framework.Config.Editor`）**：内置图标网格原来全部用绝对坐标 `new Rect(...)` 手动绘制，`BeginScrollView` 内没有任何 GUILayout 元素向滚动视图贡献内容高度——滚动条永远不出现，一屏放不下的图标被直接裁剪；且列数写死 6，窗口拉宽右侧大片空白、拉窄格子被压扁。修法分两层：① 网格改为「`GUILayout.Box(GUIContent.none, GUIStyle.none, Width, Height)` 占位 → `GetLastRect()` 取精确格区 → 手动绘制」——占位框向滚动视图贡献真实高度（滚动条恢复），格区尺寸不随布局拉伸；列数按窗口宽度实时分档（可用宽预留垂直滚动条位，列数 = `floor(可用宽/100)` 下限 2，**格子等分填满可用宽度**——整行无剩余空间，`GUIStyle.none` 的 `stretchWidth` 无处可拉伸，拉宽拉窄格子都保持精确）；② 布局改三段式——顶部搜索框 / 中部图标网格（`BeginScrollView` 占满剩余高度）/ **底部独立按钮栏**（`EditorStyles.helpBox` 分栏：「或选择项目内图标:」+「浏览项目图标…」+「清除」右对齐），替代原先挤在顶部的按钮行。顺手补悬停高亮（`EditorGUI.DrawRect` 半透明白，60+ 图标的网格无悬停反馈难定位）与图标名称格内居中（缓存 `MiddleCenter` 样式）。Unity 没有内置的编辑器图标选择器（`ShowObjectPicker` 只选项目资产、不列内置图标），故修自家窗口而非换通用窗口。

- **「选择图标」窗口重复 Close 触发 `Destroying object multiple times`（`Editor/DataEditor/ConfigIconPicker.cs`，ns `HYC.Framework.Config.Editor`）**：在 ObjectPicker 里选中项目图标 / 点窗口 ✕ 关闭后，Unity 报 `Destroying object multiple times. Don't use DestroyImmediate on the same object in OnDisable or OnDestroy`（调用栈：`EditorWindow.Close` ← `SafeClose` ← `HandleObjectPickerClosed` ← `OnGUI` ← `ContainerWindow.InternalCloseWindow`）。根因两层：① `ObjectSelectorClosed` 事件在同一关闭周期内会被重复送达——OnGUI 处理选中并 `Close()`，Unity 随后的 `InternalCloseWindow` 关闭流程中又 pump 一次 OnGUI，事件二次送达导致二次 `Close()`（二次 `DestroyImmediate`）；② **Unity 自身发起的关闭**（点窗口 ✕）不经我们的代码，`mClosing` 标志无从置位——关闭流程内的 OnGUI pump 里积压的 picker 事件首次送达即调 `Close()`，打在正在销毁的对象上。修法三处：① picker 关闭事件只处理第一次（`mPickerHandled` 一次性标志，取消选择同样置位；每次 `PickCustom` 重开前重置，不影响再次浏览）；② 所有关闭入口（选中内置图标 / 选中项目图标 / 清除 / Escape）统一走 `SafeClose()`；③ `SafeClose` 不再同步 `Close()`，改为挂 `EditorApplication.delayCall` 推迟到关闭流程外执行，且执行时先过 Unity 伪 null 判空（窗口已销毁则跳过）——同步 Close 打在 Unity 关闭流程内必炸，推迟 + 判空两条一起才同时覆盖「事件重复送达」与「Unity 发起关闭」两种路径。

- **配置模板图标「三处显示对不上」（数据树 / `.cs.meta` 类型图标 / Unity Project 窗口 session 图标，`Editor/DataEditor/ConfigTemplateEditor.cs` + `Editor/DataEditor/ConfigIconApplier.cs`，ns `HYC.Framework.Config.Editor`）**：同一模板的图标在数据编辑器数据树、Unity Project 窗口、配置资产（Boss/Flyer/Player/Walker…）三处互不一致——数据树里全是 bomb、Project 窗口模板多为默认蓝图标、角色配置资产却是宇航员图标。根因是三处读的是三个**不同步的数据源**且更新时机错位：① 数据树行图标读模板字段（`ConfigTemplateIcon.Resolve(tpl)`）；② 配置资产类型图标读 `.cs.meta` 的 `MonoImporter.icon`（与数据树类型图标同源）；③ Unity Project 窗口显示 `EditorGUIUtility.SetIconForObject` 的 **session** 图标（Unity 官方明确 won't persist，域重载 / 重启即丢）。旧实现里：picker 回调只写内存字段、不调 `ApplyForTemplate`（数据树与 Project 窗口都不刷新，且自定义纹理永不落地 meta）；`PersistForTemplate`（写 `.cs.meta`）只在「生成代码」时跑（meta 过期，数据树类型图标 vs Project 窗口 session 图标分裂）；`SaveInternal` 不刷 meta、不刷数据树。修复统一提交入口：① 新增 `CommitIconChange()`——picker 选中 / 清除共用，先把序列化属性落盘（`ApplyModifiedProperties` + `SetDirty` + `SaveAssetIfDirty`），再 `ApplyForTemplate` 即时应用三类对象 session 图标、`PersistIconDeferred` 经 `EditorApplication.delayCall` 推迟 `SaveAndReimport` 写 `.cs.meta`（推迟避开 picker 关闭 / 域重载打断当前交互）、`mWindow?.RefreshTree()` 立即刷新数据树行图标；② `SaveInternal` 在原本 `ApplyForTemplate` 之后补 `PersistIconDeferred(tpl)` + `RefreshTree()`；③ 自定义纹理分支额外清 `iconBuiltInName`、内置名分支清 `iconCustom`，避免双值歧义。现选图标 / 保存模板一次性把三处全部更新。注：怪物模板曾中途从 bomb 切到 Caveman 头像 + `AnimationClip Icon`，旧 `.cs.meta` 仍留过期 bomb 即「三处各显各的」的典型表现；刷新后 meta 与 session / 字段收敛一致。（用户原疑「sd 工程设置 icon 的代码」，已全工程 grep `SetIconForObject`/`MonoImporter`/`IconContent` 在宿主 `Assets` 下零命中——所有图标行为均来自本框架 `ConfigIconApplier`，问题在框架自身三数据源不同步，而非与 sd 实现对不上。）

- **`SetIconForObject` 传 SO 实例 → 图标泄漏到整个类（「设置一个模板图标，所有模板 + ConfigBase 全变了」，`Editor/DataEditor/ConfigIconApplier.cs`，ns `HYC.Framework.Config.Editor`）**：给「角色」模板设置图标后，数据树里全部模板行与检查器「基类模板」字段（ConfigBase）一起变成该图标，只有该模板自己的配置资产显示正确；此前一轮「ConfigBase 变 bomb」实为同一泄漏（当时角色模板上选的是 bomb），并非数据被写坏——全部模板资产的 icon 字段磁盘取证为空，泄漏发生在显示层。根因（Unity 2022.3 官方文档实证）：`EditorGUIUtility.SetIconForObject(obj, icon)` 的 `obj` **只接受 GameObject 或 MonoScript**；传 ScriptableObject 实例时 Unity 把图标记到该实例的类 MonoScript 上，**同类全部资产共享**（论坛同款案例：「The icon is set on the MonoScript, so it's shared between all instances of the same class」）。旧实现「① 模板资产自身 `SetIfDifferent(tpl, tex)`」即触发此语义——模板资产全是 `ConfigTemplate` 一个类，任一模板设图标 = 整类设图标；「② 逐配置资产循环」与「③ MonoScript」实际是同一通道（实例调用也落到类上），纯冗余。修复：`ApplyCore` 只保留 MonoScript 一步（类级恰好就是「模板图标 → 该模板全部配置资产」的期望语义），删除 ①② 两步与 `syncAssets` 参数；`ApplyForAsset`（工厂创建 / 复制落地）改走同一 MonoScript 通道；模板资产自身的图标回归框架自绘界面（数据树行 / 缩略图读模板字段）。session 泄漏不跨域存活，本修复重编译（域重载）后 ConfigTemplate 类图标自动恢复默认，无需手动清理。

- **框架/基类模板（`generateCode=false`）不参与图标传播（`Editor/DataEditor/ConfigIconApplier.cs`，ns `HYC.Framework.Config.Editor`）**：`ApplyCore` 入口按 `generateCode` 数据驱动跳过框架/基类模板（与 `WriteFile`、`Regenerate All` 的跳过规则同源），`Apply Template Icons` 菜单的命中计数同步排除，统计口径不失真。基类模板没有生成类型与配置资产，传播无意义；其 icon 字段在模板编辑器内仍可查看与修改（只影响数据树行 / 检查器缩略图自家显示）。

- **配置资产「移出类型文件夹」守护静默失效（`Editor/DataEditor/ConfigAssetLayout.cs`，ns `HYC.Framework.Config.Editor`）**：`ConfigAssetMoveGuard.OnWillMoveAsset` 旧实现返回 `string` 的签名在 Unity 2022.3 已不被 `AssetModificationProcessor` 回调分发器识别，导致守护**既不拦截也不弹窗**（实测在自定义数据树拖拽移动到别的文件夹无任何提示）。改为官方 `AssetMoveResult` 枚举签名——返回 `FailedMove` 取消移动、`DidMove` 放行；判定逻辑收敛到新增 `ConfigAssetLayout.ShouldBlockMove`（Project 窗口 / 数据树两条路径共用，语义一致、无漂移）。  现在两条路径都会弹「不建议移动」对话框（含「仍然移动」覆盖选项）；数据树内改为由 `MoveAsset` 触发守护、用 `GuardPrompted` 标记避免重复弹窗，并移除原来会二次触发守护的 `AssetDatabase.ValidateMoveAsset` 预检。

- **数据配置树「新建文件夹重命名后刷新还原 / 出现重复目录」（`Editor/DataEditor/ConfigDataTree.cs`，ns `HYC.Framework.Config.Editor`）**：在左侧列表对刚建的空文件夹（或任意文件/文件夹节点）重命名后，原目录残留、刷新后还原成旧名，甚至磁盘上出现新旧两个目录并存。根因分两层：① `AssetDatabase.RenameAsset` 文档明确只支持 **asset file**，对文件夹调用虽返回空（成功）但实际不做任何操作（实测）；② 之前用 `System.IO.Directory.Move` 虽能搬目录，但**不会移动文件夹的 `.meta` 文件**（GUID 真正来源），资产数据库因此损坏。修法：`ApplyRenameCore` 区分文件与文件夹——**文件**仍用官方 `AssetDatabase.RenameAsset(path, newName)`（自动处理 `.meta`）；**文件夹**改用 `UnityEditor.FileUtil.MoveFileOrDirectory(path, newPath)` 搬目录，并**再搬一次 `path + ".meta" -> newPath + ".meta"`**，GUID 保持不变。这是「搬家/改名」不是「新建+删除」，旧目录内容会完整搬到新目录。仍由 `EditorApplication.delayCall` 调度到 `RenameEnded` 回调外执行，成功后 `AssetDatabase.Refresh(ForceSynchronousImport)` + `Reload()` 重建树；保留 `SkipNextProjectChangedReload` 标记（`ConfigDataWindow.OnProjectChanged` 据此跳过一次自动 `Reload`，防刷新竞争读到中间态）。新增重命名前后诊断日志（GUID 映射 + 目录与 `.meta` 磁盘存在性），让行为可在 Unity 日志中一次性坐实。`RenameEnded` 用 `args.newName == item.displayName` 判定取消 / 无变化（此 Unity 2022.3 的 `RenameEndedArgs` 无 `accepted` 字段，Esc 取消时 newName 回退为原名，空名由 `ApplyRenameCore` 的 null/empty 守卫兜底）。`ConfigAssetMoveGuard` 不拦截此类改名：它只拦「已归置 `.asset` 被移出自身类型文件夹」，文件夹重命名与同目录文件改名（`ShouldBlockMove` 判定目标仍在类型文件夹内）均放行。

- **数据配置树拖拽资产到文件夹无响应（`Editor/DataEditor/ConfigDataTree.cs`，ns `HYC.Framework.Config.Editor`）**：用户把 `[掉落]` 类型配置从 `道具` 文件夹拖到新建的 `掉落` 文件夹时，放下后资产没有移动。根因分三层：① `HandleDragAndDrop` 计算目标路径时用了 `Path.Combine(dropPath, Path.GetFileName(dragPath))` —— 在 Windows 上 `Path.Combine` 会插入反斜杠，产出 `Assets/ConfigData/掉落\Drop_Xxx.asset`；② 即便路径修成正斜杠，`AssetDatabase.MoveAsset` 在 TreeView 拖拽回调的事务内调用仍会**返回成功但不落盘**——这与 `RenameEnded` 回调内直接调用 `RenameAsset`/`MoveAsset` 的现象同根；③ 把 `MoveAsset` 推到 `EditorApplication.delayCall` 后仍然不落盘，说明此上下文/Unity 版本下 `AssetDatabase.MoveAsset` 对这批资产就是假成功。修法：① 目标路径改为 `dropPath + "/" + Path.GetFileName(dragPath).Replace("\\", "/")`；② 抽 `ApplyMoveCore`，把 `AssetDatabase.MoveAsset` 换成 **`FileUtil.MoveFileOrDirectory(dragPath, newPath)` + `FileUtil.MoveFileOrDirectory(dragPath + ".meta", newPath + ".meta")`** 做真文件系统搬家；③ 手动复现 `ConfigAssetLayout.ShouldBlockMove` 弹窗（FileUtil 不触发 `AssetModificationProcessor`，不会自动走 `ConfigAssetMoveGuard`），保持与 Project 窗口一致的「不建议移动」守护语义；④ 套 `SkipNextProjectChangedReload` 并调 `AssetDatabase.Refresh(ForceSynchronousImport)` + `Reload()`；⑤ 祖先循环里的 `Path.GetDirectoryName` 归一化为正斜杠。`ConfigAssetMoveGuard` 对此场景本就不拦截（资产原在 `道具`，不在其类型文件夹 `掉落` 内，移入 `掉落` 是归置而非移出）。

**框架级 ECS 引导基类 `FrameworkBootstrapBase`（`Runtime/FrameworkBootstrapBase.cs`）**

- 游戏写一个 `sealed class XxxBootstrap : FrameworkBootstrapBase` 即可获得「开箱即用」的框架世界，
  无需自己拼组 / 挂 UIManager / 挂 player loop / 设步长。基类负责：跨端预取协程
  （`StreamingAssets.PreloadAsync`）、建 World、**标准自动装配**（三大根组 + Begin/EndECB +
  `UpdateWorldTimeSystem` + HYC 阶梯组 + 配置 Blob 系统自动 `OnCreate→Load` + `BTInterpreterSystem` +
  所有未标 `[DisableAutoCreation]` 的系统）、创建并挂接 `UIManager`/`UIGroup`、固定步长
  （`RateUtils.FixedRateCatchUpManager`）、UI 前置（EventSystem + 透视 UI 相机 + 默认 UI 导航热键资产）、
  挂进 player loop。
- 子类只实现 `RegisterSystems`（`[DisableAutoCreation]` 玩法系统 / 需配置的系统 / 桥接）、
  `Configure`（一次性接线）、`OnWorldCreated`（物理层等世界定制）；策略可用
  `WorldName` / `FixedStep` / `EnableUI` / `EnableBT` / `AutoAssembleDefaultSystems` 调整。
- 自动装配即「配置系统自动化」来源：`XxxConfigBlobSystem` 无 `[DisableAutoCreation]`，
  随标准 ECS 默认世界初始化一并创建并入组，宿主无需逐个 `GetOrCreateSystemManaged`。
- `abstract` 基类不会被 Entities 的 `ICustomBootstrap` 自动发现（跳过 abstract），
  不会与游戏自有 bootstrap 冲突；`UIManager` 的 `[DisableAutoCreation]` 手动创建问题由此收口
  （`FrameworkModeBootstrap` 走 `GetAllSystems(Default)` 不含 `[DisableAutoCreation]` 系统）。

**跨端数据加载契约（PC / iOS / Android / WebGL 一套代码）**

- **`StreamingAssets` 平台抽象补全 Android**（`Runtime/Streaming/StreamingAssets.cs`）：
  新增 `IsAndroid` 与 `NeedsPreload`（= `IsWebGL || IsAndroid`）。
  Android 的 StreamingAssets 在 APK 压缩包内（`jar:file://`），`System.IO.File` 同样读不到——
  与 WebGL 同属"必须走 UnityWebRequest + 内存缓存"这一类，此前只判 `IsWebGL` 是漏的。
  `Config` / `BT` / `Loc` / `Audio` 的读取判据全部从 `IsWebGL` 切到 `NeedsPreload`。
  新增 `IsPreloaded` 供引导时序判断。
- **`BlobReader`（unsafe 收口）**（`Runtime/Streaming/BlobReader.cs`）：
  `BlobReader.TryRead<T>(byte[], version, out BlobAssetReference<T>)` 把「`MemoryBinaryReader` + `fixed`」
  这段 unsafe 解析关在框架 Runtime 程序集内（`allowUnsafeCode: true`）。生成的 `XxxBlobSystem` 与
  `BTBlobLoader` 改为调用它——**宿主工程无需开启 Player Settings 的 "Allow unsafe code"**。
- **异步引导入口**：`FrameworkBootstrap.InitializeAsync` 与
  `FrameworkModeBootstrap.InitializeAsync`——先 `await StreamingAssets.PreloadAsync()`
  再建 World，最后 `FrameworkLifecycle.EnsureBootDataLoaded()` 补加载入。
  PC / iOS 上预取是 no-op，**游戏层全平台调同一个入口即可，无需写平台分支**。
- **清单构建覆盖 Android**：`StreamingAssetsManifestBuilder` 由"仅 WebGL"扩为 WebGL / Android，
  打包时把 StreamingAssets（除 `aa/` 与 `.meta`）列成 `hyc_streaming_manifest.txt`。
- **本地化引导时序守卫**：`LocalizationBlobSystem` 的 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`
  在 NeedsPreload 平台上若发现缓存未就绪会主动跳过并提示改用异步引导入口，
  避免读出一堆空数据后业务侧难定位。
- **`CfgAssetAttribute.Hide` + `CfgAssetAttribute.IsHidden(Type)`**：配置资产特性新增 hide 开关与静态判定。
  框架基础设施类型（模板 / 生成配置的基类 / 音频总览）自标 `Hide = true`，「配置创建器」与
  「配置引用」选择器据此过滤；用户创建的配置不填即默认显示。
- **行为树资产创建工厂 `BTAssetFactory`（`Editor/BT/BTAssetFactory.cs`）**：行为树资产（`BTTreeAsset`）的统一程序化创建入口，与 `ConfigAssetFactory` 平行。强制 `TreeId` 由工厂扫描工程内现有 `BTTreeAsset.TreeId` 派发唯一值，取代 `DateTime.Now.Ticks % 100000` 与 `[CreateAssetMenu]` 默认 `TreeId=0` 的撞车隐患（子树引用靠 `TreeId` 选树）。`Create(name)` / `CreateMany(prefix, count)`，可选 `withRoot` 预置 Root 节点；图编辑器「新建行为树」与菜单 `HYC Framework/BT/New Tree`（pri 12）均已改走工厂。TreeId 改以**本机 MAC 派生**（高 48 位机器 + 低 15 位本机段序号），跨机不撞、无需局域网发号服；旧的小号 TreeId 由扫描兜底。节点 ID（`BTNodeData.NodeId`）同样收口到工厂 `NextNodeId(tree)`（树内最小未占用正整数，必然唯一），外部 / AI 不得手填，原 `Guid.NewGuid().GetHashCode()` 不校验的写法已删除。

**复制资产自动重发号 `DuplicateIdGuard`（`Editor/DataEditor/DuplicateIdGuard.cs`）**：配置资产（`ConfigBase` 派生，走 `ConfigIdService`）与天赋树（`TalentTreeAsset.treeId`）在 Project 窗口 Duplicate / Ctrl+C-V / 跨文件夹拖复制后**自动重派发唯一 ID**，应用层无需记忆"别复制"。原理：借 `AssetModificationProcessor.OnWillCreateAsset` 捕获"新资产"路径（Unity 无 OnDuplicate 回调），导入完成后建一次「既有 ID 集」（排除本次新建的自己），新资产 ID 命中即**判定为复制→直接重派发**（O(1) 判定，不走全量查重），只动新资产、不误伤原件；工厂新建资产 ID 唯一不命中→跳过。另提供手动兜底菜单 `HYC Framework/Config/Reissue Duplicate IDs`（pri 3）与 `HYC Framework/Talent/Reissue Duplicate Tree IDs`（pri 51），复用 `FindIssues`/`Reissue` 同款查重逻辑修历史脏数据。

### Changed

- **`ConfigBase` 基类移入框架（`Runtime/ConfigBase.cs`，`abstract`）**：所有生成配置的默认基类（仅 `ID` / `GUID` 两个 `[ReadOnly]` 字段）随包预编译，不再由代码生成器往宿主工程吐出。`ConfigTemplate` 新增 `generateCode` 开关（默认 `true`）；框架模板 `FrameworkTemplates/ConfigBase.asset` 置 `false`，生成器 `WriteFile` 据此跳过该模板的主类代码产出（跳过逻辑数据驱动，不写死类名）。框架侧手写提供同名支撑类型：`ConfigBaseClient` / `ConfigBaseServer`（侧类型基类，承载 ID/GUID）与 `ConfigBaseExport`（导出管线基类）；派生配置的主类 / 侧类型 / 导出类分别继承三者，形成与配置链一致的三条继承链。宿主 `Assets/GeneratedConfigs/` 下旧 `ConfigBase*` 文件（含 `.meta`）应删除并在 Unity 重生成配置。
- **配置侧类型恢复继承链（撤回扁平化）**：`GenerateSideType` 的 `baseSideName` 不再因 `generateCode=false` 置空——链上直接基类存在即继承（宿主生成基类 → 同命名空间短名；框架级基类 → `HYC.Framework.Config.{side}.{Base}{side}` 全限定名）。派生侧类型只声明自身字段，ID/GUID 等基类字段经继承获得；`From` 平铺赋值全部导出字段不变（继承字段经派生引用可直接访问）。修复扁平化期间 `From` 给未声明字段 `r.ID`/`r.GUID` 赋值导致宿主 CS1061 编译不过的问题。struct 模式（`SideTypeIsStruct=true`）保持平铺（结构体不能继承）。
- **导出类改为薄子类，导出管线上收框架基类 `ConfigBaseExport`**：生成的 `XxxExport` 从 `static class`（约 126 行重复管线/类）改为 `ConfigBaseExport` 的非静态子类（约 55 行/类），只填类型相关钩子（`CheckData` / `CreateClientDto` / `CreateServerDto` / `ClientIsBlob` / `WriteClientBlob`）。基类负责：导出管线（检查→客户端→服务器→`AssetDatabase.Refresh`）、单实例/批量入口、JSON 落盘与目录规则、按 `ConfigType` 精确匹配的资产收集（派生类型的资产归它自己的导出类）。导出链与配置主类链一致（`XxxExport : [基类配置的导出类 :] ConfigBaseExport`）。`HYC.Framework.Editor.asmdef` 新增 `Newtonsoft.Json` 引用（基类用 `JsonConvert`）。
- **`ConfigExportService` 改实例化调用**：生成的 `XxxExport` 不再提供静态 `Export`/`ExportAll` 入口，Service 用 `Activator.CreateInstance(exportType, configType)` 实例化后调 `ExportCore`/`ExportAllCore`。原因：继承链上同名静态方法会让 `GetMethod` 产生歧义（`AmbiguousMatchException`）。旧版静态类（IL 层面 abstract+sealed）由 `IsAbstract` 识别并提示重新生成。行为保持不变：单实例客户端/服务器各写 `{ClassName}.json`（单对象），批量客户端逐资产写 `{asset.name}.json`、批量服务器写一个列表 JSON。
- **`XxxCheck` 改为只查自身字段**：基类字段由基类导出类的 `CheckData`（`base.CheckData` 向上链）负责，每层只查自己那层，避免分层继承后重复报错。`XxxCheck` 无其他调用方（仅 `XxxExport` 经 `CheckData` 使用）。
- **配置 Blob 落点收口为单一真值**：`ConfigDataSettings.BlobLoadPath` 改为由
  `BlobOutputDir` 派生（剥掉 `Assets/StreamingAssets/` 前缀），设置窗口的「运行时加载路径」
  变为只读展示。导出的落点从 `ClientExportDir` 改为 `BlobOutputDir`，并加校验：
  Blob 输出目录必须位于 `Assets/StreamingAssets` 内，否则导出直接报错返回。
  一次性迁移：旧版改过 `BlobLoadPath` 的工程，首次读取时按旧值反推 `BlobOutputDir` 并落盘。
- 生成的 `XxxBlobSystem` 不再输出 `using System.IO;` 与 `using Unity.Entities.Serialization;`
  （加载走 `StreamingAssets.ReadBytes` + `BlobReader.TryRead` 两次安全调用，文件内无
  `Path`/`File`/`MemoryBinaryReader` 用法，避免刷 CS8019）。
- `BTBlobLoader` 的 WebGL / Android 分支由内联 unsafe 解析改为调用 `BlobReader.TryRead`，
  框架内只保留一份 unsafe 实现。

### Removed

- **`StartupSetting` ScriptableObject（`Runtime/StartupSetting.cs`）**：原设计为
  「AA 加载 → entity object singleton → `UIManager.OnCreate` 读取」的启动配置载体，
  但生产端从未在框架/宿主接线，读取恒走 `catch` 兜底（配置静默失效）；且 10 个字段中
  6 个（`HotkeyIcons` / `HotkeyHoldThreshold` / `HotkeyStyle1~5`）在 HYC 无消费方
  （HotkeyManager 已重写，长按判定改注册时逐键 `holdTime`）。UI 适配 / 导航参数改由
  `UIManager.Setting`（static，唯一真源，**默认值不变**）承载，宿主持家在
  `FrameworkBootstrapBase.Configure` 钩子里改字段（static + 实时读，即时生效）；
  输入资产绑定走既有的 `BindHotkeyAsset` 覆盖钩子。将来若需数据驱动，把这些字段搬进
  `FrameworkSettings`（Resources 模式）即可，`UIManager.Setting` 作为真源不变。

### Fixed

- **「添加字段」复制上一行数据（配置模板编辑器，事故级 UX bug）**：`ConfigTemplateEditor` 的「+ 添加字段」用裸 `mFieldsProp.arraySize++`——Unity 对 `List<类>` 的 SerializedProperty 增容会**复制上一个元素**，新字段把上一行的类型 / 列表标志 / 读写标志 / 范围 / 三条检查规则 / 客户端与服务端导出设置（`mode`/`memberName`/`fixedStringSize`）全部带过来。事故形态：在 string 行后加字段，新字段直接就是 string；上一行若是 ToString / 资源导出模式，新加的 long 也被导出成 string，直接打包即错。现改为 `AddEmptyField()`：增容后把 16 个属性逐项写回 `ConfigTemplateField` 默认值（含两个嵌套导出设置子对象复位），名称默认 `newField`、与既有字段（含继承链）重名时递增后缀，避免生成即报「字段名重复」。Attr 属性目录 / 枚举值编辑器本就「插入后显式复位」，此为前提补齐。
- **「添加条目」复制上一行（ComponentBinder 表编辑器）**：同类根因——`ComponentBinderTable.Item` 是类，`_items.arraySize++` 把上一行 Name/Target/Component/Desc 全复制过来，新行直接重名（生成 binder 报 CS0102）且串绑。现增容后显式复位四项为空（Name 留空由用户填，行绘制红色提示；填了 Node 后自动取 Node 名兜底）。

- **`ConfigBase` 移入框架后的编译/编辑器收尾**：
  - `ConfigTemplateCodeGen.WriteFile(template, out error)` 的 `generateCode=false` 提前返回分支漏赋 `error` 即 `return true`，报 **CS0177**（out 参数未赋值）。已补 `error = null;`。
  - `IsGenerated` 仅查宿主 `.cs` 是否存在，会把框架级基类 `ConfigBase`（`generateCode=false`、宿主 `.cs` 已删）误判为「未生成代码」，在 `ConfigTemplateEditor`（基类链校验/红框）与 `ConfigDataTree`（" (未生成)" 后缀）误导用户去生成本就不该生成的基类。新增 `ConfigTemplateCodeGen.IsBaseReady(template)`：① 框架级模板（`generateCode=false`，随包预编译）恒就绪；② 宿主 `.cs` 已生成为就绪；③ 兜底反射——框架 Runtime 已编译出 `HYC.Framework.Config.{className}` 同名基类类型即就绪（即便 `baseTemplate` 误指向旧宿主副本也认定可用，让编辑器能「抓到」框架里的基类）。按类名在单域会话内缓存，避免重复扫程序集。上述基类检查点全部切换。
- **「配置创建器」/「配置引用」选择器列出框架基础设施类型**：`ConfigCreateWindow` 的 `[CfgAsset]` 类型扫描原本只按类型黑名单排除 `ConfigEnumDefinition`，漏掉 `ConfigTemplate`（模板）、生成配置的基类 `ConfigBase`（代码生成器给框架级模板也吐了 `[CfgAsset]`）与 `AudioCatalog`（音频总览）——三者都有专用创建入口（数据树右键「创建配置模板」/ 代码生成 / Audio Manager「创建总览」），不该由用户在这些界面创建。现改为声明式：新增 `CfgAssetAttribute.Hide`，基础设施类型自标 `Hide = true`；两个界面统一按 `CfgAssetAttribute.IsHidden(type)` 过滤（只认类型自身直接标注，继承链不算——避免基类藏掉派生配置）。代码生成器对 `FrameworkTemplates` 下的框架级模板吐 `Hide = true`。
- **生成的下发配置加载器要求宿主开 `/unsafe`（CS0227）**：跨端改造后生成的
  `XxxBlobSystem.Load()` 内联了 `unsafe { fixed { new MemoryBinaryReader(...) } }`，
  导致宿主工程（未开 "Allow unsafe code"）编译报 8 个 CS0227。现将 unsafe 收口至框架
  `BlobReader`，生成体与 `BTBlobLoader` 均改为安全调用；已同步修正宿主既有生成产物。
- `StreamingAssetsManifestBuilder` 缺 `using UnityEngine;` 导致 `Application` 报 CS0103。
- **git URL 安装时缺依赖报 CS0234**：`package.json` 此前只声明了
  `com.unity.addressables` 与 `com.unity.nuget.newtonsoft-json`，
  以 git URL 方式安装到宿主工程时 UPM 不会拉取 DOTS 全家桶，
  所有 Runtime 脚本报 `error CS0234: "Entities" does not exist in the namespace "Unity"`。
  现补齐 `dependencies`（版本与宿主工程实测组合一致）：
  `com.unity.entities 1.0.16` / `com.unity.burst 1.8.16` /
  `com.unity.collections 2.1.4` / `com.unity.mathematics 1.2.6` /
  `com.unity.inputsystem 1.7.0` / `com.unity.timeline 1.7.7` /
  `com.unity.textmeshpro 3.0.7` / `com.unity.ugui 1.0.0`。

### Added

**音频系统**（框架此前零音频代码，本次从零建链路）

- **统一门面 `Audio`**（`Runtime/Audio/Audio.cs`）：三后端共用一套 API
  （`Play` / `Stop` / `StopChannel` / `Pause` / `SetVolume` / `SetMuted` / `Dispose`）。
  切后端只换门面背后的实现，**业务代码与配置表零改动**。
- **后端契约 `IAudio`** + `AudioHandle` 句柄（`Runtime/Audio/IAudio.cs`）。
  新增后端（FMOD / 自研）实现此接口即可接入。
- **框架默认后端 `DefaultAudio`**（`Runtime/Audio/DefaultAudio.cs`）：
  Addressables 载 `AudioClip` + 池化 `AudioSource`。
  **Unity 原生与框架模式合并到这一份实现**（都走 AA），区别只在配置字段存什么。
  句柄**不拥有** AudioClip 引用计数 —— 加载后常驻缓存直到 `Dispose`，
  避免"第二次播同一音效要重新加载"的卡顿（与 UI 的 `Addressables.Release` 语义一致）。
- **Wwise 后端 `WwiseAudio`**（`Runtime/Audio/WwiseAudio.cs`）：**类体全部用 `#if HYC_WWISE` 包裹**，
  没装 Wwise 时编译出空壳、不引用任何 Wwise 类型，框架包本身无需 Wwise 依赖即可编译。
  未定义宏时所有方法明确 `LogError` 而非静默失败。
- **分组音量（数据驱动）**：原写死 `enum AudioChannel` 改为可配置 `FrameworkSettings.audioGroups`；
  类型层用 `readonly struct AudioChannelId`（包一层 int，隐式转换兼容旧 catalog/Blob 的 0~4）
  与 `class AudioGroupDef{int id; string displayName; float defaultVolume}`。`Master` / `Music` / `Sfx` / `Voice` / `Ui` 为默认 5 组（id 0~4）。
  实际增益 = `Master × 组音量`。设置界面可分音乐/音效等任意组调。
- **音频总览数据结构**（`Runtime/Audio/AudioCatalog.cs`）：
  `AudioCatalogAsset` 带 `[CfgAsset("音频/音频总览", 60, Unique=true)]`——
  与其他配置类型**完全同构**，出现在配置树里、可直接编辑。
  每条 `AudioEntry` 同时挂 `addressableKey`（clip 路径）与 `wwiseEvent`（Wwise 路径）。
  Blob 侧 `AudioCatalogRow` / `AudioCatalog` / `AudioCatalogBlobSystem`，
  产物落 `StreamingAssets/AudioBlob`，与 Loc / BT 的 Blob 管线同构。
- **音频管理窗口** `HYC Framework/Audio/Audio Manager`（priority 60，独立一档）：
  - **后端切换**：Wwise 选项按"装了没"+"宏有没有"点亮，两者缺一即置灰并显示对应指引
    （装了但缺宏 → 给"打开 Player Settings 添加宏"按钮；没装 → 给 Launcher 安装指引）。
  - **音频清单大列表**：启用 / 音频名字 / Addressables 地址 / Wwise 事件名 / 分组 / 已导出 / 描述。
  - **「是否已导出成框架配置」列**：比对导出时刻写入的内容 hash（FNV-1a，跨会话稳定）
    与当前总览内容，改动后整列自动转「否」并提示重新导出。
  - **扫描工程音频**：`AssetDatabase.FindAssets("t:AudioClip")`，
    自动填 Addressables 地址 + 按路径猜分组（BGM/Music→音乐、UI→界面…），已存在的跳过不覆盖。
  - **扫描 Wwise 事件**：解析官方 `SoundBanksInfo.json`
    （`SoundBanksInfo.SoundBanks[].IncludedEvents[].Name`）。
    找不到时提示在 Wwise Authoring 勾选 *Generate JSON Metadata* 后重新 Generate，
    并提供手动指定文件路径的口子。
- **`HYC Framework/Audio/Export Catalog Blob`**（priority 61）：导出 Blob 到 StreamingAssets。
- **清单改为按「物理文件夹」一级分组**：可把 **AudioClip 文件或文件夹**直接拖进窗口虚线落区；
  列表按每条目的 `sourceFolder`（物理文件夹）分组，每个文件夹是一行**可展开/折叠的实体节点**
  （拖文件夹会递归扫描其内 clip、并**自动登记进 Addressables** 同名分组——folder entry，子资源自动纳入；
  整棵子树登记为一个分组，避免根 + 子目录各建条目互相嵌套冲突）。
  ★ 递归扫来的每个 clip **按自身的物理子目录归组**（如 `Assets/Audio/music`、`bgm`、`sfx` 各成一行），
  而不是全挤在拖入的根目录下；零散拖入的 clip 同样归到其物理所在文件夹；扫描工程音频亦然。
  音量组初值按资源路径推断（BGM/Music→音乐、UI→界面…），与「扫描工程音频」一致。
  文件夹行带整组批量操作：**全启用 / 批量改音量组 / 登记 AA / 移除组**；展开态存 EditorPrefs。
  另有**自愈**：导入时把 `sourceFolder` 重新对齐到 clip 当前的物理目录（修复早期误归到根目录的数据）。
  原「分组」列改名「音量组」（= `AudioChannel` 音量路由），与物理文件夹分组是两个独立维度。

**配置系统接入「框架/音频」字段类型**（`ConfigFieldType.AudioEvent`）

- 存**带前缀的字符串**：`clip:<Addressables地址>` / `wwise:<Wwise事件名>`。
  前缀决定路由到哪个后端 → **同一张配置表在两种后端下都能播，切后端不改配置**。
  与现有 `Addressable` 字段一样进 Blob 走字符串（`IsStringLike()`），
  与 `Unity 对象/音频`（AudioClip 强引用）**并存不冲突**。
- 6 个接入点全部同步：枚举定义 `Runtime/ConfigTemplate.cs`、
  菜单路径 `ConfigTemplateEditor.cs`、代码生成加 `[AudioField]` `ConfigTemplateCodeGen.cs`、
  类型映射 `string` + `using HYC.Framework.Audio`、`IsStringLike()`、
  属性绘制 `DrawAudioField`（`GUIDrawer.cs`，下拉列出总览条目 + 未登记值标 ⚠ 不静默改写）。
- 两个 asmdef 加 `versionDefines`：装 `com.audiokinetic.wwise.addressables` 时自动定义 `HYC_WWISE`。
  **新版 Wwise Unity Integration 是 `.unitypackage`（无 package.json），versionDefines 侦测不到**，
  故窗口同时用 `AssetDatabase` 探测 `Assets/Wwise/Deployment` 并提示手动加宏。

**设置窗口：两栏布局 + 配置式分组**（`HYC Framework/Settings/Project Settings`）

- **窗口改为 Unity Preferences 式「左栏分组 / 右栏内容」**：左栏 200px 分组树（`/` 嵌套缩进一层）、
  右栏显示选中分组的条目；选中态为**整行纯色高亮**（不加竖条），选中页存 `[SerializeField] _selectedPath`，域重载不丢。
- **分组改为特性声明，窗口不再写死清单**：新增 `Editor/HYCSettingsGroupAttribute.cs` 与
  `Editor/HYCSettingsRegistry.cs`。字段加 `[HYCSettingsGroup("General")]` → 自动出控件
  （`bool`/`int`/`float`/`string`/枚举）并即时写 EditorPrefs；**无参 `void` 方法**加
  `[HYCSettingsGroup("Audio/Backend")]` → 自己画复杂区块（按钮 / 下拉 / 提示框）。
  路径 `/` 表示嵌套；可选 `Label` / `Order` / `Key`（显式指定可避免改名丢值）/ `Persist`。
- **扫描范围**：只扫引用了 `HYC.Framework.Editor` 的程序集；静态成员任何工程可直接挂，
  实例成员须 `HYCSettingsRegistry.RegisterHost()`（窗口已在 `OnEnable` 登记自身、`OnDisable` 注销）。
- 现有分组：`General`（两个 bool 字段）/ `ID Server` / `Audio` / `Validation`（三者为绘制方法）。
- 文档：新增 `Documentation~/91-设置窗口-配置式分组.md`（写法 / 嵌套 / 持久化键 / 10 条雷区），
  `90-编辑器工具链` 与 `02-操作手册` 挂入口。

**音频音量分组 / 通道推断可在 HYC Settings 配置（项目级，出包生效）**

- **`Audio/Volumes`**：主音量 + 5 通道（Master / Music / Sfx / Voice / Ui）滑块 + 全局静音。
  改动实时应用到音频门面（编辑器预览），并写入 `FrameworkSettings` 资产
  （`Assets/Resources/FrameworkSettings.asset`）；运行时由 `Bootstrap` 在 `SetBackend` 之后应用，
  **出包保留设定**（此前音量恒为代码默认 1.0、完全不可配）。
- **`Audio/Inference`**：可编辑「关键词 → 通道」推断规则表。扫描音频时按资源路径/名称
  包含关键词匹配自动归类（靠上规则先匹配，首命中优先，空关键词忽略，无命中回退 Sfx）。
  `AudioScanner.InferChannel` 由硬编码（BGM/Music→Music、UI→Ui、Voice/Dialog→Voice）
  改为读取 `FrameworkSettings.audioChannelRules`，使用方可在设置里自定归类。
- **`Audio/Groups`（数据驱动分组）**：音量分组集合本身从写死 `enum AudioChannel` 改为可配置表
  `FrameworkSettings.audioGroups`（id 0 = Master 固定全局总音量、锁定不可删；其余分组可在本页增/删/改名）。
  `AudioChannel` 枚举替换为类型安全的 `readonly struct AudioChannelId`（包一层 int，隐式转换兼容旧 catalog/Blob 的 0~4）
  与 `class AudioGroupDef{int id; string displayName; float defaultVolume}`。扫描 / 推断 / 总览里的分组下拉全部改读该表，
  增删改名后即时同步（路由看 id 不看显示名）。
- **后端选择补持久化**：顺带把 `DrawAudioBackendSection` 也写 `FrameworkSettings.audioBackend` 资产，
  使「项目级后端」真正出包生效（此前仅写 EditorPrefs，Bootstrap 读的资产始终为默认值）。

### Notes

- 主菜单路径**全英文**约定：`HYC Framework/Audio/Export Catalog Blob`（原写成中文被 `verify-gate.sh` 拦下）。
- 索引更新至 **226 文件 / 392 类型 / 18 分组**（新增 `Editor/HYCSettingsGroupAttribute.cs`、
  `Editor/HYCSettingsRegistry.cs`、`Editor/DataEditor/ConfigAssetFactory.cs`；数字由 `Tests/Editor/FileIndexAuditTests.cs` 守护）。

### 勘误：音频模块首轮编译的 16 处错误（2026-10-08 实测发现，已修）

沙箱禁 `csc`，首轮写完没编译过，用户在 Unity 里一开就是 16 个错误。根因四类：

1. **CS0103 `Debug` 不在上下文**（6 处，`WwiseAudio.cs`）：
   `using UnityEngine;` 被我放进 `#if HYC_WWISE` 块里，但 `#else` 分支（未装 Wwise 时，
   也就是**默认编译路径**）里的 `Debug.LogError` 同样需要它。
   → **条件编译分支两侧都要用的 using 必须提到 `#if` 外面。**
2. **CS1001 `BlobString` 无 `value`**：用了不存在的属性。正确做法是
   `ref BlobString` 转托管字符串（框架 Loc 有 `ToUtf8String()`；音频自写一份避免跨模块耦合）。
3. **CS1017 `BlobAssetReference<T>` 无 `CreateFromMemory`**：Entities 1.0 里不存在。
   框架的正确 API 是 **`TryRead(path, version, out blob)`**（`BTBlobLoader` 用的就是这个）。
4. **EA0001/EA0009 Blob 数组只能按 ref 访问**（8 处）：
   托管代码直接遍历 `_ref.Value.Rows` 触发分析器约束。
   框架 `ConfigBlobTable.TryGetRowAt` 的注释**早就记录了这个坑**，正解是
   `ref var rows = ref _ref.Value.Rows;` 或值拷贝到托管数组后遍历。
   另给 Blob 根补上 `where TRow : unmanaged` 约束，与 `BlobRoot<TRow>` / `ConfigBlobTable<TRow>` 同构。

另外 `AllocateString` 是 **`ref` 参数**（`builder.AllocateString(ref arr[i].Ref, value)`），
不是返回值形式 —— 与配置管线生成代码一致。

### 勘误：给非泛型声明加 where 约束（CS0080）

第一轮修复里我仿照 `BlobRoot<TRow> where TRow : unmanaged` 给 `AudioCatalogBlob` 加了
`where AudioCatalogRow : unmanaged` —— 但 `AudioCatalogBlob` **不是泛型**，C# 直接
**CS0080: Constraints are not allowed on non-generic declarations**。

**教训**：照抄代码要抄到"为什么成立"这一层，不能只抄表面形状。
`BlobRoot<TRow>` 能加约束是因为它**是泛型**；非泛型类型加了就是错。

而那个约束本身**根本不需要** —— `BlobArray<AudioCatalogRow>` 自身就要求
T 满足 unmanaged，一旦 `AudioCatalogRow` 含托管引用（`AudioClip` / `string`）
会在编译期直接报错。约束是多余的。

**已加固**：`scripts/verify-gate.sh` 新增第 5 项检查（全仓扫「非泛型声明带 where」），
这类纯语法错不该再漏到 Unity 才被发现。同项做了双向验证。

### 勘误：blob 行类型不能按值传出（EA0009）

第二轮修 EA0001 时我用了「值拷贝到托管数组」，但 `AudioCatalogRow` 住在 blob 存储里
且含 `BlobString`，分析器禁止把这种类型**按值作为 out 参数 / 返回值**传出：

```
error EA0009: You may only access AudioCatalogRow by (non-)readonly ref
    in AudioCatalog.TryGet(long, out AudioCatalogRow)
```

框架的 `ConfigBlobTable.TryGet(long, out TRow)` 之所以能 `out TRow`，是因为它带
`where TRow : unmanaged` 约束 —— **含 `BlobString` 的行不在那个覆盖范围内**。

**正解**：新增托管 DTO `AudioCatalogEntry`（`Ref` 为 `string`），Blob→托管的转换
**在 `Snapshot()` 里一次做完**，Blob 类型不越过 API 边界。
`TryGet` / `TryGetByRef` / `All()` 全部改返回 DTO。

顺带修两处设计问题：
- 门面的 `Play` 此前**从不查总览**（直接用引用里的地址播），导致整个查询层是死代码。
  现在接上了：命中总览则用规范地址 + 总览登记的分组；未登记则原样播（手填地址也能响）。
- 分组冲突（调用方传了 vs 总览登记了）用 **`Play` / `PlayIn` 两个重载**表达，
  不用隐式标志位 —— 后者易错且不可读。

另给 `AudioCatalogBlobSystem` 加 `[RuntimeInitializeOnLoadMethod]` 自动加载
（照 `LocalizationBlobSystem` 的写法），否则总览在运行时永远是空的。

### 勘误：Editor 侧 7 处编译错（命名空间遮蔽 / 变量残留 / 二义）

1. **CS0234 `Audio.CurrentBackend` 不存在于命名空间**（3 处）：
   静态类 `Audio` 与命名空间 `HYC.Framework.Audio` **同名**。本文件在
   `HYC.Framework.AudioEditor` 里，裸写 `Audio.Xxx` 会被解析成**命名空间**。
   → 加 `using AudioFacade = HYC.Framework.Audio.Audio;` 别名。
2. **CS0103 `exported` 不存在**：把 `bool exported = ExportStatus()` 改成字段
   `_exportFresh` 时漏了一处引用（重构改名留漏网）。
3. **CS1929 `string[]` 无 `Contains`**：用了 LINQ 的 `Enumerable.Contains`，
   数组本身没有 → 补 `using System.Linq;`。
4. **CS0104 `GUIDrawer` 二义**：`UnityEditor.GUIDrawer` 与
   `HYC.Framework.Config.Editor.GUIDrawer` 同名，同时 using 两个命名空间即二义
   → **去掉** `using HYC.Framework.Config.Editor`，调用处写全名。

**注**：第 4 类（跨命名空间同名二义）在框架里已有先例
（`UnityEditor.GUIDrawer`），将来跨模块引用要预判。

### 闸门新增检查项的教训：误报率高的检查不如不加

本轮尝试给 `verify-gate.sh` 加第 6 项「未定义标识符残留」（想静态抓 CS0103 类问题），
结果**全仓 20+ 文件假红**（`_x` 这类字段声明、`foreach (var x in ...)` 都判不出来）。
判据太粗 ⇒ 噪声淹没信号 ⇒ 这项检查没有价值，已撤掉。

**结论**：静态检查项必须**在全仓跑一遍看误报**，不能只看"能不能抓到我这次的错"。
抓得到目标错误但误报一片的检查项，宁可不加 —— 留着只会让人忽略真信号。

### 勘误：IMGUI 运行时错误（Invalid GUILayout state）

编译通过后开窗口即报 `Invalid GUILayout state in AudioManagerWindow view`，
连带 `ArgumentNullException: Value cannot be null. Parameter name: target`。

**根因（主要）**：修 CS0103 时误把两行并成一行 ——
`EditorGUILayout.BeginHorizontal();  var newEnabled = ...`。
IMGUI 在 Layout 阶段解析到畸形行直接错位，后续所有控件位置失效。
**是我自己的编辑造成的**，静态检查第一轮没抓到（正则只数了 Begin/End 个数，没看单行是否含多条语句）。

**同时修掉三处同类的条件性布局**（都会在状态切换那一帧触发同样的报错）：
IMGUI 要求**每帧控件数量与类型完全一致**（Layout 与 Repaint 两阶段必须匹配），
条件性地增减控件就会错位：
1. `if (!_exportFresh) LabelField(...)` → 提示行始终画，按状态换文案。
2. `if (installed && !compiled) {...} else if (!installed) {...}`
   → 整块始终画，按状态换内容与按钮可用性。
3. `foreach (var m in _lastScan.Messages) HelpBox(m)`
   → 改成固定「摘要行 + 最近一条」，条数不画。
另外 `OnGUI` 里的 `if (_catalog == null) { ...; return; }` 改成 if/else
（提前 return 会让两种状态走不同长度的布局）。

**教训**：改 IMGUI 代码时，一条语句一行；批量编辑后必须扫
「同一行 `;` 后是否还有代码」—— 这条比 Begin/End 计数更能抓到真实问题。

### 勘误：InitializeOnLoad 挂到了静态构造器上（CS0592）

`ConfigTemplateCodeGen.WriteExportFile` 生成的 `XxxConfigExport.cs` 里，
`[UnityEditor.InitializeOnLoad]` 被吐在了**静态构造器**上：

```csharp
public static class ActorConfigExport
{
    [UnityEditor.InitializeOnLoad]                                    // ← 错
    static ActorConfigExport() => ConfigExportService.Register(...);
}
```

Unity 报 `CS0592: Attribute 'UnityEditor.InitializeOnLoad' is not valid on this
declaration type. It is only valid on 'class' declarations.`

**为什么**：该特性的语义是「修饰**类**，由 Unity 在程序集重载后调用该类的静态构造器」，
不是「修饰任意静态构造器」。框架里 `BTAutoRegisterOnPlay` 的正确写法是把它挂在
`class` 上、由那个类的静态构造器干活——生成器把两者拆错了位。

**修复**：特性上移到 `class` 声明上，静态构造器只留注册语句。注册语义不变
（`ConfigExportService.Register` 是幂等字典赋值，且 `FindExportType` 还有
全程序集扫描兜底，所以这一步纯粹是"零反射快捷路径"）。

**已加固**：`scripts/verify-gate.sh` 新增第 6 项检查（全仓扫
「`InitializeOnLoad` 是否修饰在 class 上」，并用 `\b` 天然排除合法的
`InitializeOnLoadMethod`）。生成产物落在宿主工程里、包内编译不到，
这类由生成器吐出的语法错只能静态抓。已做双向验证 + 包全量零误报。

### 变更：ID 服务器状态从「窗口角落绿点」改为「HYC Settings 文字状态行」

原先状态点画在 `ConfigDataWindow` 与 `ConfigDataSettingsWindow` 的**窗口内右下角**
（`position.width - 18`），视觉上紧贴属性面板的「保存模板」按钮，容易被误认成按钮状态指示。

**现状**：绿点已移除；`HYC Framework/Settings/Project Settings`（HYC Settings 窗口）里新增
「ID 服务器（配置发号）」区段 ——

- **状态行**：12x12 圆点（绿/黄/红）+ 文字（`已连接（127.0.0.1:8920）` / `不可用（…）`）
  + 悬停 tips。不可用时提示本地模式始终可用、局域网模式需先启动服务器。
- **设置项**：类型（本地/局域网）、服务器 IP、端口、「检测」按钮、
  「在 Unity 内启动 ID 服务器」（无启动插件时给降级提示）。
  改动**即时写入** `ConfigDataSettings`（EditorPrefs），不需点保存；
  类型/IP/端口任一变化都调 `ResetProvider()` 重建 Provider（旧连接已失效）。
- 后台心跳：仅在**非 Connected** 时探（本地模式 `Ping` 恒真不必探），5 秒间隔。

与「数据编辑器设置」窗口操作**同一份** `ConfigDataSettings`（EditorPrefs），两处等价、互不冲突。

**API 变化**：`ConfigIdService.DrawStatusDot(Rect)` **已删除**（无调用点，不留死 API）。
新增 `GetStatusTexture()` / `StateText` / `StatusTooltip` 三个只读成员供 UI 复用。

### 修复：「生成代码」后模板长期带未保存标记（标题的 `*`）

点「生成代码」后资产一直带 `*`，必须再点一次「保存模板」才消。**有两个独立源头**：

1. `ConfigTemplateEditor.Generate()` 只有 `mTarget.ApplyModifiedProperties()` ——
   它只把 `SerializedProperty` 提交到**内存对象**，从不写盘。
2. `ConfigTemplateCodeGen.WriteFile` 内部为记录 `lastGeneratedClassName`
   会 `EditorUtility.SetDirty(template)` —— **是生成动作自己把资产弄脏的**。

只修 ① 不够：生成器内部那行 `SetDirty` 会在保存之后**重新**标脏。两处都要处理。

**修复**：

- 抽出 `SaveInternal(bool showNotification)`（「净化显示名 → 同步资产名 → SetDirty +
  SaveAssetIfDirty」），`Save()` 与 `Generate()` 共用。
  ⚠️ 抽取时**保留原行为**：`Save()` 原本不关闭窗口，`SaveInternal` 因此不带 `close` 参数。
- `Generate()` 改为**生成前落盘一次、生成后再落盘一次**（后者消掉生成器内部的 `SetDirty`），
  提示改为「已保存并生成 Xxx.cs」；落盘失败时降级提示请手动保存。
- 右键菜单入口 `ConfigDataTree.GenerateTemplateCode` 同样补上前后两次落盘
  （原先只有 `WriteFile` + `Reload`，同样会留 `*`）。
- `lastGeneratedClassName` 的 `SetDirty` **保留不动** —— 它是改名追踪的依据，去掉会破坏改名功能。

**副作用（正向）**：生成前落盘使「改了显示名没保存就生成」的场景下，
生成用的资产名与保存后的保持一致（`SyncTemplateAssetName` 一并纳入流程）。

### 变更：框架音频字段从「下拉选总览条目」改为「拖入 AudioClip」

原先 `DrawAudioField` 列出音频总览（`AudioCatalogAsset`）的条目做下拉。实际使用中不便：
必须先在 Audio Manager 里把条目建好，配置侧才能选；没建总览时整列只能显示「(无)」
（截图实测：模板里字段填好了，数据配置里仍是无 + 一行「未找到音频总览资产」提示）。

**改为拖拽式**（与 Addressable 字段的交互一致）：

- `ObjectField(AudioClip)` —— 直接把 `.clip` 拖进去，自动取它的 Addressables 地址，
  写成 `clip:<AA地址>`；清空即删除。
- 资源未打 Addressables 标签时：填入资源名兜底，并**弹窗告知**（不静默塞一个加载不了的地址）。
- 字段下方以 miniLabel 显示当前 key（原值），便于核对与手填。
- `wwise:` 前缀的值没有对应资源可拖，改用**文本框**回退编辑。

**为什么仍存字符串而不是 `AudioClip` 对象引用**：前缀决定路由（`clip:` 走框架默认后端、
`wwise:` 走 Wwise），换后端不改配置表，且字符串能直接进 Blob；存对象引用则两者都做不到。

**两个 AudioEvent 概念要分清**（此前被我混为一谈）：

| 模板字段类型 | 生成 | 面板画法 |
|---|---|---|
| `AudioClip`（Unity 原生，既有） | `public AudioClip X` | Unity 默认对象选择器 |
| `AudioEvent`（框架/音频） | `public string X` + `[AudioField]` | 本次改为拖拽式 |

**IMGUI 约束**：`wwise` 分支的 TextField 用 `DisabledScope` 而非 `if` 包裹 ——
控件数量必须每帧恒定，否则 Layout/Repaint 错位触发 `Invalid GUILayout state`。

**注**：`[Header("…")]`（模板里的「显示名字」）与字段标签（`PropertyField` 的 label，
来自 C# 字段名）是**两套独立机制**，一直并存、不冲突。`[Header]` 渲染为字段上方的分组标题。

### 修复：自定义绘制字段在数据配置面板上丢失 [Header]（模板"显示名字"不显示）

**现象**：模板里给字段填了显示名字（生成 `[Header("角色音效")]`），数据配置面板里看不到。
**不是模板写错，也不是生成器写错** —— 生成产物确实是 `[Header("角色音效")]` + `[AudioField] public string AudioEvent;`。

**根因**：Unity 只在**默认 `PropertyField` 流程**里绘制 `HeaderAttribute`。
`GUIDrawer.DrawFieldGUI` 里走自定义绘制分支的字段（Addressable / 行为树 / 多行 /
多语言Key / **音频**）绕过了那条流程 ⇒ Header 永远不画。
所以这不是音频独有的问题 —— 行为树 `TreeId` 等自定义字段同样丢描述，一直没人注意。

**修复**：

- `GUITypeField` 新增 `Header` 字段；`GUIType` 反射时读出 `HeaderAttribute.header`。
- `DrawFieldGUI` 在进入自定义分支前补画 `LabelField(field.Header, EditorStyles.boldLabel)`。
- ⚠️ 补画的**条件必须与紧随其后的 if/else-if 链逐条一致**（代码里已写明）：
  少一条 ⇒ 该字段 Header 丢失；多一条 ⇒ Header 画两遍。新增自定义分支时两处一起改。
- 默认分支不动 —— 那里 Unity 自己会画 Header，画两次会重复。

### 变更：框架音频字段布局（拖拽框与 key 分行）

上一版把 `ObjectField` 和 key 文本框**并排**画在一行，视觉上像"这里在存 AudioClip 对象"，
容易误读（用户实测截图确认）。改为：

- 第一行：`ObjectField(AudioClip)` —— 拖入 clip 自动写 `clip:<AA地址>`（与 PrefabAddress 同款）。
- 第二行：文本框显示当前 key。`wwise:` 值无资源可拖，用 `DisabledScope` 放行编辑；
  `clip:` 值该框只读（改值请重新拖 clip）。

**存储类型仍是字符串**（`public string AudioEvent` → Blob 里 `BlobString`），
拖拽框只是**选取音频资源的交互手段**，不改变存储形态 —— 这样才能用前缀路由到
框架默认后端或 Wwise，且能直接进 Blob。

## [2.0.6] - 2026-10-07
> 版本说明：`2.0.5`（存档存储等）与本节合并为一次发布，`package.json` 由 `2.0.4` 直接 bump 到 `2.0.6`。
> 此前两个 `- Unreleased` 段落从未发布过，故合并不会影响任何已发布版本。

### 勘误：历史 tag 与包版本脱节（2026-10-07 实测发现）

`v2.0.0` ~ `v2.0.4` 这**五个 tag 内的 `package.json` 全部写着 `"version": "1.0.0"`**，
与 tag 名完全脱节（另外 `v2.0.5` 无对应 tag，tag 跳号）。2026-10-07 打的 `v2.0.6` 是首个一致的。

**影响**：tag 本应充当"可寻址的发布点"，版本脱节后它失去这个作用——
按 tag 引用会拿到一个自称 1.0.0 的包。

**处理**：历史 tag **无法改写**（改写会让已引用它的下游失效），故：
1. 在 `scripts/verify-tag-version.sh` 的 `KNOWN_LEGACY_MISMATCH` 中登记为既成事实；
2. 该脚本对已登记的历史遗留**只报告不失败**，但**任何新 tag 若与包版本不一致即失败**（退出码 1），
   并提示"不要为了让 CI 变绿而随便加进基线"。

### Changed
- **编辑器菜单三档分级**（用户反馈：「Setting 和 Samples 明显和其他功能不是一回事」、
  「不能只是去掉横线」）。菜单不按字母序，改为按**使用频率与语义归属**分三档：

  | 档 | 成员 | 使用频率 |
  | --- | --- | --- |
  | ① 内容制作 | Config / BT / UI / Localization / Attributes / Talent | 每天都在用 |
  | ② 通用工具 | Tools / Assets | 偶尔用，出问题时才找 |
  | ③ 项目级 | Settings / Samples | 装一次 / 改配置 |

  核心变化：**Settings 与 Samples 从字母序中段沉到最底部**，并用分隔线与上面的能力区隔开。
  原先它们夹在 Attributes/BT/Config/Localization/Samples/Settings/Talent/Tools/UI 中间，
  看上去与 `Config` 这类能力是同级的。

  技术要点：分隔线**不是画出来的**，是 Unity 按 priority 差 ≥ 11 自动生成的
  （官方文档实证）。因此 **20 个菜单项逐个**都显式标了 priority
  ——**不能只给父菜单首项写**，排序作用于每个叶子项，未写的默认 0 会全部挤到最前打乱分组；
  且**父菜单排序由其首个子菜单的 priority 决定**。
  档内相邻差 ≤ 10（不画线）、档间差 ≥ 11（画线）：`Talent(50)→Tools(100)`、
  `Tools(101)→Settings(200)` 各生成一条分隔线，恰好两条。

- **菜单分组与命名整理**。根因是 `BTBlobExporter` 写了 `priority = 30`，
  与前一项差值 30 ≥ 11 ⇒ BT 上方凭空多出一条分隔线。改动：
  - `BT/导出 Blob(打包用)` → **`BT/Export Blob (For Build)`**。
  - `Localization/` 原有 6 个平铺子项，按职责分二级组：
    `Import/Excel (Import & Build Blob)`、
    `Editors/{Key Browser, Scan Key Usage, Sensitive Words}`、`Settings/Localization Settings`。
  - `Tools/Validate Config` → **`Config/Validate Config`**（分组按**能力归属**，不按实现类型）；
    `Tools/{Show Dependents…, Clean Missing Asset…}` → `Tools/Assets/{Show Dependents, Clean Missing References}`
    （`Tools/` 只放与具体子系统无关的通用工具）。
  - 中文菜单名统一改英文：`BT/导出 Blob(打包用)`、`Tools/Tests/更新 Public API 快照`、
    `Tools/Tests/查看 Public API 差异` → 全部英文。
  - `Samples/` 下三个中文项**有意保留**——它们导入示例后才出现在用户工程，属"使用者的操作指引"，
    此时中文界面更合适，与主菜单取舍不同。
  - 新增 **`Editor/MenuLayout.md`** 作为菜单结构唯一真源，内含 priority 两次踩坑记录、三档设计意图、
    改动检查清单；`Documentation~/90-编辑器工具链.md` 补全带 pri 列的全量菜单表。
  - 同步订正 12 篇文档共 35 处菜单路径引用。**CHANGELOG 历史段落中的旧菜单名按历史事实保留**，
    不追溯改写。

> **Unity 菜单的硬限制**：菜单**不支持自定义分组标题**，只能画分隔线。
> 所以"分级"只能靠**位置语义**表达——这也是**排序比命名更重要**的原因。

### Added
- **静态闸门脚本（`scripts/verify-gate.sh`，新增）**：一次跑完四类"Unity 编译发现不了"的检查——
  索引文件数 / 类型数 / 分组和与磁盘一致、新增 `.cs` 都有 `.meta` 且无孤儿 `.meta`、
  tag 与 `package.json` 版本一致、主菜单路径全英文。纯 bash + python，**不需要 Unity**，
  提交前 3 秒出结果；CI 上每次 push / PR 自动跑。
  - **首版自身有两个 bug，均由反向验证抓出**：① 统计类型数时没剥离注释，
    XML 文档注释里的 `<c>class Foo</c>` 被计入（报 387 而实际 362，虚高 25）；
    ② 孤儿 `.meta` 分支从未被真正触发——第一次注入测试用的是"移走 `.meta`"（变成"缺 `.meta`"）
    而不是"造孤儿 `.meta`"（有 meta 无 cs）。
  - 因此确立一条流程要求：**每个检查项都要先注入一个错误、确认退出码非 0，再恢复**。
    只跑"正着过"证明不了检查是活的。当前四项检查均已双向验证。
- **CI 工作流（`.github/workflows/gate.yml`，新增）**：
  - `gate` job **启用**：Ubuntu + Python 3.12，跑 `verify-gate.sh`，无 Unity 依赖。
  - `unity-tests` job **停用**（`if: false`）：需Unity 许可密钥。Unity 个人版许可绑定桌面激活、
    **不提供 CI 密钥**，公开仓库的 Actions 上拿不到可用的 batchmode 许可；强行开启只会得到一个必红的 job。
    开启步骤写在 `Documentation~/95-CI与本地验证.md`。这不等于放弃——先把不依赖许可的部分全做掉。
- **CI 壳工程生成脚本（`ci/make-test-project.sh`，新增）**：本仓库是 **UPM 包**，没有
  `ProjectSettings/`，Unity batchmode 打不开——脚本生成一个最小壳工程（`ci/TestProject/`，已gitignore），
  以 `file:` 方式把包引进去。壳工程依赖版本从 `package.json` 的 `dependencies` 读取而非硬编码；
  Unity 版本以包根新增的 **`.unityVersion`** 为单一真源，并与 `package.json` 的 `unity` 字段交叉校验
  （版本猜错的后果是"编译报一堆看不懂的 API 错误"，比直接失败难查得多）。
  - 踩坑：Git Bash 下 `/e/...` 是 MSYS 虚拟路径，**`python.exe` 与 `Unity.exe` 都是原生 Win32 程序、
    收不到这个转换**。喂给 Python 的路径先 `cd` 进去用相对路径；写给 Unity 的用 `pwd -W` 转成 `E:/...`。
- **EditMode 测试 13 → 55 个用例**（4 个新文件）：
  | 文件 | 用例 | 钉住的契约 |
  | --- | --- | --- |
  | `SaveManagerTests.cs` | 16 | 存档三条本分保证：原子写（`.tmp`→`File.Replace`，旧版进 `.bak` 且内容是**上一版**）、坏档改名 `.corrupt_*` 绝不静默覆盖、格式版本不匹配按坏档处理。含"序列化抛异常时**不留正式文件**"——否则玩家下次读档拿到的是损坏存档而非"读不到存档" |
  | `LocalizationManagerAtomicCommitTests.cs` | 10 | `Reload` 失败 ⇒ 现有一字不动。**这是 2026-10-06 示例洗掉宿主 18 条key 那次雪崩的根因**（旧实现"先清空静态字段再读盘"）。三种坏输入（目录不存在 / null / 空目录）+ 失败时不广播 `onLanguageChanged` + 索引惰性重建自愈 |
  | `ConfigCodeGenWriteTests.cs` | 7 | `ConfigTemplateCodeGen.WriteIfChanged`：内容未变不重写（避免无谓重编译），换行差异视为变化 |
  | `BTManagerRuntimeStateTests.cs` | 6 | BT 运行态释放入口对"从未注册 / 默认 / 已释放"实体安全且幂等 |
  - 两个测试类通过**反射**直接摆 `LocalizationManager` / `SaveManager` 的静态字段（这两处没有注入/Reset API，
    构造合法 blob 成本远高于用例价值）。代价已写进代码注释：
    **`LocalizationManager` 新增静态字段时必须补进 `TrackedFields`**，否则用例跨用例污染。
  - ⚠️ 写涉及日志的用例时：`Log.Error` 走 `Debug.LogError`，Unity 测试框架遇到**未声明**的 Error
    会直接判失败——必须显式 `LogAssert.Expect`（本批 4 处）。
- **索引文档自检（`Tests/Editor/FileIndexAuditTests.cs`，新增文件）**：断言
  `Documentation~/100-文件与类索引.md` 声明的文件数 / 类型数 / 各分组之和等于实际扫描结果。
  只做文本解析与目录扫描、**不碰任何 `UnityEditor` API**，因此即使 CI 拿不到 Unity 许可，
  它仍能在纯 .NET 环境作为兜底闸门运行。首跑即抓出索引已过期（声明 208 / 345，实际 209 / 356），
  已据此更新文档——证明这类"文档腐化"确实在发生，不是假想。
- **tag 版本一致性校验（`scripts/verify-tag-version.sh`，新增）**：见上方勘误段。
  已双向验证：已登记的历史遗留 → 退出码 0；临时造一个不一致的新 tag → 正确报错。
- **World 生命周期钩子（`Runtime/FrameworkLifecycle.cs`，新增文件）**：进程级静态单例此前**没有任何释放时机**——
  `BTManager` / `ConfigManager` 等持有 `Allocator.Persistent` 原生容器，World 销毁时无人调用 `DisposeAll()`。
  新增 `FrameworkLifecycle.DisposeAll(reason)` 统一释放 BT / 黑板 / 事件总线 / 节点注册表 / 配置表，
  并挂两处钩子：`[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`（进 Play 前，
  兼容关闭 Domain Reload 的场景——此时 Unity 已回收原生分配但托管字典还在）与
  `FrameworkCleanupSystem`（`UpdateGroup_Root` 且 `OrderLast`，World 销毁时兜底）。
- **EditMode 测试程序集（`Tests/Editor/`，新增）**：`HYC.Framework.Tests.Editor.asmdef` 带
  `UNITY_INCLUDE_TESTS` 约束 + `autoReferenced: false`，不进播放器、不被使用方工程编译。
  首批用例覆盖两处最容易静默退化的契约：`AttributeKey` 的 FNV-1a 哈希（哈希会烘进生成的 `AttrKeyMap`，
  算法一改历史数据资产就静默变默认值）与 `ConfigBlobTable` / `ConfigManager` 的查询语义。
  运行入口：Unity **Window → General → Test Runner → EditMode**。
- **公开 API 签名守护（`Tests/Editor/PublicApiSnapshot*.cs`，新增）**：把 `HYC.Framework.Runtime` 与
  `HYC.Framework.Editor` 两个程序集的 public 类型与 public 成员签名序列化成
  `Tests/Editor/PublicApiSnapshot.txt`，由 `公开API签名_与快照一致` 测试比对。
  - **为什么需要**：框架被使用方以 `file:` 方式引用、**没有内嵌副本**，改框架即时生效且使用方无版本锁。
    任何"顺手改个签名"都会立刻打断使用方编译，而使用方只能自己撞到编译错误才知道。
    典型隐性破坏：把 `Foo(int)` 改成 `Foo(int, bool = false)` 源码级看似兼容，二进制签名已变。
  - **纳入快照的细节**：参数默认值也进快照（改默认值会静默改变已编译调用方的行为）；
    `ref`/`out` 前缀显式还原；`IsSpecialName` 的编译器生成成员（属性访问器）排除以免噪音；
    只收 public（含 protected 成员可见性标注），私有怎么改都不该制造噪音。
  - 配套菜单 `Tools/Tests/更新 Public API 快照` 与 `Tools/Tests/查看 Public API 差异`
    （差异明细输出到 Console，**刻意不引入外部 diff 工具 / `RevealInFinder`**——那两条路径框架内零使用、未经验证）。
  - 首次运行会自动生成快照并提示提交；之后有意改 API 就更新快照，无意改动则测试直接失败并列出差异行。
- **`Editor/Dlls/THIRD-PARTY-NOTICES.md`（新增）**：补齐第三方许可披露——
  NPOI 2.5.6.0（Apache-2.0）、SharpZipLib 1.3.3（MIT 变体）、BouncyCastle.Crypto 2.5.29.19（MIT + Legacy 双许可），
  版本号由 dll 元数据实读而非记忆。`package.json` 加 `thirdPartyNoticesUrl` 指针。
- **配置导出类改为注册表查找**：生成的 `XxxExport` 静态构造里 `Register` 自身，
  `ConfigExportService.FindExportType` 先查字典、未注册才做一次性程序集扫描（原先每个类型都扫一遍全程序集）。
- **`Scan Key Usage` 新增 `Packages/` 开关与扫描进度条**，并把"收集全工程引号字符串"
  改为"扫到即查表命中才记"（原先几十万条字符串字面量全进 HashSet）。

### Fixed（2026-10-07 框架审计）
> 本轮全框架审计发现的修复项，按严重度排序（🔴 为数据丢失/内存泄漏级）。

- **🔴 实体销毁时 BT 原生内存泄漏（审计未捕获，由核实实体销毁路径时发现）**。
  `BTManager._nodeStates` 里每个 `(实体, 树ID)` 持一份 `Allocator.Persistent` 的 `NativeArray`，
  原本**唯一**的释放路径是「树跑完（结果 != Running）时调 `ClearNodeStates`」。
  但实体可能在树仍 Running 时被销毁——典型如「旧实体被销毁并由新实体取代」
  正是这种情形：`RunningBT` 组件随实体消失 → Query 不再返回它 → 那条释放路径**永不执行** →
  每死一个敌人漏一份 Persistent 分配；实体私有黑板（10 个 Native 容器）同理。
  修法：`BTInterpreterSystem` 用两份 `NativeHashSet<Entity>` 交替记住「上一帧 / 本帧」活跃实体，
  帧末发现「上一帧有、本帧没有」即调新增的 `BTManager.ReleaseEntityRuntimeState(entity)`
  （子树态 + per-node 态 + 守卫锁 + 黑板 + 事件，一次清干净）；`OnDestroy` 另加
  `BTManager.ClearAllRuntimeStates()` 兜 World 销毁。
- **`SaveManager` 的"原子写"名不副实**：原实现是 `File.Delete(path)` + `File.Move(tmp, path)`，
  两步之间崩溃则新旧档同时丢失。改为 `File.Replace(tmp, path, bak)` 真正原子替换并留 `.bak`；
  `PlatformNotSupportedException`（WebGL 等不支持 `File.Replace`）时退回"先备份再移动"。
- **`LocalizationManager.Reload` 读盘失败会永久丢表**：原实现先清空全部字段再读盘，
  目录不存在 / 读到 0 条就再也回不去了（10-06 本地化事故的根因）。改为"先读进局部变量，
  全部成功才原子替换字段并 `RefreshAll()`"；读不到 `mSensitiveWords` 时保留原值。
- **`BTNodeRuntimeRegistry` 的静默失败与静默覆盖**：`catch { }` 改为 `LogError` 并点名类型
  （实例化失败会让该节点在所有树里永久 Failed 却毫无线索）；`asm.GetTypes()` 区分
  `ReflectionTypeLoadException` 与其他异常；裸键 `_bySubType` 冲突时 `LogError` 而非"最后一个赢"
  （原症状：AI 树的动作被子类型相同的技能树节点执行）。
- **`BTGraphWindow` 恢复失败时每帧重试**：`_current` 为空时每帧 `LoadAllTrees()`（全量 `FindAssets`）
  且失败则每帧 `CreateInstance`。加 `MaxRecoverAttempts = 3` 上限，超过即停止并报错。
- **`BTGraphIMGUI` 连线绘制的三重 O(n²)**：连线绘制循环里的 `List.Find`（O(连线×节点)）、
  `ChildIndex` 的线性扫连线（O(连线²)）、每节点一次 `Connections.Count(linq)`，
  改为按「节点数/连线数」作指纹惰性构建的字典索引，全部降为 O(1)；顺带删掉零调用的
  `IsConnectedIn`/`IsConnectedOut` 死代码。
- **`ConfigExportService` 硬编码 `"Assembly-CSharp-Editor"`** 去反射：一旦使用方把生成代码放进自定义
  asmdef 就定位不到导出类，且旧兜底只按**短名**匹配，会命中其它工程/命名空间下的同名类并静默导出错表。
  改为注册表优先 + 一次性全程序集扫描（按 `Type.FullName` 全名匹配），扫描结果整域会话缓存。
- **代码生成每次都无条件重写文件**：一次生成要产出六七个 `.cs`，无条件 `WriteAllText` + `ImportAsset`
  会白白触发一串重新导入（`.meta` 时间戳刷新、编译抖动、版本库无意义 diff）。
  新增 `ConfigTemplateCodeGen.WriteIfChanged`（内容一致则跳过写入与导入），7 个生成点与枚举生成器共用；
  校验路径改用 `LoadAllTemplates(ensureFolder: false)`，**不再为了一次校验在磁盘上留下目录**；
  className 唯一性判断从两处重复实现收敛到 `FindClassNameConflict`。
- **`UIManager.Close` 不清理窗口态**：`ViewStates` / `FocusedWindow` / `SingleUISystem`
  对已关闭窗口的引用永不释放（每开关一次窗口泄漏一份 `WindowState`）。
  `Close` 末尾补 `ForgetWindowState(window)`，`OnDestroy` 补 `ViewStates.Clear()`。
- **`BTInterpreterSystem` 缺 `[UpdateInGroup]`**：系统落在 Root 组根级，
  相对 A0~B9 整条阶梯的执行顺序取决于 Unity 自动排序——改别的系统的 `Update` 属性就可能悄悄变顺序，
  且无任何编译期提示。已显式落到 `UpdateGroup_A3`（Gameplay，与技能/战斗同梯队）。
- **`HotkeyManager.BindAsset` 重复订阅** `InputSystem.onActionChange` → 事件重复触发。补 `-=`。
- **`DataModel` 的 `NewItemFlagSystem` 每帧 Temp 分配未释放**：`ToEntityArray(Allocator.Temp)` 后
  `if (entities.Length == 0) return;` 直接返回，跳过了 Dispose。补 `try/finally`，
  并加 `RequireForUpdate(_query)` 使无标记时整帧跳过。
- **`BTManager.DisposeAll` 漏清 `_guards` / `_breakpoints`**：守卫锁是进程级字典、
  断点集合跨 Play 会串。补齐，并改为幂等（重复调用安全）。
- **包内示例（Samples~）与一键导入**：新增 `Samples~/HYCSimple/`（带 `~`，Unity 不编译；导入后位于
  `Assets/Samples/com.hyc.framework/<版本>/HYCSimple/`）作为框架接入参考——当前含**属性 Attr** 全链路示例
  （`GameAttrKey` / `GameAttrKeyMap` / `GameAttrBag` 三件套 + 自包含 `HYCSimpleAttrDemoSystem` + `AI-NOTES.md`）。
  - `package.json` 新增 `samples` 声明（`path: Samples~/HYCSimple`）——这是 Samples 能在 Package Manager
    显示、且 `Assets/Samples/<包名>/` 能被导入的**必要条件**（缺它时 Unity 把该路径当 PM 保留区，
    `Refresh` 到 `<包名>` 一层即停，Project 窗口展开为空）。
  - 新增编辑器菜单 `HYC Framework/Samples/Import HYCSimple`（`Editor/Samples/HYCSimpleSampleImporter.cs`）作快捷方式：
    **每次全量重拷**（先删旧副本，避免「上次只导了 Attr、这次补了其余块」时漏拷/残留）`Samples~/HYCSimple` →
    **`Assets/HYCSimple/`**，再 `ImportAsset(ImportRecursive)` 强制导入并**自检是否真的进了 AssetDatabase**。
    - ⚠️ 落点刻意**不是** `Assets/Samples/<包名>/`：那是 Package Manager **保留区**，包未被解析时 Unity 扫到 `<包名>`
      一层即停、不导入其内容，Project 窗口展开为空且不报错（官方文档点名的 "samples field in the package manifest is empty"）。
      官方落点仍走 Package Manager 的 **Samples → Import**（新增 `samples` 声明后需重启 Unity 重新解析）。两路**二选一**，避免类型重复定义。
  - 本次补齐 **Config / BT / Loc / Scene** 四块接入示例（此前仅有 Attr），每块均配 `AI-NOTES.md`
    （心智模型 / 端到端步骤 / 雷区 / 验证清单），命名框架中立、自包含可编译：
    - `Config/`：`HycSimpleItemRow`（blittable 行，等价于生成器产物）+ `HYCSimpleConfigDemoSystem`
      （代码造表 → `ConfigManager.Register` → `Get/TryGet` 读取）。
    - `BT/`：`HYCSimpleBTDemoSystem`（运行期 `BTBlobLoader.LoadAll` + 挂 `RunningBT` 让
      `BTInterpreterSystem` 驱动）+ `Editor/HYCSimpleBTTreeBuilder`（菜单程序化建树并 `BTBlobExporter.ExportAll` 导出 Blob），
      重点讲清「编辑器自动注册 vs 真机导出 Blob」打包链路。
    - `Loc/`：`HYCSimpleLocSampleData`（用框架同款 API 运行时造最小多语言 blob，规避 `BlobBuilder.AllocateString` 的 CJK 截断）
      + `HYCSimpleLocDemoSystem`（`Reload`/`GetText`/`SetLanguage`/`HasKey`）。
    - `Scene/`：`HYCSimpleSceneBootstrap`（MonoBehaviour，Start 时按 A1 数据阶段顺序串联 Loc/BT/Config）。
- **天赋树：本地化 Key 字段显示翻译内容，且改用 Unity 原生下拉**：弹出字段主显示改为当前语言翻译
  （`LocAccess.GetText`），原始 key 作为暗色副行；`LocKeyPickerWindow.OpenDropdown`（基于 `AdvancedDropdown`）
  的叶子项也带翻译预览。选择控件用 `EditorGUI.DropdownButton` + `EditorStyles.popup`，与 Unity 原生
  枚举/层级下拉同皮（此前是自绘琥珀色按钮 + 自定义窗口，观感不统一）。
- **天赋树：节点改为 ScriptableObject 子资源**：`TalentNode` 由 `[Serializable]` 普通类改为 `ScriptableObject`，
  挂在 `TalentTreeAsset` 下（`AssetDatabase.AddObjectToAsset`）。现在能在 Project 窗口展开树资产、把
  **单个节点**直接拖进 UI 的 slot（口子）。画布增删/复制/粘贴改用 `ScriptableObject.CreateInstance`
  + `Undo.RegisterCreatedObjectUndo` / `Undo.DestroyObjectImmediate`；未落盘临时树的节点在保存时由
  `TalentTreeWindow` 统一补 `AddObjectToAsset` 落定。
- **天赋树运行期基类（解锁 / 点亮 / 可点逻辑）**：
  - `Runtime/Talent/TalentRuntime.cs`：`TalentRuntime` 状态机（`IsUnlocked` / `CanUnlock` /
    `TryUnlock` / `SetUnlocked` / `UnlockCost`）+ `ITalentResourceProvider` 资源接口（框架只给规则，
    点数由游戏侧接入）+ `TalentCostView` 快照结构。
  - `Runtime/Talent/TalentTreeUIBase.cs`：UGUI UI 基类，`treeAsset` + `slots`（口子，
    `TalentNodeSlot.node` 拖节点进来）+ `Initialize` / `Refresh` / `ApplySlot`(virtual) / `RequestUnlock`。
- **天赋树「生成代码」一键产出 prefab + 管理代码**：工具栏「生成代码」按钮（或 `TalentGenerateWizard`）
  → 先 `TalentTreeValidator.Validate` 拦截 → 生成三件套到可配置目录（默认 `Assets/Talent/Generated/`）：
  - `<前缀>TalentController.cs`（逻辑代码，继承 `TalentRuntime` 并 implements `ITalentResourceProvider`，
    `#region USER` 里接点数）、
  - `<前缀>TalentUI.cs`（UI 代码，`TalentTreeUIBase` 子类，`#region USER` 里写 `ApplySlot`）、
  - `<前缀>TalentTree.prefab`（基类组件 + `treeAsset` 指好 + `slots` 按节点预填）。
  文件内 `#region GENERATED` 块重生成会被覆盖、`#region USER` 保留（幂等）。
- **通用属性配置系统（Attr，ECS 版）**：框架级、配置表驱动、per-entity 的基础属性能力，
  不绑定具体属性名 / 公式 / buff（按需由使用方扩展）。
  - `Runtime/Attr/AttributeType.cs`：`enum AttributeType`（Int / Float / Long / Bool），决定
    `AttributeValue.raw` 的解释方式（`AttributeValue.cs` 的 `raw` 为统一载体，Float 存 IEEE754 bits）。
  - `Runtime/Attr/AttributeValue.cs`：`struct AttributeValue : IBufferElementData`，挂在实体上的
    属性袋元素；`keyHash` 用属性名 FNV-1a 哈希（与 BT 黑板同算法、独立实现，不跨模块引用 BT），
    用名哈希而非枚举序号，保证属性增删/重排时运行时袋数据不串。
  - `Runtime/Attr/AttributeKey.cs`：`AttributeKey.Hash(string)` —— FNV-1a 64 位哈希。
  - `Runtime/Attr/IAttributeDef.cs`：属性目录行（`AttributeDef` 配置行）与框架的接缝接口
    （`Key` / `ValueType` / `DefaultRaw` / `MinRaw` / `MaxRaw`）。使用方用配置管线生成的配置行去
    实现它，框架只读接口，零耦合不认识 "HP" / "ATK" 等具体属性名。
  - `Runtime/Attr/IAttrBag.cs`：类型安全的属性读写契约（ulong key 版），Job/Burst 外使用；
    Job/Burst 内直接操作 `DynamicBuffer<AttributeValue>` + `AttributeStore` 的 ulong 重载。
  - `Runtime/Attr/AttributeStore.cs`：基于 `EntityManager` 的主线程读写 API，按 `keyHash` 线性查找
    （几十个属性内足够）；找不到 key 时 Get 返回调用方默认值、Set 为 no-op（袋即声明过的属性集合，不自动扩容）。
  - `Runtime/Attr/AttributeInitializer.cs`：`Init<T>(em, e, List<ConfigRowRef> refs)`
    把"实体引用了哪些属性"（编辑器勾选得到的 `ConfigRowRef` 列表 = **数据**，不生成代码）初始化成
    `DynamicBuffer<AttributeValue>`；默认值按 `[MinRaw, MaxRaw]` 钳制（Float 按 double 数值钳制后回存
    bits），运行期 `Set` 不再钳制（袋即运行时真相，配置边界只约束默认值）。
  - `Runtime/Attr/AttrBag.cs`：`readonly struct AttrBag : IAttrBag`，包 `(EntityManager, Entity)`，
    转调 `AttributeStore`。使用方 codegen 的枚举版 `AttrBag` 在内部把枚举经哈希映射后转调此处。
  - **codegen 便利层（使用方基于属性目录生成，不进框架）**：`enum AttrKey`（int 序号，加属性同步增长）
    + `AttrKeyMap.Hash[]`（枚举序号 → FNV-1a 哈希）+ 枚举版 `AttrBag`（实现 `IAttrBag`，暴露
    `GetInt(AttrKey.ATK)` 这类友好重载）——零手写 key、IntelliSense、拼错编译期报错。
  - 交互原型：`Documentation~/prototypes/attribute-config-3.html`（编辑 + 枚举 `IAttrBag` 用法演示，jsdom 冒烟通过）。
  - **通用属性配置系统（Attr）四大优化（单一清单 / 代码生成 / 预设自动初始化 / 编辑器校验）**：在核心
    8 文件基础上补齐"更少手写、更多生成、更易用"的工具链（用户选定四个方向全做）：
    - **单一属性清单（B）**：`Runtime/Attr/AttributeCatalog.cs` —— `AttributeCatalog : ScriptableObject`
      （菜单 `HYC Framework/Attributes/Attribute Catalog`）持有 `List<AttributeCatalogEntry>`
      （key / type / defaultRaw / minRaw / maxRaw），成为所有派生产物的**单一真源**；`ToValue()` 把一条目
      烘成 `AttributeValue`（FNV-1a keyHash + 按 [min,max] 钳制）。
    - **内置代码生成器（A）**：`Editor/Attr/AttributeCodeGenerator.cs` + `AttributeGenerateWizard.cs`
      （`ScriptableWizard`，独立文件）→ 由目录生成三件套到 `Assets/HYC/Generated/Attributes/`：
      `<prefix>AttrKey.cs`（`enum`，序号 = 目录顺序）、`<prefix>AttrKeyMap.cs`（`static ulong[] Hash`
      由 `AttributeKey.Hash` 算出、`AttributeType[] Type`）、`<prefix>AttrBag.cs`（实现 `IAttrBag`，
      暴露 `GetInt(AttrKey.ATK)` 这类友好重载，零手写 key、IntelliSense、拼错编译期报错）。完全生成、
      无 `#region USER`；`AttributeCatalogEditor` 内「生成属性访问器」按钮与菜单
      `HYC Framework/Attributes/Generate Accessor` 打开向导，`OnWizardUpdate` 先过 `AttributeValidator`。
    - **预设 + 自动初始化（C）**：`Runtime/Attr/AttributeSetAsset.cs`（`AttributeSetAsset : ScriptableObject`，
      菜单 `HYC Framework/Attributes/Attribute Set`）勾选目录条目、`OnValidate` 调 `Rebuild()` 把所选
      属性烘成 `List<AttributeValue>`；`AttributeSetRef`（托管 `IComponentData`，持 `asset` 引用）挂在实体上；
      `AttributeInitSystem : SystemBase` 每帧查「有 `AttributeSetRef` 但无 `AttributeValue` buffer」的实体，
      `AddBuffer` 后把预设值灌入，免去手写初始化循环。
    - **编辑器 + 校验（D）**：`Editor/Attr/AttributeCatalogEditor.cs` / `AttributeSetAssetEditor.cs`
      （`[CustomEditor]`）提供条目管理与「勾选即加属性」UI；`AttributeValidator.Validate` 返回
      `List<AttributeIssue>`（空 key / 重名 / 非法 C# 标识符 / min>max / 默认值越界 / Float bits 为 NaN·Inf），
      向导与目录编辑器共用。
    - **独立编辑窗口（B 的可视化入口）**：`Editor/Attr/AttributeCatalogWindow.cs`（`EditorWindow`），
      菜单 `HYC Framework/Attributes/Attribute Catalog Editor` 打开，自动绑定选中 / 首个 `AttributeCatalog`，
      可就地编辑 `entries` 并触发「生成访问器」「校验目录」；与资产 `Inspector` 编辑器并存。
      窗口顶部两个分页：**`属性目录`**（类型感知表格：Float 显示小数 / Bool 勾选框 / 整数，行首 ≡ 拖拽重排、
      行尾 ✕ 删除、底部「＋ 添加属性」）与 **`属性组`**（组装 `AttributeSetAsset`：左列工程内全部组资产、
      右列从目录勾选 key 并即时 `Rebuild()` 烘焙 `values`，「＋ 新建属性组」自动绑定当前目录）；
      复用 `AttributeSetAssetEditor` 的勾选逻辑，与资产 `Inspector` 行为一致。
    - **解耦（D）**：`ConfigRowRef` 由 `HYC.Framework.Talent`（`TalentTreeAsset.cs` 内联类）**迁至
      `Runtime/ConfigRowRef.cs`（`HYC.Framework.Config`）**，消除 `Attr → Talent` 的反向依赖；
      `TalentTreeAsset.cs` / `TalentGraphIMGUI.cs` 加 `using HYC.Framework.Config;` 并移除内联定义。

### Fixed（示例加固，2026-10-06 血泪案）

- **🔴 示例会把宿主工程的本地化表整张洗掉（`HYCSimple` 示例侧安全加固）**。
  `LocalizationManager` 是**进程级静态单例**，而 `Reload(folder)` 的第一步是把旧状态全部清空
  （`mIDs/mLangs/mTexts/mLang2Index/mKey2Index = null`）再按目标目录重填。宿主工程
  通常在 `[RuntimeInitializeOnLoadMethod]` 阶段已从 `StreamingAssets/Localization` 加载了自己的 key 表，
  而 `HYCSimpleLocDemoSystem` 作为普通 `SystemBase` 会被自动拉进 World，第一次 `OnUpdate` 就
  `Reload` 到示例自己的 3 条数据 —— 宿主整张表当场丢失，现象是**主菜单全线 `未找到Key : xxx`**，
  唯独 `LocalizedBase.Key` 传空（走 `if (!string.IsNullOrEmpty(Key))` 守卫跳过刷新）的按钮保留硬编码文本；
  又因为示例每帧兜底，宿主**重导多少次本地化都会被下一帧洗掉**，极难定位。
  加固三处：
  - 4 个示例 System（`Loc` / `BT` / `Config` / `Attr` 的 `*DemoSystem`）全部加 `[DisableAutoCreation]`，
    不再自动进 World；改由 `HYCSimpleSceneBootstrap`（MonoBehaviour，用户显式挂场景才生效）
    用 `world.GetOrCreateSystemManaged<T>().Update()` 手动驱动，并支持逐项开关。
  - 新增 `HYCSimpleLocSampleData.CanTakeOver()`：全局表非空且不含示例 key → 判定为宿主数据，
    **跳过 `Reload`** 并打黄色警告；`EnsureLoaded()` 改为返回 `bool`（是否接管），
    调用方据此决定能否调用 `SetLanguage`（不接管时不改宿主语言）。
  - `README.md` / `Loc/AI-NOTES.md` 补「示例默认不自动运行」与雷区 #8（含上述现象的最小复现特征），
    并新增「宿主回归」验证项。
- **示例树 TreeId 撞车宿主常用 Id**：`HYCSimpleBTDemoSystem` / `HYCSimpleBTTreeBuilder` 的示例树 Id
  由 `1001` 改为 **`900001`**（`1001` 是 Player/第一关/第一棵树的高频业务 Id，示例导出会覆盖宿主自己的
  `1001.blob`）；`BT/AI-NOTES.md` 新增雷区条目并同步验证清单为 `900001.blob`。
- **菜单路径统一：BT「导出 Blob(打包用)」由顶层 `Tools/HYC/BT/…` 迁到 `HYC Framework/BT/…`**。
  全框架 23 个菜单项里 22 个都在 `HYC Framework/<分类>/…` 下，唯独这一项残留在早期的 `Tools/HYC/…`
  命名空间，造成「同一个 BT 模块，编辑器在 `HYC Framework/BT/`、导出却在 `Tools/HYC/BT/`」的割裂。
  2.0.6 进一步统一为**全英文命名** **`HYC Framework/BT/Export Blob (For Build)`**
  （`Editor/BT/BTBlobExporter.cs` 的 `[MenuItem]`）；
  同步订正 `Runtime/BT/BTBlobLoader.cs` 两处提示文案、`manifest.txt` 注释、包 `README.md`、
  `Samples~/HYCSimple/BT/AI-NOTES.md`，以及 `Documentation~/01`、`Documentation~/02`、`Documentation~/80`~`84`、`Documentation~/90` 的菜单表；
  顺带订正 `Documentation~/80` / `Documentation~/81` / `Documentation~/84` 里把 BT 编辑器误写成 `Tools/HYC/BT Editor` 的旧错
  （正确路径一直是 `HYC Framework/BT/BT Editor`）。
- **天赋树：节点子资源无法持久化（`TalentNode` 类名与文件不匹配）**。Unity 只给「与文件同名的类」
  生成 `MonoScript`，而 `AssetDatabase.AddObjectToAsset` 写子资源时必须解析到它——`TalentNode` 原先
  定义在 `TalentTreeAsset.cs` 里，导致写出的节点子资源以 "missing script" 落盘、重新导入后变成 `null`
  （现象：树里节点全没了、连线全悬空、左栏计数还在）。已把 `TalentNode` 移到独立文件
  `Runtime/Talent/TalentNode.cs`。
- **天赋树：旧格式树资产不再是"一屏悬空连线"**。2.0.6 之前保存的树，`nodes` 是内联数据，改子资源后
  读不出来。现在校验器明确提示「节点数据已失效」，画布上给出说明 +「清理失效节点」按钮，
  左栏节点计数改为「有效/总数 ⚠」。
- **天赋树：修掉"新建节点后画布依旧空白、minimap 却看得到节点"**。根因：`ZoomToFit()` 在一棵
  **节点全为 null**（旧格式失效）的树上运行时，min/max 停在 `±float.MaxValue`，算出
  `Pan ≈ ±3.4e38`、`Zoom = 0.25`；此后 `ScreenToCanvas` 与视口裁剪
  （`NodeRect(n).Overlaps(_visibleCanvasRect)`）双双崩坏，任何新节点都被判为不可见。
  修复：① `ZoomToFit` 只统计有效节点、无有效节点时复位视图而非算出垃圾 Pan；② 新增
  `SanitizeViewAndPositions()`，每帧自愈——把离谱的 `Pan/Zoom` 复位、把离谱的节点坐标重排到网格
  （已损坏的旧资产打开即自动修复）；③ `AddNode` 落点增加兜底，视图被污染时先修复再重算；
  ④ 「清理失效节点」后顺手自愈并重新取景。
- **天赋树：「生成代码」对话框报「没有可生成的天赋树资产」**。根因同 `TalentNode` 那条——文件名规则：
  `TalentGenerateWizard : ScriptableWizard` 原先挤在 `TalentCodeGenerator.cs` 里，Unity 无法为其生成
  `MonoScript`（窗口顶部 `Script` 槽显示 `None (Mono Script)`），导致窗口字段无法序列化、`asset` 丢失。
  已拆到独立 `Editor/Talent/TalentGenerateWizard.cs`，并在 `OnWizardUpdate` 加静态兜底
  （`asset == null` 时从 `_pendingAsset` 恢复）。

## [2.0.5] - Unreleased

### Added
- **存档存储（`Runtime/Save/`）**：按**类型**存取可序列化对象，是配置（只读 Blob）的可写 counterpart。
  - `SaveManager`（namespace `HYC.Framework.Save`，静态）：`Save<T>(data)` / `Get<T>()` /
    `GetOrCreate<T>()` / `Has<T>()` / `Delete<T>()` / `DeleteAll()` / `Init(SaveOptions)`。
    类型即键，文件落在 `Application.persistentDataPath/Save/<类型全名>.dat`。
  - `ISaveSerializer` + 两个实现：`NewtonsoftSaveSerializer`（默认，支持 Dictionary / 多态 /
    顶层 `List<T>`，构造可传 `prettyPrint`）与 `JsonUtilitySaveSerializer`（零依赖备选，
    但不支持 Dictionary / 多态 / 顶层 `List<T>`）。
  - 存储层本分的三条保证：**原子写**（先落 `.tmp` 再换名，写一半崩不留半截文件）、
    **坏档改名 `.corrupt_<时间戳>` 备份绝不覆盖**、**格式版本写在文件里**（`SaveEnvelope.formatVersion`，
    不匹配时按坏档处理）。读写即时落盘、无内存缓存。
  - 明确**不做**的事（属表现层 / 玩法层，归使用方）：存档槽、列表界面、摘要、自动保存时机。
    想要多档就自己存 `List<T>` 或自己定义类型。
  - 依赖新增 `com.unity.nuget.newtonsoft-json@3.2.1`（写进 `package.json` 与 Runtime asmdef references）。
  - 文档：`Documentation~/52-存档存储.md`（架构 / 关键类 / 用法 / 9 条坑）。
- **天赋树模块（Talent）**：可视化编辑天赋树 + 运行期只读查询，**黑板属性引用配置表**。
  - `Runtime/Talent/TalentTreeAsset.cs`：`TalentTreeAsset`（ScriptableObject）+ `TalentNode` /
    `TalentLink` / `TalentCost` / `TalentAttribute` / `ConfigRowRef`。
    **节点没有分类**——天赋节点不像行为树需要 Selector / Sequence / Action 那类功能性区分，
    图上每个节点的区别只在「叫什么名字」和「消耗什么」，所以 `TalentNode` 只有
    名称 / 消耗 / 最高等级 / 编辑器坐标，没有玩法语义的枚举。
    `Runtime/Talent/TalentTree.cs`：`TalentTree` 只读视图（`Build` 建索引、`GetNode` /
    `PredecessorsOf` / `SuccessorsOf` / `Validate` 运行期自检）。
  - **资产刻意放在 Runtime 程序集**：行为树的 `BTTreeAsset` 在 Editor 程序集，打包后一棵树都拿不到，
    不得不补一条「导出 Blob → 运行期读回」的链路，还落了个「编辑器能跑 ≠ 真机能跑」的坑。
    天赋树数据量小、查询不频繁，直接序列化即可，打包即用，不需要那条链路。
  - **属性来自配置表**：`TalentAttribute.key` 是树内唯一符号，`sourceRow`（`typeName` +
    `ConfigId.id`）指向配置表某一行。天赋树不认识"力量"是什么，只认识 key。
  - **图必须是有向无环图**：编辑器拉线时做环检测直接拦截，校验器与运行期 `Validate` 都会查。
  - `Editor/Talent/TalentGraphIMGUI.cs`：**画整个编辑器**，不止画布——工具栏(21px) / 左栏资源面板(210px) /
    画布 / 右栏分段面板(262px) / 底栏校验(88px) / 右下 minimap，逐块与 BT 编辑器对齐
    （同一个 `DrawGUI` 顺序、同一套常量、同一套手绘滚动条）。节点几何沿用垂直流
    （上方入 / 下方出、贝塞尔切线沿 Y 轴），含视口裁剪、平移缩放、框选、右键菜单、错误节点描红、
    BFS 分层自动布局。
  - `Editor/Talent/TalentTreeWindow.cs`：菜单 `HYC Framework/Talent/Talent Tree Editor`。
    **薄壳宿主**——只建一个 `VisualElement` + `IMGUIContainer(() => _graphIMGUI.DrawGUI())`，
    一个像素都不画。含资产被删后的 `Update()` 守卫（行为树编辑器当年漏过，删完树整屏空白无人恢复）。
  - **外观与 BT 编辑器共用一份样式**：不另建 `TalentStyles`，直接 `using HYC.Framework.BT.Editor;`
    复用 `BTEditorStyles`（调色板 / 圆角盒 / 圆点 / 贝塞尔 / 画布底色）。改一次两边同时生效，
    "两个编辑器看起来一样"是结构上的一样，不是抄一遍数值。
  - `Editor/Talent/TalentTreeValidator.cs`：节点 id 重复 / 悬空连线 / 环 / 孤立节点 /
    消耗引用了黑板上没有的属性 / 属性没绑配置行 / 属性无人使用 / treeId 跨资产冲突。
    节点没有分类概念，因此不做"起点数量"之类的检查——多入口是合法的。
  - `Editor/Talent/TalentConfigPicker.cs`：**独立居中的**配置行拾取窗口（560×600），扫描带
    `[CfgAsset]` 的类型实例，复用框架已有的 `ConfigIdService.TryReadId`，不自己反射 ID 字段。
    含搜索框、配置表分类筛选、↑↓/Enter/Esc 键盘导航、行尾资产路径。
  - 文档：`Documentation~/53-天赋树.md`；视觉原型 `Documentation~/prototypes/talent-tree-editor.html`。
- **行为树打包支持（补齐缺口）**：`BTTreeAsset` / `BTBlobBuilder` 位于 Editor 程序集，且 `BTAutoRegisterOnPlay`（进 Play 自动注册）是编辑器钩子——打包后两者都不存在，真机拿不到任何树。现补上与配置管线（`StreamingAssets/ConfigBlob` + `TryRead`）同构的打包路径：
  - `Editor/BT/BTBlobExporter.cs`：菜单 `Tools/HYC/BT/导出 Blob(打包用)`，把全工程树资产导出为 `Assets/StreamingAssets/BTBlob/<treeId>.blob` + `manifest.txt`（导出前清空旧产物，避免删树/改名后残留）；`ExportAll(dir)` 可供构建流程直接调用。
  - `Runtime/BT/BTBlobLoader.cs`：`LoadAll()` / `Load(treeId)`，`BlobAssetReference<BTRootBlob>.TryRead` → `BTManager.Register`；清单缺失时回退扫描目录；检测到 `jar:`（Android APK 内）路径时给出明确报错而不是静默失败。
  - `BTBlobBuilder.BuildToFile(asset, path, version)`：把树资产直接写成 Blob 文件，与 `Build` 共用同一段构建逻辑（`Fill`），两条路产物字节一致。
- 文档：`82-BT-运行时与Blob` 新增「七、打包：导出文件 → 运行时读回」（含格式版本、Android StreamingAssets 限制与绕法）；`80-行为树-总览` 补打包链路图、菜单入口与「编辑器能跑 ≠ 真机能跑」注意事项。
- **配置 / BT 面板：保存并导出 + 全量清理后全量导出**：
  - `Editor/DataEditor/ConfigExportService.cs`：新增 `SaveFolder(folder)`（递归 `SetDirty` + `SaveAssets`，跳过 `ConfigTemplate` 与非本命名空间 SO）与 `CleanAndExportAll(client, server)`（先清空 `ClientExportDir` / `ServerExportDir` 下的 `*.blob`，再存盘后逐类型 `ExportAll`，返回成功导出的类型数）。
  - `Editor/DataEditor/ConfigDataTree.cs` + `ConfigDataWindow.cs`：数据编辑器新增根目录右键「全量清理后全量导出」与工具栏「全量导出」按钮，配置文件 / 文件夹右键新增「保存并导出」。
  - `Editor/BT/BTBlobExporter.cs`：新增 `ExportFolder(folder)`（递归该目录下全部 `BTTreeAsset`，先存盘再导出）与 `ExportTrees(trees)`（只覆盖这批 blob，不清空目录，导出后 `SyncManifest` 重建清单）；`TreeTypeFilter` 由 private 改 public。
  - `Editor/BT/BTGraphIMGUI.cs`：BT 编辑器 File 菜单新增「全量清理后全量导出」，单棵树 / 文件夹右键新增「保存并导出」。
- **文档：补齐枚举配置（此前该能力无文档）**：新增 `Documentation~/13-枚举配置.md`——枚举定义资产 → 生成 C# 枚举类 → 配置字段引用的完整机制、`ValueOf` 的 **1 基**（`index+1`）与 Flags（`1<<index`）数值约定、**配置类字段是枚举类型而 Blob 字段是 `int`** 这一最易误判点、校验规则与 9 条踩坑（含从 0 基手写枚举迁移要 `+1`、脚本化 `EnsureEnumDefs()` 必须排在 OutputDir 清理之后）。`11-字段类型与导出设置` 的 `Enum` 行、`Documentation~/README.md` 索引、`90-编辑器工具链` 同步更新。
- **文档：新增 `Documentation~/02-操作手册-按任务.md`（处方型，补齐「读完知道怎么做」的缺口）**：现有各篇都是「机制说明」（有什么、是什么），缺「要完成 X 按顺序做这些」。02 篇按任务组织——新增配置表（**脚本驱动 / 编辑器驱动两条路**，此前只讲了编辑器一条，而多数使用方工程都是脚本驱动）、字段类型与导出模式决策表、枚举字段步骤、运行期读配置、行为树从零到跑通（含 `BTInterpreterSystem` 是 struct、须用 `GetOrCreateSystem<T>()`）、新增 UI 窗口、**基类 / 接口一览表**、「改完必须重跑什么」清单、菜单路径速查。另含两条易漏铁律：UI 类必须写 `partial`、引用 `UpdateGroup_*` 必须 `using HYC.Framework.Dots;`。
- **文档订正（菜单路径此前写错，会直接误导 AI）**：行为树编辑器实际是 `HYC Framework/BT/BT Editor`，`01` / `90` 两篇误写为 `Tools/HYC/BT Editor`（已订正）；`01` 篇列出的 `HYC Framework/Config/Generate Config` **并不存在**（已移除）；`90` 篇补上遗漏的 `HYC Framework/Localization/Editors/Scan Key Usage` 与两个 UI Binder 生成变体。全部菜单路径已逐条比对源码 `[MenuItem]`。

### Changed
- `BTBlobBuilder` 内部把「结构校验」与「构建填充」抽成 `Validate` / `Fill` 两个私有方法，供 `Build` 与 `BuildToFile` 共用。**对外 API、校验规则与产物行为完全不变**（纯内部重构，非破坏性变更）。

### Fixed
- **天赋树：节点「本地化 Key」改成只能选、不能手填**：原先是个自由输入框，key 拼错了编译期和
  校验器都发现不了，要到运行期界面空白才暴露。现在这一行**没有输入框**，只有一个按钮 +
  复用配置数据编辑器同一套 `LocKeyPickerWindow`（折叠多级菜单 + 顶部「搜索 key…」）。
  按钮三态配色：未选 = 琥珀、已选 = 正常（tooltip 显示当前语言翻译）、
  已选但表里查不到 = 红；右侧 `×` 或右键清空。
  ⚠️ 只有在 `LocAccess.GetKeys()` 确实非空时才判定"key 不存在"——本地化数据没导入时不乱标红。
  顺带修掉一个编译错误：`DrawBlackboardSection` / `DrawNodeSection` 用了 `Event` 却没接 `Event e`
  参数（CS0103），调用链 `DrawGUI → DrawRightPanel(Event e) → DrawXxxSection(x,y,w)` 中间把事件断了。
- **天赋树编辑器：点左栏的树，右边画布什么都不显示（整窗重写）**：三处叠加——
  ①**换树不换实例**：`Pan` / `Zoom` / 选中集都是 `TalentGraphIMGUI` 的实例字段，
  换树只调 `SetAsset()` 会带着上一棵树的缩放平移去画新树，新树布局在原点附近时画布看起来就是空的；
  ②**没有自动适配**：切过去后没有 `ZoomToFit`，节点在视口外；
  ③**界面分家**：原先窗口画 chrome（GUILayout 三栏）、画布类只画画布，两边各存一份布局常量。
  修复：按 BT 编辑器的最终形态重写——`OpenTree` 每次 `new TalentGraphIMGUI(asset)`，
  构造 / `SetAsset` 时置 `_pendingFit`，等 `DrawGUI` 拿到真实 `_canvasRect` 后做一次 `ZoomToFit`；
  窗口退化为薄壳，界面 100% 由画布类绘制；视觉直接复用 `BTEditorStyles`（不再自带样式类）。
  顺带修掉：左栏列表未落盘的"临时树"不可见（现单独顶一行"(未保存)"）、
  `OnGUI` 回调里重建 `IMGUIContainer`（新建/切树统一走 `EditorApplication.delayCall` 推迟一拍）、
  端口命中晚于节点命中导致连不上线。
- **天赋树：配置行拾取下拉太窄、不好点**：`TalentConfigPicker` 从 `ShowAsDropDown` 薄条
  改为独立居中窗口，带搜索框 / 分类筛选 / 键盘导航 / 行尾资产路径；
  打开时先关掉已存在的重复实例，`Pick()` 先把回调置 null 再调用以防重入。
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
