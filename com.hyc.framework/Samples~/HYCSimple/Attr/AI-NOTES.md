# HYCSimple · 属性系统（Attr）· AI 接入笔记

> 本文档是**写给 AI（和人类）看的接入说明**：讲清「属性系统怎么用、坑在哪、菜单路径」，
> 不解释内部机制。配合同目录 `GameAttrKey.cs` / `GameAttrKeyMap.cs` / `GameAttrBag.cs` /
> `HYCSimpleAttrDemoSystem.cs` 食用。
> 框架级、配置表驱动、per-entity 基础属性能力；框架不认识 "HP/ATK" 等具体名，也不内置公式/buff。

---

## 一、一句话心智模型

- **`AttributeCatalog`**（ScriptableObject）：属性的「单一真源」清单。每个条目 = `key` + `type`(Int/Float/Long/Bool) + `defaultRaw`/`minRaw`/`maxRaw`。
- **`AttributeSetAsset`**（ScriptableObject）：给某类实体（如 Hero）从目录里**挑一篮子属性**的预设。勾选即烘成 `List<AttributeValue>`（values）。
- **生成访问器**（向导）：把目录生成 `GameAttrKey`(enum) + `GameAttrKeyMap`(Hash/Type) + `GameAttrBag`(类型安全读写)，**零手写 key 字符串**。
- **运行时**：实体挂 `AttributeSetRef` → `AttributeInitSystem` 进 Play 自动把 `values` 灌进 `DynamicBuffer<AttributeValue>` → 玩法用 `GameAttrBag` 读写。

---

## 二、端到端步骤（照做即可）

1. **建目录资产**：Project 右键 `HYC Framework/Attributes/Attribute Catalog`，加条目（key/type/默认/min/max）。
   或在菜单 `HYC Framework/Attributes/Attribute Catalog Editor` 打开独立面板编辑（类型感知表格 + 「属性组」分页，见下）。
2. **（可选）建属性组**：菜单 `HYC Framework/Attributes/Attribute Set`，绑定 catalog，勾选"这个实体要哪些属性"。保存时 `OnValidate` 自动烘 `values`。
3. **生成访问器**：在 Catalog 编辑器点「生成属性访问器」→ 向导生成到 `Assets/HYC/Generated/Attributes/`（默认前缀 `Game`）。
4. **实体挂引用**：实体加 `AttributeSetRef { asset = 你的Set }`。进 Play 后 `AttributeInitSystem` 自动 `AddBuffer<AttributeValue>` + 灌值。
   或手动 `AttributeInitializer.InitFromSet(em, e, set)`。
5. **玩法读写**：`var bag = new GameAttrBag(em, e); int hp = bag.GetInt(GameAttrKey.HP);`

---

## 三、`Attribute Catalog Editor` 窗口（两页）

菜单 `HYC Framework/Attributes/Attribute Catalog Editor` 打开，自动绑定选中/工程内首个 Catalog。

- **页 1 · 属性目录**：类型感知表格（替代默认 PropertyField）。
  - Float 显示小数（raw 内部走 IEEE754 bits，自动往返）；
  - Bool 只显示勾选框，隐藏 min/max；
  - Int/Long 显示整数；
  - 行首 `≡` 拖拽重排、行尾 `✕` 删除、底部「＋ 添加属性」。
- **页 2 · 属性组**：组装 `AttributeSetAsset`。
  - 左列工程内全部组资产（`AssetDatabase.FindAssets("t:AttributeSetAsset")`）；
  - 右列从当前目录勾选 key，勾选变化即时 `Rebuild()` 烘焙 `values`；
  - 「＋ 新建属性组」创建并自动绑定当前目录。
  - 复用 `AttributeSetAssetEditor`(CustomEditor) 的勾选逻辑，与资产 Inspector 行为一致。

---

## 四、给 AI 的「雷区」（写代码前必读）

1. **Float 存 bits**：`AttributeValue.raw` 对 Float 是 `BitConverter.DoubleToInt64Bits`/`Int64BitsToDouble`。
   手写 raw 必须走这套转换；NaN/Inf 会被 `AttributeValidator` 拦下。
2. **枚举是目录的快照**：目录增删/改顺序后**必须重新 Generate**，否则枚举与运行时袋数据序数错位。
   keyHash 按**名**算（非序数），同 key 改名不影响旧袋。
3. **运行期 `Set` 不钳制**：配置边界只约束默认值；`InitFromSet` 灌值时按 `[Min,Max]` 钳制，之后 `Set` 是运行时真相，不自动 clamp。
4. **keyHash 命中失败是静默的**：`Get` 返回调用方默认值、`Set` 为 no-op（袋即"声明过的属性集合"，不自动扩容）。
   读到 0 先确认实体确实 `AddBuffer<AttributeValue>` 且 key 在袋内。
5. **`AttributeSetRef` 是托管 `IComponentData`（class）**：不走 Burst/Job，适合低频初始化，不进逐帧热路径。
6. **`AttributeCatalog` / `AttributeSetAsset` 是 Runtime ScriptableObject**：打包即用，无需像 BT 那样导出 Blob。
   它们不进 Editor asmdef（Editor 程序集打包后不存在），所以真机能直接拿到。
7. **命名须框架中立**：示例用 `HP`/`MP`/`MoveSpeed`/`IsInvincible` 这类通用名；真实工程按自己领域命名，
   但**框架样例里不要写使用方具体玩法**（如某游戏的 `ItemKind`/`WavePointStrategy`）。

---

## 五、本样例怎么用真实 `.asset`（默认就走真实链路）

`HYCSimpleAttrDemoSystem` **优先走真实资产链路**：首次 Update 用 `AssetDatabase.LoadAssetAtPath<AttributeSetAsset>`
读 `Assets/HYCSimpleSampleAssets/GameHeroSet.asset`，命中后调用 `AttributeInitializer.InitFromSet(em, e, set)`
灌袋（与 `AttributeInitSystem` 同源逻辑）——和真实项目「建目录 → 建 Set → 挂 `AttributeSetRef`」完全一样。

资产由菜单 `HYC Framework/Samples/Attr/创建示例属性资产`（或统一菜单 `HYC Framework/Samples/创建全部示例资产`）
一次性生成到 `Assets/HYCSimpleSampleAssets/`：
- `GameAttrCatalog.asset`：列出 `HP`/`MP`/`MoveSpeed`/`IsInvincible`（Float 的 default/min/max 走 `BitConverter.DoubleToInt64Bits`）。
- `GameHeroSet.asset`：引用目录 + 勾选 4 个 key，保存时 `Rebuild()` 烘焙出 `values`。

> **回退**：若资产还没生成（没跑过菜单），系统会 `Debug.LogWarning` 并**退回手写 buffer**（HP/MP/MoveSpeed/IsInvincible），
> 保证「不跑菜单也能编译运行、演示 AttrBag 读写」。但这只是兜底——示例的**主路径就是真实 `.asset`**，
> 不依赖手写二进制，符合「示例要演示真实资产」的诉求。想看真实链路，先跑一次 `创建示例属性资产` 菜单再 Play。

---

## 六、验证清单（进 Unity 后）

- [ ] 导入 Sample 后 `HYCSimpleAttrDemoSystem` 能编译（依赖 `HYC.Framework.Attr` 与 `Unity.Entities`）。
- [ ] 进 Play，Console 打印 `[HYCSimple] 属性演示：HP=110 ...`（SetInt 后 HP+10）。
- [ ] 走真实链路：建 Catalog → 建 Set → 生成访问器 → 实体挂 AttributeSetRef → Play 自动获得 buffer。
- [ ] 目录加一个属性后重新 Generate，确认 `GameAttrKey` 枚举新增、旧枚举没错位。
