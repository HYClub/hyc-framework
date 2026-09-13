# 82 · BT 运行时：Blob、解释器与黑板

## 一、导出：`BTTreeAsset` → `BTRootBlob`

```csharp
if (BTBlobBuilder.Build(asset, out var blob))
{
    BTManager.Register(asset.TreeId, blob);
}
```

`BTBlobBuilder.Build` 会在以下情况**失败并打 `LogError`**：

| 失败原因 | 错误信息 |
| --- | --- |
| 资产为空 | `[BT] 树资产为空, 无法导出` |
| 没有 Root 节点 | `[BT] 树缺少 Root 入口节点(右键菜单 入口/开始 添加), 无法导出` |
| 多个 Root | `[BT] 树存在多个 Root 节点, 只允许一个, 无法导出` |
| Root 没连线 | `[BT] Root 节点未连线(连到实际起始逻辑), 无法导出` |

`BTRootBlob` 是纯数据（Blob），节点以连续数组存储、子节点索引内联，**解释器用指针遍历，无托管分配**。

---

## 二、注册与查询：`BTManager`（`unsafe static`）

```csharp
// 注册 / 反注册
BTManager.Register(treeId, blobRef);
BTManager.Unregister(treeId);

// 查询
BTRootBlob* tree = BTManager.TryGet(treeId);   // 未注册返回 null
bool ok = BTManager.Contains(treeId);

// 全清（切场景/重进）
BTManager.DisposeAll();
```

### 节点跨帧状态

组合节点的状态（上次运行子索引、finished 掩码、重复计数、随机种子）由 `BTNodeRuntimeState` 持久化，按 **(实体, 树ID)** 存：

```csharp
var states = BTManager.GetNodeStates(entity, treeId, nodeCount);   // NativeArray
BTManager.ResetNode(entity, treeId, tree, nodeIndex);              // 复位单个节点
BTManager.ClearNodeStates(entity);                                 // 清该实体全部
```

### 子树状态

`SubTree` 节点执行另一棵树时，被调树的状态单独按 (实体, 子树ID) 存：

```csharp
BTManager.TryGetSubTreeState(entity, subTreeId, out var st);
BTManager.SetSubTreeState(entity, subTreeId, st);
BTManager.ClearSubTreeStates(entity);
```

### Guard（跨分支令牌互斥）

`Guard` 节点用：

```csharp
int holder = BTManager.GuardHolder(entity, token);       // 谁持有令牌
BTManager.SetGuard(entity, token, nodeIndex);
BTManager.ClearGuard(entity, token, nodeIndex);
```

### 断点（调试用）

```csharp
BTManager.ToggleBreakpoint(treeId, nodeIndex);
BTManager.SetBreakpoint(treeId, nodeIndex, true);
bool on = BTManager.IsBreakpoint(treeId, nodeIndex);
BTManager.ClearBreakpoints();
```

---

## 三、ECS 驱动：`BTInterpreterSystem`

`BTInterpreterSystem` 是 `ISystem`，注册在玩法阶段，对**挂了 `RunningBT{TreeId}` 组件的实体**逐帧 Tick：

```
foreach (entity with RunningBT)
    ├─ BTBlackboardRegistry.TryGetForEntity(entity, out entityBB)
    ├─ BTBlackboardRegistry.TryGetGlobal(out sharedBB)
    ├─ 构造 BTContext（上下文 + 黑板 + GameHandler）
    ├─ BTInterpreter.Tick(tree, ref runState, ref ctx)
    └─ 结果写回 bt.ValueRW.RunState.Result
```

解释入口：

```csharp
public static BTNodeState Tick(BTRootBlob* tree, ref BTRunState state, ref BTContext ctx)
```

`BTRunState` 携带上一轮结果 `Result` 与执行轨迹（`RecordTrace`），**轨迹就是运行时高亮/调试的数据来源**。

---

## 四、状态：`BTNodeState`

| 值 | 含义 |
| --- | --- |
| `None` | 未运行 |
| `Running` | 运行中（跨帧） |
| `Success` | 成功 |
| `Failed` | 失败 |
| `Optional` | 可选——**父组合忽略其成败**（`Optional` / `Remapper` 节点会产生） |

---

## 五、黑板

| 概念 | 说明 |
| --- | --- |
| `BTTreeAsset.Blackboard` | 编辑器里定义的黑板参数（`Key` / `ValueType` / `DefaultValue`），**定义只读** |
| `BTBlackboardRuntime` | 每棵树实例一份，**实例可写** |
| `BTBlackboardRegistry` | 注册表：`TryGetForEntity(entity, out bb)` 取实体私有、`TryGetGlobal(out bb)` 取全局共享 |

`BTContext` 里带黑板引用，节点通过黑板读写跨节点/跨帧数据。
`CheckBlackboard` / `CheckDistance` 节点直接读黑板做判断。

---

## 六、事件：`BTEventBus`

配合 `SendEvent` / `CheckEvent` 节点：

- `Long[0]` = 事件 key 哈希
- `Long[1]` = 范围：`0` 本实体 / `1` 全局（`SendEvent`）；是否消费（`CheckEvent`：`0` 只查询 / `1` 消费后返回成功）

事件走 `BTEventBus`，与 ECS 消息系统（`MessageEntity`）是两套东西——**BT 事件在树内传递，ECS 消息是单帧实体消息**。

---

## 七、非 ECS 路径

`BTManager` + `BTEventBus` + `BTNodeRuntimeRegistry` 提供手动驱动能力（不需要 ECS World）。
与 ECS 路径共用同一份 Blob 和同一个 `BTInterpreter`。

---

## 注意事项 / 坑

1. **`BTManager` 是 `unsafe static`**，注册后全局可见；`DisposeAll()` 会释放所有 Blob 与状态，切场景务必调用。
2. **`GetNodeStates` 返回 `NativeArray`**，用完要按 Entities 的约定释放，不要长期持有。
3. **跨帧状态按 (实体, 树ID) 存**——同一个实体跑多棵树不会串味，但**实体销毁后要 `ClearNodeStates`**，否则泄漏。
4. **`TreeId` 是运行期唯一键**，重排/改名资产不影响，但改 `TreeId` 会断所有引用（`SubTree`、`BehaviourTree` 配置字段都存的是 ID）。
5. **`Optional` 状态语义特殊**——父组合会忽略它，用之前确认父节点类型是否支持。
