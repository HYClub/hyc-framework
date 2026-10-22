# HYCSimple · HYC Framework 示例集

> 框架自带的最小可用示例。
>
> **两种引入方式（二选一，别同时用，否则同一批类型编译两次 → CS0101）：**
> - **官方**：Package Manager → **Samples → HYCSimple → Import**，落到 `Assets/Samples/com.hyc.framework/<版本>/HYCSimple/`。
>   前提：包已被解析且 `package.json` 声明了 `samples`（新增后**需重启 Unity** 才重新解析）。
> - **菜单快捷**：`HYC Framework/Samples/Import HYCSimple` → **全量重拷**到 `Assets/HYCSimple/`（普通目录，一定被导入）。
>   ⚠️ 不要把示例拷进 `Assets/Samples/<包名>/`：那是 Package Manager **保留区**，包未解析时 Unity 扫到 `<包名>` 一层就停，
>   Project 窗口展开为空**且不报错**（官方文档点名的 "samples field in the package manifest is empty" 症状）。
>
> 本目录（`Samples~`）的 `~` 后缀让 Unity **不编译、不默认导入**它；只有导入动作才把它带进工程。
>
> **⚠️ 示例默认「不自动运行」（动手前先读）**
> 所有 `*DemoSystem` 都标了 `[DisableAutoCreation]` —— Unity **不会**自动把它们拉进 World。
> 原因：框架的 Manager（Localization / Config / BT）是**进程级静态单例**，示例 System 一旦偷偷自启，
> 就会在宿主工程毫无察觉的情况下改写它们（`Reload` 是整表替换：成功读到另一份目录即覆盖；
> 失败只报错并保留原数据）。由此定下三条硬规矩：
> 1. 示例 System 一律 `[DisableAutoCreation]`，由 **`HYCSimpleSceneBootstrap`**（MonoBehaviour）显式手动 Update。
> 2. 示例**不得抢占**宿主已加载的全局单例：`HYCSimpleLocSampleData.CanTakeOver` 会在
>    「全局表已有非示例数据」时跳过 `Reload`，并打印醒目警告。
> 3. 想把示例跑起来，请**新建一个空场景**，把 `HYCSimpleSceneBootstrap` 挂到任一 GameObject 上再 Play；
>    宿主工程什么都不挂就**零副作用**。
>
> **编译 / Assembly 说明**：示例自带两个 asmdef，导入后编译成独立程序集（**不污染你的 `Assembly-CSharp`**）：
> - `HYCSimple.asmdef`（根，运行时，含 Attr/Config/BT/Loc/Scene）：`allowUnsafeCode: false` ——
>   `Loc` 示例已改用 Unity 内置 `BlobBuilder.AllocateString`（2022.3 / Entities 1.0 已按 UTF-8 真实字节数分配，中文不再截断，且是**安全 API**），
>   因此**不需要**在工程的 Player Settings 里开「Allow unsafe code」，也不会出现 CS0227。
> - `BT/Editor/HYCSimple.Editor.asmdef`（仅编辑器）：放 `HYCSimpleBTTreeBuilder` / `HYCSimpleAttrSampleAssets` / `HYCSimpleSampleDataCreator`，引用 `HYC.Framework.Editor` + `HYC.Framework.Runtime`。
> ⚠️ **不要删除这两个 asmdef**：删了示例就会退回 `Assembly-CSharp`，与旧 PM 副本同批类型重复 → CS0101。
>
> **示例含真实数据资产（不是只手搓代码）**：进 Unity 后先跑
> `HYC Framework/Samples/创建全部示例资产`（或单独跑 `BT/创建示例树并导出`、`Attr/创建示例属性资产`），
> 会生成：
>   · `Assets/HYCSampleTrees/HYCSimpleDemoTree.asset`（BT 树）+ 导出 `StreamingAssets/BTBlob/*.blob`
>   · `Assets/HYCSimpleSampleAssets/GameAttrCatalog.asset`（属性目录）+ `GameHeroSet.asset`（属性预设）
> 之后进 Play，BT / Attr 示例系统就会读取这些资产跑真实链路。
>
> **写给 AI**：每个子系统目录都有一份 `AI-NOTES.md`，讲清「怎么接入、菜单路径、雷区」。
> 这是框架对外教学/上手的事实入口，注释与说明都面向「先读源码/样例再写」。

---

## 目录结构

```
Samples~/HYCSimple/
├─ README.md                 ← 本文件（总览 + 注释规范）
├─ Attr/                     ← 通用属性配置（已完成）
│  ├─ AI-NOTES.md            ← 属性系统接入笔记（含两页编辑器说明 + 雷区）
│  ├─ GameAttrKey.cs         ← 示例生成访问器：枚举
│  ├─ GameAttrKeyMap.cs      ← 示例生成访问器：枚举→哈希/类型 映射
│  ├─ GameAttrBag.cs         ← 示例生成访问器：类型安全读写
│  └─ HYCSimpleAttrDemoSystem.cs ← 运行时演示（加载 GameHeroSet.asset → InitFromSet 真实链路，回退手写 buffer）
├─ Config/                   ← 配置接入（已完成）
│  ├─ AI-NOTES.md
│  ├─ HycSimpleItemRow.cs    ← 示例行结构体（blittable，等价于生成器产物）
│  ├─ HYCSimpleConfigDemoSystem.cs ← 运行时演示（从 items.csv 读真实数据 → Build → Register → Get/TryGet）
│  └─ Resources/items.csv   ← 真实物品数据（8 行，配置即数据）
├─ BT/                       ← 行为树接入（已完成）
│  ├─ AI-NOTES.md
│  ├─ HYCSimpleBTDemoSystem.cs   ← 运行时演示（LoadAll + 挂 RunningBT 让解释器驱动）
│  ├─ HYCSimpleEnemyBTNodes.cs   ← 运行时自定义节点（条件/攻击/巡逻，反射自动注册，树真实执行）
│  └─ Editor/
│     ├─ HYCSimpleBTTreeBuilder.cs      ← 编辑器菜单：程序化建树 + 导出 Blob（生成 HYCSimpleDemoTree.asset）
│     ├─ HYCSimpleAttrSampleAssets.cs  ← 编辑器菜单：生成 GameAttrCatalog/GameHeroSet.asset
│     └─ HYCSimpleSampleDataCreator.cs ← 编辑器菜单：一键「创建全部示例资产」
├─ Loc/                      ← 多语言接入（已完成）
│  ├─ AI-NOTES.md
│  ├─ HYCSimpleLocSampleData.cs   ← 运行时自包含数据生成（规避 CJK 截断；含「不抢占宿主表」守卫 CanTakeOver）
│  └─ HYCSimpleLocDemoSystem.cs   ← 运行时演示（Reload/GetText/SetLanguage/HasKey）[DisableAutoCreation]
├─ Scene/                    ← 场景/Bootstrap 串联（已完成）
│  ├─ AI-NOTES.md
│  └─ HYCSimpleSceneBootstrap.cs   ← MonoBehaviour：唯一入口，手动 Update 各 DemoSystem（挂到空场景 GameObject 才跑）
├─ Input/                    ← 输入组件化接入（已完成）
│  ├─ AI-NOTES.md
│  ├─ SampleInputAssetBuilder.cs   ← 运行时构造示例 InputActionAsset（UI/Sense 两 map，无 .inputactions 落盘）
│  ├─ SampleInputPlayerSpawner.cs  ← A0：建玩家实体 + 挂 UI/Sense 组件 + Register（自动运行，未标 DisableAutoCreation）
│  ├─ SampleInputReaderSystem.cs   ← B9：每 1 秒打印输入快照（自动运行）
│  ├─ UI/UIInputComponent.cs        ← 生成器产物（auto-generated，已预生成；含 AddTo 扩展方法）
│  ├─ Sense/SenseInputComponent.cs  ← 生成器产物（auto-generated，已预生成）
│  └─ Editor/SampleInputSetup.cs    ← 菜单：HYC Simple/Input/Generate Sample Input Components
└─ Cinemachine/             ← 双屏相机样例（两个可运行场景，已完成）
   ├─ AI-NOTES.md           ← 判断标准 + 复现步骤 + 雷区 + 验证清单
   ├─ DemoFactory.cs        ← 搭场景工具：primitive + 克隆自带材质 + 建 CM 机位（不用外部素材、不改 Tags and Layers）
   ├─ SameWorldSplitDemo.cs ← 例 ①：同一个大场景、红蓝两个角色、各跟各的相机（接缝合成路径，Game 视图可拖）
   ├─ DualStyleSplitDemo.cs ← 例 ②：同场景两侧视觉不同 + 任意接缝 + 压缩比例（接缝合成路径）
   └─ Scenes/
      ├─ SameWorldSplitDemo.unity ← 打开后直接 Play
      └─ DualStyleSplitDemo.unity ← 打开后直接 Play
```

---

## 「给 AI 看的注释」规范（本 Sample 的统一约定）

1. **每个子系统一份 `AI-NOTES.md`**：结构固定为
   - 一句话心智模型
   - 端到端步骤（菜单路径逐条核对源码，不编造）
   - 编辑器/窗口使用要点
   - 「雷区」清单（写代码前必读）
   - 验证清单（进 Unity 后）
2. **示例代码顶部 block comment**：说清「这文件是什么、正常由谁生成、本例为何手写在 Sample 里」。
3. **代码内联注释**：只对「非显然 / 易错」处注释（如 Float 走 bits、枚举序数、Set 不钳制、CJK 截断），不逐行灌水。
4. **命名框架中立**：用 `HP`/`MP`/`Hero`/`Monster`/`EquipSlot`/`Item`/`Price` 这类通用概念；
   **禁止**把使用方工程的具体玩法（如某游戏的 `ItemKind`/`WavePointStrategy`）写进框架样例。
5. **示例自包含可编译**：尽量不依赖手写的 `.asset`/`.prefab`/`.unity` 二进制（易错）；
   用代码在运行时构造等价数据，并在 `AI-NOTES.md` 里讲清「真实项目走哪条路」。

---

## Cinemachine 双屏样例（两个场景）

两个场景都**不依赖任何外部美术**：全部用 Unity 内置 Cube / Sphere / Plane 在 `Awake` 里搭出来，
用 `DemoFactory` 统一上色（克隆 primitive 自带材质，所以 Standard 与 URP 都能跑）。
层用**无名层 28 / 29**，不需要你去 Project Settings 改 Tags and Layers。

> ⚠️ **打开场景时 Hierarchy 里只有一个空物体、Game 视图写着 "No cameras rendering" —— 这是正常的。**
> 场景文件只是「一个挂着 demo 脚本的空壳」，相机 / 机位 / Brain / 台子 / 柱子 / 方块 / 灯光
> 全在 `Awake` 里现搭。**点 Play 之后**那棵树立刻长出来，Game 视图就是分屏画面。
> 好处：样例零美术资源、也不用维护一份容易和你的 Unity 版本打架的场景 YAML。

**相机结构走框架推荐的那套**：底层 `Camera` 只负责出图（挂 `CinemachineBrain`），
视角来自 **`CinemachineCamera` 机位** —— 跟随机位挂 `CinemachineFollow` + `CinemachineRotationComposer`，
固定机位什么都不挂（CM 直接拿机位的 Transform 当画面位姿）。
层级是 `Rig/` 下 `Camera` 与 `vcam_*` **平级**：机位是位姿来源，Camera 是出图设备，
把机位挂到 Camera 底下会让 Brain 每帧写回的位姿经父子关系反喂给机位（固定机位会永远停在原地）。

### 例 ① `Scenes/SameWorldSplitDemo.unity` —— 一个大场景、两个角色、一人一半

> 打开场景 → Play → **在 Game 视图里**按住发光的接缝线拖，或者按住红 / 蓝方块拖。
> 结论：左右两半是**同一张地图**（同一座中央高塔、同一条地面），只是从左 / 右两个角色背后看过去。
> 拖红方块时左半屏的取景跟着平移；右半屏的地图是同一个，只是机位在蓝方块那边。

- 走的是接缝合成路径（`ViewRenderMode.Composite`）：两台相机各自渲**整屏**到 RT，
  再由 shader 按接缝逐像素择一 ⇒ 接缝可以任意角度 / 任意占比，还能在 Game 视图里直接拖。
- **两台相机都看整个世界**（`~(1 << 5)`，只排除内置 UI 层）—— 「两半是同一张地图」不靠 cullingMask 实现。
- 每侧 rig 各一台跟随机位（`vcam_Follow Cam`），**各带一个 Brain**；输出通道由
  `CinemachineViewSplitter` 自动分流（第 1 份 `Default`、第 2 份起 `Channel01`），
  两个 Brain 抢同一台机位会让两半画面一模一样且不报错。
- `AutoFraming` 保持默认 `true`：合成模式下相机渲整屏再裁，不补偿的话角色正好落在接缝上；
  框架的接缝取景会把每个角色推到自己那半的中间。
- 面板按钮：`分屏 Split → Separate(Style)` / **`合屏 Merge → CombineAroundPlayers()`** —— 合屏是
  **两台相机慢慢并拢到两人中点并拉远**，两侧画面真的并成一张（不是把一侧挤走）；
  `竖着切 / 横着切 / 斜着切` → `Separate(SplitStyle)`；
  `接缝光带：开/关` → `SetGlow` / `ClearGlow`；`两个角色回原位` → 拖太远时一键复位；
  `两侧视角复位` → 右键转出来的角度清零。
- **右键按住拖动 = 转鼠标那一侧的视角**（左右转圈、上下抬压，`SetOrbit`）。
  两侧可以转成不同角度；点「合屏」时框架会把两侧偏移混成同一个方向与长度，
  所以**视角不同也能并拢**成一景。
- 想省到 1 倍像素（`Camera.rect` 路线）也在框架里，但接缝只能轴对齐、不能任意拖，
  见 `Documentation~/59-…使用指南.md` §1.2；切换是**单向**的，选了就别回头。

### 例 ② `Scenes/DualStyleSplitDemo.unity` —— 同一个场景，两侧看到的东西不一样

> 打开场景 → Play → 在 Scene 视图里拖 `Player A` / `Player B`，再点面板按钮。
> 结论：**同一位置上的方块，左半是红的 Cube、右半是绿的 Sphere**；
> 拖动方块让它跨过切线，切到哪边就显示哪边的外形 —— **逐像素切换，没有一行交叉检测代码**。

- 走的是接缝合成路径（`ViewRenderMode.Composite`）：两台相机各自渲**整屏**到 RT，
  再由 shader 按接缝逐像素择一，所以任意角度 / 曲线 / 带缝隙的接缝都支持。
- 场景是**一份**：地面、背景墙、柱子都在共享层（Default），两边都看得见；
  只有需要「长得不一样」的物体才做两份（A 层 / B 层），由 `CinemachineDualVariant` 负责分派。
- 视角是 `Rig A` 下的**一台固定机位**（`vcam_Fixed Cam`，不挂 Body/Aim）。
  `Rig B` 没有 Brain、也没有机位：它的相机位姿由 `CinemachineDualMirror` 每帧从 A 侧复制
  —— 两个 Brain 抢同一台相机会打架，所以刻意只留一个；这也是合成路径成立的前提
  （两半必须是同一张图切开的）。
- 固定机位下 `AutoFraming = false`（取景偏移是给「各跟各的角色」用的，会把固定机位推开）。
- 面板按钮覆盖：
  - `分屏 Split / 合并屏 Combine` → `Separate(Style)` / `Combine(0.6f)`
    （本样例是固定机位、没有两人可并拢，所以只能**溶化**保留 A 侧；想看并拢用例 ①）
  - `竖切 / 横切 / 斜切 / 跟着两人切` → `CinemachineDualView.Separate(SplitStyle)`
  - `A 窄 30% / 对半 / A 宽 70%` → 固定接缝时把接缝沿法线推到目标占比（用 `GetNormalSpan` 反算）；
    跟随模式下改 `SplitWeight`
  - `A 侧压黑 / B 侧压白`、`清掉染色` → `Tint` / `ClearTint`

### 两个例子的分界线在哪

| | ① 同一场景双人分屏 | ② 同场景视觉分叉 |
|---|---|---|
| 两半为什么不一样 | 相机**跟着不同角色** ⇒ 视角不同 | 相机位姿相同 ⇒ **内容层**不同 |
| 渲染路径 | 双 RT + 接缝合成（Composite） | 双 RT + 接缝合成（Composite） |
| 像素成本 | 2× | 2× |
| 两半关系 | 同一张地图的两个角度，**不需要逐像素对齐** | 必须是同一张图，**位姿必须一致** |
| 接缝形状 | 任意角度 / 占比，Game 视图可拖 | 同左 |
| 相机 | 两台各自跟随（各带 Brain，通道分流） | 一台固定机位 + `CinemachineDualMirror` 复制 |

### 四个坑（写样例时踩到的）

- `CinemachineDualVariant` 默认按**层名**（`HYC.WorldA` / `HYC.WorldB`）解析层，无名层解析不出来。
  样例用 `SetLayers(28, 29)` 直接给层号；你要用层名的话，先去 Tags and Layers 里命名。
- **机位必须与 `Camera` 平级**（别做成 Camera 的子物体）。机位当子物体时，Brain 每帧把位姿写回
  Camera，父子关系又把 Camera 的位姿反喂给机位 —— 跟随机位会收敛得发飘，固定机位直接原地不动。
- **多份 rig 各带 Brain 必须分流输出通道**：CM3 的 Brain 是从**全局**机位表挑最高优先级的
  （只看 `OutputChannel` / `ChannelMask`，不看层级），两份 rig 都用默认通道会抢到同一台机位，
  **两半画面一模一样且不报错**。`CinemachineViewSplitter.Register` 现在自动分配
  （第 1 份 `Default`、第 2 份 `Channel01`…），`ManageOutputChannels` 可关。
- 样例的 rig 里有 `CinemachineCamera`，所以分屏调度器的 **FOV 补偿真的生效了**：
  默认 `FovPolicy.KeepHorizontal` ⇒ 竖切时每半屏的**纵向** FOV 会被放大、保持横向视野不缩水
  （看起来像「拉宽了」是预期行为）。想让它只裁不补，把 `splitter.FovPolicy` 设成 `None`。

---

## 状态

- ✅ Attr（属性）：示例 + AI-NOTES 已完成。
- ✅ Config（配置）：`HycSimpleItemRow` + `HYCSimpleConfigDemoSystem` + AI-NOTES 已完成。
- ✅ BT（行为树）：运行时 `HYCSimpleBTDemoSystem` + 编辑器建树导出 `HYCSimpleBTTreeBuilder` + AI-NOTES 已完成。
- ✅ Loc（多语言）：`HYCSimpleLocSampleData` + `HYCSimpleLocDemoSystem` + AI-NOTES 已完成。
- ✅ Scene（场景/Bootstrap）：`HYCSimpleSceneBootstrap` + AI-NOTES 已完成。
- ✅ Input（输入组件化）：`SampleInputAssetBuilder` + `SampleInputPlayerSpawner` + `SampleInputReaderSystem` + `Editor/SampleInputSetup` + 预生成 `UI/Sense` 组件 + AI-NOTES 已完成。
- ✅ Cinemachine（双屏相机）：`Scenes/SameWorldSplitDemo.unity`（一个大场景 + 两个角色，Game 视图可拖接缝/角色）+ `Scenes/DualStyleSplitDemo.unity`（同场景视觉分叉），机位全部由 `CinemachineCamera` 驱动，`DemoFactory` 统一搭场景。
- 🔒 **示例默认不自启**：所有 `*DemoSystem` 加 `[DisableAutoCreation]`，改由 `HYCSimpleSceneBootstrap`
  手动 Update；Loc 侧另加 `CanTakeOver` 守卫，宿主已加载自己的本地化表时**跳过 Reload**。
  详见文件头「示例默认不自动运行」一节。
- ⚠️ 全部示例**尚未在 Unity 编译验证**（框架侧 C# 也还在等 Unity 编译）；导入后请按各 `AI-NOTES.md` 的验证清单跑一遍。
- ⚠️ 版本漂移：`package.json` 当前 `version=3.0.0`，故官方 PM 导入落点为 `Assets/Samples/com.hyc.framework/3.0.0/HYCSimple/`；
  发版前记得核对 CHANGELOG 顶部与服务端版本号一致。
- 🛠️ 菜单快捷导入落点是 `Assets/HYCSimple/`（不是 `Assets/Samples/<包名>/`，规避 PM 保留区）；两路请二选一。
