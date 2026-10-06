# HYCSimple · 行为树（BT）· AI 接入笔记

> 写给 AI（和人类）的接入说明：讲清「树怎么建、怎么导出、运行期怎么跑、打包链路是什么」，不解释内部机制。
> 配合同目录 `HYCSimpleBTDemoSystem.cs`（运行时）与 `Editor/HYCSimpleBTTreeBuilder.cs`（编辑器建树+导出）食用。
> 行为树 = 配置表驱动 + ECS System 逐帧 Tick 的「数据化 AI」。
>
> **本示例的树是「有真实行为数据」的敌人 AI 树**（不是占位 NoOp）：
> `Root → Selector[ Sequence( IsTargetInRange(条件) → Attack(动作) ), Patrol(动作) ]`，
> 三个叶子是 `GameCustom` 节点（subType 1/2/3），对应 `HYCSimpleEnemyBTNodes.cs` 里的 `BTCustomNode` 子类——
> 它们会被 `BTNodeRuntimeRegistry` **反射自动注册**，进 Play 后控制台真的打印「目标在范围? / 攻击 / 巡逻」逐帧日志。
>
> **一键生成全部真实资产**：菜单 `HYC Framework/Samples/创建全部示例资产` 会一次性调用
> `HYCSimpleBTTreeBuilder.CreateSampleTree()`（建树资产 + 导出 Blob）与 `HYCSimpleAttrSampleAssets.CreateAssetsInternal()`
> （建 `GameAttrCatalog.asset` + `GameHeroSet.asset`）。推荐先跑这个再进 Play，让 BT / Attr 示例都读到真实 `.asset`。

---

## 一、一句话心智模型

- **编辑器**：在 `BTTreeAsset`（`CreateAssetMenu "HYC/BT/Tree"`）里画节点/连线，或用代码 `EnsureRootNode()` 程序化建树。
- **导出**：`BTBlobExporter.ExportAll()`（菜单 `HYC Framework/BT/Export Blob (For Build)`）→ `Assets/StreamingAssets/BTBlob/*.blob` + `manifest.txt`。
- **运行期**：`BTBlobLoader.LoadAll()` 把 blob 读回并 `BTManager.Register(treeId, blob)`。
- **驱动**：给实体挂 `RunningBT { TreeId }`，框架自带 `BTInterpreterSystem : ISystem` 每帧 `Tick`，结果写回 `BTLastResult`。
- **游戏层自定义行为**：`GameCustom` 节点用 `LongParams[0]` 当子类型 subType，`BTNodeRuntimeRegistry` 运行期反射扫描程序集、
  把 `BTCustomNode` 子类按 (TreeKind, subType) 自动注册并分发执行——示例 `HYCSimpleEnemyBTNodes.cs` 就是这么接的，无需手动注册。

---

## 二、端到端步骤（照做即可）

1. **建树资产**：Project 右键 `HYC/BT/Tree`，或跑本 Sample 菜单
   `HYC Framework/Samples/BT/创建示例树并导出`（转发到 `HYCSimpleBTTreeBuilder.CreateSampleTree()`）。
   它**程序化造一棵有真实语义的敌人 AI 树**并直接导出 Blob：
   ```
   Root
    └─ Selector(EnemyAI)            优先战斗，否则巡逻
        ├─ Sequence(Combat)
        │    ├─ GameCustom(1) IsTargetInRange  (条件)
        │    └─ GameCustom(2) Attack            (动作)
        └─ GameCustom(3) Patrol                 (动作, 持续 Running)
   ```
   `GameCustom(1/2/3)` 的 `LongParams[0]` 即 subType，运行时去 `HYCSimpleEnemyBTNodes.cs` 找对应节点执行。
   ⚡ 或跑统一入口 `HYC Framework/Samples/创建全部示例资产`（同时生成 BT 树 + Attr 资产，见文首）。
2. **画树**：必须有且仅有一个 `Root`，其余节点靠 `Connections`（Source→Target + PortIndex 决定子节点顺序）连成树。
   节点类型见 `BTNodeType`（Root / Sequence / Selector / Wait / NoOp / Condition / GameCustom=128 …）。
3. **导出 Blob（打包必做）**：菜单 `HYC Framework/BT/Export Blob (For Build)`。
4. **运行期加载 + 挂组件**：
   ```csharp
   BTBlobLoader.LoadAll();                       // 读 StreamingAssets/BTBlob/*.blob 注册
   entityManager.AddComponentData(e, new RunningBT { TreeId = 900001, RunState = default, TickInterval = 0f });
   ```
   `BTInterpreterSystem` 已 `RequireForUpdate<RunningBT>()`，会自动驱动。

---

## 三、编辑器自动注册 vs 真机导出 Blob（最关键！）

- **编辑器 Play**：`BTAutoRegisterOnPlay` 钩子进 Play 时自动把工程内 `BTTreeAsset` 注册进 `BTManager`，
  所以编辑器里**不导出也能跑**。
- **真机打包**：`BTTreeAsset` / `BTBlobBuilder` 在 **Editor 程序集，打包后不存在**，`BTAutoRegisterOnPlay` 也不执行 →
  **真机一棵树都拿不到**。必须走「导出 Blob → `BTBlobLoader.LoadAll()` 读回」这条路。
- ⚠️ **改完树一定要重新导出**：否则真机跑的是旧版本（编辑器走自动注册，看不出来）。

---

## 四、给 AI 的「雷区」（写代码前必读）

1. **`BTTreeAsset` / `BTBlobBuilder` 是 Editor 程序集**：引用它们（建树/导出）的代码必须放 Editor 汇编
   （`Editor/` 子目录，或 `#if UNITY_EDITOR` 守卫）。运行期只用 `BTBlobLoader` / `BTManager` / `RunningBT` / `BTInterpreterSystem`（均 Runtime）。
2. **连接即 children**：`BTConnectionData.PortIndex` 是源节点**输出端口序号**（Sequence 的第 0、1 个子节点），
   导出时据此构造 children 顺序；漏连或 PortIndex 重复会导致导出失败（`BTBlobBuilder` 会校验并报错）。
3. **`RunningBT.RunState`** 是 `BTRunState`（跨帧状态），用 `default` 即可；`TickInterval=0` 每帧，`>0` 按秒节流。
4. **`BTManager.Register` 重复覆盖**：同 TreeId 再次注册会释放旧 blob；编辑器自动注册 + 运行期 LoadAll 都注册同 Id，无害。
5. **`BTBlobLoader` 路径限制**：`Application.streamingAssetsPath` 在 **Android 是 APK 内（jar:）**，文件 IO 读不到
   （配置管线同受限）→ 真机先把 blob 拷到 `persistentDataPath` 再传目录，或用 `UnityWebRequest` 读字节后
   `BlobAssetReference<BTRootBlob>.Create(byte[])` 自行 `Register`。
6. **GameCustom 节点** = 框架留给游戏层的「自定义行为」口子：`LongParams[0]` 是子类型 subType，运行期
   `BTNodeRuntimeRegistry` 反射扫描程序集、把 `BTCustomNode` 子类按 (TreeKind, subType) 自动注册并分发执行
   （无需手动注册）。本示例 `HYCSimpleEnemyBTNodes.cs` 就是三个 GameCustom 叶子（条件/攻击/巡逻），进 Play 会被真正执行。
   注意：自定义节点必须放在**运行时程序集**（不能只在 Editor），否则反射扫不到、节点变空操作。
7. **命名须框架中立**：示例树用 `IsTargetInRange/Attack/Patrol` 这类通用 AI 语义；真实节点语义按使用方领域，但**框架样例不写具体玩法**（如某游戏的 `BossPhaseSwitch`）。
8. **⚠️ 示例树 Id 必须避开业务常用 Id**：导出会写 `StreamingAssets/BTBlob/<treeId>.blob`，
   用 `1001` 这类 Id 极易覆盖宿主工程自己的树（血泪：1001 常被用作 Player / 第一关 / 第一棵树）。
   本例固定 `900001`；导入工程后如已生成过旧的 `1001.blob`，手动删掉即可（那是示例污染，非工程资产）。

---

## 五、验证清单（进 Unity 后）

- [ ] 跑菜单 `HYC Framework/Samples/创建全部示例资产`（或单独 `HYC Framework/Samples/BT/创建示例树并导出`），
  `Assets/HYCSampleTrees/HYCSimpleDemoTree.asset` + `Assets/StreamingAssets/BTBlob/900001.blob` + `manifest.txt` 出现。
- [ ] 进 Play，`HYCSimpleBTDemoSystem` 日志显示「行为树加载：成功 1 棵」「已为实体挂载 RunningBT」。
- [ ] 进 Play 后控制台持续打印 `[HYCSimple] BT条件[目标在范围?] = 是/否`、`BT动作[攻击]…`、`BT动作[巡逻]…`（说明 GameCustom 叶子被真正执行，而非空跑）。
- [ ] 改动树节点后**重新导出**，确认真机链路拿到新版（编辑器自动注册路径无法验证这点）。
- [ ] （可选）挂 `BTLastResult` 读 `State`，确认树每帧 Tick 出 Success/Running/Failed。
