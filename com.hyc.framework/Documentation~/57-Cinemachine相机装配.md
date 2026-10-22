# 57 · Cinemachine 相机装配（Camera Rig）

> **面向宿主游戏**的用法文档。源码：`Runtime/Cinemachine/`（14 个文件）、`Editor/Cinemachine/`（4 个文件）。
> 依赖 `com.unity.cinemachine` **3.1.5**（框架 package.json 已声明，随框架依赖自动安装；
> CM 自身依赖 `com.unity.splines` 也一并传递安装，无需手工加）。
>
> 本文只讲**装配**（rig prefab / 机位 / binder / 布局切换）。
> 两个玩家分屏、两侧看到不同东西、任意斜切弯曲 → 见
> [59-Cinemachine分屏与双世界-使用指南](./59-Cinemachine分屏与双世界-使用指南.md)；原理见 58 号。

## 一句话

**rig prefab 就是配置本体**：菜单一键生成「大脑（CinemachineBrain）+ 多机位」结构的 prefab，
之后所有调配都在 **Rig Editor 窗口**里点选完成（不碰 CM 组件 Inspector）；
运行期用 Addressables 实例化 rig，`CinemachineRigBinder` 负责绑挂点、列机位、切机位，
并附一个随 prefab 生成的机位访问器类（`{名字}Cameras.g.cs`）供代码强类型取相机；
多人同屏 / 分屏由 `CinemachineViewSplitter` 一键切换。

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
├── vcam_Framed            pri  0  PositionComposer + RotationComposer
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

### 5.1 最小接入

```csharp
// 1) 实例化 rig（Addressables key 由你自己的配置/常量给出，框架不规定）
var op = Addressables.InstantiateAsync("PlayerRig", spawnPos, spawnRot);
var rig = op.WaitForCompletion();   // 或用 await / Completed 回调

// 2) 绑挂点 + 切机位
var binder = rig.GetComponent<HYC.Framework.Cinemachine.CinemachineRigBinder>();
binder.SetTargets(playerHead, playerHead);            // 跟随 + 朝向，赋完即下发
binder.SwitchTo(HYC.Framework.Cinemachine.RigKind.ThirdPerson);
```

> **赋值即下发**：`FollowTarget` / `LookAtTarget` 的 setter 会立刻把目标推给所有机位。
> 早期版本只在 `Awake` 下发一次，于是「`InstantiateAsync` 之后再赋属性」不生效（Awake 早跑完），已修。

- **两个目标都留空 = 不改写**：各机位保留编辑期指向的 RigTarget 占位（适合固定机位 rig）。
- 只填其中一个亦可：Follow/LookAt 各自独立生效。
- 框架**不**替你调 Addressables 加载/卸载——生命周期归宿主（与 ECS UI 的 `UIManager` 同策略）。

### 5.2 binder 的 API

| 成员 | 说明 |
| --- | --- |
| `Cameras` | rig 内全部机位（惰性扫描并缓存，顺序 = prefab 子节点顺序） |
| `Active` | 当前活跃机位（优先级最高且启用）；无可用机位返 `null` |
| `Get(RigKind)` / `Get(string)` | 取机位；取不到返 `null`（不抛异常） |
| `TryGet(RigKind/string, out cam)` | 同上但返 `bool`（推荐写法） |
| `SwitchTo(RigKind / string / CinemachineCamera)` | 切机位；目标不存在返 `false` 且当前机位不变 |
| `SetTargets(follow, lookAt)` / `ApplyTargets()` | 一次设两个挂点 / 手动重下发 |
| `ActiveChanged` | 切换**实际生效**时触发（HUD、音效、输入方案可挂这里） |
| `Rescan()` | 运行期增删机位后重建缓存（并重拍基础优先级） |

**机位怎么认类型**：每个机位节点上有一个 `CinemachineRigCameraTag`（窗口新建 / 切类型时自动写入，
也可在 Inspector 手改）。`SwitchTo(RigKind.X)` 按它命中；同类型有多个机位时取 prefab 里靠前的那个，
要精确定位就用节点名（`SwitchTo("vcam_FreeLook")`）。

**切换机制（重要）**：CM 3 没有「激活某台相机」的 API，活跃相机 = **Priority 最高的那台**。
binder 首次扫描时快照各机位的基础优先级，切换时把目标抬到「基础最高 + 10」、其余复原基础值，
反复切换不会让优先级无限膨胀、原有梯度也不被破坏。
**⇒ Priority 归 binder 所有**：宿主不要再手改机位的 Priority，否则下次切换会被复原成基础值。

### 5.3 生成的机位访问器

Rig Editor 在**保存 / 新建 / 打开 rig** 时，会为该 prefab 生成一个访问器类
（`GeneratedPaths.Cinemachine` = `Assets/HYC/Generated/Cinemachine/{名字}Cameras.g.cs`，
命名空间 `{生成代码命名空间 root}.Cinemachine`）：

```csharp
using YourCompany.Cinemachine;

var head = PlayerRigCameras.FreeLook(binder);   // 取机位，不存在返 null
PlayerRigCameras.ToFreeLook(binder);            // 切机位，不存在返 false
PlayerRigCameras.Count;                         // 机位数量
PlayerRigCameras.Names;                         // 全部机位节点名
```

- **生成物禁手改**：结构变了回窗口 Save（或重开该 rig）即可重写，手改会被覆盖。
- **prefab 改名 / 删除**：任意一次生成都会清掉失效的旧文件；类名是 `{prefab 名}Cameras`，
  不同目录同名 prefab 会自动追加目录名 / 短 GUID 区分。
- 宿主程序集需引用 `HYC.Framework.Runtime` 与 `Unity.Cinemachine`（访问器返回 CM 的相机类型）。

### 5.4 完整示例（复制即用）

```csharp
using HYC.Framework.Cinemachine;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>把一份 rig prefab 挂到某个角色身上：实例化 → 绑挂点 → 切机位 → 退出时释放。</summary>
public sealed class PlayerCameraRig : MonoBehaviour
{
    [SerializeField] string m_RigKey;            // Addressables key / 地址
    [SerializeField] Transform m_FollowTarget;   // 角色根或头部挂点

    private CinemachineRigBinder m_Binder;
    private AsyncOperationHandle<GameObject> m_Handle;
    private bool m_Loaded;

    void Start()
    {
        m_Handle = Addressables.InstantiateAsync(m_RigKey, transform.position, transform.rotation);
        m_Handle.Completed += OnRigLoaded;
    }

    private void OnRigLoaded(AsyncOperationHandle<GameObject> handle)
    {
        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogError("[Camera] rig 加载失败：" + m_RigKey);
            return;
        }

        m_Loaded = true;
        m_Binder = handle.Result.GetComponent<CinemachineRigBinder>();
        m_Binder.SetTargets(m_FollowTarget, m_FollowTarget);   // 挂点，赋完即下发
        m_Binder.ActiveChanged += OnCameraChanged;
        m_Binder.SwitchTo(RigKind.ThirdPerson);
    }

    /// <summary>瞄准时换构图机位（由输入 / 状态机调用）。</summary>
    public void EnterAim()
    {
        if (m_Binder != null)
            m_Binder.SwitchTo(RigKind.Framed);
    }

    private void OnCameraChanged(CinemachineCamera camera)
    {
        // HUD / 音效 / 输入方案的挂钩点：camera 是新的活跃机位
    }

    void OnDestroy()
    {
        if (m_Binder != null)
            m_Binder.ActiveChanged -= OnCameraChanged;
        if (m_Loaded)
            Addressables.ReleaseInstance(m_Handle.Result);
    }
}
```

> **一个 rig = 一台相机**：rig 根节点自带 `Camera` + `CinemachineBrain`，所以分屏 / 多人必须
> **每个玩家各实例化一份**（key 相同、实例不同），不要指望一份 rig 同时跟两个角色。
> 分屏时若要区分视口，实例化后自行调该实例上 `Camera` 的 `depth` / `rect` / `cullingMask`。

**在编辑器里先跑通**：`HYC Framework/Cinemachine/New Rig` → 选位置 → Rig Editor 调参数 → `Save`
（此时生成 `{prefab 名}Cameras.g.cs`）→ 把 prefab 标为 Addressable（key 例如 `PlayerRig`）→ 宿主按上面示例接入。

### 5.5 同屏 / 分屏（一键切换）

场景里挂一份 `CinemachineViewSplitter`，实例化 rig 后登记，之后一个 `SetLayout` 切布局：

```csharp
using HYC.Framework.Cinemachine;

splitter.Register(rigA);                          // 每实例化一份 rig 就登记一次
splitter.Register(rigB);

splitter.Focused = rigA;                          // 同屏 / 画中画时谁占主视角
splitter.SetLayout(ViewLayout.Vertical);          // ← 一键：左右分屏
splitter.SetLayout(ViewLayout.Single);            // ← 一键：回同屏（B 的相机停渲染，不销毁）
splitter.SetLayout(ViewLayout.Quad, rigB, rigA);  // 指定槽位顺序：B 进左上
splitter.LayoutChanged += layout => RebuildHud(layout);
```

| 布局 | 槽位 | 说明 |
| --- | --- | --- |
| `Single` | 1 | 只显示 `Focused`，其余 rig 的 `Camera.enabled = false`（CM 状态不丢，切回来无跳变） |
| `Vertical` | 2 | 左右各半 |
| `Horizontal` | 2 | 上下各半 |
| `Quad` | 4 | 左上 → 右上 → 左下 → 右下 |
| `PictureInPicture` | 2 | `Focused` 满屏 + 第二份缩成右下角小窗（小窗 `clearFlags = Depth`，不遮主画面） |

超出槽位数的 rig 自动停渲染、不报错；中途补人只需再 `Register` + `SetLayout` 一次。
注销用 `Unregister`（会把该 rig 还原成整屏与原始 FOV）。

**FOV 补偿**（`FovPolicy`，默认 `KeepHorizontal`）：

| 值 | 行为 |
| --- | --- |
| `None` | 不动 FOV ⇒ 左右分屏两格看得更窄、上下分屏看得更宽 |
| `KeepHorizontal` | 保持横向视野：左右分屏时纵向 FOV 放大，两格看到的世界宽度与同屏一致 |
| `Overscan` | 只放大不缩小：取「不补偿」与「保持横向」中较大的 FOV |

补偿写的是各机位的 `Lens.FieldOfView`——相机 FOV 由 CM 每帧按镜头回写，直接改
`Camera.fieldOfView` 会被覆盖掉。`Quad` 与 `PictureInPicture` 的每格宽高比与整屏一致，不产生补偿。
**Camera 的 FOV Axis 保持 `Vertical`（默认）**，改成 Horizontal 时上述换算会偏。

**边界（框架不管，宿主自理）**：

- **实例化 / 销毁**：仍归 Addressables，`Register` / `Unregister` 只是登记注销。
- **输入分路**：手柄 1 控 rig A、手柄 2 控 rig B 是输入层的事（见 40 号文档）。
- **HUD / UI**：全屏 Canvas 不受视口影响；要「每格自己的 HUD」按 `Screen.width/2` 算锚点。
- **AudioListener**：`ManageAudioListener`（默认开）只让 `Focused` 留一个监听器，纯为消掉
  Unity「多个监听器」告警；听谁的音是玩法规则。
- **过场 / 演出**：Timeline 用主相机期间别切分屏，否则画面打架。
- **性能**：分屏 = 场景渲染 N 遍，后处理按相机各算；可在 `LayoutChanged` 里降画质档。

> 这一节只是「矩形槽位」那一档。要**任意斜切 / 弯曲 / 缝隙**、要**两侧看到不同东西**、
> 要**接缝跟着两个角色走**、要**分屏线压比例** → 换 `CinemachineDualView`：
> 见 [59-Cinemachine分屏与双世界-使用指南](./59-Cinemachine分屏与双世界-使用指南.md)。
> ⚠️ 那两条路不要混用：`CinemachineDualView` 的装配引导会把 `RenderMode` 单向切成合成。



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
10. **窗口参数渲染走 CM 组件自身序列化字段**：机位参数逐字段遍历 `SerializedObject`
    （CM 升级加减字段自动跟随）。两处是**按名硬编码**的——rig 级的
    `UpdateMethod` / `DefaultBlend.Style` / `DefaultBlend.Time`（CM 3.1.5 实证字段名），
    以及 CM 组件目录的类型名。CM 真改了名 ⇒ 该字段静默不画（`FindProperty` 返 null 有判空，
    不抛异常），此时按新名更新窗口对应常量即可，其余照常。
11. **机位认类型靠 `CinemachineRigCameraTag`**，不靠节点名、也不靠组件配方反查：
    节点名随便改、用户可能手工加减组件，两者都会把类型判错。窗口 Save 时会给缺标记的机位补上；
    在 Unity 里手工加的机位，回窗口按一次 Save 即可补全。
12. **Priority 归 binder 所有**：切换机位会改写全部机位的 Priority（见 §5.2），
    宿主手改会被下一次切换复原。
13. **生成物路径与命名空间走真源**：目录 `GeneratedPaths.Cinemachine`、
    命名空间 `HYCSettings.CinemachineCodeNamespace`（= `{root}.Cinemachine`），
    不要在生成器里写死路径。
14. **画中画只能是个矩形**：原生 `Camera.rect` 没有圆角 / 边框 / 阴影；要装饰得走
    RenderTexture + 全屏 RawImage 那条路（本模块未提供，按需自行实现）。
15. **分屏期间别手改 rig 的 Camera / Lens**：`rect` / `depth` / `clearFlags` / `enabled`
    与机位 `Lens.FOV` 都归 `CinemachineViewSplitter` 独占，手改会在下次布局切换时被覆盖。

---

## 七、关键类速查

| 类型 | 程序集 | 可见性 | 职责 |
| --- | --- | --- | --- |
| `RigKind` | Runtime | **public** | 机位类型枚举（运行期切换、生成的访问器都用它） |
| `CinemachineRigCameraTag` | Runtime | **public** | 机位节点上的类型标记（`SwitchTo(RigKind)` 按它命中） |
| `CinemachineRigBinder` | Runtime | **public** | rig 运行期门面：挂点 / 列机位 / 切机位 / `ActiveChanged` |
| `CinemachineViewSplitter` + `ViewLayout` + `FovPolicy` | Runtime | **public** | 同屏 / 左右 / 上下 / 2x2 / 画中画 的视口调度 |
| `CinemachineRigWindow` | Editor | public | Rig Editor 窗口（菜单 80/81） |
| `CinemachineRigFactory` | Editor | internal | 新建 rig prefab + 按配方装配组件 + 写类型标记 |
| `CinemachineRigAccessorCodeGen` | Editor | internal | 生成 `{prefab 名}Cameras.g.cs` 机位访问器 |
| `RigKindRecipe` / `RigKinds` / `CmComponentCatalog` | Editor | internal | 机位类型配方表 + 组件目录（按名反射解析，抗版本漂移） |

命名空间：`HYC.Framework.Cinemachine`（Runtime）/ `HYC.Framework.Cinemachine.Editor`（Editor）。
