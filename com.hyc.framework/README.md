# HYC Framework

> 一套基于 **Unity 2022.3**、以 **DOTS / ECS 数据驱动** 为核心的轻量级游戏开发框架。  
> 内置配置管线、多语言本地化、ECS UI、输入 / 热键、过场演出与**可视化行为树**，并配套完整的编辑器工具链，帮助中大型 Unity 项目快速搭建、稳定迭代。

![Unity](https://img.shields.io/badge/Unity-2022.3-blue)
![License](https://img.shields.io/badge/License-MIT-green)
![Version](https://img.shields.io/badge/Version-2.0.4-orange)

---

## ✨ 特性

- **DOTS / ECS 数据驱动架构**：以 `World` + 分层 `ComponentSystemGroup` 更新阶梯组织游戏逻辑；配置、本地化、行为树全部以**不可变 Blob** 存储，运行期零托管分配、可 Burst 友好遍历。
- **配置管线（Config）**：Excel / 模板驱动，生成期把行烘焙为 blittable 结构体与 `BlobAssetReference`；运行期以**世界无关**的 `ConfigManager` 静态表提供无分配查询。
- **多语言本地化（Loc）**：基于 Blob 的多语言系统，支持 key 查询、`string.Format` 占位符、运行时切语言、MonoBehaviour 自动刷新与敏感词过滤。
- **ECS UI 系统**：UI **即 ECS System**（而非 MonoBehaviour 窗口管理器）。`UIManager` 统一管理分层 Canvas 与 Addressables 实例化，`ComponentBinderTable` 生成强类型组件绑定。
- **输入 / 热键（Input）**：基于 Unity InputSystem 的 action-name 分发热键中心，支持 Press / Hold、设备切换、键盘 / 手柄导航模式与静音 / 打断控制。
- **过场演出（Timeline）**：轻量过场导演，维护播放队列、支持跳过，片段播放由游戏层重写 `PlayClip` 接入（Timeline / Playable / 视频皆可）。
- **可视化行为树（BT）**：`BTTreeAsset` 可视化编辑 → 序列化为 `BTRootBlob` → `BTManager` 注册 → `BTInterpreterSystem` 逐帧解释执行；支持自定义节点与运行时黑板。
- **编辑器工具链**：Excel 配置自动生成、组件绑定代码生成、本地化 Key 管理、行为树可视化编辑器与校验器，大幅降低手工出错率。

---

## 📦 环境要求

| 依赖 | 版本 |
| --- | --- |
| Unity | `2022.3` 或以上 |
| `com.unity.addressables` | `1.21.21` |
| `com.unity.entities`（DOTS / ECS） | `>= 1.0.16`（Unity 2022.3 验证；Unity 6.x 以 `#if UNITY_6000_0_OR_NEWER` 分版本适配） |
| NPOI（仅编辑器，Excel 本地化导入） | — |

---

## 🚀 安装

> ⚠️ 本仓库为**私有库**：HTTPS 方式需要 access token（`https://<token>@github.com/...`）或本机已配置 SSH key；包位于 `com.hyc.framework/` 子目录，安装 URL 必须带 `?path=com.hyc.framework`。

### 方式一：Package Manager（Git URL）

Unity 菜单 **Window → Package Manager → 左上角 `+` → Add package from git URL**，填入：

```
https://github.com/HYClub/hyc-framework.git?path=com.hyc.framework
```

### 方式二：直接编辑 manifest.json

在工程的 `Packages/manifest.json` 的 `dependencies` 中追加：

```json
{
  "dependencies": {
    "com.hyc.framework": "https://github.com/HYClub/hyc-framework.git?path=com.hyc.framework"
  }
}
```

> 💡 可追加 `#<tag>` 或 `#<commit>` 锁定版本，例如
> `https://github.com/HYClub/hyc-framework.git?path=com.hyc.framework#v2.1.0`
> （建议基于当前 `main` 打一个 tag，便于可复现地引用某个发布版本）。

---

## 🏗️ 整体架构

框架的数据流可概括为四个阶段，配置 / 本地化 / 行为树三者产物都是**不可变 Blob**：

1. **生成期（Editor）**：Excel / 配置模板经 `ConfigGenerator` 产出 `[BlobGenerate]` 的 blittable `struct`（命名空间 `HYC.Framework.Config.Generated`），经 `ConfigExportService` 烘焙成 `BlobAssetReference<BlobRoot<TRow>>`；本地化表经 `LocalizedExcelReader`(NPOI) 产出 `id` / `lang` / `filter` 与每语言 `{lang}.lang` Blob；行为树在 `BTTreeAsset` 中可视化编辑，由 `BTBlobBuilder.Build` 序列化为纯数据 `BTRootBlob`。三者均可在运行期用 `BlobAssetReference<T>.TryRead` 直接读入，无需反序列化托管对象。
2. **引导期（A0 / A1 数据阶段）**：游戏 Bootstrap 把 Blob 读入，调用 `ConfigManager.Register(table)`（世界无关静态表）、`LocalizationManager.Reload(folder)`（或 `LocalizationBlobSystem` 自动从 `StreamingAssets/Localization` 加载）、`BTManager.Register(treeId, blobRef)`。
3. **运行期（A2 / A3 玩法阶段）**：玩法系统从 `ConfigManager` 查行（无需 World 句柄）、从 `LocalizationManager` 取文本；行为树由 `BTInterpreterSystem`（ISystem）对挂 `RunningBT` 的实体逐帧 `Tick`，`BTInterpreter` 以指针遍历 `BTRootBlob`，通过 `BTContext.GameHandler` 把 `GameCustom` 节点派发到 `BTNodeRuntimeRegistry`（反射缓存的 `BTCustomNode` 子类），并读写 `BTBlackboardRuntime`（每树实例一个，定义只读、实例可写）。
4. **表现期（B9，最后运行）**：UI 系统（`UIManager` + `AbsUISystem`）最后运行，读玩法阶段产生的最新状态刷新 HUD / 窗口；单帧 `MessageEntity` / `EventMessage` 在玩法阶段被消费，帧末由 `MessageExpirySystem` / `MessageClearSystem` 清理，消息绝不过帧。

**UI 如何挂在 ECS 上**：`AbsUISystem`（`[DisableAutoCreation]`）由游戏显式 `CreateSystemManaged` 并加入某 `UpdateGroup`（通常 B9 内）；其 `PrefabKey` 是 Addressable key，`UIManager` 在打开时 `Addressables.InstantiateAsync` 实例化 Prefab 并挂到分层 Canvas 下。视图内组件通过 `ComponentBinderTable` 配置、由 `ComponentBinderCodeGenerator` 生成强类型 `class X : IComponentBinder`，在视图（重）显示时 `Reset` 重新绑定，避免跨场景后引用失效。

---

## 📚 模块详解

### 1. 运行时基建 / Bootstrap / Simulation / 消息

框架的启动宿主与 ECS 更新阶梯——用 `IFrameworkInstaller` 组合安装世界、以双模式（Live / Sim）启动，并通过单帧消息实体在系统间解耦通信。

> ⚠️ `FrameworkBootstrap` **不会**被 Entities 的 `ICustomBootstrap` 自动发现（避免与游戏自有 bootstrap 冲突）。游戏需直接调用 `Initialize`，或自定义 `ICustomBootstrap` 来 `new FrameworkWorld()`。

```csharp
// 1) 自定义安装器，把系统挂进世界
class MyInstaller : HYC.Framework.Runtime.IFrameworkInstaller
{
    public void Install(World world)
        => world.GetOrCreateSystemManaged<MyGameSystem>();
}

// 2) 注册安装器并启动（Live 模式）
HYC.Framework.Runtime.FrameworkBootstrap.Installers.Add(new MyInstaller());
HYC.Framework.Runtime.FrameworkBootstrap.BootMode = HYC.Framework.Dots.SimulationBootstrap.Live;
new HYC.Framework.Runtime.FrameworkBootstrap().Initialize("Game World");

// 3) 在系统内发一条单帧引导消息（UI 系统的 OpenGuide 内部即如此实现）
var beginSim = world.GetExistingSystemManaged<BeginSimulationEntityCommandBufferSystem>();
var ecb = beginSim.CreateCommandBuffer();
var e = ecb.CreateEntity();
ecb.AddComponent<HYC.Framework.Dots.MessageEntity>(e);
ecb.AddComponent(e, new HYC.Framework.Dots.EventMessage { EventID = 1001, IsRepeated = false });
// 帧末由 MessageClearSystem 自动销毁，无需手动回收
```

**更新阶梯顺序**：`Root → A0(引导) → A1(数据) → A2(场景) → A3(玩法) → B9(UI 最后)`。  
**双模式语义**：`Live = 1` 联网 / 正常；`Sim = 2` 离线确定性模拟，`SimOnly` 标记的系统只应在模拟会话中运行。

核心类型：`FrameworkWorld`、`IFrameworkInstaller`、`FrameworkBootstrap`、`SimulationState`、`SimOnly`、`SimulationBootstrap`、`UpdateGroup_*`、`Message` / `MessageEntity` / `EventMessage`、`MessageClearSystem` / `MessageExpirySystem`。

---

### 2. 配置管线（Config）

Excel / 模板驱动的「不可变 Blob 配置表」——生成期把行烘焙为 `[BlobGenerate]` blittable 结构体，运行期以**世界无关**的 `ConfigManager` 静态表提供无分配查询。

```csharp
// 1) 生成期由 Excel / 模板产出（示意，真实由 ConfigGenerator 生成）：
// namespace HYC.Framework.Config.Generated
// [HYC.Framework.Config.BlobGenerate("Item")]
// public struct ItemCfg : Unity.Entities.IComponentData, System.IEquatable<ItemCfg>
// { public long Id; public int Price; public int StackMax; }

// 2) 运行期从 .blob 读取并注册（id 取行内 Id 字段）
BlobAssetReference<BlobRoot<ItemCfg>> blobRef = /* BlobAssetReference<BlobRoot<ItemCfg>>.TryRead(...) */;
var table = ConfigBlobTable<ItemCfg>.FromBlob(blobRef, row => row.Id);
HYC.Framework.Config.ConfigManager.Register(table);

// 3) 任意处读取（无需 World 句柄）
if (HYC.Framework.Config.ConfigManager.TryGet<ItemCfg>(itemId, out var cfg))
    int price = cfg.Price;
```

**注意**：
- 行结构体必须 **blittable**（`ConfigBlobTable<TRow> where TRow : unmanaged`）；`ConfigValidator` 会在编辑器菜单 `HYC Framework/Tools/Validate Config` 校验缺 `[BlobGenerate]` 标记或非 blittable 字段。
- `ConfigManager` 的读访问是**世界无关**的——UI 与玩法层查表都不需要 World 句柄。

核心类型：`ConfigManager`、`ConfigBlobTable<TRow>`、`BlobRoot<TRow>`、`ConfigTemplate`、`ConfigEnumDefinition`、`CfgAssetAttribute`、`IConfigIdProvider`、`BlobGenerateAttribute`。

---

### 3. 本地化（Loc）

基于 Blob 的多语言系统——从 `id` / `lang` / `filter` 文件 + 每语言 `{lang}.lang` Blob 加载，提供 key 查询、运行时切语言、MonoBehaviour 自动刷新与敏感词过滤。

```csharp
// 1) 代码取文本（支持 {0}{1} 占位符，走 string.Format）
string s  = HYC.Framework.Loc.LocalizationManager.GetText("ui/start");
string s2 = HYC.Framework.Loc.LocalizationManager.GetText("ui/welcome", playerName);

// 2) 运行时切换语言
HYC.Framework.Loc.LocalizationManager.SetLanguage(HYC.Framework.Loc.Locale.Chinese);

// 3) 在 Prefab 上挂 LocalizedTMP 组件，Inspector 设 Key = "ui/start"，
//    切语言时 onLanguageChanged → 组件自动 Refresh，无需手写。

// 4) 从 StreamingAssets 重新加载（LocalizationBlobSystem 在 BeforeSceneLoad 已自动调一次）
HYC.Framework.Loc.LocalizationManager.Reload(Application.streamingAssetsPath + "/Localization");
```

**注意**：`LocalizedExcelReader` 用真实 UTF-8 字节数分配，专门修复中文被截断的问题；`GetTextByLang` 在 key 不存在时返回 `"未找到Key : {key}"`。

核心类型：`LocalizationManager`、`Locale`、`LocaleUtil`、`CfgLocalization`、`LocalizationBlobSystem`、`LocalizationExtension`、`LocalizedBase` / `LocalizedText` / `LocalizedTMP`、`SensitiveWordManager`。

---

### 4. ECS UI 系统

用 ECS System（而非 MonoBehaviour 窗口管理器）承载 UI——`UIManager` 拥有根节点 / 分层 Canvas，`AbsUISystem` 通过 Addressables 实例化 Prefab 视图，并由 `ComponentBinderTable` 生成强类型 `IComponentBinder` 绑定组件。

```csharp
// 1) 定义一个窗口系统（[DisableAutoCreation]，由游戏 bootstrap 显式创建并加入分组）
public class BagWindow : HYC.Framework.UI.AbsUISystem
{
    public override string PrefabKey => "UI/Bag";           // Addressable key
    protected override void OnViewOpen()
    {
        base.OnViewOpen();
        var title = FindComponent<TMPro.TextMeshProUGUI>("Title");
        RegisterHotkey(HYC.Framework.Input.HotkeyActionNames.UI_Func_Close,
                       ctx => { /* 关闭窗口 */ });
    }
}
// UIManager 在打开时通过 Addressables.InstantiateAsync(PrefabKey) 实例化视图并挂到 Canvas 下

// 2) 打开 / 关闭
HYC.Framework.UI.UIManager.OpenHudNotice(typeof(BagWindow));
HYC.Framework.UI.UIManager.Close<BagWindow>();

// 3) 编辑器生成强类型 Binder：在 ComponentBinderTable 上配字段后点 Generate，
//    产出 class BagBinder : IComponentBinder，挂到 Prefab 上交由 UIManager 自动 Reset 绑定
```

**注意**：`UIManager` 与 `AbsUISystem` 均标 `[DisableAutoCreation]`，必须由游戏 bootstrap 显式创建并加入某 `UpdateGroup`；热键改用 `HotkeyActionNames` 常量字符串分发（已取代原 `HotkeyID` 枚举）。

核心类型：`UIManager`、`AbsUISystem`、`BaseWindowSystem` / `AbsParentBaseWindowSystem`、`BaseDialogSystem` / `BaseHudSystem` / `BaseLoadingSystem`、`BaseWindowPart`、`IGameFunctionWindow`、`UIGroup`、`IComponentBinder`、`Binder`、`ComponentBinderTable`、`UIAnimationHook`。

---

### 5. 输入 / 热键（Input）

基于 Unity InputSystem 的「按 action 名分发」热键中心——`HotkeyManager` 静态注册表把 action 名映射到 `Action<HotkeyInputContext>` 回调，提供 Press / Hold、设备切换、导航模式与静音等运行时控制。

```csharp
// 1) 启动时绑定一次 InputActionAsset（会清空旧注册并订阅 InputSystem.onActionChange）
HYC.Framework.Input.HotkeyManager.BindAsset(myInputActionAsset);

// 2) 按 action 名注册热键（返回 HotkeyHandle 用于后续注销）
var handle = HYC.Framework.Input.HotkeyManager.RegisterHotkey(
    HYC.Framework.Input.HotkeyActionNames.UI_Func_Bag,
    ctx => { OpenBag(); },
    holdTime: 0,
    parent: transform,
    text: "打开背包",
    style: HYC.Framework.Input.HotkeyElementStyle.UI,
    priority: 0);

// 3) 读摇杆值（手柄 / 键鼠统一上下文）
HYC.Framework.Input.HotkeyManager.RegisterHotkey(
    HYC.Framework.Input.HotkeyActionNames.UI_UGUI_Horizontal,
    ctx => { float v = ctx.ReadValue<float>(); });

// 4) 注销
HYC.Framework.Input.HotkeyManager.UnregisterHotkey(handle);
```

**注意**：用 action-name 替代游戏枚举，摆脱原 `InputManager` 强耦合；`holdTime > 0` 即 `HotkeyMode.Hold`，否则 `Press`；`SetSilence` 可屏蔽回调，`StopCurrentInvokeList()` 可打断当前派发列表。

核心类型：`HotkeyManager`、`HotkeyHandle`、`HotkeyMode`、`InputDevice`、`HotkeyInputContext`、`HotkeyInputPhase`、`BaseHotkeyElement`、`IHotkeyElement`、`HotkeyElementStyle`、`HotkeyActionNames`。

---

### 6. 过场演出（Timeline）

简单的过场导演——维护过场队列、按顺序播放、支持跳过；具体「片段如何播放」由游戏层重写 `PlayClip` 实现（框架只管调度）。

```csharp
class MyDirector : HYC.Framework.Timeline.CutsceneDirector
{
    // 框架只调度队列，片段具体怎么播由游戏实现
    protected override void PlayClip(HYC.Framework.Timeline.Cutscene clip)
    {
        // 用 Addressables 加载 clip.AssetKey 的 Timeline / Playable，播放结束必须调：
        // NotifyFinished();
    }
}

var director = new MyDirector();
director.Enqueue(new HYC.Framework.Timeline.Cutscene { Id = 1, AssetKey = "Cutscene/Intro", Loop = false });
director.Play(1);
// 需要跳过时：director.Skip();
```

核心类型：`Cutscene`、`CutsceneDirector`。

---

### 7. 行为树（BT）⭐

可视化编辑（`BTTreeAsset`）→ 序列化为纯数据 `BTRootBlob`（Burst 友好）→ 运行期 `BTManager` 注册 → `BTInterpreterSystem` 对挂 `RunningBT` 的实体逐帧 `Tick`，由 `BTInterpreter` 递归解释执行的完整 DOTS 行为树方案。

```csharp
// 1) 编辑器：Create → HYC/BT/Tree 建 BTTreeAsset，可视化连好节点，
//    保存即被 BTBlobBuilder 烘焙为 BTRootBlob

// 2) 运行期注册树（从资源加载出 BlobAssetReference<BTRootBlob> 后）
HYC.Framework.BT.BTManager.Register(treeId, blobRef);

// 3) 给实体挂 RunningBT，BTInterpreterSystem 每帧自动 Tick
var e = ecb.CreateEntity();
ecb.AddComponent(e, new HYC.Framework.BT.RunningBT
{
    TreeId = treeId,
    RunState = new HYC.Framework.BT.BTRunState { TreeId = treeId, RootNode = 0 }
});

// 4) 自定义一个游戏节点（自动被 BTNodeRuntimeRegistry 反射扫描，无需手动注册）
public class FindNearestEnemy : HYC.Framework.BT.BTCustomNode
{
    public override long SubType => 1;
    public override string NodeName => "找最近敌人";
    public override HYC.Framework.BT.BTCustomNodeKind Kind => HYC.Framework.BT.BTCustomNodeKind.Condition;
    public override HYC.Framework.BT.BTNodeState Execute(ref HYC.Framework.BT.BTContext ctx, ref HYC.Framework.BT.BTNodeView view)
        => /* 读 ctx.GameContext.Data 判断 */ HYC.Framework.BT.BTNodeState.Success;
}
```

**⚠️ 关键限制（务必注意）**：  
框架默认的 `BTInterpreterSystem` 构造的 `BTContext.GameHandler = null`。这意味着 `BTNodeType.GameCustom`（= 128）节点在默认情况下会返回 `Failed`。要让自定义节点真正执行，游戏必须把 `GameHandler` 接到 `BTNodeRuntimeRegistry.Execute`：

```csharp
// 在驱动 BT 的系统中，构造 ctx 时设置 GameHandler
ctx.GameHandler = (ref HYC.Framework.BT.BTContext c, ref HYC.Framework.BT.BTNodeView v) =>
{
    long sub = v.Node.LongCount > 0 ? v.GetLong(0) : 0;
    HYC.Framework.BT.BTNodeRuntimeRegistry.Execute(HYC.Framework.BT.BTTreeKind.AI, sub, ref c, ref v, out var r);
    return r;
};
```

**其它要点**：
- 内建节点：Sequence / Selector / Parallel / Random* / Invert / Repeat / UntilSuccess / UntilFail / Always* / CooldownGate / Conditional / TimeLimit / CheckDistance / CheckBlackboard / Wait / NoOp / SubTree，均在 `BTInterpreter.EvaluateNodeInner` 的 `switch` 内实现。
- 树实例状态（`RunningBT` / `BTRunState`，含 `Stack[16]`、`Trace[32]`）挂在实体上**跨帧保留**，解释器保留执行轨迹用于调试高亮；`BTManager.ToggleBreakpoint(treeId, nodeIndex)` 支持断点调试。
- 黑板 `BTBlackboardRuntime` 每树实例一个（定义只读、实例可写），键用 FNV-1a 哈希；`TimeLimit` / `CooldownGate` / `Wait` / `Check*` 等节点依赖 `ctx.Blackboard.IsCreated`，未创建黑板时返回 `Failed`。
- 自定义节点采用双注册机制：编辑器 `BTCustomNodeScanner` 写入 `BTGameNodeRegistry`（供节点面板显示），运行期 `BTNodeRuntimeRegistry` 反射扫描 `BTCustomNode` 子类并缓存；二者以 `(BTTreeKind, SubType)` 为键，技能树与 AI 树的子类型数字互不冲突。

核心类型：`BTManager`、`BTInterpreter` / `BTInterpreterSystem`、`BTBlackboardRuntime`、`BTContext`、`BTCustomNode`、`BTGameNodeRegistry`、`BTNodeRuntimeRegistry`、`BTRootBlob` / `BTNodeBlob`、`BehaviourTreeFieldAttribute`（标记 `long` 为行为树引用）、`BTValidator` / `BTBlobBuilder` / `BTTreeAsset`（编辑器侧）。

---

### 8. 其它运行时工具

- **日志**：`Log`（写 `persistentDataPath/logs` 滚动文件）、`GameLog`（写 `log.json`，带时间戳）。
  ```csharp
  HYC.Framework.Runtime.Log.Info("启动完成");
  HYC.Framework.Runtime.Log.Error("加载失败: " + path);
  ```
- **数据模型**：`DataModel` / `DataItem` / `DataStatistics` 提供简单的物品与数值统计设施（`NewItemFlagSystem` 为预留扩展点）。
- **命令行参数**：`GameCommandArgs` 单例解析 `--account` / `--token` / `--appid` / `--serverip` / `--serverport` / `--lang` / `--width` / `--height` / `--fullscreen`。
  ```csharp
  var lang = HYC.Framework.Runtime.GameCommandArgs.Instance.Lang; // --lang 的值
  ```
- **ToolTip**：`ToolTipManager` + `AbsTipView<T>` / `AbsTipComponent` 提供 12 向自动停靠的提示系统，沿基类链查找渲染器。

---

## 🛠️ 编辑器工具链

| 工具 | 作用 |
| --- | --- |
| `ConfigGenerator` / `ConfigValidator` / `ExcelReader` | 从 Excel / 模板生成 blittable 配置结构体并校验 |
| `ComponentBinderCodeGenerator` | 由 `ComponentBinderTable` 生成强类型 `IComponentBinder` |
| `LocaleWindow` / `LocalizedExcelReader` / `LocalizedKeyPickerWindow` | 多语言 Key 浏览、Excel 导入（NPOI，修复中文截断）、Key 选取 |
| `SensitiveWordWindow` | 敏感词管理 |
| `BTGraphWindow` / `BTDataWindow` / `BTNodeCreatorWindow` / `BTValidator` / `BTBlobBuilder` | 行为树可视化编辑、自定义节点生成、校验与 Blob 构建 |

---

## 📁 目录结构

```
hyc-framework/                 ← 仓库根
├── README.md                  ← 仓库 README（GitHub 主页）
├── com.hyc.framework/         ← Unity 包（package.json 在此）
│   ├── package.json
│   ├── LICENSE
│   ├── CHANGELOG.md
│   ├── README.md              ← 包内 README（Package Manager 展示）
│   ├── Runtime/               # 运行时代码（HYC.Framework.Runtime.asmdef）
│   │   ├── Bootstrap.cs / Simulation.cs / UpdateGroups.cs
│   │   ├── ConfigManager.cs / BlobTable.cs / ConfigTemplate.cs
│   │   ├── Localization*.cs
│   │   ├── UIManager.cs / AbsUISystem.cs / Binder.cs
│   │   ├── HotkeyManager.cs / InputDevice.cs
│   │   ├── CutsceneDirector.cs
│   │   ├── BT/                 # 行为树运行时
│   │   ├── Components/ / ToolTip/ / Attributes/
│   │   └── ...
│   └── Editor/                # 编辑器工具（HYC.Framework.Editor.asmdef）
│       ├── ConfigGenerator.cs / ComponentBinderCodeGenerator.cs / ExcelReader.cs
│       ├── LocaleWindow.cs / LocalizedExcelReader.cs
│       ├── BT/                 # 行为树可视化编辑器
│       └── DataEditor/
```

---

## 🤝 贡献

欢迎 Issue 与 PR。提交前请运行编辑器菜单 **HYC Framework / Tools / Validate Config** 与行为树校验，确保配置结构体 blittable、行为树无孤立节点。

---

## 📄 许可证

本项目以 **MIT 许可证** 开源，详见 [LICENSE](./LICENSE)。
