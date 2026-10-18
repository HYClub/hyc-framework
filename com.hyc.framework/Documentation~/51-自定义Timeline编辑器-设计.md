# 自定义 Timeline 编辑器 — 设计文档

> **⚠️ 状态：规划中，尚未开发。** 本文档记录已锁定的设计方案与决策，**代码实现尚未开始**。文中任何类名 / 方法名 / 资产名目前均不存在于仓库，引用前请先确认已实现。请勿据此编写依赖代码。

---

## 0. 背景与目标

- 框架现有 `HYC.Framework.Timeline.CutsceneDirector`（Runtime）只是「过场队列 + 播放契约」骨架（Pause/Resume/Interrupt/Priority/Loop 已在代码中实现，文档滞后）；`PlayClip` 由宿主实现为加载 Unity `PlayableDirector`。**框架没有可视化轨道编辑器、也不创作轨道/片段级内容。**
- 目标：在 HYC 框架上自建一套**轨道/片段式可编辑时间线 + 运行时求值器**，原生驱动 ECS 实体与框架系统（Audio / Config / BT / UI / Attr / Spawn），并支持：
  1. **无码自定义轨道类型**（策划不写代码、不重编译就能造新轨道类型）；
  2. **无码信号接收端**（无码轨发出的信号有「接的地方」，端到端无码闭环）；
  3. **蓝图生成代码**（特殊逻辑可落到 C# 手写区，重生成不覆盖）。

---

## 1. 设计原则（最高优先级）

1. **不扩展 Unity `TrackAsset`/`PlayableAsset`**：Unity 自定义轨道强绑 `PlayableGraph` + 场景 GameObject/MonoBehaviour/Animator，纯 DOTS 实体与框架系统驱动不了。自建轻量时间线：IMGUI 手绘编辑器 + 与 World 解耦的求值内核，轨道/片段面向 ECS 实体与框架系统。
2. **接入而非改造 `CutsceneDirector`**：Director 继续管队列/暂停/打断，新系统管轨道级编排与播放；保留 `NotifyFinished` 契约。
3. **默认全无码（学 Unity VS/Bolt 的无码理念）**：尽量把「造轨道、放片段、发信号、接信号、处理信号」做成无码。扩展内置动作库与控制流节点，把无码边界推到极致；只有真正特殊逻辑才允许手写 C#。
4. **无码只能造「类型 + 数据结构 + 触发 + 接收 + 调已有动作」**，造不出 brand-new 运行时行为；真正新效果由游戏侧消费信号/读 payload 落地（与 Unity UnityEvent 同理）。
5. **资产工厂 + 发号服**：`TimelineAsset` / `TimelineTrackTypeDef` 创建走工厂，Id 走 `ConfigIdService`（对齐 AGENTS.md 铁律）。
6. **生成器吐的代码不受包内编译保护**（铁律 3）：Codegen 模板改动后必跑 verify-gate，最终进 Unity 编译由用户验。

---

## 2. 与 CutsceneDirector 的关系

```
CutsceneDirector（已有，管队列/暂停/打断）
   └─ PlayClip(由宿主实现) 加载 TimelineAsset(Addressable)
        └─ TimelinePlayerSystem（新，挂框架 World UpdateGroup）逐帧求值轨道/片段
             └─ 落到框架各系统：Audio / Spawn / BT / UI / Attr / Signal Bus
```

Director 不变，新系统补上「轨道级编排与播放」的真空。

---

## 3. 运行时形态

- **`TimelinePlayer`（内核，纯逻辑）**：`Play / Pause / Seek / Evaluate(t)`，与 World 解耦。**编辑器内不进 Play Mode 也能 Scrub/Play 预览**（preview context 下 Spawn/改实体走空实现，Audio 可真播）。
- **`TimelinePlayerSystem`（ECS System）**：挂框架 World 的 UpdateGroup，每帧 `player.Advance(dt)`，对处于激活区间 `[Start, Start+Duration]` 的片段调 `OnEnter / OnUpdate / OnExit`。
- **`TimelineEvalContext`**：携带当前时间/Δt、`World`/`EntityManager`、目标实体、以及 `Audio`/`ConfigRegistry`/`BTManager`/`UIManager` 等服务句柄。

---

## 4. 数据模型（规划字段）

| 类型 | 职责 | 关键字段 |
|---|---|---|
| `TimelineAsset : ScriptableObject` | 一条时间线 | `Id`(long) / `Guid`(string) / `Length`(float s) / `FrameRate`(int) / `Tracks`(List`<TimelineTrackData>`) |
| `TimelineTrackData`(Serializable) | 一条轨道 | `TrackTypeId` / `Name` / `Mute` / `Solo` / `Lock` / `Clips`(List`<TimelineClipData>` via `[SerializeReference]`) |
| `TimelineClipData`(基类, `[SerializeReference]`) | 一个片段 | `ClipTypeId` / `Start` / `Duration` + 各子类自定义参数 |
| `TimelineTrackTypeDef : ScriptableObject` | **无码轨道类型定义**（数据资产） | `typeId` / `displayName` / `category` / `color` / `schema:List<FieldSchema>` / `action:ActionBinding` |
| `TimelineFieldSchema` | 字段定义 | 名字 + 类型（基础类 + 引用类） |
| `TimelineFieldValue` | 类型化值容器（`[SerializeReference]`） | 按 schema 存混合类型 |
| `TimelineArgs` | 片段/信号字段包 | 按 schema 组装 `List<FieldValue>`，`GetX(name)` 取值 |
| `TimelineSignalBus` | 信号收发 | `Emit(id, args)` / 注册 handler |
| `TimelineSignalReceiver` | 无码接收器（数据资产） | `List<ReactionRule>` |
| `ReactionRule` | 一条无码反应 | `signalId` + 可选 `conditions` + `action`（内置动作 + 参数） |
| `ITimelineSignalHandler` | 蓝图生成代码的契约 | `long SignalId` / `void Handle(args)` / `void RunActions(args)` |
| `ITimelineAction` | 统一动作库契约 | required-fields 契约，被 Reaction 与蓝图 action 节点共用 |

---

## 5. 三层可扩展性

| 层 | 形态 | 加一种新能力的成本 |
|---|---|---|
| **代码层（宿主扩展）** | `[TimelineTrackDef]` / `[TimelineClipDef]` 特性 + `TimelineTrackRegistry`（只扫引用框架程序集，对齐 `HYCSettingsRegistry`） | 写类 + 一行特性 + 重编译 |
| **无码层（策划扩展）** | 编辑器里建 `TimelineTrackTypeDef` 数据资产（schema 含引用类字段 + ActionBinding） | **零代码、零编译** |
| **信号接收端（三层）** | ① 无码 Reaction / ② 蓝图生成代码 / ③ 纯代码订阅 | 见 §6 |

`TimelineTrackRegistry` 统一收集「代码内置类型 + 自定义类型资产」，合并进编辑器 Add Track 菜单；片段用 `[SerializeReference]` 多态 + 动态字段包 `TimelineArgs`，**无需每类型一个 C# 子类**。

---

## 6. 信号接收端：Signal Handler Codegen（核心）

无码轨发出的 `GenericSignal` 必须能「被接住」，否则端到端无码断在最后一环。提供三层接法：

1. **无码 Reaction**：`TimelineSignalReceiver` 资产含 `List<ReactionRule>`（signalId + 可选条件 + 内置动作），**零代码零文件**，在 `Signal Receivers` 编辑器窗口配。同一信号可被多 receiver 复用。
2. **Signal Handler Codegen（蓝图→代码）**：
   - 触发：`Signal Receivers` 面板对「无处理信号」亮 **「Generate Handler」** 按钮。
   - 蓝图编辑器：v1 = 拖 action 卡片的有序列表（带参数）；**v2 = 贝塞尔连线画布**（学 Unity VS/Bolt 的节点式无码 authoring，payload 输出连到 action 输入）。两者生成物一致。
   - **手写保护区机制（已定，不用正则、不用标记注释）**：生成的 `Handle(TimelineArgs)` 方法的**整个方法体**即用户手写区；重生成时用**括号匹配**定位 `Handle` 方法、把方法体原样搬回新文件。蓝图拖的 action 编译进**独立的生成方法 `RunActions(args)`**（每次重生成覆盖），由 Signal Bus 自动先调 `RunActions` 再调 `Handle`（顺序可调）。**彻底无脆弱标记、无正则解析。**
   - 生成物落 **`Assets/HYC.Generated/Timeline/`**（宿主工程，引用框架 asmdef），实现 `ITimelineSignalHandler`，由扫「引用框架程序集」的注册表（对齐 `HYCSettingsRegistry`）反射发现、缓存 `id → handler` 字典，发射时 O(1) 命中。
   - 默认手写区为空；**打 log 之类有现成 Log action 节点，不需手写**。
3. **纯代码订阅**：工程师直接用 Signal Bus API 订阅，不经编辑器。

**统一动作库 `ITimelineAction`**（含 required-fields 契约）被 ① Reaction 与 ② 蓝图 action 节点共用，供 schema 校验 + 复用。

---

## 7. 全无码动作库规划（学 VS/Bolt，尽量覆盖）

目标是让大多数「信号处理」无需手写 C#。**动作节点（无码可配参数）**：

| 动作 | 参数（示例） | 说明 |
|---|---|---|
| Log | message | 调试，替代手写打 log |
| PlayAudio | AudioRef, channel, volume, delay | 调 HYC Audio 门面（非 Unity AudioTrack） |
| StopAudio | handleRef | 停指定句柄 |
| SpawnEntity | EntityPrefabRef, position, rotation | 生成 ECS 实体 |
| DespawnEntity | targetRef | 销毁实体 |
| SetAttr | target, attrKey, value, mode(Set/Lerp), duration | 改实体属性，可瞬时或区间插值 |
| ToggleUI | windowId, visible | 开关节点 UI 窗口 |
| SendBTEvent | BTRef, eventName, payload | 触发行为树事件 |
| EmitSignal | signalId, payload | 链式信号（无码串接多条时间线/逻辑） |
| InvokeTimeline | timelineRef, priority | 播另一条时间线 |
| SetBlackboard / GetBlackboard | key, value | 时间线内/跨时间线共享变量 |
| Wait / Delay | seconds | 时序（在蓝图序列里） |

**控制流节点（无码关键，学 Bolt 的 Flow 概念但保持轻量）**：

| 节点 | 说明 |
|---|---|
| Branch | 按 payload 条件分支（`field == / > / < value`），无码 |
| Sequence | 顺序执行多个 action |
| Parallel | 并行执行 |

> v1 用「有序 action 列表 + 条件」实现控制流；v2 连线画布让 Flow 可视化。

---

## 8. v1 内置 7 种轨道

| 轨道 | 运行时行为 |
|---|---|
| Audio | 调 HYC Audio 门面 `Audio.Play(AudioRef)`（AA/Wwise），非 Unity AudioTrack |
| Signal·事件 | 发框架信号，驱动逻辑 |
| Spawn | 按时间 `EntityManager` 生成 ECS 实体 / 实例化 Addressable 预制体 |
| BT | 在时间点运行一棵行为树子树 |
| UI | 片段区间内开/关一个 ECS UI 窗口 |
| Attr | 按时间改实体属性（瞬时或区间插值） |
| AnimationProxy | 桥接 Unity `PlayableDirector`/动画（保留现有能力，唯一仍碰 Unity 动画之处） |

---

## 9. 编辑器 UX（规划）

- 宿主：`TimelineWindow`(EditorWindow) + `IMGUIContainer` 承载 `TimelineIMGUI`；复用 `GUI.matrix` 平移、`ScreenToCanvas` 时间→X 映射、`GUIDrawer` 画片段 Inspector。
- 左侧轨道头列（名 + M/S/L + 加片段）；顶部时间尺（缩放/帧刻度/吸附线）；中部车道（按类型着色片段 + 红色播放头）；底部选中片段 Inspector（按 schema 动态渲染字段，复用现有引用类 picker 如 `DrawAudioField`）。
- 交互：拖片段改 Start、拖边缘改 Duration、框选、Shift 多选、整体移动、右键加轨/加片段/删除、滚轮缩放、标尺 Scrub。遵循 IMGUI 铁律（每帧控件集合一致）+ Unity `*` 脏标记双修（`SetDirty` + `SaveAssetIfDirty`）。
- `Signal Receivers` 窗口：列所有已知信号，无处理信号亮「Generate Handler」。
- 蓝图编辑器：拖 action 卡片 / 连线画布 → 生成 `Assets/HYC.Generated/Timeline/` 下的 C#。

---

## 10. 实施阶段（尚未开发）

- **P1** 数据模型 + 工厂 + 特性 + 注册表 + 无码类型资产骨架 + 动作库契约 + Signal Bus 骨架
- **P2** 编辑器骨架（窗口 + 轨道头 + 时间尺 + 片段 + marker 轨）
- **P3** 片段/类型交互（拖移/缩放/吸附/框选/右键/动态字段复用 picker / Undo）
- **P4** `TimelinePlayer` 内核 + 预览
- **P5** `TimelinePlayerSystem` 接 World + 接 Director
- **P6** 内置 7 轨行为
- **P7** 无码 Reaction 运行时 + `Signal Receivers` 编辑器窗口
- **P7.5** Signal Handler Codegen：蓝图编辑器（v1 列表式）+ 生成器 + 括号匹配保护 + 注册发现
- **P8** 文档/索引/菜单同步 + verify-gate → 进 Unity 编译归用户验
- **v2** 贝塞尔连线画布 / Blob 导出 / 轨道分组·子时间线 / Curves 视图 / blending 进阶

---

## 11. 风险（已知）

- **A** 生成器是新子系统（模板引擎 + 括号匹配提取 + 写盘 + 触发重生成），多一类 bug → 仅当新内容≠旧内容才写盘，避免无谓触发 Unity 重编译。
- **B** 两种接信号机制并存（Reaction / 生成代码）可能让用户困惑 → 明确引导：标准动作用 ①，特殊逻辑/要代码产物用 ②。
- **C** 蓝图真·连线画布工作量大（≈又做一个 `BTGraphIMGUI`）→ v1 列表式，v2 画布。
- **D** 生成文件落宿主工程 → 固定 `Assets/HYC.Generated/Timeline/`，文档写清 asmdef 引用约定。
- **E** 生成器吐代码不受包内编译保护（铁律 3）→ 模板改动必跑 verify-gate + 进 Unity 验。
- **F** 测试缺失 → 加 EditMode 测试（生成器 + 括号匹配保护）。

---

## 12. 决策记录（已锁定，待开发）

- 接入 `CutsceneDirector` + ECS System/内核 + 菜单 `Timeline`(pri 51/52) + 命名空间 `HYC.Framework.Timeline`。
- v1 内置全部 7 种轨道。
- 无码轨道类型：运行时行为「两者都要」（GenericSignal + BoundBuiltin），字段类型「含引用类字段」。
- 信号接收端三层：无码 Reaction / 蓝图生成代码 / 纯代码订阅。
- **手写保护区 = `Handle` 方法体（括号匹配，不用正则/标记）**；蓝图 action 进独立 `RunActions` 方法。
- **默认全无码**，学 VS/Bolt，扩展动作库 + 控制流节点。
- 全部功能**尚未开发**，本文档为规划存档。
