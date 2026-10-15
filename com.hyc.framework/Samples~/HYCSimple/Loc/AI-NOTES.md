# HYCSimple · 本地化（Loc）· AI 接入笔记

> 写给 AI（和人类）的接入说明：讲清「多语言数据怎么来、运行期怎么查、雷区」，不解释内部机制。
> 配合同目录 `HYCSimpleLocSampleData.cs`（自包含数据生成）/ `HYCSimpleLocDemoSystem.cs`（运行时查询）食用。
> 基于 Blob 的多语言系统：从 `id` / `lang` / `filter` 文件 + 每语言 `{lang}.lang` Blob 加载，支持 key 查询、运行时切语言、MonoBehaviour 自动刷新与敏感词过滤。

---

## 一、一句话心智模型

- **数据文件**（在 `StreamingAssets/Localization/` 或任意文件夹，由 `Reload(folder)` 读取）：
  - `id`：键列表（一个 `CfgLocalization` blob，每行一个 key）。
  - `lang`：纯文本，默认语言码（如 `cn` / `en`）。
  - `{lang}.lang`（如 `cn.lang` / `en.lang`）：每语言一个 `CfgLocalization` blob，文本顺序与 `id` 对齐。
  - `filter`：（可选）敏感词列表。
  - 🔴 跨端：目录判断/枚举走 `StreamingAssets.Exists("Localization")` / `GetKeysUnder(...)`，
    **不要**用 `Directory.Exists(Application.streamingAssetsPath + …)`（WebGL / Android 无效，见雷区 6）。
- **运行期**：`LocalizationManager.GetText(key)` / `GetText(key, args)` / `SetLanguage(Locale)` / `HasKey(key)`。
  - 切换语言触发 `onLanguageChanged`，挂了 `LocalizedTMP` / `LocalizedBase` 的组件自动刷新。

---

## 二、端到端步骤（照做即可）

1. **产出数据**：用 Loc 数据编辑器（Excel → `id` / `lang` / `*.lang` 等 blob）导出到 `StreamingAssets/Localization/`。
2. **加载（跨端注意）**：框架的 `LocalizationBlobSystem` 在 `BeforeSceneLoad` 自动加载 `StreamingAssets/Localization`
   （内部走 `StreamingAssets` 抽象，不写平台分支）。但 **WebGL / Android 上** StreamingAssets 不能直接读文件，
   自动加载可能早于预取完成而跳过——所以出包到这两个平台的引导入口必须是
   `await FrameworkBootstrap.InitializeAsync("World")`（先 `await StreamingAssets.PreloadAsync()` 再建 World，
   预取完成后自动补加载入，见 `FrameworkLifecycle.EnsureBootDataLoaded`）。PC / iOS 上同步/异步入口等价。
   也可手动 `LocalizationManager.Reload(folder)` 重新加载（如热更后）。
3. **代码取文本**：
   ```csharp
   string s  = LocalizationManager.GetText("ui/start");            // 当前语言
   string s2 = LocalizationManager.GetText("ui/welcome", name);    // {0} 占位符走 string.Format
   ```
4. **运行时切换**：`LocalizationManager.SetLanguage(Locale.Chinese);`（或 `SelectedLanguage = "cn"`）。
5. **UI 自动刷新**：Prefab 上挂 `LocalizedTMP`，Inspector 设 `Key = "ui/start"`，切语言时自动刷新，无需手写。

> 本 Sample 的 `HYCSimpleLocSampleData` 在**运行时用框架同款 API 造了最小数据并写到 persistentDataPath**，
> 这样导入即跑、不依赖先导出真实 blob。真实项目请走第 1 步的编辑器导出。

---

## 三、给 AI 的「雷区」（写代码前必读）

1. **`id` / `*.lang` 都是 `CfgLocalization` blob**（`BlobArray<BlobString>`），不是纯文本；
   只有 `lang` 是纯文本语言码，`filter` 也是 `CfgLocalization` blob。手写文件极易错 → 用编辑器导出。
2. **中文不再截断 + 无需 unsafe**：`BlobBuilder.AllocateString` 在 **Unity 2022.3 / Entities 1.0** 已经是
   **安全 API**，且按 **UTF-8 真实字节数**分配缓冲，CJK（3 字节/字）不再被截断，也**不需要**工程开启
   `/unsafe`、不需要示例 asmdef 开 `allowUnsafeCode`。本 Sample 的 `HYCSimpleLocSampleData` 直接用内置
   `builder.AllocateString(ref arr[i], texts[i])`，中文「开始游戏 / 欢迎」能与英文一起正常写出。
   ⚠️ 仅当你在用**极老**的 Unity（内置版仍按「字符数×2+1」截断 CJK）时，才需要改用框架
   `LocalizedExcelReader.AllocateString` 那套自定义 unsafe 写法——现代版本不要画蛇添足引 unsafe 进来。
3. **文本顺序必须与 `id` 对齐**：`Reload` 按数组下标关联，错位会导致 A key 取到 B 文本。
4. **找不到 key**：`GetText` 返回 `"未找到Key : {key}"`；`HasKey` 可预先判断。
5. **`Locale` / `LocaleUtil`**：`Locale.Chinese` → 码 `"cn"`（不是 `"zh"`）；`en`/`jp`/`kr` 同理。
   `FromCode` 兼容 `zh`/`chs` 等别名，但文件命名请用规范码。
6. **`Reload(folder)` 跨端语义**：内部已按 `StreamingAssets.NeedsPreload` 分流——PC/iOS 走 `Directory.*` /
   `File.*`，WebGL/Android 从预取缓存枚举读取（`GetKeysUnder` + `ReadBytes`），**调用方不需要写平台分支**。
   目录不存在/为空时**只报错并保留现有数据**（原子替换语义，不会洗掉宿主的表）；但注意
   WebGL/Android 上缓存里没有对应前缀同样读空，先确认引导走过 `PreloadAsync`。
   另：`folder` 传绝对路径（`Application.streamingAssetsPath + "/Localization"`）或相对 StreamingAssets 根的路径均可，
   内部会归一化。
7. **命名须框架中立**：示例使用 `ui/start` / `ui/welcome` / `item/name` 这类通用 key；
   真实项目按自己命名规范，但**框架样例不写使用方具体玩法文案**。
8. 🔴 **`Reload` 是「整表替换」，会把宿主的表整艘端走**（本 Sample 最惨的一次事故，务必记住）：
   `LocalizationManager` 是**进程级静态单例**，`Reload(folder)` 一旦**成功读到一份有效数据**
   （目录存在且 `id` 非空），就把 `mIDs/mLangs/mTexts/mLang2Index/mKey2Index` **整张换成新目录的**——
   旧实现是「先清空再读盘」，读盘失败（最常见是路径写错）会把宿主表洗成空，且毫无日志线索；
   现已改为**原子替换**（先读进局部变量、全部成功才一次性提交，失败只报错并保留原数据）。
   但「成功读到错目录」仍然会端走宿主整张表，现象是宿主 UI 全线 `未找到Key : xxx`，
   唯独 `LocalizedBase.Key` 为空的组件（走 `if (!string.IsNullOrEmpty(Key))` 守卫跳过刷新）保留原文本——
   这个「只有某几个按钮正常」的特征很容易被误判成「那条数据缺失」，其实整张表都被换了。
   而且若 `Reload` 由某个每帧兜底的 System 触发，宿主**无论重导多少次本地化都会被下一帧洗掉**。
   因此本 Sample 立了两条规矩：
   - 示例 System 加 `[DisableAutoCreation]`，只在 `HYCSimpleSceneBootstrap` 里被显式 Update；
   - `HYCSimpleLocSampleData.EnsureLoaded()` 先过 `CanTakeOver()`：全局表非空且不含示例 key →
     **跳过 Reload** 并打警告，`EnsureLoaded` 返回 false，调用方据此不得调用 `SetLanguage`。
   写宿主代码时同理：**不要在生产路径里无条件 `Reload` 全局本地化表**；确需热更重载时，
   先确认目标目录就是当前工程自己的数据目录。

---

## 四、验证清单（进 Unity 后）

> 前置：**新建空场景**，把 `HYCSimpleSceneBootstrap` 挂到任一 GameObject 上再 Play
> （`HYCSimpleLocDemoSystem` 标了 `[DisableAutoCreation]`，不挂就完全不跑）。

- [ ] 进 Play，`HYCSimpleLocDemoSystem` 日志依次打出中文「开始游戏 / 欢迎，冒险者！」与英文「Start Game / Welcome, Adventurer!」。
- [ ] `HasKey(item/name)` 为 true；故意查一个不存在的 key 应得 `未找到Key : xxx`。
- [ ] 走真实链路：用 Loc 编辑器导出 `cn.lang`/`en.lang` 到 `StreamingAssets/Localization`，再 `Reload` 查真实 key。
- [ ] 切语言时，场景中挂了 `LocalizedTMP` 的文本组件自动刷新（无需手动调 GetText）。
- [ ] **宿主回归**：在有自己本地化表的工程里导入本示例、什么都别挂进场景，进 Play ——
      宿主 UI 必须**一切正常**，且 Console 出现 `[HYCSimple] 检测到宿主工程已加载自己的本地化数据…跳过 Reload`
      的黄色警告。`persistentDataPath/HYCSimpleLoc/` 会照常生成，但宿手表不受影响。
