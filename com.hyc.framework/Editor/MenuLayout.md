# HYC Framework 编辑器菜单布局

> 本文件是**菜单结构的唯一真源**。改动任何 `[MenuItem("HYC Framework/...")]` 时，
> 必须同步更新本文件与引用了该路径的文档，否则会留下失效路径。
>
> 最后核对：2026-10-13

---

## 一、Unity 菜单 priority 的坑（为什么会莫名出现分隔线）

Unity `MenuItem(itemName, isValidateFunction, priority)` 的 `priority` **不只是排序**，
它还隐式控制**分隔线**：

> **相邻两项的 priority 差值 ≥ 11 时，Unity 会在它们之间画一条分隔线。**

官方文档原文（2022.3 / 2023.2）：
> "If a menu item has a priority value that is 10 or greater than the item that precedes it,
> a separator line divides the two menu items from each other in the menu."
> 注意示例里用 100 与 111（差 11）才有线，**改成 110（差 10）就没有线**。

### 本框架的踩坑记录（两次）

**第一次（错）**：`HYC Framework/BT/导出 Blob(打包用)` 写了 `priority = 30`，而它前一项 priority 为 0，
差值 30 ≥ 11⇒ **BT 子菜单上方凭空多出一条分隔线**，视觉上像是"HYC Framework 标题"与"BT"分属两个分组。

**第二次（对）**：为做三档分级，给**每个**菜单项都显式给了 priority。
注意——**不能只给父菜单的首项**：Unity 的排序作用于**每一个叶子菜单项**，
没写 priority 的项默认为 0，会全部挤到最前面，把分组打乱。
所以 20 个菜单项**逐个**都标了值（见第二节结构图右侧的数字）。

**最终约定**：

1. **每个 `[MenuItem]` 都必须显式写 priority**，且值必须落在第二节结构图标注的区间里。
2. **档内相邻差 ≤ 10** ⇒ 不画线；**档间差 ≥ 11** ⇒ 自动生成分隔线。
3. **父菜单的排序由它首个子菜单的 priority 决定**，所以父菜单顺序也是靠首项的 priority 控制的。
4. 想新增/删除菜单，必须重新核算整套 priority，否则会意外产生或消除分隔线。

---

## 二、菜单结构（2026-10-07 整理后）

```
HYC Framework/
│   ══════════════ ① 内容制作（高频，每天用）══════════════
├── Config/            配置管线
│   ├── Config Window            配置模板定义（className/字段）      pri 0
│   ├── Validate Config          结构校验                          pri 1
│   ├── Data Editor              填表                                pri 2
│   ├── Reissue Duplicate IDs   复制重复 ID 手动重派发（兜底）         pri 3
│   ├── Regenerate All Config Code  一键重生成全部配置代码            pri 4
│   └── Apply Template Icons    手动应用模板图标到 Project 窗口（兜底）  pri 5
├── BT/                行为树
│   ├── BT Editor                可视化编辑器（画布）               pri 10
│   ├── Export Blob (For Build)  导出 Blob 供真机加载（打包必做）    pri 11
│   └── New Tree                新建行为树（统一走 BTAssetFactory）   pri 12
├── UI/                ECS UI
│   ├── Generate UI Binders                                        pri 20
│   ├── Regenerate UI Binders (Clean)                             pri 21
│   └── Generate UI Binders (Selected)                            pri 22
├── Localization/      多语言
│   ├── Import/
│   │   └── Excel (Import & Build Blob)                            pri 30
│   ├── Editors/
│   │   ├── Key Browser                                            pri 31
│   │   ├── Scan Key Usage                                         pri 32
│   │   └── Sensitive Words                                        pri 33
│   └── Settings/
│       └── Localization Settings                                  pri 34
├── Attributes/        通用属性配置
│   ├── Attribute Catalog Editor 两页：属性目录 / 属性组           pri 40
│   └── Generate Accessor        生成 AttrKey/AttrKeyMap/AttrBag   pri 41
├── Talent/            天赋树
│   ├── Talent Tree Editor                                         pri 50
│   └── Reissue Duplicate Tree IDs 复制重复 treeId 手动重派发        pri 51
├── Audio/             音频（Addressables / Wwise）
│   ├── Audio Manager                                              pri 60
│   └── Export Catalog Blob                                       pri 61
│   ══════════════ ② 通用工具（偶尔用）══════════════
├── Tools/             与具体子系统无关的通用资产工具
│   └── Assets/
│       ├── Show Dependents                                        pri 100
│       └── Clean Missing References                               pri 101
│   ══════════════ ③ 项目级（低频，装一次/改配置）══════════════
├── Settings/          框架设置
│   └── Project Settings                                           pri 200
└── Samples/           示例（导入是破坏性操作，故置于末位）
    └── Import HYCSimple                                           pri 210

> **三档之间的分隔线不是画出来的，是priority 差 ≥ 11 自动生成的**：
> `Talent(51) → Tools(100)` 差 49、`Tools(101) → Settings(200)` 差 99 ⇒ 恰好两处分隔线。
> 档内相邻差均 ≤ 10 ⇒ 档内不画线。**不要随意改动这些数值**，改前先读本文档第一、二节。
```

另有**不在 HYC 树内**的开发者自检菜单（刻意与使用方功能入口分开）：

```
Tools/Tests/
├── Update Public API Snapshot
└── Show Public API Diff
```

### 已知例外：`Samples/` 下的 3 个中文项

`HYC Framework/Samples/` 下的三个示例创建入口**保留中文**
（`创建全部示例资产` / `BT/创建示例树并导出` / `Attr/创建示例属性资产`）。
它们定义在 `Samples~/HYCSimple/**/Editor/` 下，**导入示例后才出现在用户工程里**，
属于"示例使用者的操作指引"而非框架功能入口——此时用户看到的是中文界面，
中文反而更符合场景。与主菜单（全英文）的取舍不同，故不强制统一。

---

## 三、为什么这样分级（设计意图）

排序**不是按字母序，而是按使用频率与语义归属**。分三档：

| 档 | 成员 | 使用频率 | 判据 |
| --- | --- | --- | --- |
| ① 内容制作 | Config / BT / UI / Localization / Attributes / Talent | 每天都在用 | 这些是"做游戏内容"本身的能力 |
| ② 通用工具 | Tools/Assets | 偶尔用，出问题时才找 | 与具体子系统无关的通用工具 |
| ③ 项目级 | Settings / Samples | 装一次 / 改配置 | 项目设置与示例导入，都不是日常操作 |

**Settings 与 Samples 为什么不合并**：两者语义不同——`Settings` 是**配置项目**（改配置），
`Samples/Import HYCSimple` 是**往`Assets/` 写文件的破坏性动作**（每次全量重拷）。
把导入塞进设置面板里，用户找"导入示例"会去配置面板找，找不到；
而且破坏性操作藏在设置里更容易被误触。**并列沉底**既压低了存在感，又保持直达。

**这个分级曾经做错过一次**：初版只做了"去掉分隔线 + 改英文名"，
`Samples` 和 `Settings` 仍按字母序夹在中间（Attributes/BT/Config/Localization/**Samples**/**Settings**/Talent/Tools/UI），
看上去和 `Config` 这类能力是同级的——这正是本节要避免的问题。

> ⚠️ **Unity 菜单的硬限制**：菜单**不支持自定义分组标题**，
> 只能在项之间画分隔线。所以"分级"只能靠**位置语义**表达，
> 没法写"内容制作""项目设置"这种标题行。这也是为什么"排序"比"命名"更重要。

---

## 三、分组原则

1. **按能力归属，不按实现类型**。
   例：`Validate Config` 属于配置管线的一环，故放 `Config/` 而非 `Tools/`。
2. **`Tools/` 只放与具体子系统无关的通用工具**。
   能力类工具归到各自子系统，否则 `Tools/` 会变成杂物间。
3. **子项多的父菜单加二级分组**。`Localization/`原有 6 个平铺子项，已按
   `Import /`（数据进来）、`Editors/`（数据浏览编辑）、`Settings/`（配置）三分。
4. **命名统一英文**。曾出现 `导出 Blob(打包用)`、`更新 Public API 快照` 等中文项，
   夹在英文菜单中显得突兀，且与"框架对使用方统一英文"的基调不符。
5. **开发者自检工具不进 `HYC Framework/`**。
   前 23 个 `HYC Framework/` 菜单是给使用方的功能入口；API 快照这类框架自检工具放`Tools/Tests/`。

---

## 四、改菜单时的检查清单

- [ ] 是否需要 priority？**默认不给**。仅"需要沉底"时才给，且确认与相邻项不相邻。
- [ ] 改路径后 `grep -rn "HYC Framework/" --include="*.md" .` 找出所有文档引用并同步。
- [ ] 更新本文件的结构图。
- [ ] 更新 `CHANGELOG.md`（若用户可见）。