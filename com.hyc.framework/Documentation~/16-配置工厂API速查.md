# 16 · 配置工厂 API 速查（AI 调用手册）

> **对象**：`HYC.Framework.Config.Editor.ConfigAssetFactory`（`Editor/DataEditor/ConfigAssetFactory.cs`）
> 及其两个公开规格类 `TemplateSpec` / `TemplateFieldSpec`。
> **读者**：AI 助手与自动化脚本。本页所有签名、参数、异常均取自仓库源码，改工厂后请同步本页。
> **定位**：速查表 —— 要理解机制看 [10-配置管线-总览](./10-配置管线-总览.md)，要理解字段与默认值看
> [11-字段类型与导出设置](./11-字段类型与导出设置.md) 的「四·D」，要按任务操作看
> [02-操作手册（按任务）](./02-操作手册-按任务.md)。

---

## 零、铁律（调用前先看）

1. **创建任何配置资产 / 模板必须走 `ConfigAssetFactory`**。禁止
   `ScriptableObject.CreateInstance<T>()` + `AssetDatabase.CreateAsset()` + 手填 `ID` / `GUID`：
   `ID` / `GUID` 必须由 `ConfigIdService`（发号服）派发，手填会跨工程撞号。
2. **模板（`ConfigTemplate`）不走发号服**：它是框架基础设施，无 `ID` / `GUID` 字段。
   对模板调 `Create` / `CreateMany` 会因派发失败而整体回滚，必须走 `CreateTemplate*`。
3. **生成物禁手改**：模板改完走「生成代码」（`ConfigTemplateCodeGen`），不要手改生成的 `.cs`。
4. **先生成代码并等 Unity 编译完成，再建配置资产** —— 生成类在编译前不存在，`Create<T>()` 无法编译。

---

## 一、方法总表

### 1.1 普通配置资产工厂

| 签名 | 返回 | 用途 |
| --- | --- | --- |
| `Create<T>(string assetName, string folder = null)` | `T` | 泛型单建，派发 ID |
| `Create(Type type, string assetName, string folder = null)` | `ScriptableObject` | 非泛型单建（编译期不知道类型时用） |
| `CreateOrGet<T>(string assetName, string folder = null)` | `T` | **幂等**：同名资产已存在则复用，不重新派发 ID |
| `CreateWith<T>(string assetName, Action<T> initialize, string folder = null)` | `T` | 创建 + 立即填值 + `SetDirty` + `SaveAssets` |
| `CreateMany(Type type, string prefix, int count, string folder = null)` | `IReadOnlyList<ScriptableObject>` | 前缀序号批量：`prefix_001 …` |
| `CreateMany(Type type, IEnumerable<string> names, string folder = null)` | `IReadOnlyList<ScriptableObject>` | 自定义资产名批量 |
| `CreateMany<T>(string prefix, int count, string folder = null)` | `IReadOnlyList<T>` | 泛型批量（省去转型） |

`folder` 为空时自动归置到「类型文件夹」（`ConfigDataSettings.RootFolder/<类型短名>`），显式传入时尊重调用方。
所有创建都自动做：发号服派发 `{ID, GUID}` → `ConfigAssetLayout.Register` → 落盘 → 应用类型图标。

### 1.2 模板工厂

| 签名 | 返回 | 用途 |
| --- | --- | --- |
| `CreateTemplate(string displayName, string className = null, string folder = null)` | `ConfigTemplate` | 单模板（**不带字段**） |
| `CreateTemplateWithFields(string displayName, string className, params TemplateFieldSpec[] fields)` | `ConfigTemplate` | 单模板 + 字段一步到位 |
| `CreateTemplates(IEnumerable<TemplateSpec> specs)` | `ConfigTemplate[]` | 批量（全程一次 `SaveAssets` + 一次 `Refresh`） |
| `CreateTemplates(params TemplateSpec[] specs)` | `ConfigTemplate[]` | 可变参数版，委托上一行 |
| `CreateTemplatesAndGenerate(params TemplateSpec[] specs)` | `(ConfigTemplate[] templates, int ok, int failed, string errors)` | 建模板 + 批量生成代码一条龙 |
| `Template(string displayName, string className, params TemplateFieldSpec[] fields)` | `TemplateSpec` | 构造模板规格（配合上面三行） |
| `AddFields(ConfigTemplate template, params TemplateFieldSpec[] fields)` | `int` | 给已有模板批量追加字段，返回新增条数 |
| `RemoveFields(ConfigTemplate template, params string[] fieldNames)` | `int` | 幂等批量删字段，返回实际删除条数 |
| `FindTemplate(string className)` | `ConfigTemplate` | 按生成类名查已有模板；找不到返回 `null` |

`CreateTemplates*` 默认给每个模板挂框架级 `ConfigBase` 基类模板（保证生成的配置类带 `ID` / `GUID`）；
文件名由 `displayName` 净化而来，冲突自动加序号；任一条规格失败 → **整体回滚已创建资产**。

---

## 二、普通工厂：方法详解

### `Create<T>` / `Create(Type, …)`

```csharp
var a = ConfigAssetFactory.Create<HeroConfig>("Hero_001");
var b = ConfigAssetFactory.Create(typeof(HeroConfig), "Hero_002");
```

| 项 | 说明 |
| --- | --- |
| 参数 | `assetName` 必填非空；`folder` 可选（必须在 `Assets` 下） |
| 返回 | 带合法 `ID` / `GUID` 的资产 |
| 异常 | `ArgumentException`（类型不是 ScriptableObject / 名字为空 / 目录不在 Assets 下）；`InvalidOperationException`（唯一类型已存在 / 同名文件已存在 / ID 派发失败，资产回滚） |
| 副作用 | 一次 `SaveAssets` + 一次 `Refresh`，调用一次就落盘 |

### `CreateOrGet<T>`（幂等，批处理首选）

```csharp
var boss = ConfigAssetFactory.CreateOrGet<BossConfig>("Boss01");   // 重复跑不会报错
```

先按「类型文件夹 + 净化名 + `.asset`」查现有资产；命中且可转为 `T` 时直接返回（**不重新派发 ID**），
未命中转 `Create<T>`。AI 脚本可重复执行而不会中断；若该路径被其它类型占用，`as T` 会得到 null，
随后 `Create<T>` 会因「文件已存在」报错（不会覆盖别人的资产）。

### `CreateWith<T>`（创建即填值）

```csharp
var mob = ConfigAssetFactory.CreateWith<MobConfig>("Mob01", c => { c.hp = 50; c.speed = 3.5f; });
```

工厂内完成 `initialize(asset)` → `EditorUtility.SetDirty` → `AssetDatabase.SaveAssets`，AI 不必记这三步。
`initialize` 抛异常时**删除刚创建的资产再抛出**，不留下半初始化资产。
`initialize` 为 `null` 抛 `ArgumentNullException`。

### `CreateMany` 三个重载

```csharp
var mobs  = ConfigAssetFactory.CreateMany<MobConfig>("Mob", 10);                 // Mob_01 … Mob_10
var named = ConfigAssetFactory.CreateMany(typeof(MobConfig), new[] { "Slime", "Bat" });
var raw   = ConfigAssetFactory.CreateMany(typeof(MobConfig), "Mob", 10);          // 非泛型
```

| 项 | 说明 |
| --- | --- |
| 命名 | 前缀版 = `{prefix}_{序号}`，位数按 `count` 自动补零；自定义名字版逐个净化文件名 |
| 校验 | `count < 1` 抛 `ArgumentOutOfRangeException`；名字空白 / 重复抛 `ArgumentException` / `InvalidOperationException`；唯一类型禁止批量 |
| 回滚 | 先预检全部目标路径，再逐个建资产、逐个派发 ID；**任一失败删除本批全部已建资产**后抛出 |
| 性能 | 全批只做一次 `SaveAssets` + 一次 `Refresh`；比分 N 次调 `Create` 快得多 |
| 实现 | 前缀版 / 自定义名字版 / 泛型版共用私有 `CreateManyCore`，行为完全一致 |

---

## 三、模板工厂：方法详解

### 最常用：建模板 + 生成代码一条龙

```csharp
var r = ConfigAssetFactory.CreateTemplatesAndGenerate(
    ConfigAssetFactory.Template("角色/英雄配置", "HeroConfig",
        TemplateFieldSpec.Int("maxHp", 100).Desc("最大生命"),
        TemplateFieldSpec.Float("moveSpeed", 3.5f),
        TemplateFieldSpec.Bool("isBoss", false).ReadOnly(),
        TemplateFieldSpec.Text("heroName", "新英雄"),
        TemplateFieldSpec.Enum("heroType", "HeroType", "Warrior"),
        TemplateFieldSpec.Sprite("icon")),
    ConfigAssetFactory.Template("道具/道具配置", "ItemConfig",
        TemplateFieldSpec.Int("price", 10)).Unique());

// r.templates / r.ok / r.failed / r.errors
```

返回 `GenerateTemplates` 的 `(成功数, 失败数, 错误汇总)`。**生成失败不回滚模板资产**（资产本身合法），
逐条错误在 `r.errors`；生成完需等 Unity 编译，之后才能用生成的配置类建资产。

### `CreateTemplateWithFields` / `CreateTemplate`

```csharp
var tpl = ConfigAssetFactory.CreateTemplateWithFields("角色/英雄配置", "HeroConfig",
    TemplateFieldSpec.Int("maxHp", 100));
var empty = ConfigAssetFactory.CreateTemplate("占位/待补配置", "TodoConfig");  // 不带字段
```

`CreateTemplateWithFields` 内部走 `CreateTemplates(Template(...))`，因此享有同一套校验与回滚。
故意不叫 `CreateTemplate`：与既有 `CreateTemplate(string, string, string)` 同名会让 `CreateTemplate(null)` 产生重载二义。

### `AddFields` / `RemoveFields` / `FindTemplate`（增量改模板）

```csharp
var tpl = ConfigAssetFactory.FindTemplate("HeroConfig");
if (tpl == null) throw new Exception("模板不存在");

ConfigAssetFactory.AddFields(tpl,
    TemplateFieldSpec.Bool("enabled", true),
    TemplateFieldSpec.Long("exp", 0).ReadOnly());
ConfigAssetFactory.RemoveFields(tpl, "obsoleteField", "tmpField");
```

| 方法 | 行为 |
| --- | --- |
| `FindTemplate` | 扫描全部模板资产按 `className` 精确匹配（区分大小写）；找不到 `null` |
| `AddFields` | 全部字段先构造 + `ConfigDefaultValue.TryValidate` 校验，全通过才一次性写入；同名字段直接报错、**不静默改名**；只查自身字段，继承链重名由生成阶段拦 |
| `RemoveFields` | 幂等：不存在的名字跳过；有实际删除才 `SetDirty` + `SaveAssets` |

### `TemplateSpec`（模板规格）与链式修饰

```csharp
ConfigAssetFactory.Template("角色/英雄配置", "HeroConfig",
        TemplateFieldSpec.Int("maxHp", 100))
    .Unique()                                   // 全工程唯一（Single）
    .Base(otherTemplate)                        // 指定基类模板；不设默认 ConfigBase
    .InFolder("Assets/Configs/自定义目录")        // 覆盖本模板落盘目录
    .Fields(TemplateFieldSpec.Bool("enabled"))  // 追加字段
    .OverrideDefaults(                          // 覆盖基类字段默认值（类型须与基类一致）
        TemplateFieldSpec.Int("hp", 250),
        TemplateFieldSpec.Enum("element", "HeroElement", "Water"));
```

| 字段 / 方法 | 说明 |
| --- | --- |
| `displayName` / `className` | 显示名（必填）/ 生成类名 |
| `folder` / `InFolder(path)` | 该模板自己的落盘目录 |
| `unique` / `Unique(bool = true)` | 声明 Single；工厂禁止重复创建 |
| `baseTemplate` / `Base(tpl)` | 基类模板 |
| `fields` / `Fields(params …)` | 字段规格列表 |
| `overrides` / `OverrideDefaults(params …)` | 基类字段默认值覆盖；生成派生类构造函数赋值，见 [11-字段类型与导出设置](./11-字段类型与导出设置.md) 四·E |

> **目录规则**：批次默认目录 = 第一条规格的 `folder`（都没设时 = 配置模板类型文件夹）；
> 每条规格可用 `InFolder` 覆盖自己，各目录独立做重名冲突检测。
> （2026-11-10 修复：此前只有 `list[0].folder` 生效，其余规格的 folder 被静默忽略。）

---

## 四、`TemplateFieldSpec` 静态工厂全表

**默认值按真实类型传，工厂自动落值槽**：AI 写 `Int("maxHp", 100)` 即可，不必知道
`defaultNumber` / `defaultReal` / `defaultFlag` / `defaultText` / `defaultEnumValue` 这些内部槽位，也不会填错槽。

### 支持默认值的类型

| 工厂 | 默认值参数 | 生成字面量示例 |
| --- | --- | --- |
| `Int(name, int? v = null)` | `int` | `100` |
| `Long(name, long? v = null)` | `long` | `1000L` |
| `Short(name, short? v = null)` | `short` | `100` |
| `Byte(name, byte? v = null)` | `byte` | `100` |
| `UInt(name, uint? v = null)` | `uint` | `100u` |
| `Float(name, float? v = null)` | `float` | `3.5f` |
| `Double(name, double? v = null)` | `double` | `3.5d` |
| `Bool(name, bool? v = null)` | `bool` | `true` / `false` |
| `Text(name, string v = null)` | `string` | `"新英雄"`（自动转义） |
| `LocKey(name, string v = null)` | 多语言 key | `"loc.hero.name"` |
| `Addressable(name, string v = null)` | AA 地址 | `"Assets/..."` |
| `AudioEvent(name, string v = null)` | 音频事件 | `"clip:xxx"` / `"wwise:xxx"` |
| `Enum(name, enumClassName, memberName)` | **枚举成员名** | `MyGame.Enums.HeroType.Warrior` |
| `Enum(name, enumClassName, int value)` | 枚举数值 | 同上（跳过反查） |
| `Reference(name)` | — | **单参数重载 = 自引用**，连关键字都不用写 |
| `Reference(name, refTypeFullName)` | — | 配置引用（不支持默认值）；全名可传 `"self"` |
| `SelfReference(name)` | — | 自引用的语义化别名，等价 `Reference(name)` / `Reference(name, "self")` |

### `self` 关键字：不用拼类名

`ConfigTemplateCodeGen.SelfRefKeyword = "self"`（大小写不敏感、可带空格）是唯一真源，
由 `ResolveRefArgument(raw, className)` 统一解析成「当前命名空间 + 本模板 className」。
**所有输入口在落盘前解析**，模板资产里不存魔法字符串，生成器 / UI / 导出链路无需感知。

| 输入口 | 写法 |
| --- | --- |
| 工厂（代码，推荐） | `TemplateFieldSpec.Reference("parent")` 或 `SelfReference("parent")` |
| 工厂（文本参数） | `TemplateFieldSpec.Reference("parent", "self")` |
| 批量创建窗口（文本行） | `关卡/关卡配置,LevelConfig|parent:Reference:self,index:Int:1` |
| Excel 导入（类型参数） | `Reference` 的 TypeArg 填 `self` |

`self` 只在输入口有效：若手工把 `self` 直接写进 .asset，模板汇总区会红字提示「self 未解析」，
「生成代码」硬拦截。`className` 为空时关键字不猜默认类名，工厂直接抛异常。

- 数值 / bool / 字符串工厂：**不传默认值 = 不生成初始化器**；`Text(name, "")` 明确表示默认空串。
- `Enum(..., memberName)` 现场查 `ConfigEnumDefinition` 资产，按 `ValueOf` 把成员名换算成数值存专用 `int` 槽；
  **枚举定义不存在 / 成员名找不到直接抛异常**，不静默写 0。Flags 组合值暂不支持。
- `Enum(..., int value)` 适用于已知数值、或枚举定义资产还没建的场景。

### 不支持默认值的类型（只能 `Of` 或快捷入口）

| 工厂 | 等价 `Of(name, …)` |
| --- | --- |
| `Sprite` / `Texture2D` / `GameObject` / `AudioClip` / `AnimationClip` | 同名 `ConfigFieldType` |
| `Vector2` / `Vector3` / `Color` / `BehaviourTree` | 同名 `ConfigFieldType` |
| `Of(name, ConfigFieldType type)` | 任意类型（含结构值类型 / Char / Decimal / 其它资源族） |

### 链式修饰（所有工厂通用）

| 方法 | 作用 |
| --- | --- |
| `.Desc("描述")` | 写模板字段表的「描述」列 |
| `.ReadOnly(bool = true)` | 生成 `[ReadOnly]`，数据编辑器里不可编辑 |
| `.AsList(bool = true)` | 设为 `List<T>`（**列表不支持默认值**） |

---

## 五、端到端示例（AI 直接照抄）

```csharp
// ① 建模板 + 生成代码（一条龙）
var r = ConfigAssetFactory.CreateTemplatesAndGenerate(
    ConfigAssetFactory.Template("角色/英雄配置", "HeroConfig",
        TemplateFieldSpec.Int("maxHp", 100).Desc("最大生命"),
        TemplateFieldSpec.Float("moveSpeed", 3.5f),
        TemplateFieldSpec.Bool("isBoss", false).ReadOnly(),
        TemplateFieldSpec.Enum("heroType", "HeroType", "Warrior")),
    ConfigAssetFactory.Template("道具/道具配置", "ItemConfig",
        TemplateFieldSpec.Int("price", 10)));
if (r.failed > 0) throw new Exception(r.errors);

// ② 等 Unity 编译完成（生成类此时才存在），再建配置资产
var hero = ConfigAssetFactory.CreateWith<HeroConfig>("Hero_001", c => c.maxHp = 200);
var same = ConfigAssetFactory.CreateOrGet<HeroConfig>("Hero_001");   // 复用，不重复建
var mobs = ConfigAssetFactory.CreateMany<ItemConfig>("Item", 20);

// ③ 增量改模板
var tpl = ConfigAssetFactory.FindTemplate("HeroConfig");
ConfigAssetFactory.AddFields(tpl, TemplateFieldSpec.Bool("enabled", true));
ConfigAssetFactory.RemoveFields(tpl, "obsoleteField");
```

---

## 六、坑 / 约束

1. **模板无 ID/GUID**：对模板调 `Create` / `CreateMany` 必然失败并回滚，只能走 `CreateTemplate*`。
2. **唯一类型（Single）**：`Create` 遇到已存在直接抛；`CreateMany` 直接拒绝。用 `CreateOrGet` 做幂等。
3. **同名文件**：`Create` / `CreateMany` 预检到同名 `.asset` 直接抛异常；批处理请改用 `CreateOrGet` 或先清理。
4. **批量回滚**：任一失败删除本批**全部**已建资产；`CreateTemplatesAndGenerate` 的生成失败则不回滚模板资产。
5. **默认值只落主类初始化器**：侧类型 / Blob 是 `struct`，C# 不允许实例字段初始化器（CS0573），
   导出器反射读资产值，故导出链路零改动。
6. **默认值支持范围**：列表 / 结构值类型（Color、Vector…）/ `Char` / `Decimal` / 资源引用 / 配置引用一律不支持；
   折叠区会画禁用态并说明原因。校验唯一真源 `ConfigDefaultValue.TryValidate`。
7. **枚举成员顺序 / 增删是破坏性变更**：按数值存的默认值与导出侧 `int` 一样会串号，改动后要重新生成代码并复核。
8. **枚举默认值要求枚举定义资产已存在**（否则 `TemplateFieldSpec.Enum(..., 成员名)` 抛异常）；
   若枚举还没建，用 `Enum(..., int 数值)` 或先建枚举定义。
9. **「已存在资产新增字段是否吃到初始化器值」需在 Unity 实测**：新建资产链路是确定的；
   若存量资产不吃，走回填工具，不要 hack 生成器。
10. **本页不覆盖的范围**：字段检查规则、客户端/服务端导出设置、关联参数（visibility）
    目前仍要在模板编辑器里配置，工厂规格暂不暴露。
11. **`folder` 必须在 `Assets` 下**，否则 `EnsureFolder` 抛 `ArgumentException`。
12. **派生默认值覆盖**：`OverrideDefaults` 的目标必须在基类继承链中、基类字段未勾 `readOnly`、
    规格类型与基类字段一致，且类型支持默认值；任一不满足直接抛异常并整体回滚。
    生成结果是派生类构造函数赋值（不是字段初始化器），**只对新建资产生效，不回填存量资产**；
    基类字段删 / 改名 / 改为只读后，模板汇总区红字且「生成代码」硬拦截。
13. **自引用字段**：`Reference(name)` 单参 / `SelfReference(name)` / `Reference(name, "self")` 三种等价写法，
    关键字见上文「`self` 关键字」。建模板时解析成「当前命名空间 + 本模板 className」，
    生成期按**模板资产 className** 判定引用存在（不依赖编译类型）；因此模板与生成代码都还不存在也能建。
    生成并编译后 UI 按短名找到真实类型；生成前下拉显示「引用:ClassName（代码未生成）」，
    汇总区不会误报「引用类型不存在」。典型用途：树形配置（parent / nextLevel）引用同表另一行。

---

## 七、审查记录（2026-11-10）

本轮通读 `ConfigAssetFactory.cs` 后列出并处理的 AI 调用障碍：

| # | 发现 | 处理 |
| --- | --- | --- |
| 1 | `CreateMany` 只能同类型 + 前缀序号 | 加自定义名字版 + 泛型版，三入口共用 `CreateManyCore` |
| 2 | 创建后填字段要自己 `SetDirty` + `SaveAssets` | 加 `CreateWith<T>` |
| 3 | 重复跑脚本因同名资产抛异常 | 加 `CreateOrGet<T>` |
| 4 | 批量逻辑若再加一口会复制回滚代码 | 抽 `CreateManyCore` 为唯一真源 |
| 5 | 单模板 `CreateTemplate` 不带字段 | 加 `CreateTemplateWithFields`（避开 null 二义，不改原重载） |
| 6 | 建完模板还要手动调 `GenerateTemplates` | 加 `CreateTemplatesAndGenerate` |
| 7 | **`CreateTemplates` 只有 `list[0].folder` 生效，其余被静默忽略** | **修复**：每条 spec 可用自己的 folder，各目录独立做重名检测 |
| 8 | 增量改模板只能 Add，不能查 / 删 | 加 `FindTemplate` / `RemoveFields` |
| 9 | `TemplateSpec` 的 unique / base / folder 只能手写字段 | 加 `.Unique()` / `.Base()` / `.InFolder()` / `.Fields()` 链式 |
| 10 | 常用资源/结构类型每次都要写 `Of(name, ConfigFieldType.X)` | 加 9 个快捷工厂 |
