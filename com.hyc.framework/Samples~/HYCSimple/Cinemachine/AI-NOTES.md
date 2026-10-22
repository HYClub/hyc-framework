# Cinemachine 双屏样例 · 接入笔记

> 面向「先读源码再动手」的人（含 AI）：为什么要两个例子、两个例子各自证明什么、以及在宿主工程里复现的步骤。

## 一句话心智模型

**两个样例都是「一个世界、一块地图」。** 差别只在「两半为什么不一样」：
① 是**位置不同**（两台相机各跟一个角色，看到的是同一张地图的不同角度），
② 是**外观不同**（位置完全一样，靠层切分让左右看到两套美术）。

| | ① 同一场景双人分屏 | ② 同场景视觉分叉 |
|---|---|---|
| 需求 | 一个大场景、两个角色，一人一半屏 | 场景一份、位置固定，两侧看到的东西不一样 |
| 两半为什么不同 | 相机跟着不同角色 ⇒ **视角不同** | 相机位姿相同 ⇒ **内容层不同** |
| 组件 | `CinemachineDualView`（`Composite`） | `CinemachineDualView`（`Composite`） |
| 像素 | 2× | 2× |
| 接缝 | 任意角度 / 占比，可在 **Game 视图直接拖** | 同左 |
| 相机 | 两台各自跟随，位姿**不同** | 必须**同一张图**，位姿由 `CinemachineDualMirror` 保证 |

> **框架还有一条更省的路**：`CinemachineViewSplitter` 的 `Viewport` 模式（`Camera.rect`，每帧 **1× 像素**），
> 代价是接缝只能轴对齐（竖 / 横 / 四宫格 / 画中画）、**不能任意拖**。样例不再演示它，
> 用法与取舍见 `Documentation~/59-…使用指南.md` §1.2。要用它请注意：
> **`Camera.cullingMask` 是「掩码」不是「层号」** —— 传 `28` 会得到层 2/3/4，画面全空且不报错（早期样例就这么错过）。

## 两个场景

| 场景 | 演示什么 | 打开后做什么 |
|---|---|---|
| `Scenes/SameWorldSplitDemo.unity` | **一个大场景**（地面 / 中央高塔 / 两排拱门 / 散落箱子）+ 红蓝两个角色，两台相机各跟一个（`Composite`，2× 像素） | Play → 直接在 **Game 视图**按住发光接缝拖分屏线、按住红/蓝方块拖角色；两半始终是同一张地图 |
| `Scenes/DualStyleSplitDemo.unity` | 一份共享场景 + 5 组「同位置 A 是 Cube、B 是 Sphere」的变体（`Composite`，2× 像素） | Play → 拖 `Player A/B` 让方块跨过切线，看外形逐像素切换；面板切接缝角度 / 占比 / 染色 |

面板按钮与 API 的对应：

| 按钮 | 调的是 |
|---|---|
| 分屏 Split / 合屏 Merge（① ） | `Separate(Style)` / **`CombineAroundPlayers()`** —— 两台相机慢慢收到两人中点并拉远，左右两半并成一张；再点「分屏」各自撤回，但接缝按当前切法**重新对中**（`BuildSeam` 里 `Point = (0.5,0.5)`），想切在别处得拖接缝 |
| 两侧视角复位（① ） | `SetOrbit(side, rot * 基准偏移)` 把 yaw / pitch 归零（右键拖出的角度全清） |
| 分屏 Split / 合并屏 Combine（② ） | `Separate(Style)` / `Combine(0.6f)` —— 本样例是**固定机位**（`AutoFraming = false`），没有两人可并拢，只能溶化保留 A 侧；想看并拢用 ① |
| 竖着切 / 横着切 / 斜着切（① ② ） | `CinemachineDualView.Separate(SplitStyle.Xxx)` |
| 跟着两人切（② ） | `Separate(SplitStyle.FollowPlayers)` —— 接缝跟着两人屏幕连线转 |
| 接缝光带：开 / 关（① ② ） | `SetGlow(width, color, pulseHz)` / `ClearGlow()` |
| 两个角色回原位（① ） | 直接写 `transform.position`（拖太远时一键复位） |
| A 窄 30% / 对半 / A 宽 70% | 跟随模式改 `SplitWeight`；固定接缝用 `GetNormalSpan` 反算后 `SetSeam` |
| A 侧压黑 / B 侧压白 · 清掉染色 | `Tint(Color, Color)` / `ClearTint()` |

## 在宿主工程里复现（以 ① 为例）

1. 给两个角色各配一份 rig（`Camera` + `CinemachineBrain` + `CinemachineCamera` 跟随该角色，
   或用菜单 `Cinemachine/New Rig` 生成 prefab）。两台相机都看**整个世界**
   （样例用 `~(1 << 5)` 排除内置 UI 层）——「两半是同一张地图」不靠 cullingMask 实现。
2. 场景里挂 `CinemachineDualView`，`Bind(rigA, rigB, playerA, playerB)`。
3. `Separate(SplitStyle.Vertical)`；`AutoFraming` 保持默认 `true`，框架会把每个角色推到
   自己那半的中间（合成模式下相机渲整屏再裁，不补偿的话角色正好落在接缝上）。
4. 想给玩家拖：`SetGlow(...)` 让接缝可见，再用 `OnGUI` + `Event.current` 判拖拽（§3.8 有完整样板）。
5. **两份 rig 各带一个 Brain 时必须分流输出通道** —— 框架的分屏调度器 `Register` 第 2 份 rig 时
   自动分配 `OutputChannels.Channel01`（`ManageOutputChannels` 可关）；两个 Brain 抢同一台机位
   ⇒ 两半画面一模一样且不报错。

② 的装配见 `Documentation~/58-Cinemachine进阶-接缝合成与双世界-设计.md` §4.9 —— 顺序是
**先 `AutoFraming = false`，再 `Separate`，最后 `mirror.Refresh()`**。

## 机位怎么摆（两个样例都是这套）

```
Rig A/                     ← CinemachineRigBinder（分屏调度器认它）
├─ Camera                  ← Camera + CinemachineBrain（+ AudioListener，只留一个）
└─ vcam_Follow Cam         ← CinemachineCamera + CinemachineFollow + CinemachineRotationComposer
```

- **机位与 `Camera` 必须平级**。机位是位姿来源、Camera 是出图设备；把机位挂成 Camera 的子物体，
  Brain 每帧写回 Camera 的位姿会经父子关系反喂给机位 —— 跟随机位收敛发飘、**固定机位直接原地不动**
  （它每帧「读到自己在哪、就写回自己在哪」）。
- **跟随机位** = `CinemachineFollow`（Body：目标 + 固定偏移）+ `CinemachineRotationComposer`（Aim：看向目标）。
  示例把阻尼设 0，拖动方块时相机立刻到位，看得清因果关系。
- **固定机位** = `CinemachineCamera` 什么都不挂：CM 每帧拿机位 Transform 当 `CameraState` 初值，
  没有管线组件就原样输出 ⇒ 相机停在这台机位的位姿上。
- **`vcam.Priority = 20` 要显式写**：`PrioritySettings` 的 `Enabled` 默认 false，
  只写 `Value` 不写 `Enabled` 的机位会被当成优先级 0（可能整台相机不生效）。
  `Priority = int` 的隐式转换会顺带把 `Enabled` 打开，所以直接用赋值即可。
- **一个 rig 一个 Brain**：合成路径下 B 侧是镜像相机（位姿由 `CinemachineDualMirror` 复制），
  那一侧不能再挂 Brain，否则两个 Brain 抢同一台相机。
- **多份 rig 各带 Brain 时，通道必须分流**。CM 3 的 Brain 是从**全局**机位表里挑优先级最高的，
  `IsValidChannel` 只看 `vcam.OutputChannel` 与 `brain.ChannelMask` 是否相交，**不看层级** ——
  两份 rig 都用默认通道时两个 Brain 会挑到同一台机位，两半画面一模一样且不报错。
  `CinemachineViewSplitter` 在 `Register` 时自动分配（第 1 份 `Default`、第 2 份 `Channel01`…），
  自己手搓多机位时也得照办。

## 雷区

1. **`Viewport`（`Camera.rect`）和 `Composite` 不能混着切。** `CinemachineDualView` 的装配引导会调
   `SetSeam`，而 `SetSeam` 把 `RenderMode` 单向切成 `Composite`（2 倍像素，`DowngradeToViewport` 回不来，
   切的时候画面还会跳）。选了 rect 路线就别挂 `CinemachineDualView`；两个样例都走 `Composite`。
   顺带记住 rect 的另一个硬伤：相机宽高比变成半屏，同垂直 FOV 下水平视野被压窄
   （16:9 全屏 hFOV≈91°，半屏只剩≈54°），两半**不是同一张图切开的** → 接缝必然错位。
   所以「要拼成一张连续画面」就必须 `Composite`。
2. **`Camera.cullingMask` 是「掩码」不是「层号」。** 直接传层号 `28`，得到的是 `0b11100` = 层 2/3/4 ⇒
   相机什么都看不到：**只有清屏色、一点几何都不渲染，而且不报任何错**（样例①早期就这么错过，
   被误判成「渲染坏了」）。必须写 `1 << 28`。现在两个样例都用 `~(1 << 5)`（除内置 UI 层外什么都看），
   从根上不会再犯这个错。
3. **② 必须关自动取景**（`AutoFraming = false`）：取景偏移是给「每人跟一个角色」设计的，
   固定机位下会把两台相机推开。
4. **层号 vs 层名**：`CinemachineDualVariant` 默认按**层名**（`HYC.WorldA` / `HYC.WorldB`）解析，
   而 `LayerMask.NameToLayer` 对无名层返回 `-1`。样例用无名层 28/29，所以走 `SetLayers(28, 29)`。
   正式项目建议老老实实命名层（`CinemachineDualWorld` 也按层名配 cullingMask）。
5. **机位要建在 `CinemachineRigBinder` 之前**（或建完调 `binder.Rescan()`）：binder 在 `Awake` 里
   一次性扫描机位并缓存，晚加的机位它看不见（`Cameras` 为空 ⇒ FOV 补偿也不生效）。
6. **FOV 补偿现在真的生效了**：`Splitter` 的 `FovPolicy`（默认 `KeepHorizontal`）只作用于 rig 里的
   `CinemachineCamera`。竖切时它把每半屏的**纵向** FOV 放大以保持横向视野，看起来像「镜头拉宽了」；
   想只裁不补就把 `splitter.FovPolicy = FovPolicy.None`。
7. **Viewport ↔ Composite 切换会踩两个框架坑**（本次已修）：`Camera.rect` 路径会把没进槽位的相机
   `enabled = false`，切到合成后没人再打开 ⇒ 那一半永远黑；被 `Suspend()` 过之后 `SetTargets` 对
   同一对 rig 幂等、不会自动恢复 ⇒ 整屏黑。现在由 `ApplyCameraTargets` 强制 `enabled = true`、
   `TryBindComposite` 显式 `Resume()` 兜住。
8. **样例不做物理**：`DemoFactory` 用 `DestroyImmediate` 移除 primitive 自带的 Collider
   （用 `Destroy` 会延迟到帧末，而同一帧里 `CinemachineDualVariant` 就会检查子树碰撞体并误告警）。

## 验证清单（进 Unity 后）

- [ ] **打开场景时 Hierarchy 只有一个空物体（"Same World Split Demo" / "Dual Style Split Demo"）、
      Game 视图显示 "No cameras rendering" —— 正常**，内容全在 `Awake` 里搭，Play 后才长出来。
- [ ] 打开 `SameWorldSplitDemo` → Play → 左右两半是**同一张地图**（同一座中央高塔、同一条地面），
      只是从左 / 右两个角色背后看过去 —— 不是两个不相干的世界。
- [ ] **画面是正立的**（地面在下、塔在上）。早期版本因为多翻了一次 Y，画面会整幅上下颠倒
      而接缝位置仍正确 —— 现在 `Compositor.FlipY` 默认 0，不该再出现。
- [ ] 接缝上有一条**会呼吸的暖黄色光带**（`SetGlow`），说明「这里能拖」；点面板「接缝光带」可开关。
- [ ] **在 Game 视图里按住光带拖动** → 分屏线跟手；竖切 / 横切 / 斜切都可拖。
- [ ] **在 Game 视图里按住红 / 蓝方块拖动** → 角色在地图里跑，那一半屏的取景整个平移。
- [ ] **右键按住拖动** → 鼠标所在那一侧的相机绕自己的角色转（左右转圈、上下抬压）；
      两侧可以转成完全不同的角度，这是正常的。合屏之后右键转 → **两侧一起转**。
- [ ] 两侧转到不同角度后点「合屏 Merge」→ 仍然能并拢成一景（框架把两侧偏移混成同一个方向与长度）。
- [ ] 拖一个角色往中间靠：它出现在接缝上被切成两半（各半裁的是同一张整屏图）。
- [ ] 点「横着切」→ 上下分屏；再点「竖着切」→ **两边都在**（不黑半屏，见雷区 7）。
- [ ] 点「合屏 Merge」（①）→ **两台相机慢慢并拢**：接缝两侧的内容逐渐对齐成同一张图，
      相机同时拉远到两人都在画面里；光带同步淡出。观感不是「一侧被挤走」。
- [ ] 合屏到位后点「分屏 Split」→ 两台相机各自撤回自己角色，接缝回到居中（不是回到你手拖的位置）。
- [ ] 点「两个角色回原位」→ 两个方块弹回初始位置。
- [ ] Console 无 `AudioListener` 重复告警（① 只有 Rig A 的相机挂了监听器）。
- [ ] Console 无「找不到机位」类告警 —— 两份 rig 各带 Brain，输出通道由 `Splitter` 自动分流。
- [ ] 打开 `DualStyleSplitDemo` → Play → 左右两侧同一位置分别是红 Cube / 绿 Sphere。
- [ ] 层级里 `Rig A/` 有 `vcam_Fixed Cam`，`Rig B/` 只有 `Camera`（无 Brain）—— 这是有意为之。
- [ ] 拖 `Player A` 让方块压在接线上：方块被切线逐像素切开（左半 Cube、右半 Sphere）。
- [ ] 切「斜切」→ 斜线两侧仍逐像素切换；切「跟着两人切」→ 接缝跟着两人连线转向，
      此时拖接缝无效（位置由驱动源每帧重算），但**拖角色有效**。
- [ ] 点 30% / 50% / 70% → 接缝平移、两侧比例随之变化。
- [ ] 跨缝带能看到轻微**错位折痕**（双相机分屏的几何本质，58 号 §4.8 后果 2 有说明与三档处方）。
- [ ] 点「合并屏 Combine」（②）→ 整屏 0.6 s 溶化到 A 侧（画面不移动，因为固定机位没有可并拢的对象）。
