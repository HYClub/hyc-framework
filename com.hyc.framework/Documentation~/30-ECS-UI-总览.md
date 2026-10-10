# 30 · ECS UI 总览

## 职责

用 **ECS System 当窗口**，而不是 MonoBehaviour 窗口管理器。每个界面 = 一个 `SystemBase` 子类 + 一个 Addressables 预制体。

命名空间：**`HYC.Framework.UI`**

一句话：**窗口是 System，界面是 GameObject，两者由 `UIManager` 在运行时缝合。**

---

## 设计要点

| 传统 MonoBehaviour UI | HYC ECS UI |
| --- | --- |
| `UIManager` 单例持有所有窗口实例 | `UIManager` 是一个 `SystemBase`，只持有**层节点 + 视图状态表** |
| 窗口是 `MonoBehaviour` + 预制体 | 窗口是 `SystemBase`（`AbsUISystem`）+ 预制体 |
| 生命周期靠 `Awake/OnEnable/OnDestroy` | 生命周期靠 `OnViewOpen / OnViewUpdate / OnViewClose / OnViewFocus / OnViewLost` |
| 打开 = `Instantiate` | 打开 = `Addressables.InstantiateAsync` + `group.AddSystemToUpdateList` |
| 层级靠 Canvas sortOrder 手工维护 | 层级由 `WindowLayer × 1000 + 层内序号` 自动算 |

---

## 核心类

| 类 | 位置 | 职责 |
| --- | --- | --- |
| `UIManager` | Runtime | `[DisableAutoCreation]` 的 `SystemBase`。根节点、层节点、视图状态、焦点、UI 场景缓存、分辨率适配、点击碰撞检测 |
| `AbsUISystem` | Runtime | 所有窗口/HUD/弹窗的抽象基类（本身也是 `SystemBase`） |
| `BaseWindowSystem` / `<T>` | Runtime | 窗口层基类（`Focusable = true`） |
| `BaseDialogSystem` / `<T>` | Runtime | 弹窗层基类（`Focusable = true`） |
| `BaseHudSystem` / `<T>` | Runtime | HUD 基类（**`Focusable = false`**） |
| `BaseLoadingSystem` / `<T>` | Runtime | Loading 基类（`Focusable = false`） |
| `AbsParentBaseWindowSystem` | Runtime | 能开子窗口的父窗口 |
| `BaseWindowPart` / `<T>` | Runtime | 可复用窗口部件（挂在父窗口节点下） |
| `UIGroup` | Runtime | 默认系统组，`[UpdateInGroup(typeof(LateSimulationSystemGroup))]` |
| `IComponentBinder` | Runtime | 生成的绑定类协议：`Reset(GameObject)` |
| `BinderEngine` | Runtime | 运行时按路径解析组件 |
| `ComponentBinderTable` | Runtime | 编辑器侧 authoring 组件（`[AddComponentMenu("HYC Framework/UI/UI 元素绑定")]`） |
| `UIAnimationHook` | Runtime | Animator 动画事件桥接 |

---

## 层级（WindowLayer）

`UIManager` 内部的**私有枚举**，顺序即层级（数值越大越靠前）：

| # | 层 | 打开 API | 说明 |
| --- | --- | --- | --- |
| 0 | `HUD` | `OpenHud<T>()` | 常驻 HUD，`BaseHudSystem`（不可聚焦） |
| 1 | `HUDNotice` | `OpenHudNotice<T>()` / `OpenHudSingleNotice<T>()` | HUD 上的飘字/提示；Single 变体全局只允许一个 |
| 2 | `Window` | `OpenWindow<T>(...)` / `OpenWindowAndCloseOther<T>(...)` | 功能窗口 |
| 3 | `WindowNotice` | `OpenWindowNotice<T>()` | 窗口之上的提示 |
| 4 | `Dialog` | `OpenDialog<T>()` | 对话框 |
| 5 | `TopWindow` | `OpenTopWindow<T>()` | 最顶层窗口 |
| 6 | `ToolTip` | `OpenToolTip<T>()` | Tooltip（也可用非泛型重载 `OpenToolTip(Type, ...)`） |
| 7 | `Loading` | `OpenLoading<T>()` | 加载遮罩 |
| 8 | `Cursor` | `OpenCursor<T>()` | 光标 |
| 9 | `Mask` | `OpenMask<T>()` | 最上层遮罩 |

**sortingOrder 计算**：`i * 1000 + j`（`i` 从 1 开始，`j` 是层内按 `Order` 排序后的序号，也从 1 开始）。
所以单层的窗口数量**不要超过 999**，否则会串到下一层的号段。

`Order` 是 `AbsUISystem` 的虚属性（默认 0），同层内按它升序排。

---

## 节点结构

```
UIManager                        ← DontDestroyOnLoad
├── IdleNode                     ← 回收池（setActive(false)），关闭的 Canvas 挂到这里
├── HUD
├── HUDNotice
├── Window
│   └── MyWindow                 ← 运行时动态建的 GameObject
│       ├── Canvas               ← renderMode = WorldSpace
│       ├── GraphicRaycaster
│       └── MyView               ← Addressables 实例化的预制体（名字去掉 "(Clone)"）
├── ...
```

- 根节点 `DontDestroyOnLoad`，`OnDestroy` 时 `GameObject.Destroy(RootNode)`。
- Canvas 是**运行时 new 出来的**，不是预制体里的——`renderMode = WorldSpace`，`worldCamera` 取 `UIManager.UICamera ?? Camera.main`，layer 设为 `"UI"`。
- 关闭时 Canvas 被挪到 `IdleNode` 回收（复用），同时 `Addressables.Release(ViewLoadOperation)`。

---

## 打开流程

```
UIManager.OpenWindow<T>(args...)
  ↓
GetSystemGroup(type)        读 [UpdateInGroup]；没有就用 UIGroup
  ↓
world.GetOrCreateSystemManaged(type) as AbsUISystem
  ↓
group.AddSystemToUpdateList(window); group.SortSystems();
  ↓
从旧层列表移除 → 加入目标层列表 → Canvas 挂到层节点 → 记录 windowState.Layer
  ↓
SceneKey 为空?
  ├─ 是 → LoadPrefabBegin
  └─ 否 → Addressables.LoadSceneAsync(key, Additive)
           完成后 OnSetCullingMask?.Invoke(LayerMask.GetMask("UIScene"))
                  ResetHudVisible()
                  window.OnSceneOpen(scene, args)
                  → LoadPrefabBegin
  ↓
Addressables.InstantiateAsync(window.PrefabKey)
  ↓ Completed
LoadWindowCompleted
  ├─ new GameObject(类型名) + Canvas + GraphicRaycaster
  ├─ View 挂到 Canvas 下，名字去掉 "(Clone)"
  ├─ 找 UIAnimationHook → 包成 ViewAnimationHook
  ├─ window.View / Canvas / Camera 赋值；window.Enabled = true
  └─ window.OnViewOpen(args)
  ↓
ResetWindowOrder() / ResetWindowFocus() / ResetWindowFit() / ResetGUIInput() / ResetHudVisible()
```

⚠️ **打开是异步的**：`Open<T>()` 立刻返回 system 实例，但此时 `View == null`。所有依赖 View 的初始化必须写在 `OnViewOpen` 里，**不能写在 `OnCreate` 里**。

⚠️ `UIManager` 带 `[DisableAutoCreation]`——**宿主必须在引导期手动创建它**（`World.GetOrCreateSystemManaged<UIManager>()` 并加进更新列表），否则所有窗口 API 都拿不到层节点（静态字段为 null）。

---

## 关闭流程

```csharp
UIManager.Close<MyWindow>();          // 泛型，默认播放退出动画
UIManager.Close(typeof(MyWindow), false);  // 跳过动画
UIManager.CloseBy(w => w is IBattleUI);    // 按选择器批量关
UIManager.CloseHudNotice(window);     // HUDNotice 专用
```

1. `animation == true` 且有 `ViewAnimationHook` → 找 Animator 里的 `"Exit"` **Trigger** 并 `SetTrigger`，找到就 **return（本帧不关）**。
2. Animator 的 `OnAnimationExitEvent` 触发后，回调里再 `Close(window, false)` 真正关闭——**退出动画靠 Animator 的 Trigger 名为 `Exit` 驱动**。
3. `OnViewClose()`（异常被 catch 成 `LogError($"关闭窗口时出错 : ...")`）。
4. 归还 UI 场景（若该场景已无窗口 → 记录 `mSceneKey2ClearTime`）。
5. 从层列表移除 → Canvas 挪到 `IdleNode` → `Addressables.Release` → 清空 State。
6. `ResetWindowFocus()` / `ResetGUIInput()` / `ResetHudVisible()`。
7. `group.RemoveSystemFromUpdateList(window); window.Enabled = false;`

---

## 焦点与导航

- `ResetWindowFocus()`：遍历所有层，**最后一个 `Focusable == true` 的窗口**获得焦点（所以越靠后的层越优先）。
- 切换时旧窗口 `OnViewLost()`（把所有热键 `Silence = true`），新窗口 `OnViewFocus()`（`Silence = false`）。
- `ResetGUIInput()`：从 `Mask` 往下扫到 `HUD`，遇到第一个 `Focusable` 的就 `HotkeyManager.SetNavigateMode(true)`，否则 `false`。
- 手柄/键盘导航由 `AbsUISystem.UpdateByFocusManager()` 驱动，只在 `Focused && HotkeyManager.IsNavigateMode` 时跑。

**导航参数**（来自 `StartupSetting`，缺省值在 `UIManagerSetting`）：

| 参数 | 默认 | 作用 |
| --- | --- | --- |
| `ReferenceResolution` | 1920×1080 | 参考分辨率 |
| `MaximumResolution` | 3440×1440 | 最大分辨率（与参考取 `Vector2.Max`） |
| `NavigationKeyDeathArea` | 0.6 | 摇杆死区，平方后与输入向量 `sqrMagnitude` 比 |
| `NavigationKeyDelayTime` | 0.5s | 同方向连发前的延迟 |
| `NavigationKeyRepeateCount` | 10 | 每秒重复次数（间隔 `1f / count`） |

---

## HUD 自动隐藏

`ResetHudVisible()` 在以下任一条件成立时，把 **所有 HUD / HUDNotice 层**的 `Canvas.enabled` 与 `GraphicRaycaster.enabled` 置 false，并 `Stop` 掉 View 下所有 `ParticleSystem`（`StopEmittingAndClear`）：

- 存在已加载的 UI 场景（`mSceneKey2Scene.Count > 0`）
- 存在 `Window` / `Dialog` 层的窗口
- 调用过 `UIManager.HideAllUI()`

`HideAllUI()` 遍历时**跳过 `layer >= WindowLayer.Loading`**（即 Loading/Cursor/Mask 不受影响）；`ShowAllUI()` 则全部 `Show()`。

---

## 分辨率适配

`OnUpdate` 里检测 `Screen.width/height` 变化 → `ResetScreenData` + `ResetUIFitData` + `ResetWindowFit`。

```
scaleFactor = min(screenW / refW, screenH / refH)
maximumSize = max(ref, maxRes) * scaleFactor
bestSize    = min(maximumSize, screen)
ViewportRect = 居中裁剪区域（相对比例）
Aspect       = bestSize.x / bestSize.y
```

世界空间 UI 的 Z：`ZInCamera = camera.farClipPlane / 4`，用 FOV 反算出该距离的视锥高宽，再同样算一遍 scaleFactor。

最终每个窗口的 Canvas：

```csharp
canvasRect.localPosition = new Vector3(0, 0, ZInCamera);
canvasRect.localScale    = Vector3.one * ScaleFactor;
canvasRect.sizeDelta     = BestResolution;
root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
root.anchoredPosition = Vector2.zero;
root.sizeDelta = BestResolution;      // holdAspect 为 false 时
```

⚠️ 这段代码里 `holdAspect` **恒为 false**（局部变量写死），所以永远用 `BestResolution`。

---

## 点击检测（HitTest）

```csharp
UIManager.HitTest(Vector2 point);                 // 是否命中 Loading/Dialog/Window 上的可交互物
UIManager.HitTest(GameObject go, Vector2 point);  // 某个对象是否被点到
```

- 按 `Loading → Dialog → Window` 顺序检查，遍历时**只检测 `FocusedWindow`**。
- 命中判定：`Selectable` 要 `interactable`；否则看父级有没有 `IPointerDownHandler` / `IPointerUpHandler` / `ISelectHandler`。
- 会同时检查 View 下所有子 `GraphicRaycaster`。

---

## 注意事项 / 坑

1. **`UIManager` 是 `[DisableAutoCreation]`**，必须手动创建；忘了建会导致 `LayerNodes == null` 的空引用。
2. **打开是异步的**——`Open<T>()` 返回时 `View` 还是 null，初始化写在 `OnViewOpen`。
3. **`PrefabKey` 是抽象方法必须实现**，`SceneKey` 可选（返回 `string.Empty` 走纯预制体路径）。
4. **单层窗口别超过 999 个**（sortingOrder 号段只有 1000）。
5. **HUD 会被自动隐藏**：有 Window/Dialog 或 UI 场景时 HUD 的 Canvas 直接 `enabled = false`，`OnViewUpdate` 仍然会跑吗？——不会，因为 `OnUpdate` 只看 `View != null`（Canvas 关闭不影响 View），**HUD 的 Update 仍在跑**，只是不显示不接收点击。
6. **`Close` 的动画分支依赖 Animator 里有名为 `Exit` 的 Trigger**；没有就立即关闭。
7. **UI 场景卸载有 0.1s 延迟**（`mSceneClearDelayTime`），期间又有窗口打开会取消卸载。
8. **`OnSetCullingMask` / `OnRecoverCullingMask` / `UICamera` 需要宿主自己接线**；`UICamera` 没设会 fallback 到 `Camera.main`，`ResetUIFitData` 里若两个都没有会 `LogError("UIManager.ResetUIFitData : UICamera 未设置!")`。
9. **`IGameFunctionWindow` 是空标记接口**，语义是"场景切换时强制关闭"，但需要宿主自己实现扫描逻辑（框架内没有自动关闭代码）。
10. `MoveDirection` 枚举**不在本包内**（来自外部 Dots/Input 包），写自定义焦点导航时会用到。
