# 59 · Cinemachine 分屏与双世界（使用指南）

> 对象：宿主游戏里「两个玩家分屏」以及「两侧看到的东西不一样」这类需求。
> **本文只写怎么用**：选哪条路、怎么装配、有哪些 API、踩什么坑。
> 原理与调研（为什么只能这么做、业界怎么做的）见 [58-Cinemachine进阶-接缝合成与双世界-设计](./58-Cinemachine进阶-接缝合成与双世界-设计.md)；
> rig prefab、机位、`CinemachineRigBinder` 见 [57-Cinemachine相机装配](./57-Cinemachine相机装配.md)。

---

## 一句话

**两半需不需要拼成一张连续画面？**
需要 → 合成路径（`CinemachineDualView`，双 RT，**2× 像素**）；
不需要 → `Camera.rect` 路径（`CinemachineViewSplitter`，**1× 像素**）。

---

## 零、30 秒选型

| 你的需求 | 用哪个 | 像素 | 接缝形状 |
| --- | --- | :-: | --- |
| 两个玩家各看自己的地图，两块屏互不相干 | `CinemachineViewSplitter`（默认 `Viewport`） | 1× | 只能轴对齐：竖 / 横 / 2×2 / 画中画 |
| 同屏 → 分屏来回切（进门分、打完合） | `CinemachineViewSplitter.SetLayout` | 1× | 同上 |
| 两个玩家在同一张图里，只是各看各的 | `CinemachineDualView`（`Composite`） | 2× | 任意角度 / 曲线 / 缝隙 / 羽化 |
| 两侧看到的东西不一样（一屏黑一屏白、左半箱子右半蘑菇） | `CinemachineDualView` + `CinemachineDualVariant` | 2× | 任意 |
| 只想要「斜着切」「弯着切」「中间留条缝」 | `CinemachineDualView` | 2× | 任意 |
| 视口小窗（画中画、监控画面） | `CinemachineViewSplitter`（`PictureInPicture`） | 1× | 矩形 |

> ⚠️ **选错代价**：合成路径要渲两遍整屏，且 `RenderMode` **只能单向降档**（`DowngradeToViewport`），
> 降档那一下画面会跳。所以「两块地图不相干」这种需求**不要**上合成。

---

## 一、装配

### 1.1 公共前提：rig 的结构（两条路都一样）

```
Rig A/                     ← CinemachineRigBinder（分屏调度器认它）
├─ Camera                  ← Camera + CinemachineBrain（+ AudioListener，全场景只留一个）
└─ vcam_Xxx                ← CinemachineCamera（机位）
```

- **机位与 `Camera` 必须平级**。机位是位姿来源、`Camera` 是出图设备；
  把机位挂成 `Camera` 的子物体，Brain 每帧写回 `Camera` 的位姿会经父子关系反喂给机位 ——
  跟随机位收敛发飘、**固定机位直接原地不动**。
- rig prefab 用菜单 `Cinemachine/New Rig` 生成（见 57 号），或代码里现搭。
- 每个 rig 一个 `CinemachineBrain`（合成路径的镜像侧除外，见 1.3）。

### 1.2 路线 ①：完全分离（`Camera.rect`）

```csharp
using HYC.Framework.Cinemachine;
using UnityEngine;

public sealed class SplitBootstrap : MonoBehaviour
{
    [SerializeField] private CinemachineRigBinder m_RigA;   // 左半：A 世界
    [SerializeField] private CinemachineRigBinder m_RigB;   // 右半：B 世界
    [SerializeField] private Transform m_PlayerA;
    [SerializeField] private Transform m_PlayerB;

    private CinemachineViewSplitter m_Splitter;

    private void Start()
    {
        // 场景里预先挂好就用现成的，没挂就补一个
        m_Splitter = GetComponent<CinemachineViewSplitter>();
        if (m_Splitter == null)
            m_Splitter = gameObject.AddComponent<CinemachineViewSplitter>();

        m_Splitter.Register(m_RigA);              // 每实例化一份 rig 就登记一次
        m_Splitter.Register(m_RigB);
        m_Splitter.Focused = m_RigA;              // 同屏/画中画谁是主视角，也决定 AudioListener 留哪份
        m_Splitter.SetLayout(ViewLayout.Vertical); // 左右分屏

        m_RigA.SetTargets(m_PlayerA, m_PlayerA);  // 机位的 Follow / LookAt
        m_RigB.SetTargets(m_PlayerB, m_PlayerB);
    }
}
```

关键点：

- **两侧内容互不相干 = 两台相机的 `cullingMask` 不同**（各自只看自己那套美术/地图）。
  ⚠️ 是**掩码**不是层号：写 `1 << 28`，不是 `28`（写层号本身得到的是层 2/3/4，
  结果是「只有清屏色、什么都不渲染、也不报错」，见 §四 坑 3）。
- 两半**不需要对齐**，所以两台相机位姿可以完全不同，接缝处不会有折痕。
- 布局切换：`Single`（回同屏，另一侧相机停渲染但状态不丢）/ `Vertical` / `Horizontal` / `Quad` / `PictureInPicture`。
- 画中画 / 2×2 是「矩形槽位」，**只能**走这条路（半平面接缝表达不了四宫格）。

### 1.3 路线 ②：同场景视觉分叉（接缝合成）

```csharp
using HYC.Framework.Cinemachine;
using Unity.Cinemachine;
using UnityEngine;

public sealed class DualStyleBootstrap : MonoBehaviour
{
    [SerializeField] private CinemachineRigBinder m_RigA;
    [SerializeField] private CinemachineRigBinder m_RigB;
    [SerializeField] private Transform m_PlayerA;
    [SerializeField] private Transform m_PlayerB;

    private CinemachineDualView m_View;

    private void Start()
    {
        m_View = gameObject.AddComponent<CinemachineDualView>();
        m_View.Bind(m_RigA, m_RigB, m_PlayerA, m_PlayerB);

        // 固定机位（相机不跟人跑）必须关；跟着人跑的那种保持默认 true
        m_View.AutoFraming = false;

        m_View.Separate(SplitStyle.Vertical);

        // 前提：两台相机看同一张图。合成器建好之后才解析得到两侧相机
        var mirror = gameObject.AddComponent<CinemachineDualMirror>();
        mirror.Refresh();

        // B 侧是镜像相机：把它的 Brain 关掉（rig prefab 默认带一个）。
        // 两个 Brain 会抢同一台相机 —— 位姿由镜像组件写、Brain 又每帧改写，画面会抖
        var brainB = m_RigB.GetComponentInChildren<CinemachineBrain>(true);
        if (brainB != null)
            brainB.enabled = false;
    }
}
```

关键点：

- `CinemachineDualView` 会自动挂 `CinemachineViewSplitter` + `CinemachineSeamCompositor`，**一个组件就够**。
- **镜像侧不要再挂 Brain**：位姿由 `CinemachineDualMirror` 每帧从 A 复制，两个 Brain 会抢同一台相机。
- 固定机位 = `CinemachineCamera` 不挂 Body / Aim（相机就停在机位位姿上）；
  跟随机位 = `CinemachineFollow`（Body）+ `CinemachineRotationComposer`（Aim）。
- 两侧内容分叉靠**层**：公共内容放共享层（两侧 `cullingMask` 都含），
  需要「长得不一样」的物体做两份，分别放 A 层 / B 层，由 `CinemachineDualVariant` 分派。

> **同一个世界、两个角色（最常见的本地双人）也走这条路**，只是不需要层分叉：
> 两台 rig 各带 Brain + 各自跟随一个角色的机位，`AutoFraming` 保持默认 `true`
> （框架会把每个角色推到自己那半的中间），输出通道由 `CinemachineViewSplitter` 自动分流。
> 两台相机看**整个世界**即可（`cullingMask = ~(1 << 5)` 排除 UI 层），
> 一眼就能看出「两半是同一张地图的不同角度」。样例 ① `SameWorldSplitDemo` 就是这套。

### 1.4 装配检查清单

- [ ] 机位与 `Camera` 平级（不是 Camera 的子物体）
- [ ] 每个 rig 一个 `CinemachineBrain`（合成的镜像侧除外）
- [ ] `AudioListener` 全场景只有一个
- [ ] 多份 rig 用 `CinemachineViewSplitter` 登记（它会自动分流 `OutputChannel`，别忘了用它）
- [ ] ② 的两台相机必须同一位姿 + 同一 FOV —— 用 `CinemachineDualMirror`，不要手摆

---

## 二、API 速查

### 2.1 游戏层门面 `CinemachineDualView`（最常用）

把「分屏 / 合并 / 染色 / 跟人」收成几个动词。适合关卡脚本、Boss 战脚本直接调。

| 成员 | 说明 |
| --- | --- |
| `Bind(rigA, rigB, playerA = null, playerB = null)` | 运行期装配（Addressables 实例化之后调） |
| `Separate(SplitStyle style, float duration = 0f)` | 开始分屏；`duration` 为过渡时长，0 = 立刻 |
| **`CombineAroundPlayers()`** | **合屏 = 两台相机慢慢并拢到两人中点并拉远**，两侧画面真的并成一张；再 `Separate` 会各自撤回。要求 `AutoFraming = true` |
| `Combine(float duration = 0f, int keepSide = 0, bool releaseComposite = false)` | 合成一个屏 —— **溶化**保留某一侧（画面不移动）；`keepSide` 0 = 留 A；`releaseComposite` = 顺手退回 1× 像素（单向，之后不能再分屏） |
| `Tint(int player, Color)` / `Tint(Color a, Color b)` / `ClearTint()` | 单侧 / 双侧染色（alpha = 强度） |
| `Follow(Transform target)` | 换 `FollowTarget` 的跟随对象 |
| `Style` / `IsSeparated` / `SeparatedChanged` | 当前切法 / 是否已分开 / 分开状态变化事件 |
| `SplitWeight` / `MinShare` / `SideAShare` | 比例权重 / 最小占比 / 实时 A 侧占比（可直接驱动 UI） |
| `MergeBlend` | 合屏进度 0~1（读出来做 UI / 音效，或判断有没有并拢完） |
| `SetOrbit(int side, Vector3 offset)` / `GetOrbit(int side)` | 让某一侧相机绕自己的角色转视角（见 §3.8）；两侧角度可以不同，合屏时自动混成公共视角 |
| `AutoFraming` / `Framing` | 是否自动接管取景 / 取景协调器 |
| `SetAngle(float)` / `SetSeam(in SeamState)` / `CurrentSeam` | 只改角度 / 整条接缝赋值 / 读当前接缝 |
| `SetGlow(float width, Color, float pulseHz = 0)` / `ClearGlow()` | **让接缝看得见**：沿切线叠一条加法发光带（建议宽 0.015~0.03） |
| `Splitter` / `Compositor` | 拿到下面两层（需要细调时） |

`SplitStyle` 五个值：

| 值 | 接缝 | 典型场景 |
| --- | --- | --- |
| `Vertical` | 竖线（左右） | 两人一左一右 |
| `Horizontal` | 横线（上下） | 两人一上一下 |
| `Diagonal` | 斜线，角度取 `DiagonalAngle` | 演出 / 风格化 |
| `FollowPlayers` | 垂直于两人连线，**角度与位置每帧自动算** | Lego 式动态分屏 |
| `FollowTarget` | 保持当前角度，整体平移到某个角色屏幕上 | 镜头跟着一个人走 |

> **取景所有权**：`AutoFraming = true`（默认）时，`CinemachineDualView` 会自动挂
> `CinemachineSeamFraming`，由它**独占**两侧 rig 的 `Follow` / `LookAt` 每帧写锚点。
> 这时候不要再去调 `binder.SetTargets(...)`，两边会互相覆盖。固定机位把 `AutoFraming` 关掉即可。

**合屏有两种，别用错**：

| 想要的效果 | 用哪个 | 前提 |
| --- | --- | --- |
| 两个人**走到一起变成一景**，主相机同时照顾俩人 | `CombineAroundPlayers()` | `AutoFraming = true`，且 `Bind` 时给了两个角色 |
| 只留一侧的画面（另一侧淡出），画面本身不移动 | `Combine(duration, keepSide)` | 无 —— 固定机位也能用 |

`CombineAroundPlayers` 的做法是：两台 rig 的 `Follow` 从「各自角色」换成框架托管的跟随锚点，
合屏时把锚点插值到**两人中点**、把两侧的跟随偏移混合成**同一个方向与长度**（方向取两侧平均，
长度按两人间距放大；FOV 不碰）。最终两台相机的位置 / 朝向 / 距离完全一致，左右两半才是同一张图。
过程中的快慢由 `Framing.GroupSpeed` 控制（默认 3.5，越大越快）。

> 这一步顺带解决了「两个人视角本来就不一样」的情况：各转各的环绕角（§3.8）之后再点合屏，
> 两侧照样收敛成一景，不需要宿主自己先把角度对齐。

### 2.2 视口调度 `CinemachineViewSplitter`（底层）

| 成员 | 说明 |
| --- | --- |
| `Register(binder)` / `Unregister(binder)` | 登记 / 注销 rig（注销会还原整屏与原始 FOV） |
| `Focused` | 同屏 / 画中画的主视角，也决定 `AudioListener` 留哪份 |
| `SetLayout(ViewLayout)` / `SetLayout(ViewLayout, params binder[])` | 切布局 / 指定槽位顺序 |
| `LayoutChanged` | 布局变化事件（重建 HUD 挂这里） |
| `FovPolicy` | `None` / `KeepHorizontal`（默认）/ `Overscan`，见下文 |
| `ManageAudioListener` | 默认开：只让 `Focused` 那份监听器工作 |
| `ManageOutputChannels` | 默认开：自动给每份 rig 分配 CM 输出通道（多 rig 必须分流） |
| `RenderMode` / `DowngradeToViewport()` | 当前实现（只读）/ 单向降档回 `Camera.rect` |
| `SetSeam(in SeamState)` | 直接给接缝（会自动切到合成路径） |

**FOV 补偿**（`FovPolicy`）：竖切时半屏宽高比变窄，`KeepHorizontal` 会把各机位的
`Lens.FieldOfView` 纵向放大以保持横向视野（看起来像「拉宽了」，是预期行为）。
`Quad` / `PictureInPicture` 宽高比与整屏一致，不补偿。

> FOV 补偿写的是**机位的 `Lens`**：`Camera.fieldOfView` 会被 CM 每帧按机位覆盖，
> 直接改它没用。rig 里没有 `CinemachineCamera` 时补偿不生效（没东西可写）。

### 2.3 接缝 `SeamState` + `CinemachineSeamCompositor`

`SeamState` 是纯结构体，所有分屏布局都是它的特例：

| 字段 | 含义 |
| --- | --- |
| `Point` (`Vector2`) | 接缝过屏幕的哪一点，视口坐标（原点**左下**，0.5/0.5 = 居中） |
| `Angle` (`float`) | **法线**方向角（度）：0 = 竖线，90 = 横线，45 = 斜线 |
| `Curvature` (`float`) | 弯曲量（-1..1），抛物线，正值向外鼓 |
| `GapWidth` / `GapColor` | 缝隙宽度 / 颜色（挖一条缝把折痕藏起来） |
| `GlowWidth` / `GlowColor` / `GlowPulse` | **接缝光带**：宽度（屏幕比例）/ 颜色（alpha = 强度）/ 呼吸频率 Hz。加法混合，纯视觉、不参与 `SignedDistance` |
| `Feather` | 边缘羽化宽度 |
| `Noise` / `NoiseSpeed` | 抖动强度 / 速度（风格化用） |
| `Push` | 把接缝整体推出屏幕（合并 / 单屏用） |
| `Crossfade` | 两侧叠化（配合 `Push` 做「溶成一个屏」） |

便捷成员：`SeamState.Vertical` / `.Horizontal` / `.SingleA` / `.SingleB`；
`SignedDistance(viewport)`（玩法判定，&lt;0 = A 侧）、`SideAShare()`、
`NormalOffset()`、`GetNormalSpan(out min, out max)`、`ClampShare(minShare)`。

`CinemachineSeamCompositor` 常用成员：

| 成员 | 说明 |
| --- | --- |
| `Source` | `Authored` / `FollowPlayers` / `FollowTarget` / `Transform` |
| `Seam` / `SetSeam(...)` / `SeamChanged` | 读 / 写 / 变化事件 |
| `SplitWeight` / `MinShare` / `SideAShare` | 跟随模式下的取点权重 / 最小占比 / 实时占比 |
| `RenderTargetA` / `RenderTargetB` | 两侧整屏画面（可拿去贴到世界里的屏幕做嵌套视口） |
| `SetFollowTargets(a, b, reference)` / `SetFollowTarget(t, reference)` / `SetTransformSource(t)` | 换驱动源 |
| `SetSideTint(...)` / `ClearSideTint()` / `GetSideTint(player)` | 染色 |
| `SetCrossing(rt, width)` | 接跨缝层（由 `CinemachineSeamCrossing` 调用） |
| `Suspend()` / `Resume()` | 挂起 / 恢复合成（降档用） |

### 2.4 两侧不同外观

| 组件 | 作用 | 关键 API |
| --- | --- | --- |
| `CinemachineDualVariant` | 一个逻辑体配两套视觉体，按层分给两台相机 | `VariantA` / `VariantB`（赋值即递归设层）、`SetLayers(层号A, 层号B)`、`Apply()`、`SetSideVisible(a, b)`、静态 `SetLayerRecursively(root, layer)` |
| `CinemachineDualMirror` | 让两台相机看同一张图（复制位姿 / FOV / 正交尺寸 / 近远裁剪） | `Refresh()`、`Apply()`、`Source` / `Target` / `IsMirroring` |
| `CinemachineDualWorld` | 按层名直接拼两侧 `cullingMask`（不想自己算掩码时用） | `Bind(rigA, rigB)`、`Apply()`、`CrossVisible` / `SetCrossVisible(a→b, b→a)`、`SideALayerName` / `SideBLayerName` / `SharedLayerName` |
| `CinemachineSeamCrossing` | 跨缝物体用第三台相机重投影，让压线处接得上 | `StartCrossing()` / `StopCrossing()`、`BandWidth`、`Target` |

### 2.5 玩法查询 `CinemachineSeamTracker`

| 成员 | 说明 |
| --- | --- |
| `Bind(compositor, projectionCamera, target)` | 绑一个要追踪的目标 |
| `SideChanged` | 目标换侧事件（内部做 epsilon 比较，不会每帧刷） |
| `IsOnSideA` / `HasSample` | 当前在哪侧 / 是否已采到样本 |
| `static SignedDistanceOnScreen(seam, camera, worldPos)` | 世界点相对接缝的有符号距离（目标在相机背后返回 `NaN`） |
| `static IsWorldPointOnSideA(seam, camera, worldPos)` | 是否在 A 侧 |

### 2.6 合并演出 `CinemachineSeamMerge`

只做「接缝推出去 + 两侧叠化」，相机位姿切换交给 `CinemachineBrain` 的 `DefaultBlend`。

```csharp
var merge = gameObject.AddComponent<CinemachineSeamMerge>();
merge.Play(new MergeProfile
{
    Duration = 0.6f,
    PushTarget = -1.2f,        // 把接缝推出屏幕（负 = 推向左）
    CrossfadeTarget = -1f,     // 叠化到位
    FeatherTarget = 0.06f,
});
merge.Completed += () => Debug.Log("合成完毕");
```

`PlayReverse(profile)` 反着播（分屏），`Progress` / `IsPlaying` 可驱动表现。
`MergeProfile.MergedCameraName` **已实现**：合并时按 rig 内机位名切机位（留空则只收接缝、不切）。

---

## 三、常见需求（复制即用）

### 3.1 Boss 关：分屏 → 单侧变色 → 合屏

```csharp
// 开打：跟着两人竖着切
m_View.Separate(SplitStyle.FollowPlayers, 0.35f);

// 抬左手 → 1 号玩家那半压黑；抬右手 → 2 号那半压白
m_View.Tint(0, new Color(0f, 0f, 0f, 0.85f));
m_View.Tint(1, new Color(1f, 1f, 1f, 0.6f));

// 收招：清染色 + 两人并拢成一景（相机跟住俩人一起拉远）
m_View.ClearTint();
m_View.CombineAroundPlayers();

// HUD 跟着变
m_View.SeparatedChanged += on => m_Hud.SetSplit(on);
```

> **染色绑的是玩家编号，不是屏幕左右** —— 横切、竖切、斜切、跟着人切，变色的永远是那个人那侧。

### 3.2 分屏线压比例（一大一小）

```csharp
// 跟随模式：改取点权重（0.5 = 对半，< 0.5 → A 那半更窄）
m_View.SplitWeight = 0.3f;
m_View.MinShare = 0.2f;      // 兜底：任一侧不低于 20%

// 固定接缝（Vertical/Horizontal/Diagonal）：把接缝推到目标占比
var seam = m_View.CurrentSeam;
seam.GetNormalSpan(out var min, out var max);
var span = max - min;
if (span > 1e-5f)
{
    seam.GetFrame(out _, out var normal);
    seam.Point += normal * (min + 0.3f * span);   // 0.3 = A 侧占 30%
    m_View.SetSeam(seam);
}
```

`SideAShare` 是实时值，可以直接拿来驱动 UI 或音效。

### 3.3 斜切 / 弯曲 / 中间留缝 / 羽化

```csharp
m_View.DiagonalAngle = 28f;             // 先定角度（BuildSeam 读的是当前值）
m_View.Separate(SplitStyle.Diagonal);   // 再切

// 已经切好之后只想改角度：
// m_View.SetAngle(28f);

var seam = m_View.CurrentSeam;
seam.Curvature = 0.25f;     // 弯曲（抛物线）
seam.GapWidth = 0.006f;     // 挖一条几像素的黑缝（顺便藏住跨缝折痕）
seam.GapColor = Color.black;
seam.Feather = 0.004f;      // 边缘羽化
m_View.SetSeam(seam);
```

### 3.4 「香蕉飞到右屏变成回旋镖」

```csharp
// 逻辑根：状态机 / 碰撞体 / 玩法都在这里
var variant = logicRoot.AddComponent<CinemachineDualVariant>();

// 层没命名（原型常用无名层）→ 直接给层号；已命名则跳过这行，改填 Inspector 的层名
variant.SetLayers(28, 29);

variant.VariantA = bananaVisual;     // A 相机的层
variant.VariantB = boomerangVisual;  // B 相机的层
```

**零代码**：物体飞过接缝时按像素择一，切到哪边就显示哪边。
三个铁律：逻辑体唯一、碰撞体只在逻辑根、玩法代码不引用变体。

> ⚠️ 设层只是「放到哪一层」，**还得让相机看得见那一层**：把 28 / 29 加进两个 rig 的
> `cullingMask`（共享层两边都加），或者用 `CinemachineDualWorld` 按层名自动拼掩码。

### 3.5 切机位（近景 / 远景 / 越肩）

```csharp
m_RigA.SwitchTo(RigKind.ThirdPerson);   // 或 SwitchTo("vcam_Close")
```

两侧分别切即可；跟着两人切时建议两侧用同类机位，视角差越小、跨缝折痕越轻。

### 3.6 每半屏自己的 HUD

全屏 Canvas 不受 `Camera.rect` 影响，锚点按屏幕比例算即可：

- 竖切：A 侧锚点用 `Screen.width * 0.5` 作为右边界
- 横切：A 侧锚点用 `Screen.height * 0.5` 作为上边界
- 合成模式：屏幕坐标不变（接缝只是渲染层，UI 不参与裁切）
- 每格自己的 3D 血条：改用 world-space Canvas + `Camera` 指定为对应 rig 的相机

### 3.7 省像素（低端机兜底）

```csharp
// 打完 Boss 回到单屏，顺便退出合成路径
m_View.Combine(0.4f, keepSide: 0, releaseComposite: true);

// 或者直接单向降档（画面会跳一下，之后不可逆）
m_Splitter.DowngradeToViewport();
```

### 3.8 让玩家在 Game 视图里拖分屏线 / 拖角色

调试、demo、沙盒关卡经常要「用手把线拉一拉」。两个关键点：**先让线看得见**，再**把鼠标换算成视口坐标**。

```csharp
void Awake()
{
    // ① 一条数学上存在的线在画面上是看不见的 —— 不开光带，用户不知道往哪儿按
    m_View.SetGlow(width: 0.022f, color: new Color(0.25f, 0.92f, 1f, 0.9f), pulseHz: 0.55f);
}

// ② 拖接缝：把接缝推到「正好过鼠标点」。绝对定位 ⇒ 天然幂等，不用记增量
void DragSeam(Vector2 viewport)          // viewport 原点左下，与 SeamState 一致
{
    var seam = m_View.CurrentSeam;
    seam.GetFrame(out _, out var normal);        // normal 是**法线**方向
    seam.Point += normal * Vector2.Dot(viewport - seam.Point, normal);
    m_View.SetSeam(seam);
}
```

鼠标坐标换算（IMGUI 用 `Event.current`，**不要**用 `Input.GetMouseButton*`——工程把
Active Input Handling 设成 "Input System Package" 时后者直接抛 `InvalidOperationException`）：

```csharp
void OnGUI()
{
    var e = Event.current;
    if (!m_PanelRect.Contains(e.mousePosition))            // 别抢面板按钮的点击
    {
        var vp = new Vector2(e.mousePosition.x / Screen.width,
                             1f - e.mousePosition.y / Screen.height);   // GUI 原点在左上
        if (e.type == EventType.MouseDown && e.button == 0) m_Drag = Pick(vp);
        else if (e.type == EventType.MouseDrag && m_Drag)   DragSeam(vp);
        else if (e.type == EventType.MouseUp)               m_Drag = false;
    }
    DrawPanel();
}
```

判定「有没有点在线上」要把法线换到**像素空间**再量距离（视口空间的 x/y 被宽高比拉伸，
直接比数值会让竖线和横线的手感差一倍）：

```csharp
var normalPx = new Vector2(normal.x * Screen.width, normal.y * Screen.height);
var distPx   = Mathf.Abs(seam.SignedDistance(vp)) * normalPx.magnitude;
var onSeam   = distPx < 34f;      // 34 像素抓取半径
```

拖角色（固定机位时最简单）：`m_Camera.ViewportPointToRay` → 与该角色所在水平面求交。

```csharp
var ray = m_Camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f));
if (new Plane(Vector3.up, new Vector3(0f, target.position.y, 0f)).Raycast(ray, out var enter))
    target.position = ray.GetPoint(enter);
```

> ⚠️ **跟随类模式（`FollowPlayers` / `FollowTarget`）下别拖接缝** —— 它的位置每帧由驱动源重算，
> 拖完下一帧就被覆盖。那种模式下让用户拖**角色**，接缝自然跟着走。

#### 右键转视角（各自绕自己的角色）

双人分屏里「两个角色各看各的角度」是常态，所以要能给**每一侧**单独转视角。做法是把
「环绕角」算成跟随偏移交给框架，框架在合屏时会自动把它们混成一个公共视角：

```csharp
void Orbit(int side, Vector2 delta)          // side: 0 = A, 1 = B
{
    var rot = Quaternion.AngleAxis(yaw, Vector3.up)
            * Quaternion.AngleAxis(pitch, Vector3.right);
    m_View.SetOrbit(side, rot * m_BaseOffset);   // m_BaseOffset = 建 rig 时的 FollowOffset
}
```

- `m_BaseOffset` 用 `m_View.GetOrbit(0)` 取（`Bind` 时从 rig 的跟随组件读回来的），别自己猜。
- `CinemachineFollow` 默认 `BindingMode = WorldSpace`，所以 `FollowOffset` 就是**世界空间向量**，
  绕它转等于绕角色做球面环绕；俯角/距离都保持不变。
- **一侧转了、另一侧没转，合屏依然能并拢** —— `CombineAroundPlayers` 会把两侧的偏移混合到
  同一个方向与长度，相机位姿收尾完全一致。这是框架的保证，不需要宿主先手动对齐两个角度。
- 合屏之后要一起转：只转一侧会立刻把刚合好的画面又劈成两半。

---

## 四、坑（用户层）

1. **两块不相干的地图别上合成。** 2× 像素换不到任何好处，而且降档单向。
2. **路线 ① 不要挂 `CinemachineDualView`。** 它的装配引导会调 `SetSeam`，而 `SetSeam` 把
   `RenderMode` 切成 `Composite`（`DowngradeToViewport` 回不来）。只用 `CinemachineViewSplitter` 就够。
3. **`Camera.cullingMask` 是「掩码」不是「层号」。** 传层号 `28` 得到的是 `0b11100` = 层 2/3/4 ——
   相机什么都看不到：**只有清屏色、一点几何都不渲染，而且不报任何错**（样例 ① 早期就这么错过一次）。
   必须写 `1 << layer`；想「什么都看，只排除 UI」就写 `~(1 << 5)`。
4. **机位不能做成 `Camera` 的子物体。** 见 §1.1 —— 跟随机位发飘、固定机位原地不动。
5. **多份 rig 各带 Brain 必须分流输出通道。** CM 3 的 Brain 是从**全局**机位表里挑优先级最高的，
   只按 `OutputChannel` / `ChannelMask` 过滤、**不看层级**；不分流就是「两半画面一模一样且不报错」。
   `CinemachineViewSplitter` 已自动处理（第 1 份 `Default`、第 2 份起 `Channel01…`），
   自己手搓多机位系统时要照办。
6. **固定机位记得 `AutoFraming = false`。** 取景偏移是给「每人跟一个角色」设计的，
   会给固定机位加横向偏移，把两台相机推开。
7. **跨缝那一条带必然有折痕。** 两台相机位姿不同 ⇒ 同一物体投影不同，这是双相机分屏的几何本质。
   三档处方（按省事度）：① `GapWidth ≈ 0.006` 挖几像素黑缝藏起来；
   ② 上 `CinemachineSeamCrossing` 重投影带；③ 设计规避（快速掠过 / 闪光遮）。
   经验法则：**折痕明显程度 ∝ 两台相机在接缝处的视角差**。
8. **`CinemachineDualVariant` 的层**：默认按**层名**解析（`HYC.WorldA` / `HYC.WorldB`），
   无名层要用 `SetLayers(层号, 层号)`。正式项目建议老老实实命名层。
9. **双变体不是「两套完整世界」**：只有需要长得不一样的物体才做两份，公共内容放共享层。
10. **FOV 补偿只对机位生效**（写 `Lens.FieldOfView`）；直接改 `Camera.fieldOfView` 会被 CM 覆盖。
11. **合成没有全屏后处理**：AA 要在 RT 上做，Bloom 不跨接缝；两套相机共用一份阴影预算。
12. **两侧同时出声要自己做**：Unity 只有一个 `AudioListener`（`ManageAudioListener` 默认只留 `Focused` 那份）。
    要双人各自听到自己那侧，把另一侧音源 pan 到对应声道，或走两套 AudioMixer 分组。
13. **画面上下颠倒 = `FlipY` 多翻了一次。** 合成画在 `ScreenSpaceOverlay` Canvas 的 `RawImage` 上，
    UI 四边形 UV 与 RT 内容同向，**默认不需要翻**（`Compositor.FlipY` 默认 0）。
    早期版本按 `SystemInfo.graphicsUVStartsAtTop` 又翻了一遍，Windows 上就是整幅画面倒过来、
    但接缝位置仍然正确（接缝数学用的是视口空间，没跟着翻）—— 这个组合很好辨认。
    只有自己 `Graphics.Blit` 的自定义后端才需要把它设成 1。
14. **接缝默认是看不见的。** 不加 `SetGlow` 的话，「线在哪」只有数学上有；做可拖拽 / 需要玩家理解分屏时务必开光带。
15. **「合屏」别用 `Combine` 凑。** `Combine` 的 `Crossfade = ±1` 是**全屏混合权重**，效果是整屏溶化到某一侧、
    画面本身不移动 —— 两个人不会因此靠在一起；用 `Push` 把接缝推出去更是「一侧被挤走」的观感。
    要「两人并拢成一景」就调 `CombineAroundPlayers()`。反例：`CinemachineSeamFraming` 关掉（固定机位）时
    它没有两人可并拢，会打警告并自动退回 `Combine` 的溶化 —— 那是正确行为，不是 bug。
16. **`CombineAroundPlayers` 之后 `Separate` 回不到你手拖的位置。** `Separate` 一律走 `BuildSeam` 把接缝
    重新对中，所以回来的是「按当前切法的居中分屏」，不是合并前那条偏移过的线。

---

## 五、验收清单

- [ ] 同屏 → 分屏 → 同屏反复切，两侧画面都正常（不出现半边黑 / 整屏黑）
- [ ] 拖角色到接缝上：正常切开（不该整块消失或两边都画一遍）
- [ ] 切到 `Quad` / 画中画再切回 `Vertical`，画面还正常
- [ ] 染色跟着玩家而不是跟着屏幕左右（横切 / 斜切再验一遍）
- [ ] 比例压到极限时，两侧都不低于 `MinShare`（人不会消失）
- [ ] 点「合屏」：两台相机**慢慢并拢**（不是一侧被挤走），收尾时两侧画面完全一致、两人都在画面里
- [ ] 合屏后点「分屏」：两台相机各自撤回自己角色，接缝回到居中
- [ ] 合屏过程中拖接缝 / 拖角色不会把相机拽飞（`MergeBlend` 收尾应回到 0 或 1）
- [ ] 右键转视角：鼠标在哪一侧就转哪一侧，两侧能转成完全不同的角度
- [ ] **两侧角度不同时点合屏，依然并拢成一景**（收尾两台相机位姿一致）
- [ ] 合成路径下 Console 无「重复 AudioListener」告警
- [ ] 低端机降档后画面正常（虽然会跳一下）

---

## 六、可运行样例

### 6.1 先导入，否则 Project 窗口里根本看不到

样例放在包内的 **`Samples~/HYCSimple/`**。**目录名末尾的 `~` 表示 Unity 完全不导入它** —— 它在磁盘上真实存在，但 Project 窗口里不会出现、也不参与编译。所以「包里有场景，工程里找不到」是正常的，必须显式导入一次：

| 方式 | 操作 | 落到 |
| --- | --- | --- |
| **菜单（推荐）** | `HYC Framework / Samples / Import HYCSimple` | `Assets/HYCSimple/` |
| 官方 PM | Package Manager → HYC Framework → **Samples** → HYCSimple → **Import** | `Assets/Samples/com.hyc.framework/<版本>/HYCSimple/` |

两条路**只能选一条** —— 同时存在会让同一批类型编译两次（CS0101 重复定义）。菜单方式每次**全量重拷**（先删 `Assets/HYCSimple/`），适合跟着仓库改样例的人；PM 方式落到版本号目录，升级后要重导。

> 若菜单方式执行后 Project 窗口仍为空：`Assets → Refresh` 或重启 Unity（新拷入的目录树有时要靠 `ImportRecursive` 才被收进 AssetDatabase）。

### 6.2 两个场景

导入后位于 `Assets/HYCSimple/Cinemachine/Scenes/`（或 `Assets/Samples/.../HYCSimple/Cinemachine/Scenes/`），打开即 Play，全部用内置 primitive 搭（不引外部美术）：

| 场景 | 演示 |
| --- | --- |
| `SameWorldSplitDemo.unity` | ① **一个大场景**（地面 / 中央高塔 / 两排拱门 / 散落箱子）+ 红蓝两个角色，两台相机各跟一个 ⇒ 左右两半是同一张地图的两个角度。**左键拖接缝 / 拖角色、右键转那一侧的视角、接缝带呼吸光带**（§3.8） |
| `DualStyleSplitDemo.unity` | ② 同场景两层美术（同位 A=Cube / B=Sphere）+ 任意接缝 + 占比压缩 + 单侧染色；同样能拖接缝 / 拖角色 |

> ⚠️ **两个场景在 Play 之前是「空壳」，这是故意的，不是坏的。**
> 场景里只有**一个空物体**（挂着 demo 脚本），Hierarchy 看不到相机、看不到方块，Game 视图显示
> "No cameras rendering" —— 全部内容由脚本在 **`Awake`** 里用 `GameObject.CreatePrimitive` 现搭
> （相机 / 机位 / Brain / 地面 / 高塔 / 方块 / 灯光）。**点 Play 之后** Hierarchy 会立刻长出一整棵树。
> 这样做的理由：样例不依赖任何美术资源、也不用维护一份容易与应用方 Unity 版本打架的场景 YAML；
> 想改成「场景里预先摆好」的形态，照 `SameWorldSplitDemo.BuildRig()` 的结构手搭一遍即可，逻辑完全一样。

细节见 `Samples~/HYCSimple/README.md` 与 `Samples~/HYCSimple/Cinemachine/AI-NOTES.md`。

---

## 相关文档

- [57-Cinemachine相机装配](./57-Cinemachine相机装配.md) —— rig prefab、机位配方、binder、机位访问器
- [58-Cinemachine进阶-接缝合成与双世界-设计](./58-Cinemachine进阶-接缝合成与双世界-设计.md) —— 原理、调研证据、决策记录、完整坑清单
- [40-输入与热键](./40-输入与热键.md) —— 两人各用一套输入（分屏之后一定要做）
- [31-窗口系统](./31-窗口系统.md) —— HUD / 层表
