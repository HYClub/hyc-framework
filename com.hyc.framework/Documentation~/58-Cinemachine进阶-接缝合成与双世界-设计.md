# 58 · Cinemachine 进阶：接缝合成与双世界

> **状态：P1 / P2 / P3 已实现，待 Unity 编译验证。** 本文前半是调研与设计推导（保留下来是为了
> 说清楚「为什么是这样」），后半 §5 是**已落地的真实 API**，可直接照着写代码。
>
> 👉 **只想「怎么用」直接看 [59-Cinemachine分屏与双世界-使用指南](./59-Cinemachine分屏与双世界-使用指南.md)**
> （选型表 / 装配步骤 / API 速查 / 常见需求 / 用户层坑）。本文保留调研、决策与全部 30 条坑，供深究。
>
> 前置：[57-Cinemachine相机装配](./57-Cinemachine相机装配.md)（rig 装配、binder、`CinemachineViewSplitter`）。

---

## 一句话

把「分屏线」从 `Camera.rect` 的**轴对齐矩形**升级成**参数化接缝（Seam）**：
两台相机各渲一张**整屏** RT，由合成 shader 按接缝逐像素决定归属。
于是斜切 / 弯曲 / 加宽 / 抖动 / 溶解 / 合并成单画幅 / 双世界双表现 / 跨接缝穿越，
全部变成**同一套接口的不同参数**，而不是十套特例。

**已实现的文件**（`Runtime/Cinemachine/`）：

| 文件 | 职责 |
| --- | --- |
| `CinemachineDualView.cs` | **游戏层门面**：`Separate` / `Combine` / `Tint` / `Follow` 四个动词（见 §4.7） |
| `CinemachineSeamState.cs` | `SeamState` / `SeamSource` / `SeamPresets`：接缝参数、屏幕空间符号判定、跨屏占比与占比钳制 |
| `CinemachineSeamCompositor.cs` | 双 RT + 合成后端（`ISeamCompositeBackend` / `CanvasSeamCompositeBackend`）；跟随玩家时按 `SplitWeight` 加权分割 |
| `CinemachineSeamFraming.cs` | 合成模式下按**实时接缝**反算瞄准偏移，把角色推回各自半屏的中心 |
| `CinemachineSeamTracker.cs` | 玩法侧「这东西现在在接缝哪一侧」 |
| `CinemachineDualWorld.cs` | 双世界分层裁剪（同一物理、两套美术） |
| `CinemachineDualVariant.cs` | 双变体落地组件：两套视觉体各归一层（A 看箱子 / B 看蘑菇，见 §4.8） |
| `CinemachineDualMirror.cs` | 镜像相机：让两台相机看同一张图（同场景视觉分叉的前提，见 §4.9） |
| `CinemachineSeamMerge.cs` | 合并 / 分开时间轴（切机位交给 CM，收接缝自己做） |
| `CinemachineSeamCrossing.cs` | 跨缝带重投影（横跨接缝的物体接得上） |
| `Resources/HYC/SeamComposite.shader` | 合成 shader（放 Resources 防打包剥离） |

---

## 零、先把「双影奇境到底做了什么」钉死

调研结论按**证据强度**分三档：`A` = 官方公开讲述；`B` = 多家评测一致描述；
`C` = 我们的技术归因（Hazelight 未公开实现，属合理推断）。

| # | 现象 | 档 | 依据 | 对应能力 |
|---|------|----|------|----------|
| 1 | 接缝会移动、旋转、加宽，不是固定居中线 | B | 多家评测一致；Switch 2 掌机模式出现斜切 | **F1 参数化接缝** |
| 2 | 接缝粗细/抖动随剧情情绪变化（开局厚重僵直，合作后流动、倾斜） | B | 评测深挖 | **F1b 接缝样式（宽/羽化/噪声）** |
| 3 | 「同步时刻」接缝完全消失，两画面合成单幅全景 | B | 评测 + Kotaku 终章报道 | **F3 合并为单相机** |
| 4 | 终章：两人在**同一物理空间**，各自看到科幻 / 奇幻两套美术 | **A** | GDC 2026 关卡设计师 Hannes Gille 演讲（明确讲「两套美术必须高精度对齐」「等于做两个游戏的美术」） | **F4 双世界双表现** |
| 5 | 同一物体对两人功能不同（科幻侧是箱子，奇幻侧是可弹跳蘑菇） | A | 同上 + Kotaku | **F4 + 双套交互** |
| 6 | 早期原型做过「把物体从一个世界扔到另一个世界」的 portal 机制，因成本砍掉、只留在终章 | **A** | GDC 演讲原话 | **F5 跨接缝穿越 / 门户** |
| 7 | 终章里接缝本身旋转、枢转，变成玩法障碍（切到安全那半才活） | B | Kotaku 终章亲历 | **F1 + F6 接缝查询玩法 API** |
| 8 | 「新的视角」关：一个角色在一台**屏幕**里，另一个在外面走 | B | 流程攻略 | **F7 嵌套视口** |
| 9 | 全部内容渲染两遍；因双视角，**不能靠镜头切走作弊**（不能瞬移、藏脚、藏穿模），角色可读性必须早期规划 | **A** | 80.lv 对 Hazelight 技术团队访谈原话 | **性能预算 + 设计约束** |
| 10 | 音频用 Wwise 「Proxy Emitters」+「Spatial Panning Gain」做双听者 | B | 音频侧公开分享 | **F8 双听者混音（另立）** |

**Hazelight 没有公开的东西（别装懂）**：UE 层面的具体做法未公开。旁证是 Marvel Rivals 团队
在 GDC 2025 讲奇异博士传送门时说过：他们受 It Takes Two 启发，最后的做法是
**「加一个 local player → UE 自动开双视口分屏渲染，开销极小」**。
也就是说 Hazelight 大概率是**吃引擎现成的本地双人分屏 + 自己做的接缝合成层**。

> ⚠️ 我们是 Unity + Cinemachine，没有 UE 那条「加 local player 白送分屏」的路。
> 下面全是自己的工程实现，不是源码复刻。

---

## 一、先把需求拆成 6 个能独立验收的能力

| 编号 | 能力 | 用户原话对应 | 难度 | 优先级 |
|------|------|--------------|------|--------|
| **F1** | 参数化接缝：直线 / 斜切 / 弯曲 / 宽度 / 羽化 / 抖动 | 「变形，以不同方式切割屏幕」 | 中 | **P1** |
| **F2** | 接缝由谁驱动：导演打点 / 跟随两玩家（Voronoi）/ 世界内 Transform | 同上 | 低 | **P1** |
| **F3** | 合并为一台相机（时间轴：位姿插值 + 接缝推出 + 叠化） | 「两边分开还可以合成一个相机」 | 中 | P2 |
| **F4** | 双世界双表现（同物理、两套可视、按相机分套） | 终章同物理不同内容 | 低 | **P2** |
| **F5** | 跨接缝穿越 / 门户（物件横跨两侧、两侧看到不同内容） | 「一个物品穿越两个相机显示不同内容」 | 高 | P3 |
| **F6** | 接缝查询玩法 API（某世界点在哪侧 / 切侧事件） | 「切割屏幕成 2 个相机内容」的玩法化 | 低 | **P1** |
| F7 | 嵌套视口（RT 贴到世界里的屏幕上） | 「新的视角」 | 低 | P4（白送） |
| F8 | 双听者混音 | — | 中 | 另立 |

**关键判断：F1 + F2 + F6 是一个包（P1）；F3 + F4 是一个包（P2）；F5 单独（P3）。**

---

## 二、为什么 `Camera.rect` 走到头了

现状（57 号文档 §5.5 的 `CinemachineViewSplitter`）走的是 `Camera.rect`。
这条路只对**轴对齐矩形**成立 —— 这是硬性数学事实，不是实现偷懒：

| 需求 | `Camera.rect` | 结论 |
|------|---------------|------|
| 竖着切 50/50 | ✅ | 够用 |
| 斜切 30° | ❌ rect 只能是轴对齐矩形 | 死 |
| 弯曲接缝 | ❌ | 死 |
| 接缝飘移 + 加宽 + 羽化 | ❌ 只能整数矩形硬边 | 死 |
| 混合（左半全屏 + 右半画中画） | ❌ 一个 rect 表达不了 | 死 |
| 合并时叠化 | ❌ | 死 |
| 双世界按相机分套 | ⚠️ 靠 cullingMask 能做，但与 rect 无关 | 半通 |

正解（业界通用，Lego 系「Voronoi 分屏」、Plume3D 的 `SplitLayout.angled`、
Godot 官方 dynamic split screen demo 都是这一套）：

> **每台相机渲染到自己的「整屏」离屏目标 → 合成 shader 逐像素决定取哪张。**

Plume3D 文档把理由写得非常直白：
> "The axis-aligned split-screen path (VkViewport sub-rects) cannot draw a diagonal divider
> — a half-plane is not an axis-aligned rectangle. So SplitLayout.angled switches on the composite path."

---

## 三、核心抽象：接缝（Seam）不是布局（Layout）

**这是整个方案唯一需要想清楚的地方。**

现有 `ViewLayout { Single, Vertical, Horizontal, Quad, PictureInPicture }` 是**离散枚举**。
它能一键切，但**无法连续变形** —— 而 Split Fiction 的观感 100% 来自「连续变形」。

正确抽象：

```csharp
/// 屏幕空间接缝。所有布局都是它的特例。
public struct SeamState
{
    public Vector2 Point;      // 接缝中点，屏幕归一化 (0..1)，原点左下
    public float   Angle;      // 度。0 = 竖线（法线朝 +x），90 = 横线
    public float   Curvature;  // 弯曲量。-1..1，0 = 直；正负决定向 A 侧还是 B 侧鼓
    public float   GapWidth;   // 缝隙宽度（0..1 屏幕比例），中间露出 GapColor
    public float   Feather;    // 羽化宽度（0..1）。0 = 硬边，大 = 两画面互相渗
    public float   Noise;      // 接缝抖动幅度（0..1），做「撕纸 / 溶解」感
    public float   NoiseSpeed; // 抖动滚动速度
    public Color   GapColor;   // 缝隙颜色（黑 / 主题色 / 渐变走 UI）
    public float   Push;       // 位移偏置（-1..1）。做「接缝推出屏幕 = 合并」
    public float   Crossfade;  // 0..1，A→B 全屏叠化。做「软化合并」
}
```

### 3.1 布局 → 接缝的映射（向后兼容）

`SetLayout(ViewLayout)` **签名一个字都不改**，内部转成 `SeamState`：

| ViewLayout | Point | Angle | Curvature | GapWidth | 说明 |
|---|---|---|---|---|---|
| `Vertical` | (0.5, 0.5) | 0° | 0 | 0 | 完全等价于现在的左右分屏 |
| `Horizontal` | (0.5, 0.5) | 90° | 0 | 0 | 上下 |
| `Quad` | — | — | — | — | ⚠️ 见下 |
| `PictureInPicture` | — | — | — | — | ⚠️ 见下 |
| `Single` | (0.5, 0.5) | 0° | 0 | 0 | `Push = −1.5` → 只剩 A（`SeamState.SingleA`）；`+1.5` → 只剩 B |

> ⚠️ **Quad / PiP 不是半平面，一个接缝表达不了。**
> 处理：这两档**继续走 `Camera.rect`**（它们本来就只需要矩形，零成本零风险），
> 只有「两方分割」这一族才进合成路径。→ 见 §4.5 的 `RenderMode` 双档设计。

### 3.2 接缝由谁驱动（`SeamSource`）

```csharp
public enum SeamSource
{
    Authored,       // 导演打点：调用 SetSeam() 或动画曲线，完全可控
    FollowPlayers,  // 跟随两人：位置按 SplitWeight 在两人投影之间取点，Angle = 两人连线方向（法线指向 A→B）
    Transform,      // 世界内一根线：由场景里某个 Transform 的位置/朝向投影到屏幕
    FollowTarget,   // 跟着单个目标整体平移（角度不变）
}
```

- `FollowPlayers` 就是 Lego 那套 Voronoi：**接缝永远垂直于两人连线**。
  参考实现要抄的两个细节（Godot 官方 demo / fido-and-kitch 的 TASKS 都写了）：
  1. 角度用**最短路径插值**（`atan2` 结果差 ±π 时不能绕远路），并对旋转**限速**（~150°/s）；
  2. 两人屏幕投影重合时角度**不更新**（否则抖动）—— 典型阈值 ~5°。
  本框架在此基础上补了两条：`SplitWeight`（接缝在两人之间的取点位置，0.5 = 垂直平分）
  与 `ClampShare`（任一侧最小占比，避免被压成零宽）。Lego 那种「靠近就合并成单屏」
  在本框架里是取舍项而非默认：要不要合、什么时候合交给关卡逻辑决定。
- `Transform` 就是 Split Fiction 终章「接缝变成玩法障碍」的入口：世界里放一根线/门框，
  它的朝向直接变成接缝朝向。

---

## 四、渲染架构（P1）

### 4.1 管线

```
        ┌──────────────┐        ┌──────────────┐
        │  Camera A    │        │  Camera B    │
        │ targetTexture│        │ targetTexture│
        │ = RT_A(整屏) │        │ = RT_B(整屏) │
        └──────┬───────┘        └──────┬───────┘
               │                       │
               └───────────┬───────────┘
                           ▼
                  ┌─────────────────┐
                  │ 合成 Material   │  _TexA / _TexB / SeamState
                  └────────┬────────┘
                           ▼
        ┌──────────────────────────────────────┐
        │ Canvas(ScreenSpaceOverlay)           │
        │  └ RawImage 铺满 → 自定义 shader      │  sortingOrder = -100（压在 UI 下面）
        └──────────────────────────────────────┘
```

**为什么用 `ScreenSpaceOverlay` Canvas 而不是 `Graphics.Blit` / `CommandBuffer`：**

| 方案 | Built-in | URP | HDRP | 评价 |
|---|---|---|---|---|
| `OnRenderImage` + `Graphics.Blit` | ✅ | ❌ | ❌ | 锁死 Built-in |
| `CommandBuffer` on `CameraEvent.AfterEverything` | ✅ | ⚠️ 需 RendererFeature | ⚠️ | 要按管线写两套 |
| ScriptableRendererFeature | ❌ | ✅ | ⚠️ | 锁死 URP，且要改使用方的 Renderer 资产 |
| **Overlay Canvas + RawImage** | ✅ | ✅ | ⚠️ 有限支持 | **无管线依赖**，随便换管线都不炸 |

框架 `package.json` 里**没有任何渲染管线依赖**（无 URP / HDRP），
所以必须选最后一条 —— 这是唯一「不逼使用方选管线」的路子。

### 4.2 合成 shader 算法（核心只有 12 行）

用**有符号距离**判归属，不搞逐像素最近点（那是 Voronoi 的做法，适合 `FollowPlayers`；
导演驱动时距离场更稳、更好调参）。

```hlsl
// 屏幕归一化 uv，y 向上
float2 p = i.uv;
float2 n = float2(cos(_Angle), sin(_Angle));     // 法线（Angle = 法线方向角）
float2 t = float2(-n.y, n.x);                    // 切向
float2 d = p - _Point;

float s = dot(d, n);                             // 有符号距离：>0 归 B
float u = dot(d, t);                             // 沿缝方向坐标

// 弯曲：用抛物线扰动距离场，u^2 项让接缝中点鼓出去
s -= _Curvature * (1.0 - 4.0 * u * u);

// 抖动：只扰动 s，接缝边缘才有机感，不会整屏噪
s += (hash21(p * 32.0 + _Time.y * _NoiseSpeed) - 0.5) * _Noise * 0.06;

// 位移偏置（合并用）+ 叠化（软化合并用）
s += _Push;
float w = smoothstep(-_Feather, _Feather, s);    // 0=A, 1=B
w = saturate(w + _Crossfade * _CrossfadeDir);    // _CrossfadeDir = -1/0/+1

float3 col = lerp(tex2D(_TexA, p).rgb, tex2D(_TexB, p).rgb, w);

// 缝隙带：|s| < GapWidth/2 处画 GapColor
float band = 1.0 - smoothstep(0.0, _GapWidth * 0.5 + 1e-5, abs(s));
col = lerp(col, _GapColor.rgb, band);
```

注意点：
- **D3D UV 翻转 —— 本框架的结论是「不要翻」**（2026-10-20 实测纠正，原先写反了）：
  `RawImage` 直接显示 RenderTexture 本来就是正立的（UI 四边形 UV 与 RT 内容同向），
  再按 `SystemInfo.graphicsUVStartsAtTop` 补一次 `1 - v` 会得到**整幅画面上下颠倒、但接缝位置仍然正确**
  的结果（接缝数学用视口空间，没跟着翻）—— 这个组合极好辨认。
  现在 `SeamPass.FlipY` 默认 0，只有自己 `Graphics.Blit` 的后端才需要设 1。
  后处理链里那句 `#if UNITY_UV_STARTS_AT_TOP if (_MainTex_TexelSize.y < 0) p.y = 1 - p.y; #endif`
  是针对 **Blit 出来的源**，与「UI 上贴 RT」不是同一个场景，别直接套。
- shader 用 `UnityCG.cginc` + `vert_img` / `v2f_img`，**不要**用 URP 的 `Blit.hlsl`
  —— Built-in 和 URP 都能编译的只有前者（UI/Default 就是这么做的）。
- 必须是 `Cull Off / ZWrite Off / ZTest Always`，Queue 走 `Overlay`。

### 4.3 ⚠️ Composite 模式必须换「取景方式」——最容易忽略的一点

这是本方案里**唯一会改变既有 rig 行为的点**，必须先想通：

| | `rect` 模式（现状） | Composite 模式（新增） |
|---|---|---|
| 相机 aspect | 半屏（窄） | **整屏** |
| 相机看到的世界 | 只有自己那半应有的视野 | 比实际显示**更宽**（多出来的被裁掉） |
| 角色在自己那半的位置 | 正中 | **被推到外侧**（因为相机还在看全屏中心） |

结论：**Composite 模式下，两台相机不能各自死跟自己的角色**，否则角色永远贴在自己半屏的外缘，
观感完全不对。业界统一做法是：

> **两台相机都取「两角色中点」为取景中心，靠偏置把各自角色推到各自半屏里。**

所以 P1 必须配套一个小协调器：

```csharp
public sealed class CinemachineSeamFraming : MonoBehaviour
{
    public void Bind(CinemachineRigBinder a, CinemachineRigBinder b);
    public Vector3 Center { get; }        // 两目标中点（可加高度/前瞻偏移）
    public Vector3 SeamNormalWorld { get; }// 接缝法线的世界方向 → 用来算左右偏置
}
```

具体两种落法，**推荐第二种**：

| 落法 | 做法 | 优点 | 缺点 |
|---|---|---|---|
| ① 共享中点锚点 | 建一个运行时 `Transform`（位置 = 中点），两台 rig 的 Follow 都绑它 | 简单，接缝移动时不用改相机 | 角色容易被顶到边；要靠 `TargetOffset` 手动掰 |
| ② 共享锚点 + 法线偏置 | 同上，但每台相机的 `TargetOffset` = `±SeamNormalWorld * offsetMag`，`offsetMag` 随接缝角度/FOV 联动 | 角色稳定落在各自半屏，接缝旋转也跟得上 | 要多算一个偏置曲线 |

`offsetMag` 的经验公式：`offsetMag ≈ tan(fov/2) * distance * 0.5`，
即「半屏宽度在目标距离处对应的世界尺寸」。参数化成可调曲线，别硬编码。

**已落地的实际算法**（比上面的经验公式更硬，因为它必须支持斜切与加权偏分割）：

```
span  = 屏幕四角在接缝法向上的投影范围（以接缝为原点）  → min, max
off   = 接缝相对屏幕中心在法向上的偏移                 → SeamState.NormalOffset()
cross = min < 0 < max                                  （接缝真的穿过屏幕？）

A 侧瞄准偏移 = -(off + min * 0.5)     B 侧 = (off + max * 0.5)
方向         = right * n.x * 2*halfW + up * n.y * 2*halfH   （跟着法线，不是只会左右推）
```

两个必须踩过的点：
1. **偏移沿「法线方向」而不是相机 right** —— 否则上下切 / 斜切时角色会被推到错的位置；
2. **必须叠加 `off`** —— `min/max` 的原点是接缝，而瞄准点的原点是屏幕中心。
   少这一项，接缝一旦被 `SplitWeight` 推偏，角色就会被推向屏幕外。

接缝不穿屏（合并 / 单屏）时 `cross = false`，两侧偏移都归零，角色自然回到画面正中。

### 4.4 资源与生命周期

| 项 | 决定 | 理由 |
|---|---|---|
| RT 尺寸 | `Screen.width × Screen.height`，随分辨率变化重建 | 一对一映射，缩放会糊 |
| RT 格式 | **`RenderTextureFormat.Default`（LDR）** | 每路相机的后处理/tonemap **已经写进 RT**，合成层只需要最终 LDR 像素。用 HDR 会导致 UI 二次变换 → 画面发灰/过曝 |
| sRGB | `RenderTextureReadWrite.Default`（跟随项目色彩空间） | 手动设 `sRGB=false` 一定翻车 |
| 深度 | 24 位（相机自己要用） | — |
| MSAA | RT 上设 `antiAliasing`；项目用 post-AA 就设 1 | 合成后**没有**全屏后处理，AA 必须在 RT 完成 |
| 释放 | `OnDestroy` / 分辨率变化时 `Release()` 再 `Create()` | 漏了就是显存泄漏 |

**显存成本**：1080p 两张 LDR+24 深度 ≈ 2×(1920×1080×4 + 深度) ≈ 33 MB。可接受。

### 4.5 双档 `RenderMode` —— 不破坏现有 API

```csharp
public enum ViewRenderMode
{
    Viewport,   // 现状：Camera.rect。零额外开销，只有轴对齐布局
    Composite,  // 新增：双 RT + 合成 shader。任意接缝，代价是 2× 像素
}
```

- `CinemachineViewSplitter` 增加 `RenderMode` 属性，默认 `Viewport` ⇒ **老工程行为不变**。
- `SetLayout` 在 `Viewport` 下走原逻辑；`Composite` 下把布局转成 `SeamState`。
- 新增 `SetSeam(SeamState)` ⇒ 自动切到 `Composite`。
- `Quad` / `PiP` 强制留在 `Viewport`（半平面表达不了）。

> ⚠️ **同一场景内切换 `RenderMode` 会有一次画面跳变**（视野变了，见 §4.3）。
> 两个选择：① 规定「一个游戏只用一种模式」；② 切换瞬间按 §4.3 的公式反算一次 FOV 补偿，
> 让画面尽量对齐，再在 0.2s 内收敛。**已按 ① 实现**：`RenderMode` 只有 getter，
> 降档走 `DowngradeToViewport()`（单向、不可逆）。

### 4.6 用法（已落地，复制即用）

**最小跑通：斜切 30° 分屏**

```csharp
using HYC.Framework.Cinemachine;
using UnityEngine;

public sealed class SeamDemo : MonoBehaviour
{
    [SerializeField] CinemachineViewSplitter m_Splitter;  // 场景里已 Register 两个 rig
    [SerializeField] CinemachineRigBinder m_RigA;
    [SerializeField] CinemachineRigBinder m_RigB;

    void Start()
    {
        m_Splitter.Register(m_RigA);
        m_Splitter.Register(m_RigB);

        // 一行切斜接缝：自动走 Composite 路径
        m_Splitter.SetSeam(new SeamState
        {
            Point     = new Vector2(0.5f, 0.5f),
            Angle     = 28f,
            Curvature = 0.25f,
            GapWidth  = 0.004f,
            GapColor  = Color.black,
        });
    }
}
```

**直接控制合成器（不经过 splitter）**

```csharp
var comp = gameObject.AddComponent<CinemachineSeamCompositor>();
comp.SetTargets(rigA, rigB);            // 顺序决定 A / B

comp.SetSeam(SeamState.Vertical);       // 预设：左右
comp.SetLayout(ViewLayout.Horizontal);  // 或按布局枚举
comp.SetSeam(SeamState.SingleA);        // 只显示 A（接缝推出屏幕）

// 跟随两玩家（Lego 系 Voronoi）：接缝垂直于两人屏幕连线
comp.SetFollowTargets(playerA, playerB, null);  // 传参考相机可覆盖投影相机
comp.Source = SeamSource.FollowPlayers;

// 世界内一根线驱动（终章「接缝变障碍」）
comp.SetTransformSource(seamLine);

// 嵌套视口：RT 直接当纹理贴到世界里的屏幕上
material.mainTexture = comp.RenderTargetA;
```

**合成模式下把角色推回各自半屏**

```csharp
var framing = gameObject.AddComponent<CinemachineSeamFraming>();
framing.Bind(rigA, rigB, playerA, playerB);
framing.LateralBias = 0.5f;   // 0 = 角色留在正中；1 = 推到整屏 1/4 与 3/4
framing.Mode = SeamFramingMode.LateralOffset;
```

**玩法查询：现在在哪一侧**

```csharp
// 一次性查询
bool onA = CinemachineSeamTracker.IsWorldPointOnSideA(comp.Seam, cameraA, enemy.position);
float s  = CinemachineSeamTracker.SignedDistanceOnScreen(comp.Seam, cameraA, enemy.position);
// s < 0 = A 侧，s > 0 = B 侧，float.NaN = 目标在相机背后

// 持续跟踪 + 跨缝事件
var tracker = enemy.gameObject.AddComponent<CinemachineSeamTracker>();
tracker.Bind(comp, cameraA, enemy.transform);
tracker.SideChanged += isA => Debug.Log(isA ? "切到 A 侧" : "切到 B 侧");
```

> ⚠️ `SeamState.SignedDistance` 与 shader 的 `SeamDistance` 必须**逐项一致**。
> 改公式要两边一起改，否则会出现「玩法判定在 A、画面画在 B」。

**双世界（同一物理、两套美术）**

```csharp
var dual = gameObject.AddComponent<CinemachineDualWorld>();
dual.Bind(rigA, rigB);      // 相机 A 只看「共享 + A」，相机 B 只看「共享 + B」
dual.CrossVisible = true;   // 两侧都能看到对方的专属层
dual.SetCrossVisible(aVisibleToB: true, bVisibleToA: false);  // 逐侧控制
```

**合并成一台相机**

```csharp
var merge = gameObject.AddComponent<CinemachineSeamMerge>();
merge.Completed += () => Debug.Log("已合并");
merge.Play(new MergeProfile
{
    Duration          = 0.8f,
    PushTarget        = 1.5f,       // 接缝推出屏幕：正 = 合并到 B，负 = 合并到 A
    CrossfadeTarget   = 0.35f,      // 软化接缝处色差
    FeatherTarget     = 0f,
    MergedCameraName  = "Wide",     // rig 里预置的合并机位，交给 CM 做位姿过渡
    BlendTime         = 0.6f,
});
// 反向：merge.PlayReverse(profile);
```

**跨缝穿越（横跨接缝的物体接得上）**

```csharp
var crossing = gameObject.AddComponent<CinemachineSeamCrossing>();
crossing.BandWidth = 0.12f;   // 接缝两侧各覆盖 12% 屏宽
crossing.StartCrossing();     // 建第三台相机（只渲跨缝层），并把它从主相机剔掉
```

### 4.7 游戏层一句话接口（Boss 关那种）

上面那套是「底层能力」。真写关卡的脚本时用 **`CinemachineDualView`** —— 一个组件，四个动词。

```csharp
using HYC.Framework.Cinemachine;
using UnityEngine;

public sealed class BossCameraScript : MonoBehaviour
{
    [SerializeField] CinemachineDualView m_View;   // 场景里挂好，填两个 rig + 两个角色

    void OnBossIntro()
    {
        m_View.Separate(SplitStyle.Vertical, 0.3f);   // 竖着分开
    }

    void OnBossRaiseLeftHand()   => m_View.Tint(0, Color.black);   // 左边玩家那半变黑
    void OnBossRaiseRightHand()  => m_View.Tint(1, Color.white);   // 右边玩家那半变白
    void OnBossAttackEnd()       => m_View.ClearTint();

    void OnBossPhaseEnd()
    {
        m_View.ClearTint();
        m_View.CombineAroundPlayers();   // 两人并拢成一景（不是把一侧挤走）
    }
}
```

**全部接口就这些**：

| 一句话 | 作用 |
|---|---|
| `view.Separate(SplitStyle.Vertical)` | 竖着切 |
| `view.Separate(SplitStyle.Horizontal)` | 横着切 |
| `view.Separate(SplitStyle.Diagonal)` | 斜着切（角度见 `DiagonalAngle`） |
| `view.Separate(SplitStyle.FollowPlayers)` | 跟着两个角色切（接缝垂直于两人连线） |
| `view.Separate(SplitStyle.FollowTarget)` + `view.Follow(t)` | 分屏线跟着某个角色走 |
| `view.Combine()` | 合成一个屏（**溶化**保留 A 侧、画面不移动） |
| `view.Combine(0.5f, keepSide: 1)` | 0.5s 溶化到 B 侧 |
| **`view.CombineAroundPlayers()`** | **两人并拢成一景**：两台相机收到两人中点 + 按间距拉远，主相机同时照住俩人 |
| `view.MergeBlend` | 合屏进度 0~1（只读） |
| `view.SetOrbit(side, offset)` / `view.GetOrbit(side)` | 某一侧绕自己角色转视角（世界空间偏移）；两侧可不同，合屏自动混成公共视角 |
| `view.Combine(0f, 0, releaseComposite: true)` | 合成并退回 `rect` 省掉 2× 像素（单向） |
| `view.Tint(0, Color.black)` | 染第 0 个玩家那一侧 |
| `view.Tint(Color.black, Color.white)` | A 黑、B 白一次给 |
| `view.ClearTint()` | 清染色 |
| `view.SetAngle(28f)` | 只改接缝角度 |
| `view.SetSeam(seam)` | 直接给全部参数 |
| `view.SplitWeight = 0.35f` | 跟着人切时，A 那半更窄（0.5 = 对半） |
| `view.MinShare = 0.15f` | 任一侧最小占比，防止被压到看不见 |
| `view.SideAShare` | 当前 A 侧占屏比例（只读，可驱动 UI/音效） |
| `view.AutoFraming = false` | 关掉自动取景（宿主自己控 `SetTargets` 时） |
| `view.IsSeparated` / `view.SeparatedChanged` | 状态与回调 |

#### 合屏 = 并拢成一景，不是把一侧挤走

「合成一个屏」在双人游戏里有两种完全不同的语义，早期实现只做了后者，观感是错的：

| 语义 | 实现 | 观感 |
|---|---|---|
| **溶化**（保留某一侧） | `Combine` → `SeamState.Crossfade = ±1`、`Push = 0` | 整屏渐变到某一侧，画面不移动 |
| **并拢**（两人走到一起） | `CombineAroundPlayers` → `CinemachineSeamFraming.GroupBlend = 1` | 两台相机慢慢收到两人中点并拉远，左右两半并成一张 |

`Crossfade` 是**全屏混合权重**（shader 里 `w = saturate(smoothstep(-feather, feather, s) + _Crossfade)`），
不是位移；用 `Push = ±1.5` 把接缝推出去虽然「能看」，但那就是「一侧被挤走」的感觉 —— 所以 `Push` 已归 0。

并拢的实现摊在 `CinemachineSeamFraming` 上，要点四条：

1. **Follow 换成框架托管的锚点**。`Bind` 时把两台 rig 的 `Follow` 从「各自角色」改成
   `[HYC Seam Follow A/B]` 两个 Transform 子物体，顺手缓存 rig 里的 `CinemachineFollow`
   与其原始 `FollowOffset`。之后每帧只写锚点位置，不动 rig 结构。
2. **锚点插值到两人中点**：`follow = Lerp(自己角色, mid, blend)`，`aim = Lerp(自己瞄准点, mid + height, blend)`
   ⇒ `blend = 1` 时两台相机吃到的 Follow / LookAt **完全相同**，位置、朝向、距离才会一致，
   两侧画面才真的是一张连续图（这也是唯一的判据）。
3. **两侧的跟随偏移混合成「同一个方向 + 同一个长度」**。分屏时各写各的环绕角
   （`CinemachineFollow.FollowOffset`；`BindingMode` 默认 `WorldSpace` ⇒ 它就是世界空间向量，
   绕它转等于绕角色做球面环绕；宿主经 `SetOrbit` 改）。合屏时方向取两侧单位向量的平均
   （两侧大致反向时退回 A 的方向，否则会得到「正上方俯视」这种退化视角），
   长度取「两侧原长度的较大者」与「装得下两人的最小长度」的较大者 —— 只放大不缩小。
   **FOV 一律不碰**：FOV 归 `CinemachineViewSplitter` 的 FOV 补偿管，两边都写会互相打架。
4. **这样「两人视角本来就不一样」也照样能并拢**：锚点相同 + 偏移相同 ⇒ 两台相机位姿完全一致。
   只混合位置是不够的，偏移的方向也必须一起收敛。
5. **锚点不做二次平滑**。并拢的平滑只由 `GroupBlend` 的指数衰减承担（`GroupSpeed` 默认 3.5）；
   分屏态锚点必须与角色**同帧**，再叠一层平滑会让跟随机位整体发软（玩家感到拖影）。

配套：接缝的光带与缝隙宽度按 `MergeBlend` 同步淡出（并拢到位后那刀不该还发光），
退回分屏时按记录的基准恢复。偏移是**每帧整份写死**的（不读上一帧的 `FollowOffset`），
所以 `blend` 回到 0 时天然就是原值，不会留插值残留。

#### 动态分割：竖切自动变横切、两半一大一小

`SplitStyle.FollowPlayers` 不是「固定竖切」也不是「固定对半」，三个维度都是活的：

| 维度 | 怎么来的 |
|---|---|
| **角度** | 接缝法线取两人连线的屏幕方向 ⇒ 一左一右是竖线，一上一下是横线，斜着站就是斜线。带转速上限（默认 150°/s）和死区，不会抽筋 |
| **位置** | 接缝沿两人连线按 `SplitWeight` 取点，默认 0.5 = 垂直平分 |
| **占比** | 上一条的自然结果：两人一起挤到屏幕左边时，接缝跟着过去，左边那半就自然变窄、右边变宽 |

```csharp
// 最常见：什么都不用调，占比自己会变
view.Separate(SplitStyle.FollowPlayers);

// 想让某一边长期偏窄（比如 boss 招式期间压住 A 的视野）
view.SplitWeight = 0.3f;    // 接缝靠向 A ⇒ A 那半占 30%

// 两人贴到一起时，别让某一侧真的归零
view.MinShare = 0.12f;      // 默认 0.15
```

占比的实时值可以直接读出来做表现（比如小到一定程度切单屏/加提示）：

```csharp
if (view.SideAShare < 0.2f) hint.Show("P1 视野受限");
```

> **为什么能一大一小**：接缝是一条**垂直于两人连线**的直线，位置由权重决定。
> 权重 0.5 时它是垂直平分线，两人都靠近屏幕左边，线也跟着靠左，左边那半自然只剩很窄一条。
> `MinShare` 是兜底 —— 否则极端情况会把一侧压成零宽，那半屏就彻底看不到人了。

> **取景**：合成模式下相机渲的是整屏再被裁，角色如果不做偏移就会正好停在接缝上。
> `AutoFraming`（默认开）会按**实时接缝**反算每台相机的瞄准偏移，把角色推到自己那半的中心；
> 接缝被推偏、斜切、上下切都成立（偏移方向跟着法线走，不是只会左右推）。
> 关掉它则完全不动 rig 的 Follow / LookAt，由宿主自己摆。

> **为什么「不管横切竖切都跟着人」**：染色绑的是**玩家编号**（0 = A / 1 = B），
> 不是屏幕左右。接缝旋转时 A 侧始终是 A 玩家的画面，所以颜色永远跟着那个人走。

**装配**：把 `CinemachineDualView` 挂在常驻物体上，Inspector 里填 `RigA` / `RigB` / `PlayerA` / `PlayerB` 即可；
`CinemachineViewSplitter` 与 `CinemachineSeamCompositor` 由它自动挂。
rig 是 Addressables 运行期实例化的，就在拿到实例后调
`view.Bind(rigA, rigB, playerA, playerB)`。

### 4.8 两侧看到不同的东西（双变体）

问题：**A 相机看到的是箱子，B 相机看到的是蘑菇，同一个位置、同一个碰撞体。**

先说死结论，这决定了所有实现方式：**一个 Renderer 一次 draw，材质与网格是渲染器的属性。**
想让两台相机看到不同外观，只有三条路：

| 路 | 做法 | 能容纳什么 | 代价 | 结论 |
|---|---|---|---|---|
| **① 分层双视觉体** | 一个逻辑根挂两份视觉体，各自 layer = `HYC.WorldA` / `HYC.WorldB`；`CinemachineDualWorld` 给两台相机拼 cullingMask | 不同模型 / 不同材质 / 不同骨骼网格 / 不同粒子 / 不同 LOD，**什么都能不一样** | 双变体的物体 2× draw；美术要出两套 | ✅ **首选**，也是 Split Fiction 的实际做法 |
| ② 单渲染器 + 每相机换资源 | `Camera.onPreCull` 里按当前相机换 `sharedMesh` / `sharedMaterials` | 只能是「同结构、换皮」 | 1× draw | 只在物体数量很大、且两套资源结构一致时才值得。粒子/多材质槽/LOD 会翻车 |
| ③ CommandBuffer 手动 DrawMesh | 每台相机挂一条 `CommandBuffer` 画不同 mesh | 极致控制 | 要写管线代码，与 SRP 兼容性自己扛 | 不做 |

**关键：不需要「两套完整世界」。** 只有**需要变体的那几个物体**才做成两份；
世界的地形、道具、敌人这些两边一致的内容，放 `HYC.WorldShared` 层，一份就够。

#### 「香蕉飞到右屏变成回旋镖」为什么零代码

合成是按**屏幕空间接缝逐像素择一**的：物体在同一世界位置，被两台相机各渲一遍，
左半屏取 A 相机的像素、右半屏取 B 相机的像素。于是：

```
物体完全在左半屏  → 只看到香蕉（B 变体被 B 相机之外的地方裁掉了）
物体跨在接缝上    → 左半是香蕉 / 右半是回旋镖（半拉切换）
物体完全在右半屏  → 只看到回旋镖
```

**切换点由接缝决定，不由物体决定。** 所以「椰子掉到半屏变成保龄球」不需要任何交叉检测、
不需要在物体脚本里写 `if (crossed)` —— 这正是这套架构最省事的地方。

三个必须知道的后果：

1. **半拉切换是特性，不是 bug。** 物体压在接缝上时就是能看到左半边一种、右半边另一种。
   这正是双影奇境里「门框这边是森林、那边是工厂」的观感来源。
   想让切换干净利落，就在那一下加 `SeamState.GapWidth`（接缝裂缝）或一道闪光/粒子遮住。
2. **两半的几何可能对不齐 —— 这是本架构唯一无法消除的瑕疵。** 两台相机位姿不同
   （一个在玩家 A 身后、一个在 B 身后，可能差十几米），同一个物体在两个相机里的
   屏幕位置与透视压扁方向都不一样；接缝划过时，左半来自 A 的投影、右半来自 B 的投影，
   中间会出现一道**错位折痕**（球像被掰断、边缘被咬掉一块）。
   这属于双相机分屏的几何本质，不是 bug，杀不掉。

   分清两种情况，才知道要不要管：

   | 物体状态 | 瑕疵 |
   |---|---|
   | 完全在某一半屏内（没压到切线） | **零瑕疵**，与单相机完全一致 |
   | 正在跨接缝（物体压在切线上） | 有折痕，明显程度 = 两台相机在该处的**视角差** |

   三档处方，按省事程度排：

   | 档 | 做法 | 成本 |
   |---|---|---|
   | ① 挖缝 | `seam.GapWidth = 0.006f`（约几像素黑缝），把折痕藏进缝里；观感上还更像「世界被切开」 | 一个参数 |
   | ② 重投影带 | 把该物体放进 `HYC.SeamCrossing` 层交给 `CinemachineSeamCrossing`，用第三台相机单独渲接缝带（`BandWidth` 默认 0.12 屏宽）→ 那条带只有一种投影，严丝合缝 | 多一台相机 + 一张 RT |
   | ③ 设计规避 | 让物体快速掠过接缝、或切换那一瞬用闪光 / 粒子 / 遮挡盖住 | 美术资源 |

   > 经验法则：**两台相机在接缝处的视角差越小，折痕越轻。**
   > 这也是 `AutoFraming` 让两台相机只做横向偏置、朝向保持一致的原因 ——
   > 若把朝向与焦距也做成分叉，折痕会成倍恶化。
3. **不要在两套变体上各写一份逻辑。** 视觉体只是表现。

#### 铁律（违反必出重复物理 / 双份状态）

| 项 | 规定 |
|---|---|
| 逻辑体 | **唯一**。本组件挂在它上面；状态机、血量、AI 都挂这里 |
| 碰撞体 | **只在逻辑根上**。变体子树里有碰撞体会被本组件告警（同一处两套碰撞，角色被挡两次、射线打两次） |
| 玩法代码 | **不引用变体**。判定一律用逻辑体位置 |
| 角色动画 | 一份 `Animator` 驱动一套骨骼；两套外观各挂一个 `SkinnedMeshRenderer`（不同 mesh/material，`bones` 指同一套骨骼） |
| 粒子 / 拖尾 | 按侧放对应层，否则只在一边出现或两边重影 |
| 层 | 复用 `HYC.WorldA` / `HYC.WorldB` / `HYC.WorldShared` 三层，**变体再多也不占新层** |

#### 用法

```csharp
// 逻辑根（带碰撞体 / Rigidbody / Entity 桥接）+ 两个子物体：VariantA / VariantB
var variant = logicRoot.AddComponent<CinemachineDualVariant>();
variant.VariantA = bananaVisual;      // 赋值即设层
variant.VariantB = boomerangVisual;
variant.Apply();                      // 递归设层；层名解析失败会报错，不会静默留在默认层
```

`CinemachineDualVariant` 只做三件事：把两套视觉体递归挪到 A/B 层、可选的每帧跟随（视觉体不是子物体时用）、
逐侧开关（`SetSideVisible`）。**它不管切换** —— 切换是接缝的事。

```csharp
// 只在特定桥段让这个物体「两边看起来一样」：把它临时挪到共享层
CinemachineDualVariant.SetLayerRecursively(obj, LayerMask.NameToLayer("HYC.WorldShared"));
```

> **层没命名怎么办**：组件默认按**层名**解析（`HYC.WorldA` / `HYC.WorldB`），
> 而 `LayerMask.NameToLayer` 对**无名层**返回 `-1`。原型 / 样例常用无名层（28、29 之类），
> 这时改用层号，跳过一次名字解析：
>
> ```csharp
> variant.SetLayers(28, 29);   // 先定层号，再挂变体
> variant.VariantA = cubeVisual;
> variant.VariantB = sphereVisual;
> ```
>
> `SetLayers` 之后 `m_SideALayer`/`m_SideBLayer` 非负 ⇒ 层名被忽略。
> 正式项目仍建议老老实实命名层，因为 `CinemachineDualWorld` 也按层名配 cullingMask。

> ⚠️ **顺序坑**：`CinemachineDualWorld.Apply()` 是**整体重写**两台相机的 cullingMask。
> 如果同时用了 `CinemachineSeamCrossing`（它启动时会把自己那层从两台主相机剔掉），
> 在它启动**之后**再调 `DualWorld.Apply()` / `CrossVisible = x` 会把剔除结果冲掉 → 跨缝物体出现重影。
> 约定：`DualWorld` 的配置先做完，`Crossing.StartCrossing()` 最后调；中途改过 `DualWorld` 就重跑一次 `StartCrossing`。

#### 性能

- 只有双变体物体付 2× draw，且两套变体各只被一台相机渲染（另一台被 cullingMask 剔掉），
  **不是「两边都画全部」** —— 这一点常被误解。
- 合成本身另有 2× 整屏像素开销（§4.4），与变体数量无关。

---

### 4.9 ⚠️ 两种「分屏」不是一回事（选错代价 2 倍像素）

这两个需求长得很像，实现却走两条完全不同的路。先分清再动手。

| | **① 完全分离**：A 屏看 A 场景、B 屏看 B 场景 | **② 同场景视觉分叉**：一屏香蕉、一屏保龄球 |
|---|---|---|
| 两屏的关系 | 两个互不相关的地图，接缝只是「两块屏幕的边界」 | 同一张图的左右两半，接缝是「同一处世界被切开」 |
| 用哪个组件 | **`CinemachineViewSplitter`**（默认 `Viewport` = `Camera.rect`） | **`CinemachineDualView`**（自动进 `Composite`） |
| 每帧像素成本 | **1×**（每台相机只渲自己那半屏） | **2×**（两台都渲整屏，再合成） |
| 相机怎么摆 | 各跟各的玩家，**允许**朝不同方向、用不同 FOV | **必须完全同一位姿**（用 `CinemachineDualMirror` 保证） |
| 内容差异靠什么 | 两个不同的场景 = 两层内容（`HYC.WorldA` / `HYC.WorldB`） | 双变体换美术（`CinemachineDualVariant`）或单侧染色 |
| 切分形状 | 只能左右 / 上下 / 四宫格 / 画中画 | 任意角度、弯曲、加缝、羽化、抖动 |
| 玩家移动时 | A 走 → A 屏跟着变，B 屏**纹丝不动**（各渲各的） | 两边**同时**移动（同一台相机的镜像） |

**判断标准只有一句：两半需不需要拼成一张连续画面？**

- **不需要** → 走 `Viewport`。省一半像素，而且这就是经典分屏，四宫格、画中画都在这条路上。
- **需要** → 走 `Composite`。这是唯一能让"同一个物体在两侧看起来还在同一个位置"的路。

> **实际项目里最常见的第三种情况：同一个世界、两个角色，各跟各的相机。**
> 左右两半是**同一张地图的不同角度**（不是两张地图），所以它属于「需要拼」那一侧 ⇒ 必须 `Composite`；
> 但两台相机位姿不同 ⇒ 接缝带上会有折痕（§4.8 后果 2，可用 `GapWidth` / `CinemachineSeamCrossing` 处理）。
> 样例 ① `SameWorldSplitDemo` 演示的就是这个 —— 它比「完全分离」更贴近真实需求，
> 而「完全分离」只适合真的两块互不相干的地图（那种需求很少见，且用 `Viewport` 省一半像素）。

> **为什么 ② 不能用 `Camera.rect`**：rect 模式下相机宽高比是**半屏**，同样垂直 FOV 下水平视野被压窄
> （16:9 全屏 hFOV≈91°，半屏只剩≈54°）。于是左半看到的世界范围比"整屏图切一半"更大，
> 两半根本拼不上 —— 接缝处必然错位。`Composite` 让两台都渲整屏、再由 shader 切像素，才能对齐。

#### ① 完全分离的装配（三步，零合成）

```csharp
splitter.Register(rigA);                      // A 场景的 rig
splitter.Register(rigB);                      // B 场景的 rig
splitter.SetLayout(ViewLayout.Vertical);      // rect 切半

dualWorld.Bind(rigA, rigB);                   // 分层：各自只渲自己那层
dualWorld.CrossVisible = false;               // A 相机看不到 B 场景，反之亦然
```
> ⚠️ **不要给 ① 挂 `CinemachineDualView`** —— 它的装配引导会调 `SetSeam`，
> 而 `SetSeam` 会把 `RenderMode` 切成 `Composite` 并**再也退不回去**
> （`DowngradeToViewport` 是单向的，切的时候画面会跳）。① 用 `Splitter` 就够。

**① 的四个真坑（都是生产级问题，不是代码问题）**：

| 坑 | 现象 | 处理 |
|---|---|---|
| **灯光互相照亮** | 两个场景各带一套 Directional Light / 点光，会同时作用于两台相机 → 画面被对侧照亮 | 每个场景的灯放自己的层，相机 `cullingMask` 只含自己那层。Built-in 下 `Light.cullingMask` 与相机 cullingMask 联动生效；**URP 下不生效，要走 Rendering Layers（需实测）** |
| **Fog / Ambient** | `RenderSettings` 是**全局**的，两个场景没法各自不同 | 天空盒可以不同（`Camera.clearFlags` 是 per-camera）；雾与环境光只能二选一，要么两边都不用雾，要么自己用后处理做 |
| **声音只听到一侧** | Unity 只支持一个 `AudioListener`（`Splitter.ManageAudioListener` 已保证只留一个），另一侧场景完全没声音 | 框架不管。要"两侧同时出声"得自己做：把 B 场景音源强制 pan 到右声道 |
| **阴影预算** | 两套实时阴影共用同一份 shadowmap 预算 | 控制两侧阴影距离，或一侧改烘焙 |

#### ② 同场景视觉分叉的装配

```csharp
// 1) 两台相机必须看同一张图 —— 位姿由镜像组件保证，别手摆
var mirror = gameObject.AddComponent<CinemachineDualMirror>();   // 自动取 RigA / RigB 的相机
// 2) 先关掉自动取景：取景偏移是给「各跟各的玩家」用的，会把两台固定机位推开
view.AutoFraming = false;
// 3) 进合成、分开（Separate 内部会按 AutoFraming 决定要不要挂取景协调器）
view.Separate(SplitStyle.Vertical);
mirror.Refresh();              // 合成器建好之后再解析两侧相机
// 4) 两侧内容分层
dualWorld.Bind(rigA, rigB);
dualWorld.CrossVisible = false; // 否则两边看到同一份美术，等于没换
```

> 顺序不是死规定：`AutoFraming = false` 的 setter 内部会主动 `ReleaseFraming()`，
> 写在 `Separate` 之后也安全；但 `mirror.Refresh()` **必须**在合成器存在之后
> （它从合成器的 RigA / RigB 取相机，早调只会解析失败、等下一帧 LateUpdate 自愈）。

`CinemachineDualMirror` 每帧把 B 相机的位置、旋转、FOV、正交尺寸、近远裁剪面都复制成 A 的，
**唯独不碰 `targetTexture` / `rect` / `cullingMask` / `depth`** —— 那几项正是让两台相机不同的东西。
同步点在 `Camera.onPreCull`（不是 `LateUpdate`）：Cinemachine 在 LateUpdate 摆完真相机，
执行顺序不确定，只有挂在出图前才能保证当帧一致。

> 镜像相机上如果还挂着 `CinemachineBrain`，组件会告警一次：位姿已被接管，
> Brain 每帧白算，还可能与其他系统读到的一帧不一致 —— 建议把镜像那侧的 Brain 禁用。

---

### 4.10 可运行样例（两条路各一个场景）

上面两条路在 `Samples~/HYCSimple/Cinemachine/` 里各有一个**打开即能 Play** 的场景，
全部用 Unity 内置 primitive 在 `Awake` 里搭出来（不引外部美术、不改 Tags and Layers）。
两个场景的视角都由 **`CinemachineCamera` 机位**提供（跟随机位挂 `CinemachineFollow` + `CinemachineRotationComposer`，
固定机位不挂 Body/Aim），底层 `Camera` 只挂 `CinemachineBrain` 负责出图 —— 与框架推荐的 rig 结构一致。

| 场景 | 演示什么 | 拖什么 |
|---|---|---|
| `Scenes/SameWorldSplitDemo.unity` | ① 一个大场景 + 红蓝两个角色，两台相机各跟一个（`Composite` 路径，2× 像素） | Game 视图里拖接缝 / 拖红蓝方块 → 那一半的取景跟着平移，两半始终是同一张地图 |
| `Scenes/DualStyleSplitDemo.unity` | ② 同场景两层美术（同位置 A 是 Cube、B 是 Sphere），任意接缝 + 压缩占比（`Composite` 路径） | 拖 `Player A/B` 让方块跨过切线，看外形逐像素切换 |

面板按钮分别对应 `CinemachineDualView.Separate` / `SplitWeight` / `SetSeam` / `SetGlow` / `Tint`；
`Camera.rect`（`Viewport`）那条路仍由 `CinemachineViewSplitter` 支持，只是样例不再演示，见 59 号 §1.2。

---

## 五、六个能力的实现方案

### F1 + F2 参数化接缝（P1）

| 文件 | 职责 |
|---|---|
| `Runtime/Cinemachine/CinemachineSeamState.cs` | `SeamState` 结构 + `SeamSource` 枚举 + `SeamPresets`（`FromLayout`/`IsHalfPlane` 布局映射）；竖/横/单侧等静态接缝是 `SeamState` 上的静态属性 |
| `Runtime/Cinemachine/CinemachineSeamCompositor.cs` | 双 RT 分配/释放、Canvas+RawImage、材质参数下发、`SetSeam` / `SetTarget` |
| `Runtime/Cinemachine/CinemachineSeamFraming.cs` | §4.3 的取景协调器 |
| `Runtime/Cinemachine/Resources/HYC/SeamComposite.shader` | 合成 shader（`HYC/Cinemachine/SeamComposite`） |
| （无 .mat 资产） | 默认材质在运行时 `new Material(shader)` 创建（shader 置于 Resources 防打包剥离，见 §9 坑 6） |

对外 API（P1 落地后的样子）：

```csharp
var comp = gameObject.AddComponent<CinemachineSeamCompositor>();
comp.SetTargets(rigA, rigB);

// 一键：沿用现有布局枚举
comp.SetLayout(ViewLayout.Vertical);

// 或：任意接缝
comp.SetSeam(new SeamState {
    Point = new Vector2(0.5f, 0.5f),
    Angle = 28f, Curvature = 0.3f, GapWidth = 0.004f, Feather = 0.0f,
});

// 或：跟随两玩家
comp.Source = SeamSource.FollowPlayers;
comp.SetFollowTargets(playerA, playerB, camA);  // camA 用来做世界→屏幕投影

// FollowPlayers 的接缝转速上限是 Inspector 序列化字段（m_MaxRotationPerSecond），无公开 setter
```

### F6 接缝查询玩法 API（P1，成本几乎为零但要早定）

Split Fiction 终章的核心玩法是「切到安全那一侧」，所以必须能问：

```csharp
bool  IsOnSideA(Vector3 worldPos);        // 世界点落在 A 侧视野里吗
bool  IsOnSideA(Vector3 worldPos, Camera cam);
float SignedDistanceOnScreen(Vector2 viewportPos);  // >0 = B 侧
event Action<int> SideChanged;            // 某关注实体跨缝（puppet 用 transform 追踪）
```

实现：`cam.WorldToViewportPoint(worldPos)` → 拿 `s = dot(p - Point, n)` 再减曲率项，
和 shader 里**同一份公式**（抽成 C# 静态方法 + shader 里手抄一遍，两边必须一致；
建议在 C# 侧留一个单测断言「shader 参数 → 符号」的边界样例）。
`SideChanged` 由 `CinemachineSeamTracker` 组件挂到关注实体上，每帧算一次符号。

### F3 合并为一台相机（P2）

**关键结论：不要新造相机系统。合并 = 「切机位 + 收接缝」两件事同一条时间轴。**

```
t=0      A 相机 active                  seam 在 0.5
         │
t=0.2    binder.SwitchTo(mergedVcam)    CM 自己跑 DefaultBlend 做位姿+FOV 平滑过渡
         │   （mergedVcam = rig 里预置的一台宽景机位，Follow 绑合并锚点）
t=0.2~1.0                                    seam.Push 0 → 把接缝推出屏幕
                                              seam.Crossfade 0 → 0.5（软化接缝处色差）
t=1.0    只剩一台相机 → RenderMode 切回 Viewport（可选，省 2× 开销）
```

- 位姿插值**交给 Cinemachine**：`CinemachineBrain.DefaultBlend` 是 public 字段，
  `CinemachineBlendDefinition(Styles, float time)` 构造已实证（3.1.5 `CinemachineBlend.cs:220`），
  运行期可改：
  ```csharp
  brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.8f);
  ```
- 接缝收尾我们自己做（`SeamState` 走 `AnimationCurve` 打点，5 个字段各一条曲线）。
- 反向（分开）就是倒放，同一个组件：

```csharp
public sealed class CinemachineSeamMerge : MonoBehaviour
{
    public void Play(MergeProfile profile);        // 分开 → 合并
    public void PlayReverse(MergeProfile profile); // 合并 → 分开
    public bool IsPlaying { get; }
    public event Action Completed;
}

[Serializable] public struct MergeProfile
{
    public float Duration;
    public float PushTarget;          // 结束时的接缝位移（±1.5 足以推出屏幕）
    public float CrossfadeTarget;     // 结束时的叠化强度 -1..1
    public float FeatherTarget;       // 结束时的羽化宽度，0 = 硬边
    public AnimationCurve Ease;       // 时间曲线，留空按线性
    public string MergedCameraName;   // rig 里预置的合并机位（留空则不切机位）
    public float BlendTime;           // Cinemachine 位姿过渡时长（秒），0 = 用 Brain 当前设置
}
```

### F4 双世界双表现（P2）

Hazelight 的做法（GDC 明确讲）= **同一物理布局，两套美术，高精度对齐**。
在 Unity 里这就是**分层裁剪**，成本极低：

| 步骤 | 做法 |
|---|---|
| 1. 分层 | 约定 3 个 Layer：`HYC.WorldA` / `HYC.WorldB` / `HYC.WorldShared`（框架**不写** `TagManager.asset`，只检测 + 在窗口里给出「请手动添加这 3 个层」的提示。改项目设置是使用方的事，框架不许越界） |
| 2. 相机分套 | `CameraA.cullingMask = Shared | A`；`CameraB.cullingMask = Shared | B` |
| 3. 双表现 | 同一 `Collider`（放 Shared 层，物理共用）；两个 `MeshRenderer` 子物体同 transform，分别放 A / B 层 |
| 4. 双交互 | 交互逻辑按「哪个相机的玩家在看」分支，或给两套可交互组件各自绑层 |

```csharp
// 设计草图（非最终 API）。实际 CinemachineDualWorld：按「层名 / override LayerMask」把
// 共享层 + A/B 专属层分给两台相机（碰撞体放共享层），Bind(rigA, rigB) 已实现；
// 逐侧变体开关是 CinemachineDualVariant.SetSideVisible(bool a, bool b)。
public sealed class CinemachineDualWorld : MonoBehaviour
{
    [SerializeField] private string m_SharedLayerName = "HYC.WorldShared";
    [SerializeField] private string m_SideALayerName  = "HYC.WorldA";
    [SerializeField] private string m_SideBLayerName  = "HYC.WorldB";
    [SerializeField] private LayerMask m_SharedOverride, m_SideAOverride, m_SideBOverride;

    public void Bind(CinemachineRigBinder rigA, CinemachineRigBinder rigB); // 两侧都渲共享层
}
```

**代价**（Hazelight 原话）：美术要做两套，且**空间关系必须精确对齐**。
这是全方案里**唯一不可能靠代码省掉的成本** —— 我们能做的只有把「分层/切换/校验」自动化。

附带效益：分层裁剪**天然减负** —— A 侧相机不渲染 B 侧专属内容，over-render 的实际绘制量远低于 2×。

### F5 跨接缝穿越 / 门户（P3）

用户原话「一个物品穿越两个相机显示不同内容」有两种完全不同的含义，对应两套方案：

**含义一：同一物体两侧表现不同（= Split Fiction 的箱子/蘑菇）**
→ **不是穿越，是 F4 双世界。** 零额外成本，别想复杂了。

**含义二：物件物理上横跨接缝，视觉上要「接过去」而不是被切断**
→ 三种递进方案：

| 方案 | 做法 | 效果 | 成本 |
|---|---|---|---|
| **C1 世界内实物**（先做这个） | 物件本来就在世界里的接缝位置，两侧相机各自渲一半 | 自然「跨缝」，但两侧几何投影不同、接缝处会错位 | **0** |
| **C2 接缝带重投影**（真「递东西」） | 给要跨缝的物体单独一个 Layer，第三台相机只渲这层 → `_TexCross`；合成 shader 在 `\|s\| < crossingWidth` 的带内改用 `_TexCross` | 物体完整跨过接缝，视觉连贯 | 中：多一张 RT + shader 多一个分支 + 一个 Layer |
| **C3 真门户** | 门平面 + `Camera.CalculateObliqueMatrix(Vector4 clipPlane)`（已实证签名，`UnityEngine.CoreModule`，2022.3 可用）做斜投影，门内采样 B 相机的画面 | 门里看到另一个世界 | 高：门户相机、递归保护、裁剪、深度写入 |

**顺序建议：C1 免费直接写进文档；C2 只在真需要「递东西」时做；C3 单独成篇，别绑在分屏里。**

### F7 嵌套视口（P4，白送的）

`RT_A` 本身就是一张纹理 → 直接当材质贴到世界里的屏幕上，就是「新的视角」那一关。
不需要任何新代码，只要文档里写一句「RT 是公开属性：
`CinemachineSeamCompositor.RenderTargetA/B`」。

---

## 六、配套（不做会影响体验，但可以后补）

### 6.1 HUD 锚定

分屏后 HUD 必须跟着自己那半走。框架 UI 是 ECS System + Canvas（30/31 号文档）。
**最小侵入做法**：HUD 用 `ScreenSpaceOverlay` Canvas，挂一个 `ViewportAnchor` 组件，
按 `SeamState` 把 `RectTransform` 的 anchor 平移到对应半屏 —— 不动 UI 框架本体。

### 6.2 双听者音频（另立）

Hazelight 用 Wwise 的 Proxy Emitters + Spatial Panning Gain：一个声源复制成两份，
按两个听者的方位各算一次增益，再按接缝/焦点权重混合。
Unity 侧对应做法（**不实现，只给接口约定**）：
- 每个 rig 一个虚拟听者位置（不是 `AudioListener`，Unity 只允许一个）；
- 声源增益 = `f(到听者A距离/方位) * wA + f(到听者B距离/方位) * wB`，`wA+wB=1`；
- `wA/wB` 由「声源在接缝哪一侧 / 屏幕占比」给出 —— 正好复用 F6 的查询 API。

### 6.3 后处理与色彩分级

RT 双路 ⇒ **后处理在每路各自跑**。这反而是优点：
Split Fiction 就是两侧**不同的 color grading**，随剧情推进逐渐调和（评测明确提到）。
但要提醒：
- 合成层**不再有全屏后处理**（Bloom 不会跨接缝溢出）——想要跨接缝的光晕要另做；
- 全屏 SSGI/SSR 之类需要全屏深度的效果，在 RT 模式下**各自为政**，接缝处会有不连续。

### 6.4 性能预算与降档

| 项 | 事实 |
|---|---|
| 像素 | 合成路径**至少 2× 全屏像素**（每路都渲整屏再裁）。这是任意接缝的必然代价 |
| Draw call | 2× —— 但 F4 的分层裁剪能强制降到远低于 2× |
| 阴影 / 光照 | 共享（同一场景，只是相机不同） |
| 降档策略（按优先级） | ① 降 RT 分辨率（`renderScale` 0.75，肉眼几乎无感，省 44%）② 关掉一侧的实时阴影 ③ 接缝锁死为轴对齐 + 回落 `Viewport` 模式 |

Hazelight 的做法是「不用 Lumen / Nanite / VSM」换 60fps 锁死 —— 我们是 Unity，
对应做法是**别在分屏场景里开重量级实时 GI**，用烘焙。

---

## 七、设计层约束（给关卡 / 角色 / UI 的硬要求）

这些不是代码，是「不做就白做」的约束，Hazelight 用血的代价换来的：

1. **不能靠镜头作弊。** 单相机游戏可以「切镜头瞬移 / 把相机放到胯上藏脚 / 拿镜头挡穿模」。
   双视角下**这些全废**：一侧看到的，另一侧也在看。→ 角色动画、脚部贴合、穿模一开始就要按双视角验收。
2. **可读性优先级高于画面精度。** 「谁该动、什么时候动」必须在任何接缝形态下都一眼可读。
   → 角色轮廓光、关键交互物高亮、接缝两侧色温区分。
3. **接缝不能遮关键信息。** 接缝飘移/旋转时，必须有一条「避让规则」：接缝靠近关键目标时自动让开
   （`SeamState.Point` 加一个排斥场）。
4. **玩家会偷看对面屏幕 —— 这是特性，不是 bug。** 关卡设计要主动利用它（Split Fiction 明说
   「encouraging screen peeking」）。但**不能做成必须偷看才能玩**，否则视频通话/串流玩家直接卡死。
5. **两人必须都在各自画面里可读**，不能出现「一方在自己的画面里是个小点」的取景。
   → §4.3 的偏置要保证两角色的屏幕占比下限。
6. **合并/分开的节奏要点到即止。** It Takes Two 的经验是稳定居中最舒服，
   Split Fiction 才敢激进 —— 变形是**调味**，常态应该稳。别让接缝一直在动。

---

## 八、落地路线（每阶段都能单独验收）

| 阶段 | 内容 | 状态 |
|---|---|---|
| **P1** | `SeamState` + shader + `CinemachineSeamCompositor` + `CinemachineSeamFraming` + `CinemachineSeamTracker` + `ViewSplitter` 双档 | ✅ 已实现，待编译验证 |
| **P2** | `CinemachineDualWorld` + `CinemachineSeamMerge` | ✅ 已实现，待编译验证 |
| **P3** | `CinemachineSeamCrossing`（接缝带重投影）+ shader alpha 分支 | ✅ 已实现，待编译验证 |
| **P4** | 嵌套视口（已白送：`RenderTargetA/B`）、C3 门户（**不立项**）、双听者音频（只留接口） | — |

**验收清单（进 Unity 后按顺序验）**：

1. 编译零错；`Resources/HYC/SeamComposite.shader` 能被加载（Console 无「找不到 Shader」）。
2. 两个 rig + `SetSeam(斜 30°)` → 画面被斜线切开，两侧各是自己的视角。
3. `SeamState.Vertical` 在 `Viewport` 与 `Composite` 下都能跑（观感不同是预期的，见 §4.3）。
4. 接缝持续旋转 → 无抖动、无掉帧突变。
5. `CinemachineSeamTracker` 的 `SideChanged` 与画面切换同帧（差一帧也算 bug）。
6. 双世界：切换 `CrossVisible` 能看到/看不到对侧内容。
7. 合并：机位过渡平滑，接缝收得干净，完成后只剩一台。
8. 跨缝：物体在接缝处连续，不出现「两半错位」；主相机不重复绘制（无重影）。
9. 跟随两人：一左一右 → 竖线；走到一上一下 → 自动转成横线；过程中无抽筋、无绕大圈。
10. 加权分割：`SplitWeight = 0.3` → A 那半明显更窄；`MinShare` 拉到 0 时两人贴在一起，一侧被压到很窄但不闪。
11. 取景：接缝偏到 0.3 / 90° 斜切时，两个角色各自落在**自己那半的中心**，不在接缝上、也不出框。
12. 双变体：同一位置放两套视觉体（A 层箱子 / B 层蘑菇）→ A 相机看到箱子、B 相机看到蘑菇；
    物体横跨接缝时能看到左半一种、右半另一种（预期行为）。
13. 两种分屏各验一遍（§4.9）：`Viewport` 模式下 A 玩家走动只有 A 屏变化、B 屏纹丝不动、像素成本 1×；
    `Composite` + `DualMirror` 模式下两台相机同步运动、接缝两侧的同一物体严丝合缝。

---

## 九、坑清单

1. **`Camera.rect` 永远做不出斜线。** 别再试图用两个 rect 拼斜切。
2. **RT 的 UV 在 D3D 下是翻的。** 必须处理 `_MainTex_TexelSize.y < 0` / `_ProjectionParams.x`，
   否则 Build 出来的画面和 Editor 上下颠倒（这个 bug 只在打包后出现，最坑）。
3. **RT 用 HDR 格式 + UI 采样 = 画面发灰或过曝。** 每路相机的 tonemap 已经写进 RT 了，合成层要 LDR。
4. **`sRGB` 手动设 false 会整体偏暗。** 用 `RenderTextureReadWrite.Default`。
5. **合成后没有全屏后处理。** AA 必须在 RT 上做；Bloom 不跨接缝。
6. **shader 会被打包剥离。** 只靠 `Shader.Find` 在 Editor 能跑、打包黑屏。
   → 必须在 `Resources` 下放默认材质（或让使用方在 Inspector 拖材质槽）。
7. **`FollowPlayers` 的角度插值不做最短路径会绕大圈。** `atan2` 差 ±π 要归一；再加转速上限。
8. **两人屏幕投影重合时角度会抖。** 加 ~5° 死区。
9. **合成模式下相机不能各自死跟自己角色。** 否则角色永远贴边（§4.3）。
10. **`Quad` / `PiP` 别硬塞进接缝模型。** 半平面表达不了四宫格，留在 `rect` 路径。
11. **仿射一致性问题**：C# 侧 F6 的符号判定与 shader 公式必须逐项对齐（尤其曲率项），
    差一个系数就会出现「玩法说在 A，画面显示在 B」的灾难。
12. **框架不许写 `TagManager.asset`。** 层要使用方自己加，我们只检测 + 提示。
13. **`ScreenSpaceOverlay` Canvas 的 sortingOrder 要压到 UI 之下**，否则 HUD 被分屏画面盖住。

实现时又踩到的（都已规避）：

14. **`renderScale` 会让分辨率比较永远不成立。** RT 宽是 `Screen.width * renderScale`，
    若拿它直接和 `Screen.width` 比，`renderScale != 1` 时会**每帧重建两张 RT**（卡成幻灯片）。
    → 必须比「缩放后的期望值」，见 `CinemachineSeamCompositor.LateUpdate`。
15. **跨缝相机晚一帧就会抖。** 它的位姿必须跟着主相机**同一帧**更新，用 `LateUpdate` 追不够
    （主相机是 Cinemachine 在 LateUpdate 里摆的）。→ 挂在 `Camera.onPreCull` 上，
    在自己出图前同步位姿与 FOV。
16. **跨缝层必须从主相机剔掉。** 否则同一物体被画两遍 → 半透明重影。
    → `CinemachineSeamCrossing` 启动时自动 `&= ~mask`，停止时还原。
17. **跨缝层要按 alpha 混，不能只按 rgb。** 跨缝相机清屏为全透明，只取 rgb 会把整条带涂黑。
    → shader 里 `lerp(color, cross.rgb, band * cross.a)`。
18. **接缝驱动源不要每帧刷事件。** `FollowPlayers` 每帧都在算，若无条件 `SeamChanged?.Invoke`
    会让订阅方每帧被调。→ `CommitSeam` 里做 epsilon 比较。
19. **`SetLayout` 不能无脑覆盖合成器。** 宿主改成 `FollowPlayers` 后，任何一次 `Apply()`
    若调用 `compositor.SetLayout` 就会把驱动源打回 `Authored`。
    → 只有 `Source == Authored` 时才下发布局。
20. **取景偏移的坐标系有两个原点，别混。** `GetNormalSpan` 的 `min/max` 以**接缝**为原点，
    而瞄准偏移要以**屏幕中心**为原点。少加 `NormalOffset()`，接缝一旦被加权推偏，
    角色就被推出屏幕。这个 bug 只在「接缝不在正中」时才现形，对半切完全正常，很容易漏测。
21. **取景偏移必须沿法线方向。** 老的实现只沿 `camera.right` 推，竖切对、上下切全错。
    → 方向取 `right * n.x * halfW + up * n.y * halfH`，等于「视口里沿法线走 1 个单位」的世界向量。
22. **跟随模式存在「接缝 ↔ 取景」互为因果的闭环。** 接缝由相机 A 的投影算出，相机 A 又按接缝取景。
    本框架靠 `SplitWeight` 的收缩系数（1/3）+ 死区 + 转速上限让它稳定收敛；
    若某天要绝对稳定（比如接缝跟随做得极快），给 `SetFollowTargets` 传一台**只算投影、不渲染**的
    参考相机，把闭环拆开。
23. **`MinShare` 必须有兜底值。** 不设的话，两人贴到一起时接缝会滑到屏幕边，某一侧压成零宽 ——
    那半屏彻底没有人，玩家会以为死机。
24. **`NormalOffset()` 里的 `Push` 要取负号。** `SignedDistance` 是 `s = dot(p-P,n) + Push`，
    所以 `s = 0` 的等值线落在 `dot(P,n) - Push` 处，不是 `+ Push`。写成同号会让
    「跨屏占比」与取景偏移在**合并补间期间**反向偏一个 `Push` —— 而 `Push = 0` 时完全看不出来，
    和第 20 条同属「只有接缝被动过才现形」的家族。`GetNormalSpan` 的 `q` 在四角处必须
    与 `SignedDistance` 逐项相等，这是自检该式的唯一办法。
25. **`CinemachineDualVariant` 默认只认层名，无名层解析不出来。** `LayerMask.NameToLayer` 对
    无名层返回 `-1`，而原型 / 样例常用无名层（28、29 之类）；这时用 `SetLayers(层号A, 层号B)`
    跳过名字解析（§4.8）。正式项目仍应命名层 —— `CinemachineDualWorld` 也按层名配 cullingMask。
26. **从 `rect` 路径切进合成路径，两侧相机必须显式 `enabled = true`。** `Camera.rect` 路径会把
    「没进槽位」的相机 `enabled = false`（`ViewLayout.Single` 下尤其明显），而合成器只负责设
    `targetTexture` / `rect`，没人再打开它们 ⇒ **那一半永远是黑的**。已在
    `CinemachineSeamCompositor.ApplyCameraTargets` 里强制打开。
27. **`Suspend()` 过的合成器不会自己回来。** `SetTargets` 对「同一对 rig」是幂等的
    （早退），所以切去 `Quad` / 画中画 / 同屏再切回 `Vertical` 时，合成层一直隐藏、相机也不出图
    ⇒ **整屏黑**。已在 `CinemachineViewSplitter.TryBindComposite` 里显式 `Resume()`。
    这两个坑同源：**两档 `RenderMode` 的切换是有状态的，进合成要「重新武装」一遍**。
28. **机位不能做成 `Camera` 的子物体。** 机位是位姿来源、Camera 是出图设备；机位当子物体时
    Brain 每帧把位姿写回 Camera，父子关系又反喂给机位 —— 跟随机位收敛发飘，
    **固定机位直接原地不动**（每帧「读到自己在哪、就写回自己在哪」）。层级应是 `Rig/` 下平级。
29. **`CinemachineCamera.Priority` 的 `Enabled` 默认是 false。** 只写 `Value` 不写 `Enabled`
    的机位会被当成优先级 0；`Priority = int` 的隐式转换会顺带打开 `Enabled`，所以一律用赋值。
30. **多 rig 各带 Brain，必须用 `OutputChannel` 分流。** CM 3 的 `CinemachineBrain` 是从
    **全局**机位表里挑优先级最高的那台，`IsValidChannel` 只看 `vcam.OutputChannel` 与
    `brain.ChannelMask` 是否相交 —— **完全不看层级**（不是「只看自己的子物体」）。
    两份 rig 都用默认通道时，两个 Brain 会挑到**同一台机位**，表现为「两半画面内容一模一样」，
    而且**不报任何错**。`CinemachineViewSplitter` 现在在 `Register` 时自动分配
    （第 1 份 `Default`、第 2 份起 `Channel01/02…`，上限 9 份），`ManageOutputChannels` 可关。
    自己手搓多机位系统时也得照这个规矩来。
31. **合成画面整幅上下颠倒 = `FlipY` 多翻了一次。** `RawImage` 显示 RT 本来就正立，
    不要再按 `graphicsUVStartsAtTop` 补 `1 - v`。症状极好认：**画面倒了，但接缝位置是对的**
    （接缝数学走视口空间、没跟着翻）。现默认 `SeamPass.FlipY = 0`。
32. **接缝默认不可见。** `SeamState` 里没有任何「画一条线」的字段时，玩家看到的是两幅画面的硬切边，
    做可拖拽 / 需要玩家理解「现在在分屏」的场合必须开 `GlowWidth` + `GlowColor`
    （`CinemachineDualView.SetGlow(width, color, pulseHz)`，建议 0.015~0.03）。
33. **`Camera.cullingMask` 是掩码，不是层号。** 传 `28` 得到的是 `0b11100`（层 2/3/4）⇒
    相机什么都看不到：**只有清屏色、不渲染任何几何、也不报任何错**，最容易被误判成「渲染坏了」。
    必须 `1 << 28`；「什么都看、只排除内置 UI 层」写 `~(1 << 5)`。样例 ① 早期就这么错过一次。
34. **「合屏」不是 `Combine`，别用 `Crossfade` / `Push` 凑并拢。** `Crossfade` 是**全屏混合权重**
    （整屏溶化成某一侧，画面不移动），`Push` 把接缝推出屏幕则是「一侧被挤走」的观感 ——
    两者都不会让两个人靠在一起。要「两人并拢、主相机同时照住俩人」必须走
    `CombineAroundPlayers()`（§4.7）：它靠 `CinemachineSeamFraming` 把两台相机的
    Follow 锚点收到两人中点 + 等比放大 `FollowOffset`，收尾时两台相机位姿完全一致。
    前提是 `AutoFraming = true` 且 `Bind` 时给了两个角色；固定机位没有可并拢的对象，
    会打警告退回溶化 —— 这是正确行为。
35. **并拢过程中不要给跟随锚点再叠一层平滑。** 过渡只由 `GroupBlend` 的指数衰减承担；
    锚点再平滑一次，分屏态就会变成「相机慢半拍」，玩家感到跟随机位整体发软 / 拖影。

---

## 十、待拍板的决策点（完整版）

> **前置闸门 G1**：整包目前被 SceneContent 那批编译错卡住（`Entity`/`Translation` 命名空间、
> `Addressables.LoadAssetAsync<T>` 推断、EA0001 等 7 条），Cinema 侧始终进不了 Unity 编译。
> P1 写完也无法验证 —— **建议先清掉那批错再开工**，否则等于闭眼写码。

### 速览

| # | 决策 | 我的建议 | 选错的代价 |
|---|---|---|---|
| D1 | `ViewLayout` 替换还是并存 | ② 并存 | 使用方调用点集体编译失败 |
| D2 | 合成载体 | ① Overlay Canvas | 框架被迫绑定一条渲染管线 |
| D3 | RT 规格（格式 / MSAA / 分辨率） | ① LDR + MSAA 可配 | 画面发灰过曝 / 锯齿或糊 |
| D4 | `RenderMode` 能否运行时切 | ② 不允许（降档除外） | 切一半闪屏 + FOV 基准被污染 |
| D5 | 穿越做到哪一档 | ② C1 先做，C2 按需 | 白写一套没人用的 portal |
| D6 | 双世界层怎么定 | ③ 默认名 + Inspector 覆盖 | 相机错拿到第 31 层，画面全错 |
| D7 | 双听者音频是否同期做 | ① 只留接口 | 相机任务被音频拖死 |

---

### D1 · `ViewLayout` 是替换还是并存？

**问题**：57 号文档已经发布了 `SetLayout(ViewLayout)` 和 `LayoutChanged` 事件，
新方案的核心抽象是 `SeamState`。两者关系怎么摆？

| 选项 | 好处 | 代价 |
|---|---|---|
| ① 完全替换：删 `ViewLayout`，只留 `SetSeam` | 抽象干净，只有一条路 | `SetLayout` / `LayoutChanged` / `SetLayout(layout, params rigs)` 三个入口全破坏；已发布文档作废；使用方调用点集体编译失败 |
| ② **并存**：`ViewLayout` 保留，`Composite` 模式下内部转成 `SeamState`；新增 `SetSeam` | 零破坏；老的 `SetLayout(Vertical)` 一行不用改就能吃到新渲染；迁移是渐进的 | 两套入口，文档必须讲清「`Vertical` 在两种 `RenderMode` 下观感不同」 |
| ③ 并存 + `[Obsolete]` 标记 `ViewLayout` | 给了迁移信号 | 使用方编译告警刷屏，而 v3 根本不会删，纯噪音 |

**建议 ②**。理由：`ViewLayout.Vertical/Horizontal` 恰好是 `SeamState` 的两个特例，
映射是 1:1 的（见 §3.1），并存零成本；而删枚举是**没有收益的破坏**。
`Quad` / `PiP` 本来也留在 `rect` 路径，`ViewLayout` 必须活着承载它们 —— 这一点让 ① 直接不成立。

**落点**：`CinemachineViewSplitter.cs`（新增 `RenderMode` + `SetSeam`，`SetLayout` 路径按 mode 分流）、
57 号文档 §5.5 加一句「Composite 模式见 58 号」。

---

### D2 · 合成载体选哪个？

**问题**：两张 RT 合成到屏幕，走哪条渲染路？

| 选项 | 好处 | 代价 |
|---|---|---|
| ① **Overlay Canvas + RawImage + 自定义 shader** | 零管线依赖，Built-in / URP 都能跑；不用改使用方的 Renderer 资产 | 多一个全屏 Graphic（一次全屏采样，可忽略）；HDRP 对 Overlay Canvas 支持有限；合成后接不上场景级后处理 |
| ② URP `ScriptableRendererFeature` | 最省，能进 SRP 的 render graph，可与后处理共存 | **锁死 URP**。框架 `package.json` 现在零管线依赖，加了就等于替所有使用方选管线；且要使用方去改 Renderer 资产 |
| ③ 双实现（`#if` 或 `ISeamCompositeBackend` 双后端） | 两全 | 两套代码维护翻倍；沙箱禁编译，两条路都得你手动验，验不过来 |

**建议 ①**，但**预留 ③ 的接缝**：合成动作抽成 `ISeamCompositeBackend`（`void Composite(RenderTexture a, RenderTexture b, in SeamState seam)`），
当前只提供 `CanvasCompositeBackend` 一个实现。将来真有人要 URP 高性能后端，加一个实现即可，不动上层。

**关键依据**（已实证）：框架 `package.json` 无 `com.unity.render-pipelines.*` 依赖，全包**零 `.shader` 文件**
—— 这是框架第一个 shader，不能开「必须有 URP」这个先例。

**落点**：新增 shader + 材质 + `CinemachineSeamCompositor.cs`；**不动任何 asmdef**。

---

### D3 · RT 规格怎么定？

分三个子项，一起定。

**D3a 格式**

| 选项 | 好处 | 代价 |
|---|---|---|
| ① **LDR `RenderTextureFormat.Default`** | 每路相机的 tonemap **已经在写 RT 之前完成**，合成层拿到的就是最终 LDR 像素；UI 采样不再做二次色彩变换，结果确定 | 丢掉 HDR 高光余量（但我们也不需要，因为合成后不再做后处理） |
| ② HDR `DefaultHDR` | 保留高光余量，理论上能在合成层做跨接缝 bloom | tonemap 时机随管线/设置漂移，UI 采样 HDR 值再输出到 LDR backbuffer 极易发灰/过曝；且 RT 显存翻倍 |

**建议 ①**。论证点是**确定性**而非「HDR 一定错」：LDR 路径在任何管线、任何色彩空间下都只有一个结果，
HDR 路径的结果依赖管线的 tonemap 时机 —— 一个跨管线框架不能赌这个。

**D3b MSAA**

| 选项 | 好处 | 代价 |
|---|---|---|
| ① RT 上 `antiAliasing = 4`（默认开） | 项目没开 post-AA 时也有抗锯齿 | 每路 RT 显存和带宽涨；项目已开 TAA/FXAA 时是白花钱 |
| ② **默认 1，暴露成配置** | 不重复付钱 | 项目啥都没开时画面锯齿明显，需要文档明确提示 |
| ③ 自动探测项目后处理设置 | 智能 | 跨管线探测不可靠（URP 的 AA 配置在 Renderer 资产里，框架读不到稳定路径） |

**建议 ②** + 文档写死一句「若项目未启用 post-AA，请把 `msaaSamples` 设为 4」。
理由是合成后**没有全屏后处理**，AA 只能由 RT 承担，这个责任必须显式交回使用方。

**D3c 分辨率**

| 选项 | 好处 | 代价 |
|---|---|---|
| ① **1:1 跟随 `Screen`，随分辨率重建** | 像素一对一，不糊 | 显存固定 33MB@1080p |
| ② `renderScale` 可配（0.5–1） | 低端机省 44% 带宽 | 缩放必然糊，且接缝边缘会露出插值痕迹 |

**建议 ① 为默认 + 暴露 `renderScale` 作为降档旋钮**（见 §6.4）。默认不开，因为这是画质自残。

**落点**：`CinemachineSeamCompositor.cs` 的 RT 分配与重建逻辑。

---

### D4 · `RenderMode` 允许运行时切吗？

**问题**：`Viewport`（rect，省一半像素）和 `Composite`（双 RT，任意接缝）能不能在游戏里随时切？

| 选项 | 好处 | 代价 |
|---|---|---|
| ① 允许随意切 | 平时用便宜模式，只有剧情演出时切贵的 | 两种模式的相机 **aspect 不同** ⇒ 视野不同 ⇒ **切换瞬间必跳变**；要做 FOV 对齐收敛；且 `BaseFovs` 基准会被两种模式互相污染（现有 `Restore` 逻辑只认一份基准）→ 状态越切越乱 |
| ② **不允许，一个游戏选一种** | 零跳变风险，FOV 基准只有一份 | 全程付 Composite 的 2× 像素成本 |
| ③ ②+降档例外：只允许单向「Composite → Viewport」一次性降档 | 低端机能救一把 | 降档那一刻还是跳（但用户可接受，因为已经在掉帧了） |

**建议 ③**（本质是 ② 加一条受控逃生通道）。理由：Split Fiction 那种「平时稳、演出时变形」的节奏
听起来很诱人，但**它靠的是同一套合成管线改参数**，不是两套管线来回切 —— 我们照抄这个思路即可：
**全程 Composite，变形只是改 `SeamState`**。真正的降档是性能兜底，一次性、不可逆、跳一下无所谓。

**选错的代价**：允许随意切 = 接缝动画播到一半切模式，画面闪 + FOV 基准乱，
而且这种 bug 只在特定操作序列下出现，极难复现。

**落点**：`ViewRenderMode` 默认值 + `SetRenderMode` 是否公开。建议公开但文档标红「仅降档用」。

---

### D5 · 跨缝穿越做到哪一档？

**问题**：你说的「一个物品穿越两个相机显示不同内容」，其实对应两种完全不同的东西，先要分清。

| 你想要的 | 对应能力 | 成本 |
|---|---|---|
| 同一物体，A 相机看到箱子、B 相机看到蘑菇 | **F4 双世界**（分层裁剪） | **0**，纯配置 |
| 物体物理上横跨接缝，视觉上要「接过去」而不是被切成两半 | C1 / C2 / C3 | 见下 |

| 选项 | 做法 | 效果 | 代价 |
|---|---|---|---|
| ① C1 世界内实物 | 物件本就在接缝位置，两侧相机各渲一半 | 有「跨缝」感，但两侧投影不同、接缝处会错位 | **0** |
| ② **C1 + C2 接缝带重投影** | 跨缝物体单独一个 Layer，第三台相机只渲它 → `_TexCross`；shader 在 `\|s\| < crossingWidth` 带内改用 `_TexCross` | 物体完整跨过接缝，视觉连贯 | 中：+1 张全屏 RT、shader 一个分支、一个 Layer 约定 |
| ③ 直上 C3 真门户 | `Camera.CalculateObliqueMatrix` + 门平面重投影 | 门里看到另一个世界 | 高：门户相机、递归保护、深度写入、近平面裁剪；**且与分屏解耦，不该塞在这个任务里** |

**建议 ②**（C1 先落文档，C2 在 P3 实现）；**C3 单独立项**。
理由：C2 覆盖了「递东西 / 物件跨缝」的全部诉求，成本可控；而 C3 是一套独立的 portal 渲染系统，
它的复杂度和风险跟分屏不在一个量级，混在一起会拖垮 P1–P2 的节奏。

**先决问题（需要你回答）**：你举的「一个物品穿越两个相机显示不同内容」，
是**「同一物体两侧长得不一样」**（→ 纯 F4，免费）还是**「物体横跨界面要接得上」**（→ C2）？
这两种如果都想要，做法也不同，不能一把梭。

---

### D6 · 双世界的层怎么定？

**问题**：双世界靠 `cullingMask` 分层，那三个层的名字/编号从哪来？

框架**不能**改 `ProjectSettings/TagManager.asset`（那是使用方项目设置，改了会跟别人的层规划打架，
而且多包共存时谁都不该独占写入权）。所以层必须由使用方建，问题只是「框架怎么找它」。

| 选项 | 好处 | 代价 |
|---|---|---|
| ① 硬编码约定名 `HYC.WorldA / HYC.WorldB / HYC.WorldShared` | 配置即用，代码里 `LayerMask.NameToLayer("HYC.WorldA")` 一句拿到 | 使用方层规划被绑架；**且层不存在时 `NameToLayer` 返回 -1** |
| ② 纯序列化 `LayerMask` 字段，Inspector 手配 | 零冲突，最灵活 | 每个场景/每个 rig 都要配，漏一个就画面错，且错了不报错 |
| ③ **默认名 + Inspector 覆盖**：Inspector 留三个 `LayerMask` 槽；为空时回落到约定名；都拿不到就在编辑器窗口里报错提示 | 开箱能用，也不绑架 | 多一层回落逻辑（约 20 行） |

**建议 ③**。

**⚠️ 这里有个必须避开的真实坑**：`LayerMask.NameToLayer("不存在")` 返回 **-1**，
而 `1 << -1` 在 C# 里**不会报错** —— 位移量按低 5 位取模，等价于 `1 << 31` =
`int.MinValue` = **第 31 层**。结果就是「层名打错一个字，相机悄悄把第 31 层的东西全渲进来了」，
而且不抛异常、不报 warning，只能靠肉眼发现画面多了一堆东西。
→ 实现里**必须先判 `layer >= 0`**，并在编辑器侧做一次显式校验（`CinemachineDualWorld` 的
`OnValidate` + 一个检查窗口），这是硬要求。

**落点**：`CinemachineDualWorld.cs` + 一个编辑器校验入口。

---

### D7 · 双听者音频这次一起做吗？

**问题**：分屏后两个玩家各需要一个「听者」。Unity 只允许一个 `AudioListener`，
Hazelight 用 Wwise 的 Proxy Emitters + Spatial Panning Gain 解决。我们怎么排？

| 选项 | 好处 | 代价 |
|---|---|---|
| ① **只留接口约定** | 相机任务不被拖；音频方案还没定（框架是否接 Wwise 本身是待办） | 分屏上线初期空间音一定是错的（所有声音按一个听者算） |
| ② 一起做 | 一步到位 | 要动音频四层架构（`AudioBackend` / 音频总览 / 音量分组）—— 改动面比相机大得多，且依赖「Wwise 接不接」这个未定项 |
| ③ 做最小版：`AudioListener` 跟焦点走 + 声源按「在接缝哪一侧」做侧向衰减 | 性价比最高，复用 F6 的查询 API，不改架构 | 只是近似，不是真双听者 |

**建议 ①，并把 ③ 记为 P2.5 可选项**。理由：现在 `ViewSplitter` 的 `ManageAudioListener`
只保证「同时只有一个监听器不报警」，这已经够让分屏不崩；真双听者混音是独立课题，
且它依赖的「框架到底用不用 Wwise」还没拍板，现在做等于返工。

**落点**：58 号 §6.2 的接口约定；实现另立。

---

### 拍板结果（用户：全做，全部按建议）

- [x] **D1** ② 并存 —— `ViewLayout` 保留，新增 `SetSeam`；`Quad`/`PiP` 留 `rect` 路径
- [x] **D2** ① Overlay Canvas —— 抽象成 `ISeamCompositeBackend`，将来可加 URP 后端
- [x] **D3a** ① LDR　**D3b** ② 默认 MSAA=1（暴露配置）　**D3c** ① 1:1 + `renderScale` 降档
- [x] **D4** ③ `RenderMode` 只读 + `DowngradeToViewport()` 单向降档
- [x] **D5** ② C1 免费写进文档 + C2 已实现（`CinemachineSeamCrossing`）；C3 门户不立项
- [x] **D6** ③ 默认名 + Inspector 覆盖，`TryResolveMask` 里判 `layer >= 0`
- [x] **D7** ① 双听者音频只留接口（§6.2）
- [x] **G1** SceneContent 那批错在当前工作区已无静态残留（`Translation`/泛型调用均已修正），
      仍**需要一次真实 Unity 编译**确认

---

## 十一、与原方案的边界（别越界）

| 归本设计 | 不归本设计 |
|---|---|
| 接缝数学与合成 shader | 渲染管线选型（使用方的事） |
| 双相机调度与取景协调 | 输入配对 / 设备分配（游戏层） |
| 双世界分层裁剪 | 两套美术的制作与对齐（美术管线） |
| 接缝查询 API | 具体关卡的「何时切侧」玩法逻辑（关卡脚本） |
| 合并时间轴 | 音频双听者混音（另立任务） |

---

## 相关文档

- [57-Cinemachine相机装配](./57-Cinemachine相机装配.md) — rig / binder / `ViewSplitter` 基础
- [30-ECS-UI-总览](./30-ECS-UI-总览.md) — HUD 锚定要动的层
- [55-音频用法](./55-音频用法.md) — 双听者混音要动的层
