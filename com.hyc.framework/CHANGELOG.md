# Changelog

All notable changes to the HYC Framework package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [2.0.6] - 2026-10-07

> 版本说明：`2.0.5`（存档存储等）与本节合并为一次发布，`package.json` 由 `2.0.4` 直接 bump 到 `2.0.6`。
> 此前两个 `- Unreleased` 段落从未发布过，故合并不会影响任何已发布版本。

### Added
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

- **🔴 实体销毁时 BT 原生内存泄漏（审计未捕获，由核实 SnowBros 实体销毁路径时发现）**。
  `BTManager._nodeStates` 里每个 `(实体, 树ID)` 持一份 `Allocator.Persistent` 的 `NativeArray`，
  原本**唯一**的释放路径是「树跑完（结果 != Running）时调 `ClearNodeStates`」。
  但实体可能在树仍 Running 时被销毁——SnowBros 的"敌人冻成雪球 = 销毁旧实体 + 新建球形实体"
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
  - 交互原型：`docs/prototypes/attribute-config-3.html`（编辑 + 枚举 `IAttrBag` 用法演示，jsdom 冒烟通过）。
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
  （`mIDs/mLangs/mTexts/mLang2Index/mKey2Index = null`）再按目标目录重填。宿主工程（如 SnowBros）
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
  已统一为 **`HYC Framework/BT/导出 Blob(打包用)`**（`Editor/BT/BTBlobExporter.cs` 的 `[MenuItem]`）；
  同步订正 `Runtime/BT/BTBlobLoader.cs` 两处提示文案、`manifest.txt` 注释、包 `README.md`、
  `Samples~/HYCSimple/BT/AI-NOTES.md`，以及 `docs/01`、`docs/02`、`docs/80`~`84`、`docs/90` 的菜单表；
  顺带订正 `docs/80` / `docs/81` / `docs/84` 里把 BT 编辑器误写成 `Tools/HYC/BT Editor` 的旧错
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
  - 文档：`docs/52-存档存储.md`（架构 / 关键类 / 用法 / 9 条坑）。
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
  - 文档：`docs/53-天赋树.md`；视觉原型 `docs/prototypes/talent-tree-editor.html`。
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
- **文档：补齐枚举配置（此前该能力无文档）**：新增 `docs/13-枚举配置.md`——枚举定义资产 → 生成 C# 枚举类 → 配置字段引用的完整机制、`ValueOf` 的 **1 基**（`index+1`）与 Flags（`1<<index`）数值约定、**配置类字段是枚举类型而 Blob 字段是 `int`** 这一最易误判点、校验规则与 9 条踩坑（含从 0 基手写枚举迁移要 `+1`、脚本化 `EnsureEnumDefs()` 必须排在 OutputDir 清理之后）。`11-字段类型与导出设置` 的 `Enum` 行、`docs/README.md` 索引、`90-编辑器工具链` 同步更新。
- **文档：新增 `docs/02-操作手册-按任务.md`（处方型，补齐「读完知道怎么做」的缺口）**：现有各篇都是「机制说明」（有什么、是什么），缺「要完成 X 按顺序做这些」。02 篇按任务组织——新增配置表（**脚本驱动 / 编辑器驱动两条路**，此前只讲了编辑器一条，而 SnowBros / TD 都是脚本驱动）、字段类型与导出模式决策表、枚举字段步骤、运行期读配置、行为树从零到跑通（含 `BTInterpreterSystem` 是 struct、须用 `GetOrCreateSystem<T>()`）、新增 UI 窗口、**基类 / 接口一览表**、「改完必须重跑什么」清单、菜单路径速查。另含两条易漏铁律：UI 类必须写 `partial`、引用 `UpdateGroup_*` 必须 `using HYC.Framework.Dots;`。
- **文档订正（菜单路径此前写错，会直接误导 AI）**：行为树编辑器实际是 `HYC Framework/BT/BT Editor`，`01` / `90` 两篇误写为 `Tools/HYC/BT Editor`（已订正）；`01` 篇列出的 `HYC Framework/Config/Generate Config` **并不存在**（已移除）；`90` 篇补上遗漏的 `HYC Framework/Localization/Scan Key Usage` 与两个 UI Binder 生成变体。全部菜单路径已逐条比对源码 `[MenuItem]`。

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
