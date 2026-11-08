# 42 · 本地多人输入（`LocalPlayerSession`）

> `Runtime/Input/LocalPlayerSession.cs`（`HYC.Framework.Input`）。
> 把**设备**分配给**槽位（slot）**，并处理**热插拔**。槽位上限固定为 **2**（`MaxSlots`）。
>
> 它和另外两篇输入文档是**并列的三层**，不是替代关系：
> `40` 管热键/动作语义，`41` 管「action → component」的写入管线，**本篇管「谁在用哪台设备」**。

## 一、四种布局

`Start()` 时选定，决定 `AutoAssign()` 怎么把当前设备分到槽位。

| 枚举值 | 含义 |
| --- | --- |
| `KeyboardSplit`（默认） | 一块键盘分左右：P1 = WASD + 左手区，P2 = 方向键 + 小键盘 |
| `KeyboardPlusGamepad` | P1 键盘，P2 手柄 |
| `TwoGamepads` | 两只手柄各一人 |
| `SingleGamepadDualStick` | 单只手柄双人共用：左摇杆 / 右摇杆各一人 |

对应的**控制方案名**是常量，且**必须在 `InputActionAsset` 里真定义了同名方案**：

`KeyboardLeft` / `KeyboardRight` / `Gamepad` / `GamepadLeft` / `GamepadRight`

## 二、生命周期

```csharp
public void Start(LocalInputLayout layout, InputActionAsset template, bool autoAssign = true);
public void Stop();
public void Dispose();   // = Stop()

public bool IsRunning { get; }
public int  PlayerCount { get; }        // 已占用槽位数
public LocalInputLayout Layout { get; } // 当前布局
public InputActionAsset Template { get; } // 模板本体，**不会被启用**
```

- 重复 `Start()` 会先自动 `Stop()`（不需要调用方自己判重）。
- `Start()` 里订阅 `InputSystem.onDeviceChange`，`Stop()` 里退订 —— **必须成对**，忘了 `Stop()` 会持续持有回调。
- `template` 本体不启用：每个槽位 `Instantiate` 一份独立实例（`名字 = 模板名_P{槽位+1}`），
  这样两个玩家的 action 状态互不污染。

## 三、查询

全部**越界安全**：槽位越界返回默认值而非抛异常。

```csharp
public bool IsAssigned(int slot);
public InputUser GetUser(int slot);            // 越界 → default
public InputActionAsset GetAsset(int slot);    // 越界/未分配 → null
public string GetScheme(int slot);             // 越界/未分配 → null
```

## 四、分配

```csharp
public void AutoAssign();                                          // 按当前布局整体重排
public bool Assign(int slot, InputDevice device, string controlScheme); // 精确指定
```

`Assign` 返回 `false` 的条件（都是静默失败，不抛异常）：
会话未运行 / 槽位越界 / `device == null` / `Template == null`。

成功时会**先释放该槽位原有配对**，再走 `InputUser.PerformPairingWithDevice` +
`ActivateControlScheme`，最后重建该槽位的 asset 实例、更新 `PlayerCount`、触发 `PlayerJoined(slot)`。

## 五、事件

| 事件 | 载荷 | 触发时机 |
| --- | --- | --- |
| `PlayerJoined` | `slot` | 槽位被成功分配 |
| `PlayerLeft` | `slot` | 槽位被释放（含断开、Stop） |
| `PlayerDevicesChanged` | **固定 `-1`** | 任何设备增删事件处理完之后统一发一次 |

> `PlayerDevicesChanged` 的 `-1` 是「不针对某个槽位」的意思，**不是槽位索引**，别拿它去索引数组。

## 六、热插拔（本篇最有价值的部分）

`OnDeviceChange` 处理四类变化，语义刻意不对称：

| 变化 | 行为 | 为什么这么设计 |
| --- | --- | --- |
| `Removed` / `Disconnected` | 谁的配对里含这台设备就**释放该槽位**，然后 `AutoAssign()` 重排 | 「松手即离队」，避免设备拔了却仍在读输入的**幽灵输入**；重排是为了自动降级（如双手柄剩一只） |
| `Added` / `Reconnected` | **只在还有空槽位时** `AutoAssign()` | 不抢占已经在玩的人 —— 后插的手柄不能把别人踢掉 |

## 七、典型用法

```csharp
var session = new LocalPlayerSession();
session.PlayerJoined += slot => Debug.Log($"P{slot + 1} 加入");
session.PlayerLeft   += slot => Debug.Log($"P{slot + 1} 离开");

session.Start(LocalInputLayout.KeyboardPlusGamepad, inputActionsTemplate);

// 取某个玩家的 action asset 去读值
var actions = session.GetAsset(0);

// 场景切换 / 退出时必须停，否则回调泄漏
session.Dispose();
```

## 八、注意事项 / 坑

- **`MaxSlots = 2` 是常量**，不是配置 —— 要支持 4 人得改这里（要连带改 `AutoAssign` 的各布局分支）。
- **控制方案名必须存在于 `InputActionAsset`**，`ActivateControlScheme` 对不存在的方案会失败；
  五种常量名见第一节。
- **`Stop()` 必须调用**：`InputSystem.onDeviceChange` 是全局静态事件，忘记退订会让整个会话对象无法回收。
- **`Template` 本体不要手动 `Enable()`**：启用的是每槽位的实例化副本，动模板本体不会生效。
- **越界查询不抛异常**（返回 `default` / `null`）—— 方便的另一面是错误会被静默吞掉，
  上层最好自己判 `IsAssigned`。
- **目前在框架内部还没有调用方**：它是给宿主游戏层用的机制件，"什么时候启动会话、用什么布局"由游戏定
  （与 `40` 里那句「设备配对 / 加入逻辑由游戏层负责」对应 —— 本篇提供的就是这一层的现成实现）。
