# 60 · 引导与世界（Bootstrap）

## 职责

在启动时**组装一个 framework World**：装基础系统（输入/相机/UI/本地化/设置）、加载配置表、选择模式（Live / Sim），并提供一个统一的更新入口。

命名空间：引导在 **`HYC.Framework.Runtime`**，更新组在 **`HYC.Framework.Dots`**。

一句话：**三选一地创建 World —— `FrameworkWorld`（installer 组合）/ `FrameworkModeBootstrap`（按模式扫系统）/ 或游戏自己的 `ICustomBootstrap`。**

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

### 2. FrameworkModeBootstrap（按模式扫系统）

```csharp
var boot = new FrameworkModeBootstrap();
boot.Initialize("Framework World");   // 内部 new World(..., Game) + AppendWorldToCurrentPlayerLoop
```

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

## 启动级配置（StartupSetting）

`[CreateAssetMenu(menuName = "HYC Framework/Startup Setting")]`：

| 字段 | 默认 | 落到哪 |
| --- | --- | --- |
| `InputActionAsset` | — | 传给 `HotkeyManager.BindAsset`（**必须先设**，否则 UI 热键全失败） |
| `NavigationKeyDeathArea` | 0.6 | `UIManager` 摇杆死区 |
| `NavigationKeyDelayTime` | 0.5 | 同方向连发延迟 |
| `NavigationKeyRepeateCount` | 10 | 每秒重复次数 |
| `ReferenceResolution` | 1920×1080 | UI 适配参考分辨率 |
| `MaximumResolution` | 3440×1440 | UI 适配最大分辨率 |
| `HotkeyIcons` | — | `SpriteAtlas`，按键提示图标 |
| `HotkeyHoldThreshold` | 0.2 | 长按阈值 |
| `HotkeyStyle1~5` | — | 5 套 `BaseHotkeyElement` 提示样式预制体 |

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

## 注意事项 / 坑

1. **`UIManager` / `LocalizationBlobSystem` 等都是 `[DisableAutoCreation]`**——只有引导期手动创建/扫描才会进 World。纯 installer 组合时记得把 `UIManager` 加进更新列表（`FrameworkModeBootstrap` 的 `GetAllSystems(Default)` 会抓到它，但 `FrameworkWorld` 不会自动抓，要 installer 里 `world.GetOrCreateSystemManaged<UIManager>()`）。
2. **顺序关键**：A0 装单例 → A1 加载配置 → A3 跑玩法 → B9 刷 UI。在 A3 之前查配置会查不到（没注册）。
3. **`FrameworkModeBootstrap.Initialize` 会把 World 加进 player loop**（`AppendWorldToCurrentPlayerLoop`），所以**不要再手动每帧 `Update()`**，否则一帧跑两次。`FrameworkWorld` 才需要手动 `Update()`。
4. **模式单例只盖一个**：`Sim` 模式盖章 `FrameworkMode_Sim`，否则 `FrameworkMode_Live`。`Live` 是默认兜底（包括 `Sim` 之外的任何 mode 值）。
5. **`FrameworkBootstrap.BootMode` 默认 `SimulationBootstrap.Live`**（`= 1`）——没改的话即使你以为在 sim 也是 live。
6. **`IFrameworkInstaller` 列表是静态的**（`FrameworkBootstrap.Installers`），多次 `Initialize` 会重复装，注意只在启动时调一次。
7. **`BakerGlue` 是 authoring→entity 的辅助**（非引导核心）：`AddItems<TBuffer>` 批量加 buffer，`AddIfPresent` 条件加组件。写在 Baker 里省样板。
