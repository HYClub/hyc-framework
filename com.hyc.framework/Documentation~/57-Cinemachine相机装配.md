# 57 · Cinemachine 相机装配（Camera Rig）

> **面向宿主游戏**的用法文档。源码：`Runtime/Cinemachine/`（1 个文件）、`Editor/Cinemachine/`（3 个文件）。
> 依赖 `com.unity.cinemachine` **3.1.5**（框架 package.json 已声明，随框架依赖自动安装；
> CM 自身依赖 `com.unity.splines` 也一并传递安装，无需手工加）。

## 一句话

**rig prefab 就是配置本体**：菜单一键生成「大脑（CinemachineBrain）+ 多机位」结构的 prefab，
之后所有调配都在 **Rig Editor 窗口**里点选完成（不碰 CM 组件 Inspector）；
运行期用 Addressables 实例化 rig，`CinemachineRigBinder` 把 Follow/LookAt 目标下发给各机位。

---

## 一、架构：为什么是「prefab + 窗口直改」

| 方案 | 结论 |
| --- | --- |
| 自建配置资产 + 运行期打 blob 动态组装 profile | ❌ **否决**。Blob 是给 ECS 消费的 blittable 行数据；CM 是 GameObject/组件系统，组装出来还是组件，等于把 Unity 原生已做的事再做一遍 |
| **rig prefab 即配置 + 编辑器窗口直改 + 运行期实例化** | ✅ **采用**。prefab 就是 Unity 原生的「现组装」载体；窗口用 `SerializedObject` 直接渲染 CM 组件字段，**CM 升级新增参数自动出现在窗口里，框架零维护** |

数据流：

```
New Rig（菜单） ──► rig prefab（Camera + Brain + RigTarget + N 个 vcam 机位）
                        │
Rig Editor（菜单） ──► 窗口直改 prefab 内容（LoadPrefabContents / Save）
                        │
运行期 ──► Addressables.InstantiateAsync(rigKey) ──► binder.FollowTarget = 玩家
```

---

## 二、菜单入口

| 菜单 | pri | 用途 |
| --- | --- | --- |
| `HYC Framework/Cinemachine/Rig Editor` | 80 | 打开相机装配编辑器（选中 rig prefab 时自动加载） |
| `HYC Framework/Cinemachine/New Rig` | 81 | 新建 rig prefab（默认四机位） |

## 三、新建 rig

`HYC Framework/Cinemachine/New Rig` → 选保存位置（默认 `Assets/HYC/Cinemachine/`）→ 生成：

```
PlayerRig（根，挂 Camera + CinemachineBrain + CinemachineRigBinder，tag = MainCamera）
├── RigTarget              占位目标（编辑期各机位指向它；运行期由 binder 改写）
├── vcam_FreeLook          pri 20  OrbitalFollow + RotationComposer
├── vcam_ThirdPerson       pri 10  ThirdPersonFollow + RotationComposer
├── vcam_Framed            pri  5  PositionComposer + RotationComposer
└── vcam_PanTilt           pri  0  PanTilt
```

大脑默认混合：`EaseInOut` 0.5 秒。四个默认机位只是起点，增删改全在窗口里。

## 四、Rig Editor 窗口

- **工具栏**：拖入/选择 rig prefab → `Open`；`Save`（有未存改动时显示 `Save *`）；`New Rig...`。
- **左栏机位列表**：`+` 添加、`复制` / `删除` / `↑` / `↓` 排序；按钮上显示机位名与识别出的类型。
- **右栏参数面板**（选中机位）：
  - **名称 / 类型 Type**：类型下拉切换时按配方增删组件（见下表）；
  - **公共参数 Common**：Follow / LookAt / Priority / FOV / Near / Far；
  - **类型参数**：该配方的组件**完整序列化字段**（直接渲染 CM 组件）；
  - **更多 More**：展开该机位**全部**管线组件（Body/Aim/Noise）与扩展组件，
    可逐个调参，`+ 添加管线组件` / `+ 添加扩展组件` 从目录挑选，`移除` 删组件。
- **Rig 级设置**：大脑的 UpdateMethod / DefaultBlend（没有大脑时给「添加」按钮）。

### 机位类型配方（CM 3.1）

| 类型 | 组件配方 | 说明 |
| --- | --- | --- |
| FreeLook 环绕 | OrbitalFollow + RotationComposer | CM 2 时代 FreeLook 的 CM 3 等价物 |
| ThirdPerson 第三人称 | ThirdPersonFollow + RotationComposer | 肩后视角 |
| Framed 构图跟随 | PositionComposer + RotationComposer | 取景框跟随 |
| PanTilt 输入视角 | PanTilt | CM 2 时代 POV 的 CM 3 改名 |
| Dolly 轨道 | SplineDolly + RotationComposer | 依赖 com.unity.splines（随 CM 3 自动安装） |

> 「更多」里可添加的组件目录（管线 11 项 / 扩展 14 项）维护在
> `Editor/Cinemachine/CinemachineRigKind.cs` 的 `CmComponentCatalog`——**按类型名从
> `Unity.Cinemachine` 程序集解析**，CM 版本升级只改这份清单，窗口与工厂逻辑零改动；
> 类型不存在（版本漂移）时优雅跳过并告警，不会让窗口崩。

## 五、运行期接入

```csharp
// 1) 实例化 rig（Addressables key 由你自己的配置/常量给出，框架不规定）
var op = Addressables.InstantiateAsync("PlayerRig", spawnPos, spawnRot);
var rig = op.WaitForCompletion();   // 或用 await / Completed 回调

// 2) 绑定目标（玩家实体/角色根节点）
var binder = rig.GetComponent<HYC.Framework.Cinemachine.CinemachineRigBinder>();
if (binder != null)
{
    binder.FollowTarget = playerRoot;   // 位置跟踪
    binder.LookAtTarget = playerRoot;   // 朝向
}
```

- binder 在 `Awake` 统一下发给 rig 内**所有**子级虚拟相机（含未激活的）。
- **两个目标都留空 = 不改写**：各机位保留编辑期指向的 RigTarget 占位（适合固定机位 rig）。
- 只填其中一个亦可：Follow/LookAt 各自独立生效。
- 框架**不**替你调 Addressables 加载/卸载——生命周期归宿主（与 ECS UI 的 `UIManager` 同策略）。

## 六、坑（都已在代码里规避，列出来防再踩）

1. **CM 管线每个 Stage（Body/Aim/Noise）只取首个组件**。切换机位类型时若不把同 Stage 旧组件移除，
   旧组件继续生效 ⇒「切换了类型却没变化」。工厂 `ApplyKind` 补新组件后同 Stage 旧组件一律移除。
2. **CM 2 组件已移入 `Deprecated/`**（Transposer/Composer/POV/FreeLook…）。配方表只用 CM 3 的
   非 deprecated 类型；菜单/目录同理。
3. **`Priority` 是 `PrioritySettings` 字段**，不是 int——代码里 `vcam.Priority = 20` 靠隐式转换
   （写入即 `Enabled = true`）；读取比较大小也走 `implicit operator int`。
4. **`Lens` 是公共字段**（`LensSettings` 结构体）：改 FOV/Near/Far 必须
   `var lens = cam.Lens; lens.FieldOfView = x; cam.Lens = lens;`（拷-改-写回）。
5. **`CinemachineCamera` 是 sealed**：不能继承它做「我的专属相机」；要扩展走管线/扩展组件。
6. **扩展组件 AddComponent 即自注册**（`Awake → ConnectToVcam`），无需手动启用开关。
7. **新建 rig 用预览场景搭建**（`EditorSceneManager.NewPreviewScene`），不碰你当前打开的场景，
   不会留脏标记；落盘后预览场景关闭。
8. **窗口关闭/切 rig 时自动保存**未存改动（防丢）；域重载导致 AssetDatabase 不可用时只告警不抛出，
   保证预览内容仍被卸载（否则预览场景泄漏）。
9. **机位节点名**：`vcam_` 前缀 + 类型短名；目标占位节点名固定 `RigTarget`（binder 与窗口按此查找）。
10. **窗口参数渲染走 CM 组件自身序列化字段**：CM 升级加字段自动出现；反过来，
    CM 重命名字段时窗口无需改（`SerializedObject` 按名查找，找不到的字段静默不画）。

---

## 七、关键类速查

| 类型 | 程序集 | 可见性 | 职责 |
| --- | --- | --- | --- |
| `CinemachineRigBinder` | Runtime | **public** | rig 根节点的运行期目标绑定器（Follow/LookAt 下发） |
| `CinemachineRigWindow` | Editor | public | Rig Editor 窗口（菜单 80/81） |
| `CinemachineRigFactory` | Editor | internal | 新建 rig prefab + 按配方装配组件 |
| `CinemachineRigKind` / `RigKindRecipe` / `RigKinds` / `CmComponentCatalog` | Editor | internal | 机位类型配方表 + 组件目录（按名反射解析，抗版本漂移） |

命名空间：`HYC.Framework.Cinemachine`（Runtime）/ `HYC.Framework.Cinemachine.Editor`（Editor）。
