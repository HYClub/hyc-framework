# 60 · 引导与世界（Bootstrap）

## 职责

在启动时**组装一个 framework World**：装基础系统（输入/相机/UI/本地化/设置）、加载配置表、选择模式（Live / Sim），并提供一个统一的更新入口。

命名空间：引导在 **`HYC.Framework.Runtime`**，更新组在 **`HYC.Framework.Dots`**。

一句话：**三选一地创建 World —— `FrameworkWorld`（installer 组合）/ `FrameworkModeBootstrap`（按模式扫系统）/ 或游戏自己的 `ICustomBootstrap`。**

---

## 跨端数据加载契约（PC / iOS / Android / WebGL 一套代码）⚠️ 先读这段

**打包发布时必须用异步引导入口。** 三种引导方式各自都有 `Initialize` / `InitializeAsync` 一对：

| 入口 | 用法 |
| --- | --- |
| `FrameworkBootstrap.InitializeAsync(name)` | `await` 版，**推荐，全平台统一用这个** |
| `FrameworkModeBootstrap.InitializeAsync(name)` | 按模式扫系统 + `await` 版 |
| `FrameworkBootstrap.Initialize(name)` / `FrameworkModeBootstrap.Initialize(name)` | 同步版，仅 PC / macOS / Linux / iOS |

`InitializeAsync` 内部顺序：

```
await StreamingAssets.PreloadAsync()      // WebGL/Android：HTTP 预取进内存缓存；PC/iOS：no-op
  ↓
建 World + 装系统                          // 各 System.OnCreate 在这里读 Blob
  ↓
FrameworkLifecycle.EnsureBootDataLoaded()  // 补加载入（本地化在 BeforeSceneLoad 可能主动跳过）
```

**为什么必须这样**：WebGL 没有文件系统、Android 的 StreamingAssets 在 APK 压缩包内
（`jar:file://`），这两类平台**不能直接用 `System.IO.File` 读**，必须先把数据 HTTP 进内存缓存。
而读取都发生在建 World 时的 `System.OnCreate`，所以预取必须先于建 World 完成。
判据是 `StreamingAssets.NeedsPreload`（= `IsWebGL || IsAndroid`）。

**宿主代码因此零平台分支**：

```csharp
public sealed class GameBootstrap : MonoBehaviour
{
    private async void Start()
    {
        await new FrameworkBootstrap().InitializeAsync("Game World");
        // 之后照常：装 UI、进主菜单……
    }
}
```

> 用老的同步 `Initialize` 在 WebGL / Android 上会打明确日志提示改用 `InitializeAsync`，
> 不会静默失败。详见 [99-Blob 与数据约定 · 五·补](./99-Blob-与数据约定.md)。

---

## 更新阶梯（UpdateGroups）

`HYC.Framework.Dots` 定义了一套固定顺序的 `ComponentSystemGroup`，**全部挂在 `SimulationSystemGroup` 下的 `UpdateGroup_Root` 下**：

```
SimulationSystemGroup
└── UpdateGroup_Root           [After BeginSimulationEntityCommandBufferSystem]
    ├── UpdateGroup_A0         引导期：装单例、起管线、读命令行/启动配置
    ├── UpdateGroup_A1         数据期：配置加载、账号/状态、统计（NewItemFlag 在此）
    ├── UpdateGroup_A2         房间/场景期：房间进入、场景烘焙、环境
    ├── UpdateGroup_A3         玩法期：模拟、技能、AI、战斗（主体）
    └── UpdateGroup_B9         表现期：UI 刷新、HUD、本地化刷新（最后跑）
```

关键约束（源码注释里点名的坑）：

- `UpdateGroup_B9` **不能**写 `[UpdateBefore(typeof(LateSimulationSystemGroup))]`——`UpdateGroup_Root` 已经在 `SimulationSystemGroup` 内，而 `SimulationSystemGroup` 永远在 `LateSimulationSystemGroup` 之前跑。跨级排序会让 Entities 的 system sorter **抛 NRE**。
- `MessageExpirySystem` 用 `[UpdateInGroup(typeof(UpdateGroup_B9), OrderLast = true)]` 代替原来的 `EndSimulationEntityCommandBufferSystem` 排序——原因同上。

---

## 三种引导方式

### 1. FrameworkWorld（installer 组合，不带模式过滤）

```csharp
var host = new FrameworkWorld("Framework World");
World.DefaultGameObjectInjectionWorld = host.World;
host.Install(installers, BootMode);   // installers: IEnumerable<IFrameworkInstaller>
host.Update();                         // 每帧调
host.Dispose();
```

- `Install` 里：`GetOrCreateSystemManaged<UpdateGroup_Root>()`，依次跑每个 `IFrameworkInstaller.Install(world)`，最后 `SimulationBootstrap.Mode = mode`。
- **手动驱动**：`Update()` 调 `_rootGroup.Update()`（没有 `_rootGroup` 就 `_world.Update()`）。宿主要在自己的 `MonoBehaviour` 里每帧调。
- `IFrameworkInstaller`：`void Install(World world)` 一个方法，宿主把"装输入系统/装 UI 管理器/注册配置表"之类拆成 installer。
- **跨端**：这条路径没有现成的 `InitializeAsync`（World 由宿主自己 new）。宿主必须在
  `new FrameworkWorld(...)` **之前**先 `await HYC.Framework.IO.StreamingAssets.PreloadAsync()`
  （PC / iOS 上是 no-op），否则 WebGL / Android 上建 World 时读不到配置。

### 2. FrameworkModeBootstrap（按模式扫系统）

```csharp
var boot = new FrameworkModeBootstrap();
await boot.InitializeAsync("Framework World");   // 内部 new World(..., Game) + AppendWorldToCurrentPlayerLoop
```

> 同步版 `boot.Initialize(...)` 仍可用，但**仅限 PC / macOS / Linux / iOS**（WebGL / Android 读不到数据）。

`InstallSystems(world, mode)` 流程：

```
GetAllSystems(Default, false)
  ├─ 名字以 "Unity." 开头                      → 直接加入（引擎系统）
  ├─ 有 [FrameworkModeOnly(模式)] 且模式匹配   → 加入
  ├─ 有 [FrameworkMode(模式)]     且模式匹配   → 加入
  └─ 其它（无模式标记）                        → 加入
  ↓
AddSystemsToRootLevelSystemGroups(world, systemIndexs)
  └─ 在世界里盖一个模式单例：
       mode == Sim → AddComponent<FrameworkMode_Sim>(e)
       否则        → AddComponent<FrameworkMode_Live>(e)
```

⚠️ 模式过滤是**白名单叠加**：`FrameworkModeOnly` 表示"**仅**该模式"，`FrameworkMode` 表示"该模式才装"，**两者都没标记的系统在所有模式下都装**。

### 3. 游戏自己的 ICustomBootstrap

`FrameworkBootstrap` 和 `FrameworkModeBootstrap` 都**明确不实现 `ICustomBootstrap`**——刻意避免被 Entities 自动扫描发现、与游戏自己的 bootstrap 冲突。游戏要自动引导就：

- 在自己 `ICustomBootstrap.Initialize` 里 `new FrameworkWorld(...)` / `FrameworkModeBootstrap().Initialize(...)`；或
- 用 `FrameworkBootstrap.Initialize(defaultWorldName)`（静态 `Installers` 列表 + `BootMode`）。

⚠️ **`ICustomBootstrap.Initialize` 是同步签名，无法 `await`**——所以要走框架的 World 装配，请在
`ICustomBootstrap` 里只做「标记 + 转发」，把真正的引导放到一个 `async void Start()` 的
MonoBehaviour 里 `await ...InitializeAsync(...)`（见上文「跨端数据加载契约」的示例）。
Unity 的 `ICustomBootstrap` 在 WebGL 上无法等待预取，这是它本身的限制，不是框架能绕过的。

---

## 模式

```csharp
static class SimulationBootstrap
{
    public const int Live = 1;
    public const int Sim  = 2;
    public static int Mode { get; set; } = 0;
    public static bool IsSimulated => Mode == Sim;
}
```

| 标记 | 含义 |
| --- | --- |
| `FrameworkModeOnlyAttribute(int)` | 系统仅在该模式安装 |
| `FrameworkModeAttribute(int)` | 系统在该模式安装（其余模式不装） |
| `FrameworkMode_Live` / `FrameworkMode_Sim` | 模式单例 tag（`IComponentData`），系统用 `RequireForUpdate` 按模式开关 |

`SimulationState`（运行时单例）：`Mode` / `Beat`（sim 模式每固定步 +1）/ `ElapsedSeconds`。`SimOnly` tag 标记"只在离线模拟期跑的系统"。

---

## UI 适配与导航参数（`UIManager.Setting`）

`UIManager.Setting`（static，`UIManagerSetting`）是 UI 适配 / 导航参数的**唯一真源**：

| 参数 | 默认 | 落到哪 |
| --- | --- | --- |
| `ReferenceResolution` | 1920×1080 | UI 适配参考分辨率（`ResetUIFitData` / `ResetWindowFit` 实时读） |
| `MaximumResolution` | 3440×1440 | 最大分辨率（与参考取 `Vector2.Max`） |
| `NavigationKeyDeathArea` | 0.6 | 摇杆死区（`AbsUISystem.UpdateByFocusManager` 实时读） |
| `NavigationKeyDelayTime` | 0.5 | 同方向连发延迟 |
| `NavigationKeyRepeateCount` | 10 | 每秒重复次数（间隔 `1f / count`） |

宿主持家在引导期定制——`FrameworkBootstrapBase` 子类的 `Configure` 钩子里改字段
（彼时 `WireUI` 已建好 `UIManager`，`Setting` 必非空；static + 实时读，赋值即时生效）：

```csharp
protected override void Configure(World world)
{
    // UI 不是按 1920×1080 设计时才需要：
    UIManager.Setting.ReferenceResolution = new Vector2(2560, 1080);
}
```

输入资产不走这里：覆盖 `FrameworkBootstrapBase.BindHotkeyAsset` 绑定自有
`InputActionAsset`（默认生成 WASD/方向键 + Enter/Space/Esc 的运行时资产，已绑则幂等跳过）。

> 历史：曾有一个 `StartupSetting` ScriptableObject（AA 加载 → entity object singleton →
> `UIManager.OnCreate` 读取）作为这套参数的远程赋值源，因 10 个字段中 6 个无消费方、
> 生产端从未接线而已移除（见 CHANGELOG）。将来若需数据驱动，把这些字段搬进
> `FrameworkSettings`（Resources 模式）即可，`UIManager.Setting` 作为真源不变。

---

## 全局设置（FrameworkSettings）

`ScriptableObject`，单例懒创建；游戏可加字段。

```csharp
FrameworkSettings.Instance.defaultLanguage = "Default";
FrameworkSettings.Instance.targetFramerate = 60;
FrameworkSettings.Instance.vSync = true;
FrameworkSettings.Instance.remoteLogLevel = 2;
GameSettings.Current;                 // 别名入口
settings.ApplyCommandLine(args);     // 解析 --framerate=120 / --language=English / --v-sync=false
```

⚠️ `LoadDefaults` 是空实现——**目前不自动从 Resources 读**，单例就是默认值（除非宿主手动赋值或调 `ApplyCommandLine`）。

---

## 命令行 / 启动参数

`GameCommandArgs`（单例，`Environment.GetCommandLineArgs()` 解析）：

| 参数 | 暴露属性 |
| --- | --- |
| `--account` | `account` / `openID` |
| `--token` | `token` |
| `--appid` | `appId` |
| `--serverip` | `serverIp` |
| `--serverport` | `serverPort` |
| `--lang` | `lang` |
| `--width` / `--height` | `width` / `height`（`hasScreenSizeArg` 判断是否同时给了） |
| `--fullscreen` | `fullScreen`（`-1`=用游戏设置，`0`=窗口，`1`=全屏） |

⚠️ 重复参数会 `LogError("解析到重复的命令 : {command}")`，后者丢弃（`TryAdd` 失败）。

---

## 仅场景运行（调试用：跳过引导）

**痛点**：宿主 `ICustomBootstrap` 一旦进 Play 就会被 Entities 调用，把整套框架引导（配置 / UI / 音频 / 本地化 / 行为树）全拉起来。想单独调一个场景（如 HYCSimple 的某个 Demo、某个 Authoring 片段）也会被整个项目启动淹没。

**方案**：框架在主工具栏**中间区**注入了一个 `▶ Scene` 按钮（tooltip：`Play Scene Only (skip framework bootstrap)`）。点它 = 只跑当前场景、跳过框架引导。

### 机制（已对照源码实证）

1. 按钮点击（`Editor/PlaySceneToolbarButton.cs`，反射注入 `ToolbarZonePlayMode` 中间区、外套 `flexGrow=1 + 居中` dock 实现水平居中；2022.3.35f1c1 的 `UnityEditor.CoreModule.dll` UTF-16 字符串实证三区：`ToolbarZoneLeftAlign` / `ToolbarZonePlayMode` / `ToolbarZoneRightAlign`）：
   - 置位 `SessionState["HYC.Framework.PlaySceneOnly"]`（**跨域重载存活**）+ 直接置位运行时闸门 `BootstrapGate.ShouldSkip = true`（覆盖「关闭域重载」的情形）；
   - `EditorApplication.isPlaying = true`。
2. 进 Play 后宿主 `ICustomBootstrap.Initialize` 被调 → **三个入口顶部都查 `BootstrapGate.ShouldSkip`，为真即 `return false`**（不建 World、不装系统、不读 Audio）：
   `FrameworkBootstrap.Initialize` / `FrameworkModeBootstrap.Initialize` / `FrameworkBootstrapBase.Initialize`（第三条是宿主最常用的继承基类，宿主 bootstrap 直接继承它）。
3. Entities `DefaultWorldInitialization.Initialize` 在 `ICustomBootstrap.Initialize` 返回 `false` 时**自动建默认世界**（`new World(defaultWorldName, Game)` + 默认系统 + `AppendWorldToCurrentPlayerLoop`），`World.DefaultGameObjectInjectionWorld` 照常有效 → 场景里的 MonoBehaviour（如 `HYCSimpleSceneBootstrap`）拿到合法默认世界照常跑。
4. 退出 Play 时 `PlaySceneToolbarButton` 清掉 `ShouldSkip` + `SessionState`，保证下次普通 Play 走完整引导。

> 实证：`com.unity.entities@1.0.16/Unity.Entities/DefaultWorldInitialization.cs:140`
> `if (bootStrap != null && bootStrap.Initialize(defaultWorldName)) { ... return; }`
> → 走到 `new World(...)` 默认世界。返回 `false` 即跳过、退回默认世界。

### 契约（宿主零改动的前提）

- **宿主必须把框架 `Initialize` 的返回值透传给 Entities**：`return new FrameworkBootstrap().Initialize(name);` 或 `return boot.Initialize(name);`。框架在内部顶部查闸门，宿主无需任何额外代码。
- 完全自定义、且不调框架 `Initialize` 的宿主 bootstrap，想要此能力可自己 `if (HYC.Framework.Runtime.BootstrapGate.ShouldSkip) return false;`（可选）。
- 关闭域重载时：点击直接置位静态 `ShouldSkip`，无需 SessionState 中转，同样生效。

### 已知待验证点（进 Unity 实测，勿凭推断）

- 跳过宿主后默认世界会自动创建框架里**未**标 `[DisableAutoCreation]` 的 system（如挂在 `UpdateGroup_Root` 的 `FrameworkCleanupSystem`）。若 `UpdateGroup_Root` 不是 player-loop 根组，可能刷 warning。Demo 系统本身都是 `[DisableAutoCreation]`（由 `HYCSimpleSceneBootstrap` 手动驱动），不受此影响。该 warning 是否出现需在真实工程进 Play 验证。

---

## 注意事项 / 坑

1. **`UIManager` / `LocalizationBlobSystem` 等都是 `[DisableAutoCreation]`**——只有引导期手动创建/扫描才会进 World。纯 installer 组合时记得把 `UIManager` 加进更新列表（`FrameworkModeBootstrap` 的 `GetAllSystems(Default)` 会抓到它，但 `FrameworkWorld` 不会自动抓，要 installer 里 `world.GetOrCreateSystemManaged<UIManager>()`）。
2. **顺序关键**：A0 装单例 → A1 加载配置 → A3 跑玩法 → B9 刷 UI。在 A3 之前查配置会查不到（没注册）。
3. **`FrameworkModeBootstrap.Initialize` 会把 World 加进 player loop**（`AppendWorldToCurrentPlayerLoop`），所以**不要再手动每帧 `Update()`**，否则一帧跑两次。`FrameworkWorld` 才需要手动 `Update()`。
4. **模式单例只盖一个**：`Sim` 模式盖章 `FrameworkMode_Sim`，否则 `FrameworkMode_Live`。`Live` 是默认兜底（包括 `Sim` 之外的任何 mode 值）。
5. **`FrameworkBootstrap.BootMode` 默认 `SimulationBootstrap.Live`**（`= 1`）——没改的话即使你以为在 sim 也是 live。
6. **`IFrameworkInstaller` 列表是静态的**（`FrameworkBootstrap.Installers`），多次 `Initialize` 会重复装，注意只在启动时调一次。
7. **`BakerGlue` 是 authoring→entity 的辅助**（非引导核心）：`AddItems<TBuffer>` 批量加 buffer、`AddIfPresent` 条件加组件。写在 Baker 里省样板。
8. **WebGL / Android 上不要用 `System.IO` 读 StreamingAssets**（`File` / `Directory.Exists` / `Directory.GetFiles`）：浏览器无文件系统、Android 文件在 APK 压缩包内，都读不到。判存在性用 `StreamingAssets.Exists(相对路径)`，列目录用 `StreamingAssets.GetKeysUnder(相对目录)`；引导用 `InitializeAsync`。平台判据是 `NeedsPreload` 而不是 `IsWebGL`。
