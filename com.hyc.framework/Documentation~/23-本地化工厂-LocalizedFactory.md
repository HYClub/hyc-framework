# 23 · 本地化工厂（LocalizedFactory · AI / 工具向）

> 给 **AI 代理 / 脚本 / 工具**用的程序化建表入口：不用开编辑器，也能**加列（语言）、加行（key）、加值（译文）**，然后一键写回 `.xlsx` 并自动导入刷新 Blob。
> 编辑器里手搓表格见 [20 多语言总览](./20-多语言-总览.md) / [21 本地化数据管线](./21-本地化数据管线.md)。
> 底层读写复用 `LocalizedExcelReader.WriteWorkbook` / `ParseExcelFile`（与导入同格式、同表头约定）。

## 一、什么时候用

| 场景 | 用哪个 |
| --- | --- |
| 人工在编辑器里逐条填 | `LocaleWindow`（本地化编辑器） |
| AI / 脚本批量生成、补译、合并多份草稿 | **`LocalizedFactory` + `LocalizationTableBuilder`** |
| 已有 `.xlsx` 想增量改（AI 打开 → 加几行 → 存回） | `LocalizedFactory.Load(path)` → 改 → `SaveAndImport` |

本质：`LocalizedFactory` 是「本地化表的 `ConfigAssetFactory`」——把「建数据」从手工 UI 里抽出来，给代码/AI 一个稳定的入口。

## 二、类型与位置

- `HYC.Framework.Loc.Editor.LocalizedFactory` —— 静态工厂（入口）。
- `HYC.Framework.Loc.Editor.LocalizationTableBuilder` —— 流式（fluent）建表器，所有修改方法都 `return this` 可链式调用。
- 文件：`Editor/LocalizedFactory.cs`（Editor 程序集，依赖 NPOI 仅编辑器可用）。

## 三、API 速查

`LocalizationTableBuilder`：

| 方法 | 作用（对应你说的） | 备注 |
| --- | --- | --- |
| `AddLanguage(code)` | **加列**（语言） | 幂等；经 `LocaleUtil.NormalizeLangHeader` 归一（`中文`/`English` → `cn`/`en`） |
| `AddKey(key)` | **加行 / 加 key** | 幂等；文本用 `Set` 填 |
| `Set(key, language, text)` | **加值**（某 key 某语言的译文） | 自动补建 key 与语言列 |
| `SetMany(key, IDictionary)` | 一次填一个 key 的多种语言 | 遍历调用 `Set` |
| `RemoveKey(key)` | 删行 | — |
| `RemoveLanguage(code)` | 删列 | 同步从每行移除 |
| `DefaultLanguage` | 默认语言（写进 `lang` blob） | 默认 `en` |
| `Languages` / `Keys` | 只读快照 | — |
| `Save(path)` | 写 `.xlsx` | 单 sheet `Localization`；同时记录默认语言 |
| `SaveAndImport(path)` | 写盘 + 自动 `ImportAll()` 刷新 Blob | **最常用** |

`LocalizedFactory` 静态方法：

| 方法 | 作用 |
| --- | --- |
| `Create()` | 新建空 builder |
| `Load(path)` | 读现有 `.xlsx` 进 builder（round-trip） |
| `WriteExcel(path, table, languages=null, defaultLanguage=null, import=true)` | 一步到位：key→(lang→text) 字典 → 写盘（可选自动导入） |

## 四、示例

### 4.1 流式：加列、加 key、加值，写盘并导入
```csharp
using HYC.Framework.Loc.Editor;

LocalizedFactory.Create()
    .AddLanguage("en").AddLanguage("cn").AddLanguage("jp")
    .Set("ui.start", "en", "Start")
    .Set("ui.start", "cn", "开始")
    .Set("ui.start", "jp", "スタート")
    .Set("enemy.slime.name", "en", "Slime")
    .Set("enemy.slime.name", "cn", "史莱姆")
    .Set("enemy.slime.name", "jp", "スライム")
    .SaveAndImport("Assets/Localization/Localization.xlsx");
```

### 4.2 一次填一个 key 的多种语言
```csharp
var b = LocalizedFactory.Create().AddLanguage("en").AddLanguage("cn");
b.SetMany("item.heart.desc", new Dictionary<string, string>
{
    ["en"] = "Restores HP",
    ["cn"] = "恢复生命",
});
b.SaveAndImport("Assets/Localization/Localization.xlsx");
```

### 4.3 一步到位（One-shot）
```csharp
LocalizedFactory.WriteExcel(
    "Assets/Localization/Localization.xlsx",
    new Dictionary<string, Dictionary<string, string>>
    {
        ["ui.settings"] = new Dictionary<string, string> { ["en"] = "Settings", ["cn"] = "设置" },
        ["ui.quit"]     = new Dictionary<string, string> { ["en"] = "Quit",     ["cn"] = "退出" },
    },
    languages: new[] { "en", "cn" },   // 固定列顺序
    defaultLanguage: "en",
    import: true);
```

### 4.4 打开已有 xlsx → 补几行 → 存回（增量）
```csharp
var b = LocalizedFactory.Load("Assets/Localization/Localization.xlsx");
b.Set("ui.new.feature", "en", "New Feature")
 .Set("ui.new.feature", "cn", "新功能");
b.SaveAndImport(b.SourcePath);   // 写回原路径并刷新 Blob
```

## 五、约定与坑（和导入一致）

1. **语言列名会被归一**：传 `中文` / `English` / `日本語` 会存成 `cn` / `en` / `jp`，和 `LocalizedExcelReader` 读取时的归一规则一致，保证写回后导入无歧义。直接传 `en` / `cn` 也能正确通过。
2. **单文件 canonical**：builder 对应**一个** `.xlsx`。`SaveAndImport` 走 `LocalizedExcelReader.ImportAll()`，它会合并 `ExcelFolder` 下**所有** `.xlsx`——若目录里还有别的本地化表且与本表同名 key，导入会报 key 冲突（既有行为）。单文件模型下建议该目录只放这一个表。
3. **默认语言**：`DefaultLanguage` 写入 `LocalizationSettings.DefaultLanguage`（持久化到 EditorPrefs），再参与 `lang` blob 生成。
4. **列顺序**：未显式 `AddLanguage` 时，列按「首次 `Set` 引入该语言的顺序」出现；想固定顺序请先 `AddLanguage`。
5. **缺失译文**：单元格留空字符串即可，导入后该格为未翻译（运行期回退到 `[key]` 形式的占位）。

## 六、与编辑器窗口的关系

- `LocaleWindow`：人工可视化编辑，适合人看、手填、查缺失。
- `LocalizedFactory`：程序化 / AI 批量编辑，适合「让 AI 一次性把整张表造出来」或「合并多份草稿」。
- 两者读写的是**同一份格式**（单 sheet `Localization`、首行 `id` + 语言列），可互操作：`LocaleWindow` 里「保存到 Excel」的产物，`LocalizedFactory.Load` 能读回来继续改，反之亦然。
