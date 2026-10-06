# HYCSimple · 场景 / 引导（Scene / Bootstrap）· AI 接入笔记

> 写给 AI（和人类）的接入说明：讲清「各系统该在什么时机、按什么顺序喂饱」，把前面的 Attr/Config/BT/Loc 串成一条链。
> 配合同目录 `HYCSimpleSceneBootstrap.cs`（普通 MonoBehaviour，挂在任意场景 GameObject 上）食用。
> 框架是「数据驱动」：生成期烘 Blob → 引导期注册 → 运行期查表/驱动 → 表现期（UI 最后跑）。

---

## 一、一句话心智模型

- **更新阶梯顺序**：`Root → A0(引导) → A1(数据) → A2(场景) → A3(玩法) → B9(UI 最后)`。
- **引导期（A1）要把数据喂饱**：本地化 `Reload`、配置表 `Register`、行为树 `BTBlobLoader.LoadAll()` 都应在
  **任何玩法/UI 读它们之前**完成。
- **表现期（B9）**：`UIManager` 在帧末清消息，UI 最后处理，避免读到半截状态。

---

## 二、端到端步骤（照做即可）

1. **世界创建**：游戏 `ICustomBootstrap.Initialize` 里用 `FrameworkWorld` / `FrameworkModeBootstrap` 建世界、
   装系统（按 `BootMode` 过滤 `FrameworkModeOnly`/`FrameworkMode` 标记的系统）。
2. **数据阶段（A1）**——本例 `HYCSimpleSceneBootstrap.Start()` 演示的顺序：
   ```csharp
   LocalizationManager.Reload(streamingAssetsPath + "/Localization"); // 1) 本地化
   BTBlobLoader.LoadAll();                                            // 2) 行为树
   // 3) 配置：生成的 XxxBlobSystem 会自动 Register；也可手动 ConfigManager.Register
   ```
3. **玩法/UI 上场（A2/A3/B9）**：此后实体挂 `RunningBT` 即被 `BTInterpreterSystem` 驱动，
   `ConfigManager.Get` 随时查表，`LocalizedTMP` 随 `SetLanguage` 刷新。

---

## 三、给 AI 的「雷区」（写代码前必读）

1. **顺序错 = 读到空**：玩法在 `Reload` 前调 `GetText` 会拿到 `未找到Key`；在 `LoadAll` 前给实体挂 `RunningBT`
   会让 `BTInterpreterSystem` 把 `RunState.Result` 置 `Failed`（树未注册）。**数据阶段先跑完**。
2. **世界归属**：`HYCSimple*DemoSystem` 都是 `SystemBase`/`ISystem`（无 `[DisableAutoCreation]`），
   会自动创建进**默认 ECS 世界**。若你的游戏用 HYC 自定义 World（`FrameworkWorld`/`FrameworkModeBootstrap`），
   这些系统可能不在你驱动的世界里 —— 通过 `IFrameworkInstaller` 或显式 `GetOrCreateSystemManaged` 加入。
3. **`Reload(folder)` 需目录存在**：真实工程 `StreamingAssets/Localization` 一定在；本例留空时走
   `HYCSimpleLocSampleData.EnsureLoaded()`（运行时自生成），并对传入目录做了 `Directory.Exists` 守护。
4. **配置注册来源**：本 Sample 的 Config 演示是运行时手动 `Register`；真实项目应由生成器产出的 `XxxBlobSystem`
   （挂在 A1 数据阶段）统一注册，避免散落手动调用。
5. **命名须框架中立**：示例 key 用 `ui/start` 等通用名；真实工程按自己规范，但**框架样例不写使用方玩法**。

---

## 四、验证清单（进 Unity 后）

- [ ] 新建空场景，挂 `HYCSimpleSceneBootstrap` 到任意 GameObject，进 Play。
- [ ] Console 打出「场景引导完成：Loc 已加载，BT 加载 N 棵，当前语言=cn，示例文本=开始游戏」。
- [ ] 顺序验证：把 `BTBlobLoader.LoadAll()` 移到 `RunningBT` 挂组件之后，应见到树未注册的 Failed 日志（确认顺序敏感）。
- [ ] 接真实工程：把 `localizationFolder` 指向 `StreamingAssets/Localization`，确认读的是真实多语言数据。
