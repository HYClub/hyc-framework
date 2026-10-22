# HYCSimple · 场景 / 引导（Scene / Bootstrap）· AI 接入笔记

> 写给 AI（和人类）的接入说明：讲清「各系统该在什么时机、按什么顺序喂饱」，把前面的 Attr/Config/BT/Loc 串成一条链。
> 配合同目录 `HYCSimpleSceneBootstrap.cs`（普通 MonoBehaviour，挂在任意场景 GameObject 上）食用。
> 框架是「数据驱动」：生成期烘 Blob → 引导期注册 → 运行期查表/驱动 → 表现期（UI 最后跑）。
>
> 🔴 **跨端铁律（先读这条）**：同一套配置数据要同时出 WebGL / Android / PC / iOS，游戏层**不许写平台分支**。
>  StreamingAssets 在 WebGL（无文件系统）和 Android（APK 压缩包内）不能用 `System.IO` 读，框架已用
> `HYC.Framework.IO.StreamingAssets` 把这层差异全部收口：引导走 `FrameworkBootstrap.InitializeAsync`
> （内部先 `await StreamingAssets.PreloadAsync()` 再建 World，PC/iOS 上是 no-op），读文件走
> `StreamingAssets.ReadBytes / ReadText / Exists`。判据是 `StreamingAssets.NeedsPreload`（= WebGL || Android），
> **不是** `IsWebGL`。详见 `Documentation~/99-Blob-与数据约定.md`「五·补 · 跨端数据加载契约」。

---

## 一、一句话心智模型

- **更新阶梯顺序**：`Root → A0(引导) → A1(数据) → A2(场景) → A3(玩法) → B9(UI 最后)`。
- **引导期（A1）要把数据喂饱**：本地化 `Reload`、配置表 `Register`、行为树 `BTBlobLoader.LoadAll()` 都应在
  **任何玩法/UI 读它们之前**完成。
- **表现期（B9）**：`UIManager` 在帧末清消息，UI 最后处理，避免读到半截状态。

---

## 二、端到端步骤（照做即可）

1. **世界创建（跨端必须用异步入口）**：真实项目用
   ```csharp
   // 游戏自己的引导 MonoBehaviour / ICustomBootstrap 里（全平台同一套代码，零 #if）
   private async void Start()
   {
       var ok = await new FrameworkBootstrap().InitializeAsync("World");
       // InitializeAsync = await StreamingAssets.PreloadAsync() → 建 World 装系统 → 补加载引导期数据
       // PC / iOS 上 PreloadAsync 是 no-op；WebGL / Android 上先把 blob HTTP 进内存缓存。
   }
   ```
   为什么不能用同步的 `Initialize` / 自写 `ICustomBootstrap`：`XxxBlobSystem` / `LocalizationBlobSystem`
   都在 `System.OnCreate`（建 World 时）读 StreamingAssets，而 WebGL / Android 的预取必须在此之前完成。
   ⚠️ 注意 `ICustomBootstrap` 由 Entities 扫描自动触发、**不是 async**，等不了预取；游戏若必须走
   `ICustomBootstrap`，把建世界本身改到异步入口里做（框架的 `FrameworkWorld` / `FrameworkModeBootstrap`
   照用），别在 `ICustomBootstrap.Initialize` 里同步建依赖 StreamingAssets 的世界。
2. **数据阶段（A1）**——本例 `HYCSimpleSceneBootstrap.Start()` 演示的顺序：
   ```csharp
   // 1) 本地化：真实项目由 FrameworkBootstrap.InitializeAsync → EnsureBootDataLoaded 自动加载；
   //    要换目录热更时才手动 LocalizationManager.Reload(目录)（目录判断见雷区 3/6）。
   // 2) 行为树：BTBlobLoader.LoadAll() —— 内部已走 StreamingAssets 抽象，跨端无需平台代码。
   // 3) 配置：生成的 XxxBlobSystem 会自动 Register（低层 ConfigRegistry.Register，宿主禁手写）
   ```
3. **玩法/UI 上场（A2/A3/B9）**：此后实体挂 `RunningBT` 即被 `BTInterpreterSystem` 驱动，
   门面 `{ns}.ConfigManager.{Type}.GetByID(id)` 随时查表，`LocalizedTMP` 随 `SetLanguage` 刷新。

---

## 三、给 AI 的「雷区」（写代码前必读）

1. **顺序错 = 读到空**：玩法在 `Reload` 前调 `GetText` 会拿到 `未找到Key`；在 `LoadAll` 前给实体挂 `RunningBT`
   会让 `BTInterpreterSystem` 把 `RunState.Result` 置 `Failed`（树未注册）。**数据阶段先跑完**。
2. **示例系统默认不自动运行**：`HYCSimple*DemoSystem` 全部标了 `[DisableAutoCreation]`，
   Unity **不会**自动把它们拉进任何 World（包括默认世界）——这是刻意设计，不是缺陷。
   原因：它们会写**进程级静态单例**（`LocalizationManager` / `ConfigRegistry` / `BTManager`），
   一旦自启就会污染宿主工程已加载的数据（真实事故：示例把宿主的 18 条本地化 key 洗成示例的 3 条，
   宿主主菜单全线 `未找到Key : xxx`，且因每帧兜底而**反复导入都无法恢复**）。
   所以唯一入口是本目录的 `HYCSimpleSceneBootstrap`：
   把它挂到**空场景**的 GameObject 上，由它 `GetOrCreateSystemManaged<T>().Update()` 手动驱动。
   宿主工程不挂它 ⇒ 示例零副作用。
   （`HYCSimpleLocSampleData.CanTakeOver()` 是第二道保险：宿主已有非示例数据时直接放弃接管并打警告。）
3. **`Reload(folder)` 需目录存在**：真实工程 `StreamingAssets/Localization` 一定在；本例留空时走
   `HYCSimpleLocSampleData.EnsureLoaded()`（运行时自生成），并对传入目录做了 `Directory.Exists` 守护。
   ⚠️ 跨端注意：`Directory.Exists` / `File.ReadAllBytes` / `Path.Combine(Application.streamingAssetsPath, …)`
   在 **WebGL / Android 上一律无效**（前者返回 false，后者直接抛异常）。判存在用
   `StreamingAssets.Exists("Localization")`，列文件用 `StreamingAssets.GetKeysUnder("Localization")`
   （预取平台从内存缓存枚举；PC/iOS 走 File，同一行代码两端都对）。
4. **配置注册来源**：本 Sample 的 Config 演示是运行时手动 `Register`；真实项目应由生成器产出的 `XxxBlobSystem`
   （挂在 A1 数据阶段）统一注册，避免散落手动调用。
5. **命名须框架中立**：示例 key 用 `ui/start` 等通用名；真实工程按自己规范，但**框架样例不写使用方玩法**。
6. 🔴 **`System.IO` 不得碰 StreamingAssets（跨端第一红线）**：WebGL 没有文件系统、Android 的 StreamingAssets
   在 APK 压缩包内（`jar:file://…!/assets`），`File.*` / `Directory.*` 在两者上都读不到。
   - 读字节/文本：`StreamingAssets.ReadBytes(相对路径)` / `ReadText(相对路径)`（相对 StreamingAssets 根，用 `/` 分隔）。
   - 判存在：`StreamingAssets.Exists(相对路径)`；列目录：`StreamingAssets.GetKeysUnder(相对目录)`。
   - 时序：`NeedsPreload` 平台的读取都发生在建 World 时（`System.OnCreate`），所以引导必须先
     `await FrameworkBootstrap.InitializeAsync(...)`（内部先 `PreloadAsync`）——**没有第二步**，框架全包了。
   - 唯一允许写 `System.IO` + `streamingAssetsPath` 的地方：`StreamingAssets` 类自己（平台差异收口点）
     和 PC-only 的编辑器工具。

---

## 四、验证清单（进 Unity 后）

- [ ] 新建空场景，挂 `HYCSimpleSceneBootstrap` 到任意 GameObject，进 Play。
- [ ] Console 打出「场景引导完成：Loc 已加载，BT 加载 N 棵，当前语言=cn，示例文本=开始游戏」。
- [ ] 顺序验证：把 `BTBlobLoader.LoadAll()` 移到 `RunningBT` 挂组件之后，应见到树未注册的 Failed 日志（确认顺序敏感）。
- [ ] 接真实工程：把 `localizationFolder` 指向 `StreamingAssets/Localization`，确认读的是真实多语言数据。
- [ ] **跨端回归（出 WebGL / Android 包必做）**：引导入口换成 `await FrameworkBootstrap.InitializeAsync("World")`
      后出包，确认 Loc / BT / Config 均正常加载；编辑器里跑通不代表真机 OK
      （编辑器走 `BTAutoRegisterOnPlay` 自动注册，真机没有这条路）。

---

## 五、Scene Content 示例（场景内容物）

本目录另含场景内容物系统的最小闭环演示：

- `HYCSimpleSpawnPoint` —— **标记组件（B 来源）**：实现 `HYC.Framework.SceneContent.ISceneContentMarker`，
  挂在关卡 prefab 上即被 `SceneContentAuthoring` 自动发现，导出时写进 Blob。
- `HYCSimpleSceneContentSpawner` —— **运行期生成注入点**：实现 `ISceneContentSpawner`，按 `contentTypeId`
  决定生成什么（示例只打日志，真实项目生成 prefab/entity）。
- `HYCSimpleSceneContentDemo` —— 把「加载 Blob」演示进场景（运行期只做加载）。
- `Editor/DemoResolver.cs` —— **编辑器期解析器**：按 prefab 名派生 id，`[InitializeOnLoadMethod]` 自注册。

### 接入要点
- 关卡 prefab 挂 `SceneContentAuthoring`（`levelId` 须与导出文件名、运行期 `Load(levelId, …)` 一致）。
- 编辑器导出前，游戏须注册 prefab→id 解析器（`SceneContentResolvers.Register`，一次性，编辑器期）。
  🔴 **解析器类型在框架 Editor 程序集，运行时 asmdef 引用不了它**——这是编译期硬约束，
  `#if UNITY_EDITOR` 包在运行时 asmdef 里照样 CS0234。所以解析器必须放**样例自己的 Editor asmdef**
  （本例 `Scene/Editor/HYCSimple.Scene.Editor.asmdef`），由编辑器期自注册；Demo 的 Start 只做运行期 Load。
  另外：挂了 `SceneContentMarker` 的 prefab 走框架内置兜底解析，**无需注册**即可导出。
- 运行期在**场景加载完成**后调 `SceneContentLoader.Load(levelId, spawner)` 即可生成全部内容。
- 真实项目 `DemoResolver` 的 `name.GetHashCode()` 仅示例，应接配置表 / 常量映射保证跨会话稳定。
