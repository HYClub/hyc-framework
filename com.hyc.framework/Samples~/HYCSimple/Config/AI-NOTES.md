# HYCSimple · 配置系统（Config）· AI 接入笔记

> 写给 AI（和人类）的接入说明：讲清「配置怎么读、怎么接、坑在哪」，不解释内部机制。
> 配合同目录 `HycSimpleItemRow.cs` / `HYCSimpleConfigDemoSystem.cs` 食用。
> 配置管线是「Excel / 模板 → 不可变 Blob 表」，运行期以**世界无关**的 `ConfigManager` 提供无分配查询。
>
> **本示例的配置「来自数据文件」**：`HYCSimpleConfigDemoSystem` 从 `Config/Resources/items.csv` 读取一份真实物品数据
> （8 行，含价格/堆叠/重量/稀有度/类别），运行期 Build 成 `ConfigBlobTable<HycSimpleItemRow>` 注册——不再是代码里手敲 3 行。

---

## 一、一句话心智模型

- **模板/Excel → 生成期**：`ConfigTemplate` + `ConfigFieldType` 描述一张表的结构；`ConfigGenerator` 据此生成
  - `CfgXxx` 行结构体（`[BlobGenerate]`，blittable，含 `BlobString` 文本字段）；
  - `XxxBlobSystem`（引导期把导出 `.blob` 读回成 `ConfigBlobTable<CfgXxx>` 并 `ConfigManager.Register`）；
  - 枚举类（分类字段如「1=顺序 2=随机 3=离玩家最远」→ 1 基枚举）。
- **运行期**：任意代码用 `ConfigManager.Get<CfgXxx>(id)` / `TryGet` / `GetAllRows` / `GetRowCount` 查表，
  **无需 World 句柄**（UI 与玩法层都能查）。

---

## 二、端到端步骤（脚本驱动，与 SnowBros/TD 同构）

1. **建模板**：编辑器里定义 `ConfigTemplate`（加字段，枚举字段引用 `ConfigEnumDefinition`）。
2. **生成代码**：菜单 `SnowBros/配置/1-生成配置模板与代码`（或等价菜单）→ 生成 `CfgXxx` + `XxxBlobSystem` + 枚举。
3. **填数据 + 导出 Blob**：菜单 `.../2-导出Blob` → 把行烘焙成 `Assets/StreamingAssets/ConfigBlob/*.blob`。
4. **引导期注册**：生成的 `XxxBlobSystem`（挂在 A1 数据阶段）`BlobAssetReference<BlobRoot<CfgXxx>>.TryRead` →
   `ConfigBlobTable<CfgXxx>.FromBlob(blob, r => r.Id)` → `ConfigManager.Register(table)`。
5. **运行期读**：`ConfigManager.TryGet<CfgXxx>(id, out var cfg)`。

> 本 Sample 的 `HYCSimpleConfigDemoSystem` 把「第 4 步建表+注册」**从数据文件加载**了：
> 运行期读 `Resources/items.csv` → `ConfigBlobTable<HycSimpleItemRow>.Build(rows, index)` → `ConfigManager.Register`。
> 这样导入即跑、不依赖先跑生成器，且配置数据显式独立于代码（改数据只改 CSV）。真实项目请走上面第 2~4 步。

---

## 三、给 AI 的「雷区」（写代码前必读）

1. **行结构体必须 blittable**：`ConfigBlobTable<TRow> where TRow : unmanaged`。含 `string`/`class` 字段会编译不过；
   文本一律用 `BlobString`（生成器自动处理）。本例用纯标量规避。
2. **查表靠 id 索引**：`ConfigBlobTable.Build(rows, explicitIndex)` 的 `explicitIndex` 必须给出 `id→行序`，
   否则 `TryGet(id)` 按行序 0..n-1 找，命中不到你想要的 id。本例显式建了 `id→index`。
3. **枚举字段在 Blob 里是 `int`**：只有「生成的配置类字段」才是枚举类型；`ConfigManager` 取回的是 `int`，
   需 `(int)XxxKind.Red` 比较，且数值是 **1 基**（没有 0 值）。
4. **`ConfigManager` 读是世界无关的**：不要在系统里缓存 World 来查表；直接静态调用即可。
5. **`ConfigValidator`**：菜单 `HYC Framework/Tools/Validate Config` 会校验缺 `[BlobGenerate]` / 非 blittable 字段，
   生成器产物默认合规；手写行注意第 1 点。
6. **命名须框架中立**：示例用 `Item`/`Price`/`StackMax` 这类通用概念；真实工程按自己领域命名，
   但**框架样例里不要写使用方具体玩法**（如某游戏的 `ItemKind`/`WavePointStrategy`）。
7. **外部数据也要 blittable**：从 CSV/Excel 读到的字段，最终必须映射成纯标量 `TRow`（见第 1 点）。
   本例 CSV 里的 `Name` 只给人看，行内不存文本（避免非 blittable）；需要文本就走生成器的 `BlobString`。

---

## 四、验证清单（进 Unity 后）

- [ ] 导入 Sample 后 `HYCSimpleConfigDemoSystem` 能编译（依赖 `HYC.Framework.Config` 与 `Unity.Entities`）。
- [ ] 进 Play，Console 打印 `[HYCSimple] 配置表加载：8 行，已注册到 ConfigManager` 与
  `查 Id=1002 → Price=120 StackMax=10 Weight=0.3 Rarity=2 Category=2`。
- [ ] 确认数据来自文件：改 `Config/Resources/items.csv` 某行 Price 后重进 Play，日志里的 Price 随之变化（证明配置不在代码里写死）。
- [ ] 走真实链路：建模板 → 生成代码 → 导出 Blob → 引导期注册 → `ConfigManager.TryGet<CfgXxx>(id)` 命中。
- [ ] 行结构里加一个 `BlobString Name` 字段，确认生成器/读取对中文文本无截断。
