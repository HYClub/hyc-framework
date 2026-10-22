# HYCSimple · 输入组件化（Input）· AI 接入笔记

> 本文档是**写给 AI（和人类）看的接入说明**：讲清「输入组件化怎么用、菜单路径、坑在哪」，
> 不解释内部机制。配合同目录：
> `SampleInputAssetBuilder.cs`（运行时构造示例 InputActionAsset）/
> `SampleInputPlayerSpawner.cs`（A0，建玩家实体并注册）/
> `SampleInputReaderSystem.cs`（B9，每 1 秒打印输入快照）/
> `Editor/SampleInputSetup.cs`（菜单一键生成组件）/
> `UI/UIInputComponent.cs` + `Sense/SenseInputComponent.cs`（生成器产物，已预生成）。
>
> 输入组件化把 Input System 的 action **按 map 生成 ECS 组件**，由驱动系统每帧写进玩家实体；
> 宿主轮询式读取，或注册 lambda 在底层写完后即时回调。详见框架文档 `41-输入组件化.md`。

---

## 一、一句话心智模型

- **`.inputactions` / InputActionAsset** 是输入源：定义 map（`UI`/`Sense`）+ action + 控制类型。
- **`InputComponentMapping`**（ScriptableObject）：把「map 名 → 输出文件夹 + 命名空间 + 组件名」登记下来。
- **`InputComponentGenerator`** 按 map 生成 `<Map>InputComponent : IComponentData`（富状态字段）+ `<Map>InputComponentWriter : IInputMapWriter`
  （`[RuntimeInitializeOnLoadMethod]` 自注册进 `InputMapWriterRegistry`）。
- **`InputComponentDriverSystem`（A0）** 每帧：遍历 `InputPlayerRegistry` 的玩家 × 所有 Writer → 把当前输入写进玩家实体。
- **宿主侧**：建玩家实体 → `XxxInputComponent.AddTo(em, player)` → `InputPlayerRegistry.Register(player, 该玩家资产实例)` → 之后直接读组件（或在 A0 写后 `RegisterHandler<TComp>` 收回调）。

---

## 二、端到端步骤（照做即可）

本样例**已自带预生成的 `UIInputComponent.cs` / `SenseInputComponent.cs` 与各自 asmdef**，导入即可编译运行；下面两步是「如果改了资产想要重新生成」或「看真实链路」才需要。

1. **（可选）重新生成组件**：菜单 `HYC Simple/Input/Generate Sample Input Components`
   （`SampleInputSetup`）——用 `SampleInputAssetBuilder` 在代码里构造资产 + 建 `InputComponentMapping`
   （指向本样例 `UI/` `Sense/` 子目录）+ 跑生成器。生成产物即同名 `.cs` 与 `.asmdef`，覆盖预生成文件。
   > 真实项目这条链路是：**在 Unity Input System 编辑器里建 `.inputactions` 资产** → 建 `InputComponentMapping` 指向它 → 菜单 `HYC Framework/Input/Generate Input Components`。
   > 本样例为了「不依赖手写 `.inputactions` 文件也能编译演示」，改用代码构造资产，行为等价。

2. **进 Play 看演示**：样例系统**未标 `[DisableAutoCreation]`**，进 Play 自动跑（与其他块的 `*DemoSystem` 走 `HYCSimpleSceneBootstrap` 不同）：
   - `SampleInputPlayerSpawner`（A0）首帧建一个玩家实体、挂 `UI/Sense` 组件、克隆一份资产实例并 `Register`。
   - `SampleInputReaderSystem`（B9）每 1 秒打印该玩家输入快照（`Submit点亮` / `NavMove` / `Scroll` / `Look` / `Fire`）。

---

## 三、示例资产 / 组件字段对照

`SampleInputAssetBuilder` 构造的资产（map + action + 控制类型）：

| map | action | 类型 | 生成组件字段（类型） |
| --- | --- | --- | --- |
| `UI` | `Submit` | Button | `UIInputComponent.Submit` (`InputButton`) |
| `UI` | `NavMove` | Vector2 | `UIInputComponent.NavMove` (`InputVector2`) |
| `UI` | `Scroll` | Axis | `UIInputComponent.Scroll` (`InputAxis`) |
| `Sense` | `Look` | Vector2 | `SenseInputComponent.Look` (`InputVector2`) |
| `Sense` | `Fire` | Button | `SenseInputComponent.Fire` (`InputButton`) |

绑定（演示用）：`Submit`←Enter/Gamepad.South，`NavMove`←W/↑/左摇杆，`Scroll`←Mouse.scroll，
`Look`←Mouse.delta/右摇杆，`Fire`←Space/Gamepad.West。

---

## 四、给 AI 的「雷区」（写代码前必读）

1. **每名玩家要独立 `InputActionAsset` 实例**：`SampleInputPlayerSpawner` 用 `Object.Instantiate(...)` 克隆。
   共享同一实例会让边沿（`justPressed`）与启用状态跨玩家串味——真实项目每加一个玩家都要 `Instantiate` 一份。
2. **`UI/Sense` 组件与 Writer 是生成器产物（文件头标 `auto-generated`）**：手改会被下次生成覆盖。
   想改字段就改 `InputComponentMapping` / `.inputactions` 后重新生成。样例里它们被手写落盘只是省去首次菜单步骤。
3. **`AddTo` 是生成器随组件一起产出的静态扩展方法**（`public static void AddTo(EntityManager, Entity)`），不是你手写的，直接调用即可把组件挂到玩家实体。
4. **读取必须晚于 A0 写入**：示例 reader 挂在 `UpdateGroup_B9`（晚于 A0）；UI 组天然晚于 A0，安全。
   `RegisterHandler<TComp>` 的回调由驱动系统在 A0 写完本玩家组件后即时触发，回调里读到的即本帧最新值。
5. **`UI/` 与 `Sense/` 各自独立 asmdef（`HYC_Simple_Input_UI` / `HYC_Simple_Input_Sense`）**：两包互不可见，
   只引用 `HYC.Framework.Runtime`，靠 `InputMapWriterRegistry` 接口契约解耦——这就是「按 map 分包」的含义。
6. **Writer 靠 `[RuntimeInitializeOnLoadMethod]` 自注册**：生成的 Writer 所在 asmdef 必须能被加载（默认 `autoReferenced: true`，已生成的 asmdef 满足），否则驱动系统扫不到它、组件永远写不进。
7. **样例资产是代码构造的，没有 `.inputactions` 文件落盘**：真实项目请在 Input System 编辑器里配 `.inputactions` 并用 `InputComponentMapping` 指向它；样例的 `SampleInputAssetBuilder` 仅为自洽。
8. **样例系统自动运行、不打 `[DisableAutoCreation]`**：进 Play 即建实体 + 每 1 秒打日志，属预期演示行为。
   若想纳入 `HYCSimpleSceneBootstrap` 统一手动驱动（像其他块那样不自动跑），给这两个系统加 `[DisableAutoCreation]` 并在 Bootstrap 里显式 Update 即可——目前为「即开即看」故意保持自动。

---

## 五、验证清单（进 Unity 后）

- [ ] 导入 Sample 后 `SampleInputPlayerSpawner` / `SampleInputReaderSystem` 能编译（依赖 `HYC.Framework.Input` + `Unity.Entities` + `UnityEngine.InputSystem`）。
- [ ] 进 Play，Console 每 ~1 秒打印 `[HYC.Simple.Input] 玩家 e=... Submit点亮=... NavMove=... Scroll=... Look=... Fire=...`。
- [ ] 按 Enter / W / ↑ → 日志里 `Submit` / `NavMove` 变化；按 空格 → `Fire` 点亮。
- [ ] （可选）跑菜单 `HYC Simple/Input/Generate Sample Input Components`，确认 `UI/Sense` 组件被重新生成且编译通过。
- [ ] 真实链路自测：Input System 编辑器建 `.inputactions`（map `UI`/`Sense` + 对应 action 与 `expectedControlType`）→ 建 `InputComponentMapping` 指向它 → 菜单 `Generate Input Components` → 玩家实体挂组件 + `Register` → Play 后读组件。
