# HYCSimple · HYC Framework 示例集

> 框架自带的最小可用示例。
>
> **两种引入方式（二选一，别同时用，否则同一批类型编译两次 → CS0101）：**
> - **官方**：Package Manager → **Samples → HYCSimple → Import**，落到 `Assets/Samples/com.hyc.framework/<版本>/HYCSimple/`。
>   前提：包已被解析且 `package.json` 声明了 `samples`（新增后**需重启 Unity** 才重新解析）。
> - **菜单快捷**：`HYC Framework/Samples/Import HYCSimple` → **全量重拷**到 `Assets/HYCSimple/`（普通目录，一定被导入）。
>   ⚠️ 不要把示例拷进 `Assets/Samples/<包名>/`：那是 Package Manager **保留区**，包未解析时 Unity 扫到 `<包名>` 一层就停，
>   Project 窗口展开为空**且不报错**（官方文档点名的 "samples field in the package manifest is empty" 症状）。
>
> 本目录（`Samples~`）的 `~` 后缀让 Unity **不编译、不默认导入**它；只有导入动作才把它带进工程。
>
> **⚠️ 示例默认「不自动运行」（血泪教训，动手前先读）**
> 所有 `*DemoSystem` 都标了 `[DisableAutoCreation]` —— Unity **不会**自动把它们拉进 World。
> 原因：框架的 Manager（Localization / Config / BT）是**进程级静态单例**，示例 System 一旦偷偷自启，
> 就会在宿主工程毫无察觉的情况下改写它们。真实事故一次：
> 宿主工程已在 `[RuntimeInitializeOnLoadMethod]` 阶段从 StreamingAssets 加载了自己的 18 条本地化 key，
> 示例却在第一次 `OnUpdate` 里执行 `LocalizationManager.Reload(示例目录)` —— 宿主张表当场蒸发，
> 主菜单全线 `未找到Key : xxx`，且因为示例每帧兜底，宿主怎么重导本地化都会被下一帧洗掉，极难定位。
> （当时 `Reload` 的旧实现是「先清空再读盘」，读盘即洗表；现已改为**原子替换**——失败只报错并保留原数据，
> 但**成功读到另一份目录仍会整表替换**，所以下面的规矩依然有效。）
> 由此定下三条硬规矩：
> 1. 示例 System 一律 `[DisableAutoCreation]`，由 **`HYCSimpleSceneBootstrap`**（MonoBehaviour）显式手动 Update。
> 2. 示例**不得抢占**宿主已加载的全局单例：`HYCSimpleLocSampleData.CanTakeOver` 会在
>    「全局表已有非示例数据」时跳过 `Reload`，并打印醒目警告。
> 3. 想把示例跑起来，请**新建一个空场景**，把 `HYCSimpleSceneBootstrap` 挂到任一 GameObject 上再 Play；
>    宿主工程什么都不挂就**零副作用**。
>
> **编译 / Assembly 说明**：示例自带两个 asmdef，导入后编译成独立程序集（**不污染你的 `Assembly-CSharp`**）：
> - `HYCSimple.asmdef`（根，运行时，含 Attr/Config/BT/Loc/Scene）：`allowUnsafeCode: false` ——
>   `Loc` 示例已改用 Unity 内置 `BlobBuilder.AllocateString`（2022.3 / Entities 1.0 已按 UTF-8 真实字节数分配，中文不再截断，且是**安全 API**），
>   因此**不需要**在工程的 Player Settings 里开「Allow unsafe code」，也不会出现 CS0227。
> - `BT/Editor/HYCSimple.Editor.asmdef`（仅编辑器）：放 `HYCSimpleBTTreeBuilder` / `HYCSimpleAttrSampleAssets` / `HYCSimpleSampleDataCreator`，引用 `HYC.Framework.Editor` + `HYC.Framework.Runtime`。
> ⚠️ **不要删除这两个 asmdef**：删了示例就会退回 `Assembly-CSharp`，与旧 PM 副本同批类型重复 → CS0101。
>
> **示例含真实数据资产（不是只手搓代码）**：进 Unity 后先跑
> `HYC Framework/Samples/创建全部示例资产`（或单独跑 `BT/创建示例树并导出`、`Attr/创建示例属性资产`），
> 会生成：
>   · `Assets/HYCSampleTrees/HYCSimpleDemoTree.asset`（BT 树）+ 导出 `StreamingAssets/BTBlob/*.blob`
>   · `Assets/HYCSimpleSampleAssets/GameAttrCatalog.asset`（属性目录）+ `GameHeroSet.asset`（属性预设）
> 之后进 Play，BT / Attr 示例系统就会读取这些资产跑真实链路。
>
> **写给 AI**：每个子系统目录都有一份 `AI-NOTES.md`，讲清「怎么接入、菜单路径、雷区」。
> 这是框架对外教学/上手的事实入口，注释与说明都面向「先读源码/样例再写」。

---

## 目录结构

```
Samples~/HYCSimple/
├─ README.md                 ← 本文件（总览 + 注释规范）
├─ Attr/                     ← 通用属性配置（已完成）
│  ├─ AI-NOTES.md            ← 属性系统接入笔记（含两页编辑器说明 + 雷区）
│  ├─ GameAttrKey.cs         ← 示例生成访问器：枚举
│  ├─ GameAttrKeyMap.cs      ← 示例生成访问器：枚举→哈希/类型 映射
│  ├─ GameAttrBag.cs         ← 示例生成访问器：类型安全读写
│  └─ HYCSimpleAttrDemoSystem.cs ← 运行时演示（加载 GameHeroSet.asset → InitFromSet 真实链路，回退手写 buffer）
├─ Config/                   ← 配置接入（已完成）
│  ├─ AI-NOTES.md
│  ├─ HycSimpleItemRow.cs    ← 示例行结构体（blittable，等价于生成器产物）
│  ├─ HYCSimpleConfigDemoSystem.cs ← 运行时演示（从 items.csv 读真实数据 → Build → Register → Get/TryGet）
│  └─ Resources/items.csv   ← 真实物品数据（8 行，配置即数据）
├─ BT/                       ← 行为树接入（已完成）
│  ├─ AI-NOTES.md
│  ├─ HYCSimpleBTDemoSystem.cs   ← 运行时演示（LoadAll + 挂 RunningBT 让解释器驱动）
│  ├─ HYCSimpleEnemyBTNodes.cs   ← 运行时自定义节点（条件/攻击/巡逻，反射自动注册，树真实执行）
│  └─ Editor/
│     ├─ HYCSimpleBTTreeBuilder.cs      ← 编辑器菜单：程序化建树 + 导出 Blob（生成 HYCSimpleDemoTree.asset）
│     ├─ HYCSimpleAttrSampleAssets.cs  ← 编辑器菜单：生成 GameAttrCatalog/GameHeroSet.asset
│     └─ HYCSimpleSampleDataCreator.cs ← 编辑器菜单：一键「创建全部示例资产」
├─ Loc/                      ← 多语言接入（已完成）
│  ├─ AI-NOTES.md
│  ├─ HYCSimpleLocSampleData.cs   ← 运行时自包含数据生成（规避 CJK 截断；含「不抢占宿主表」守卫 CanTakeOver）
│  └─ HYCSimpleLocDemoSystem.cs   ← 运行时演示（Reload/GetText/SetLanguage/HasKey）[DisableAutoCreation]
└─ Scene/                    ← 场景/Bootstrap 串联（已完成）
   ├─ AI-NOTES.md
   └─ HYCSimpleSceneBootstrap.cs   ← MonoBehaviour：唯一入口，手动 Update 各 DemoSystem（挂到空场景 GameObject 才跑）
```

---

## 「给 AI 看的注释」规范（本 Sample 的统一约定）

1. **每个子系统一份 `AI-NOTES.md`**：结构固定为
   - 一句话心智模型
   - 端到端步骤（菜单路径逐条核对源码，不编造）
   - 编辑器/窗口使用要点
   - 「雷区」清单（写代码前必读）
   - 验证清单（进 Unity 后）
2. **示例代码顶部 block comment**：说清「这文件是什么、正常由谁生成、本例为何手写在 Sample 里」。
3. **代码内联注释**：只对「非显然 / 易错」处注释（如 Float 走 bits、枚举序数、Set 不钳制、CJK 截断），不逐行灌水。
4. **命名框架中立**：用 `HP`/`MP`/`Hero`/`Monster`/`EquipSlot`/`Item`/`Price` 这类通用概念；
   **禁止**把使用方工程的具体玩法（如某游戏的 `ItemKind`/`WavePointStrategy`）写进框架样例。
5. **示例自包含可编译**：尽量不依赖手写的 `.asset`/`.prefab`/`.unity` 二进制（易错）；
   用代码在运行时构造等价数据，并在 `AI-NOTES.md` 里讲清「真实项目走哪条路」。

---

## 状态

- ✅ Attr（属性）：示例 + AI-NOTES 已完成。
- ✅ Config（配置）：`HycSimpleItemRow` + `HYCSimpleConfigDemoSystem` + AI-NOTES 已完成。
- ✅ BT（行为树）：运行时 `HYCSimpleBTDemoSystem` + 编辑器建树导出 `HYCSimpleBTTreeBuilder` + AI-NOTES 已完成。
- ✅ Loc（多语言）：`HYCSimpleLocSampleData` + `HYCSimpleLocDemoSystem` + AI-NOTES 已完成。
- ✅ Scene（场景/Bootstrap）：`HYCSimpleSceneBootstrap` + AI-NOTES 已完成。
- 🔒 **示例默认不自启**：所有 `*DemoSystem` 加 `[DisableAutoCreation]`，改由 `HYCSimpleSceneBootstrap`
  手动 Update；Loc 侧另加 `CanTakeOver` 守卫，宿主已加载自己的本地化表时**跳过 Reload**。
  详见文件头「示例默认不自动运行」一节。
- ⚠️ 全部示例**尚未在 Unity 编译验证**（框架侧 C# 也还在等 Unity 编译）；导入后请按各 `AI-NOTES.md` 的验证清单跑一遍。
- ⚠️ 版本漂移：`package.json` 当前 `version=2.0.4`，故官方 PM 导入落点为 `Assets/Samples/com.hyc.framework/2.0.4/HYCSimple/`；
  CHANGELOG 顶部已到 `2.0.6 Unreleased`（发布时记得对齐版本号）。
- 🛠️ 菜单快捷导入落点是 `Assets/HYCSimple/`（不是 `Assets/Samples/<包名>/`，规避 PM 保留区）；两路请二选一。
