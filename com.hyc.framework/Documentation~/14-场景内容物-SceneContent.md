# 14 · 场景内容物（Scene Content）

> 一套**框架级通用能力**，让「关卡里有什么」成为可编辑、可导出、可加载的数据，
> 而不是散落在 prefab 里的隐式引用。与 Config / BT / Attr 平级，不绑定任何具体玩法。

---

## 用法速查（接入方 / AI 上手）

> 要让「关卡里有什么」（出生点、触发器…）成为可编辑、可导出、可加载的数据，而非散落在 prefab 里的隐式引用。来源只有**标记组件**（`ISceneContentMarker`）。Blob 按关卡**资产 GUID** 命名，但运行期用**业务 key** 经导出期生成的 `index.json` 查表加载（不需要、也不应该手抄 GUID）。以下 6 步即可接入，完整契约见下方 §一~§五。

1. **建类型（唯一入口，禁用 `ScriptableObject.CreateInstance`）**
   ```csharp
   var def = SceneContentFactory.CreateType("场景内容/出生点", "SpawnPoint");
   // 或菜单 HYC Framework/Scene Content/New Scene Content Type
   ```
2. **配字段**：在 Scene Content Window 右侧设计器加字段（字段类型下拉与配置模板同源 `ConfigFieldTypeMenu`）。GO 引用字段必须选 `HierarchyPath` 族——`Direct`/`Text` 对 GameObject 非法（会写成不稳定 instanceID）。
3. **生成代码**：点 Inspector 底部「生成代码」→ 产出 `Assets/HYC/Generated/SceneContent/<类名>.cs`（挂 GO 用的标记组件）+ `SceneContentTypes.g.cs`（id 常量表）。
4. **挂到关卡**：给关卡 prefab / scene 的出生点 GO 加生成的组件（`Add Component → HYC/Scene Content/<类名>`，或窗口工具栏「给 GO 加组件」）。`localPosition/rotation/scale` 即导出坐标。
5. **导出（对资产调用，无需手填 id、无描述器组件）**
   ```csharp
   // Project 选中关卡 Prefab / Scene 资产后；alias 建议显式传运行期已有的业务 key：
   SceneContentExporter.Export(levelPrefab, "Level/001");
   SceneContentExporter.Export(Selection.activeObject);   // 不传 alias：自动取 Addressables 地址 > 资产名
   SceneContentExporter.ExportAll();                      // 批量导出全部含标记的关卡
   ```
   Blob 落 `StreamingAssets/SceneContent/<资产GUID>.blob` 与 `…/server/<资产GUID>.blob`；
   同时维护映射清单 `StreamingAssets/SceneContent/index.json`（业务 key → 资产 GUID）。
6. **运行期加载**（关卡加载完成后，用加载器**本来就持有**的业务 key）
   ```csharp
   public class MySpawner : ISceneContentSpawner
   {
       public void Spawn(int contentTypeId, float3 pos, quaternion rot, float3 scale, NativeArray<byte> payload)
       {
           // 按 contentTypeId 分派（优先用生成的 SceneContentTypes 常量表）；payload 是 UTF8 JSON
           var json = System.Text.Encoding.UTF8.GetString(payload.ToArray());
           // GameObject 引用是层级路径，反解： SceneContentRefPath.Resolve(levelRoot, json.doorPath);
       }
   }
   // 关卡加载器手里的业务 key（配置 id / Addressables 地址 / 资产名）：
   SceneContentLoader.LoadByKey(levelKey, new MySpawner());                        // 客户端
   SceneContentLoader.LoadByKey(levelKey, serverSpawner, SceneContentSide.Server); // 专用服务器
   // 仅当手里确实已有资产 GUID 时，才用底层通道：SceneContentLoader.Load(guid, new MySpawner());
   ```

**关键契约（AI 改写时务必遵守）**：
- **运行期用业务 key，不用资产 GUID**：`AssetDatabase` 只在编辑器可用，打包后（尤其 WebGL）没有；且程序化重建 prefab 会换 GUID。导出期把「业务 key → GUID」写进 `SceneContent/index.json`，`LoadByKey` 查表后复用 `Load`。Unity 运行期无 `gameObject→资产GUID` API，GUID 不能从关卡内 GO 反查，**更不能在代码 / Inspector 里手抄**。完整说明见「业务 key 与映射清单」。
- **key 来源**：`Export(asset, alias)` 显式 alias > Addressables 地址 > 资产名；`ExportAll()` 自动推断。同名资产（不同目录）会撞 key，须显式传不重复的 alias。
- **内容 id 是稳定 int**：`ISceneContentMarker.ContentTypeId` 建议来自 `SceneContentTypeId.Compute(className)`（FNV-1a 32 位，无需发号服）；改类名 = 换 id，需重导 blob。
- **payload 自定**：标记组件实现 `ISceneContentMarkerPayload.ToPayloadJson(GameObject levelRoot, SceneContentSide side)` 决定字段与两侧差异；不实现退回 `JsonUtility.ToJson`（仅标量）。
- **生成物禁手改**（铁律 §3）：`Assets/HYC/Generated/SceneContent/*.cs` 带 `auto-generated`，改字段回类型定义重生成。
- **prefab→id 解析器放样例 Editor asmdef**：`SceneContentResolvers.Register`，内置兜底读组件自身 id。

---

## 一、它解决什么

做关卡时，「关卡里有什么」由**标记组件**描述：角色出生点、怪物出生点、触发区……
直接挂在关卡 prefab / scene 的 GO 上（实现 `ISceneContentMarker`）。

它们应能：在编辑器里**列出来看**（几个出生点、什么类型）、**导出成 Blob**、
**运行期在关卡加载后加载并生成**。本模块把标记组件统一收集成一份 Blob，用同一套加载。

> 早期设计有两套来源（A 显式实例条目 + B 标记组件）。**A 来源已移除**：它本质是把 prefab 当
> 数据手填进列表，与标记组件重复且易漂移；现在只有 B（标记组件）一种来源，导出器直接在关卡
> 资产上扫描 `ISceneContentMarker`。

---

## 二、核心概念

- **Blob 身份 = 资产 GUID，运行期身份 = 业务 key**：不再有任何「关卡描述器」组件。
  导出器直接对**关卡资产**（Prefab 或 Scene）调用，用其 `.meta` 的 GUID 命名 Blob；
  同时在 `SceneContent/index.json` 登记「业务 key → GUID」。运行期关卡加载器用自己本来就有的
  业务 key 调 `SceneContentLoader.LoadByKey(key, …)`，由框架查表换 GUID。
  组件零手动、不用关卡名、无手填 id、**不手抄 GUID**（见 3.5.1）。
- **`SceneContentTypeDef`**（普通 ScriptableObject，**不标 `[CfgAsset]`、不进配置数据编辑器**）=
  策划配的「场景内容模板」：一个定义产出一个标记组件（`ISceneContentMarker` 实现）+ 一组可配参数。
  入口是菜单 `HYC Framework/Scene Content/New Scene Content Type`，编辑走自定义 Inspector。
- **`ISceneContentMarker`**：任何标记组件实现它即被自动发现；
  `ContentTypeId` 是**稳定 int**（prefab/配置引用不能进 Blob，一律收成 id）。
  生成组件的 id 由类名 FNV-1a 32 位哈希得出（`SceneContentTypeId.Compute`），无需发号服。
- **`ISceneContentMarkerPayload`**（可选）：标记组件自定义 payload JSON 的契约。
  不实现时退回 `JsonUtility.ToJson`（**只能放标量**）；实现后可按导出侧裁剪字段、
  并把 GO 引用写成层级路径。生成的组件会自动实现它。
- **`SceneContentRefPath`**：关卡内 GO 的稳定引用——`Gate/Door#1` 这种相对关卡根的层级路径，
  同级重名才带 `#下标`；`GetPath` / `Resolve` 互逆。
- **`SceneContentJsonWriter`**：生成代码用它逐字段拼 payload（逗号/转义/不变文化自动处理）。
- **`ISceneContentSpawner`**：运行期生成实例的注入点。框架只管读 Blob + 解析，
  具体生成 GameObject 还是 Entity 由游戏决定。
- **`ISceneContentPrefabResolver`**（编辑器）：把实例条目的 prefab 引用解析成 int id 写进 Blob。
  未注册自定义解析器时走内置兜底（读 prefab 上标记组件自己的 id），**挂了生成组件就等于注册**。

### 数据流

```
关卡 Prefab / Scene 资产（.meta GUID）
└─ B. 标记组件 ISceneContentMarker（挂在资产内 GO 上）  ──→ 收集 → SceneContentBlob
                                                            StreamingAssets/SceneContent/<guid>.blob            (客户端)
                                                            StreamingAssets/SceneContent/server/<guid>.blob     (服务端)
                                                            StreamingAssets/SceneContent/index.json             (业务 key → guid)
        (运行期) 关卡加载器持有业务 key → LoadByKey(key, spawner, side)
                 → 查 index.json 得 guid → Load(guid, …) → spawner.Spawn(...)
```

---

## 三、策划工作流（类型定义 → 生成组件 → 挂点 → 导出）

### 3.1 新建「场景内容类型」

菜单 **HYC Framework/Scene Content/New Scene Content Type** →
生成类型定义资产并自动选中；**创建即按显示名命名**（与数据配置 `CreateTemplate` 一致，
不再是固定的 `NewSceneContent.asset`）。之后在 **Scene Content Window → 类型设计** 页签里选中它、
在右侧设计字段（见 3.2）。**字段设计只在本窗口内进行**：Project 里点开 `.asset` 时，
资产的原生 Inspector 主体不显示任何编辑内容（`OnHeaderGUI` 仍显示资产图标与只读资产名），
不会把同一套字段 UI 复制到两处。

- **AI / 程序化创建走唯一入口**：脚本化创建（如 AI 生成、批量建类型）一律调
  `<SceneContentFactory.CreateType(displayName, className, targetFolder)>`（对齐数据配置的 `ConfigAssetFactory` /
  行为树的 `BTAssetFactory`），**禁止手搓 `ScriptableObject.CreateInstance<SceneContentTypeDef>()` + 手填 className /
  手拼路径落盘**。工厂负责归置默认文件夹、按显示名自动命名、冲突加序号、清理不可见字符；
  类型定义不参与发号，故工厂不派发 ID。批量可用 `<SceneContentFactory.CreateTypeMany(prefix, count, folder)>`。

- **资产名自动同步显示名**：保存 / 生成代码时会把 `.asset` 文件名同步成（净化的）显示名
  （`<SceneContentTypeCodeGen.SyncTypeAssetName>`：`/`、`\` → `-`，其余文件系统非法字符 → `_`，冲突加序号），
  用 `AssetDatabase.MoveAsset` 搬家、**保留 GUID、不破坏引用**。改显示名后点「保存」即生效。
- **Inspector 头部**：自定义 `OnHeaderGUI` 只画资产图标 + 只读资产名，**不画 Unity 默认头部的「?」帮助图标**；
  资产名由上面那套同步逻辑管理，不在头部手改（避免与显示名脱节）。

> **为什么不在配置数据编辑器里**：`SceneContentTypeDef` 不是配置数据——没有 ID/GUID、
> 不参与发号、不参与配置导出。它**不标 `[CfgAsset]`**，所以既不出现在「配置创建器」窗口，
> 也不出现在配置数据树。配置创建器只服务「带 ID/GUID、要发号」的配置资产
> （它必走 `ConfigIdService.TryWriteId`，没有 ID/GUID 字段的类型会被弹窗拒绝）。
> 它的定位更像「代码模板/生成源」，入口放本模块菜单，编辑走 Scene Content 窗口内嵌的自定义编辑器。

### 3.2 配字段

选中刚建的类型定义，右侧面板：显示名、类名、**字段列表**。
**字段就在窗口内设计**，不需要跳到别处；资产的原生 Inspector 不显示字段编辑内容。

每个字段配四项：字段名 / 类型 / 是否列表 / 两侧（客户端、服务端）导出方式。

**「类型」下拉就是配置模板编辑器（Config Editor）那一份菜单**，由公共类 `ConfigFieldTypeMenu` 提供，
分类路径与动态引用项完全同源：值类型（字符串 / 整数 `Int`、`Long`、`Short`、`Byte`、`UInt` /
浮点 `Float`、`Double`、`Decimal` / 布尔 / 字符）、Unity 数学（向量 / 旋转 / 颜色 / 矩形 / 边界 /
LayerMask）、Unity 对象、框架能力（行为树 / 资源地址 / 音频 / 动画）、多语言 Key，
外加两个动态引用段：**引用/配置引用**（工程内全部 `[CfgAsset]` 类型）与
**引用/枚举引用**（数据编辑器里建的全部枚举定义）。选中引用项会自动写入
`enumRefClassName` / `refTypeFullName`，不用手填类名。

暂不支持的项**仍留在菜单里，但置灰并标出原因**（不删项、不另造一套菜单）：资源类引用
（Sprite / Material / …，只有 `GameObject` 能转成稳定引用）、`Gradient` / `AnimationCurve`
（曲线写不进 payload）、以及 loc 包未安装时的多语言 Key。
`SceneContentTypeCodeGen.SupportedTypes` 是「可生成」白名单的唯一真源，编辑器置灰与生成期校验都读它。

| 字段类型 | 生成字段 | payload 形态 | 可用的导出方式 |
| --- | --- | --- | --- |
| `String` / `LocalizedKey` / `Addressable` / `AudioEvent` | `string` | JSON 字符串 | `Direct` / `Text` / `None` |
| `Int` / `Long` / `Float` / `Double` / `Decimal` / `Bool` / `Short` / `Byte` / `UInt` / `Char` | 同名 C# 类型 | JSON 标量（`Char` 写字符串） | `Direct` / `Text` / `None` |
| `Vector2/3/4`、`Vector2Int/3Int`、`Quaternion`、`Color`、`Color32`、`Rect`、`RectInt`、`RectOffset`、`Bounds`、`BoundsInt`、`LayerMask` | 同名 Unity 类型 | JSON 数字数组（固定分量顺序） | `Direct`（数组）/ `Text` / `None` |
| `Enum`（引用数据编辑器的枚举定义） | `{EnumNamespace}.<枚举名>` | `Direct` 写 int、`Text` 写名字 | `Direct` / `Text` / `None` |
| `Reference`（配置引用）/ `BehaviourTree` | `long` | 配置成员 id / 行为树 id | `Direct` / `Text` / `None` |
| `GameObject` | `GameObject` | 路径 / 名字 / 资源路径 / 地址字符串 | `HierarchyPath`（**默认**）、`AssetName`、`AssetPath`、`AssetAddressable`、`None` |

> 框架能力项在生成的 MonoBehaviour 上是普通标量字段（`long` / `string`）：配置数据编辑器那套
> `[BehaviourTreeField]` / `[AudioField]` 等选择器属于配置面板，不套到生成组件上。
> 需要下拉选择器请在自己的 Inspector 扩展里做，不要手改生成文件。

- **两侧独立**：服务端默认 `None`——服务端通常不需要可视对象引用；需要什么就逐字段开。
- **换类型会复位两侧导出设置**：客户端按新类型给合法默认值（GameObject → `HierarchyPath`，
  其余 → `Direct`），服务端 `None`；复位后再自行调。
- **GO 引用必须选路径族**：`Direct`/`Text` 对 GameObject 是非法的（生成前就会拦下来），
  因为 `JsonUtility` 只会写成 `instanceID`，跨会话不稳定。

> **内定参数不在字段列表里**：`localPos`（相对坐标）、`worldPos`（绝对坐标）、
> `path`（相对关卡根的层级路径）、`localEuler`（本地欧拉角）由框架在导出时从
> `transform` + 关卡根现场算出，**用户不填、也不出现在类型定义里**，恒随 payload 导出（见 3.4）。
> 这四个名字是保留字段名，用户字段不能取，否则会产出重复 JSON key（生成期校验会拦下）。

**字段展示规则（对齐 Config Editor）**：字段行第 5 行 Foldout「只读 / 关联参数（显隐规则）」展开后：

| 项 | 作用 | 生成代码 |
| --- | --- | --- |
| 只读 | 组件 Inspector 中该字段不可编辑，仅影响展示、不影响导出 | `[ReadOnly]` |
| 关联参数 | 多条条件 AND，全部满足才在组件 Inspector 显示本字段；目标字段只能是本类型内 bool / enum 标量（不含自身与列表字段） | `[CfgVisibleWhen("字段", true/false)]` 或 `[CfgVisibleWhen("字段", "值1", "值2")]` |

条件结构直接复用 `ConfigVisibilityCondition`（与配置模板同一份）。bool 条件给勾选值，
enum 条件用 MaskField 多选枚举值（≤32 个）。无效条件（目标字段被删 / 改名 / 改成别的类型、
enum 值列表为空）在生成时跳过、求值时**视为满足**，字段保持显示——配置面板不会把字段吞掉。
规则由生成器发射成字段特性，`SceneContentMarkerBaseEditor` 反射读取后逐字段决定显示 / 禁用，
因此规则随生成物走，不依赖定义资产在运行期存在。
**边界**：本次只对齐「只读 + 显隐规则」；Config Editor 的检查规则（非空 / 非 0 / 范围）、
取成员、资源类型提示属于配置导出专用，Scene Content 用两侧 `clientExport / serverExport` 代替，未搬。

### 3.3 生成代码

点定义 Inspector 底部的 **生成代码**，或 Scene Content 窗口顶部的 **生成全部代码**（全量 + id 常量表）。产出两个文件：

| 产物 | 内容 |
| --- | --- |
| `Assets/HYC/Generated/SceneContent/<类名>.cs` | 标记组件：`SceneContentMarkerBase + ISceneContentMarker + ISceneContentMarkerPayload`，含字段声明（带 `[ReadOnly]` / `[CfgVisibleWhen]`）与按侧写 payload 的 `ToPayloadJson`（先写内定参数，再按侧写用户字段） |
| `Assets/HYC/Generated/SceneContent/SceneContentTypes.g.cs` | 全类型 id 常量表 `SceneContentTypes.<类名>` + `SceneContentTypeNames.Get(id)` |

命名空间由「命名空间 root」派生：`{root}.scene`（见 HYC 设置）。
生成物带「自动生成、勿手改」头，改字段请回类型定义 Inspector 改完再重新生成。

### 3.4 挂到关卡 prefab

**Add Component → HYC / Scene Content / \<类名\>**：生成器给每个标记组件都加了
`[AddComponentMenu("HYC/Scene Content/<类名>")]`，所以 Unity 原生的 Add Component 下拉里有
一整个 **Scene Content 组别，里面列出工程内全部已生成的类型**，点一下即挂组件。
这是给 GO 加内容脚本的标准路径，不需要额外菜单。
> 前提：**生成代码已编译完**。刚点完「生成代码」要等 Unity 编译结束，类型才会出现在这个组别里。
> 改过生成器后必须点「生成全部代码」重新生成，旧组件的组别名才会一起更新。

在 **Scene Content Window → 类型设计** 里还有三条快捷路径（都作用在左侧类型行上）：

| 操作 | 行为 |
| --- | --- |
| 把类型行拖到 Hierarchy 的 GO / Inspector 组件区 | 窗口把该类型**自动映射成它生成的 `MonoScript`** 再拖出去，Unity 内建地加组件；拖的不是类型定义资产 |
| 行右侧 **＋** | `Undo.AddComponent` 给当前选中的 GameObject 加组件，支持多选、可撤销 |
| 行上**右键** | 加到选中的 GameObject / 定位生成脚本 / 重新生成此类型代码 |

三条路径共用 `SceneContentComponentResolver`：反查 `{OutputDir}/<类名>.cs` →
`MonoImporter.GetScript()` → `MonoScript.GetClass()`。**未生成 / 未编译**时 ＋ 与右键项置灰并给出原因，
拖拽直接拒绝并打日志，不会拖出无效资产。Project 里选中的若是 Prefab 资产本体，行内 ＋ 会提示
先进 Prefab 编辑模式（`Undo.AddComponent` 对资产本体不生效）。加完组件后「内容总览」被标记为过期，
切到该页签时自动重扫一次。

**第四条路径：Hierarchy 右键 / 顶部 GameObject 菜单 → `Scene Content/Add Component...`**。
`[MenuItem]` 是静态特性，工程里动态生成的类型没法注册成静态子项，所以点开这个菜单项时用
`GenericMenu` **现场列出全部 `SceneContentTypeDef`**：已编译的可点，未生成 / 未编译的置灰并附原因，
第一项标明当前目标。有选中 GO 就给全部选中 GO 加组件；Hierarchy 空白处右键（没选中 GO）则
**新建一个以类型显示名命名的 GameObject 并挂载**，整步可撤销（`Undo.RegisterCreatedObjectUndo`）。
**Scene Content Window 工具栏「给 GO 加组件」**按钮调出的是同一份菜单
（`SceneContentHierarchyMenu.BuildMenu()`），两条入口共用一套判定。

> `GenericMenu.ShowAsContext()` 从静态 `[MenuItem]` 回调里弹出依赖 Unity 菜单命令的 GUI 事件上下文，
> 官方包里没有这种用法，需进 Unity 实测。若右键没反应，用工具栏那个按钮（它在窗口 `OnGUI`
> 事件里，一定有事件上下文）；两条入口的菜单内容与判定完全相同。

把组件挂到出生点 / 开关等 GO 上，摆好位置（`localPosition/rotation/scale` 就是导出坐标）。

**导出时自动生成的内定参数**（无需填写、只读、随 payload 恒导出）：

| key | 含义 | 取值 |
| --- | --- | --- |
| `localPos` | 相对坐标 | `transform.localPosition` 的 `[x, y, z]` |
| `worldPos` | 绝对坐标 | `transform.position` 的 `[x, y, z]` |
| `path` | 层级路径 | `SceneContentRefPath.GetPath(levelRoot, transform)`，如 `Gate/Door#1` |
| `localEuler` | 本地欧拉角 | `transform.localEulerAngles` 的 `[x, y, z]` |

`levelRoot` 在导出时由导出器现场取（Prefab 走 `LoadAssetAtPath<GameObject>()` 的根、
Scene 走临时 Additive 加载后的根 GO），**不再有 `SceneContentAuthoring.GetMarkerEntries` 这种聚合入口**。
这批 key 由生成器在 `ToPayloadJson` 里**先于用户字段写入**，客户端 / 服务端两侧都有。

**生成组件的 Inspector：公共基类 + 统一 CustomEditor**（Unity 惯用做法）：

- `Runtime/SceneContent/SceneContentMarkerBase.cs` 是全部生成标记组件的公共基类（抽象 `MonoBehaviour`），
  提供 `PreviewLevelRoot`（取 `transform.root`——导出器现场算关卡根，不再依赖任何关卡描述器组件）。
- 生成器吐出的类改成 `: SceneContentMarkerBase, ISceneContentMarker, ISceneContentMarkerPayload`；
  `Editor/SceneContent/SceneContentMarkerBaseEditor.cs` 用
  `[CustomEditor(typeof(SceneContentMarkerBase), true)]` 一次性接管**全部派生类**。
- 面板定位是「只读信息 + 跳回类型设计」，**不在组件上就地编辑类型 / 派生内定参数**：
  - 顶部：原生每实例字段区（`DrawInstanceFields` 逐字段自绘，消费 `[ReadOnly]` / `[CfgVisibleWhen]`）；
    内定参数（localPos / worldPos / path / localEuler）按 `BuiltInParameterNames` **在可编辑区跳过**，
    只出现在下方只读区块，**不重复、不可编辑**。
  - 头部信息卡：组件名 + 「由 Scene Content 生成器生成 · 请勿手改」+ 一条分隔线。
  - 单一主操作按钮「**在 Scene Content 编辑器中打开**」走 `SceneContentWindow.OpenAndSelect`——
    回到窗口并选中该类型定义，真正的改类型 / 字段规则都在窗口里做。
  - 轻量元信息：类型定义名、内容 id；仅当 id 不同步 / 定义缺失时才弹一条警告。
  - 可折叠区：`字段定义（N）`（只读列出字段名 / 类型 / 两侧导出方式）、
    `内定参数`（默认折叠，`BuiltInParameterNames` 4 个 key + 中文含义 + 只读当前值）、payload 预览。
- 设计取舍：用户每实例字段（如 `Type: Player`）**仍可在原生区填写**——目前只有组件 Inspector
  这一处能填每实例值（窗口「内容总览」为只读，无逐实例编辑 UI）。若后续要「全部只读、编辑收归窗口」，
  需在窗口加逐实例编辑面板再锁这里；派生的内定参数则**恒不在此编辑**。
- 定义按内容 id 反查并缓存；id 对不上但类名对得上时给「生成代码与定义不同步，请重新生成」警告。

> 为什么是基类 + `editorForChildClasses = true`：拿 `MonoBehaviour` 当 targetType 会接管全工程组件，
> 逐类生成 Editor 又会多出一堆文件；指向自家基类则只覆盖框架生成的组件。
> Unity 官方同类做法：Timeline `MarkerInspector`、Cinemachine `CinemachineComponentBaseEditor`。
> ⚠️ **改的是生成代码**：已生成的 `<类名>.cs` 必须点「生成全部代码」重新生成，否则仍是旧的
> `: MonoBehaviour`，不会用上统一 Inspector。
> 内定参数清单的唯一真源是 `SceneContentTypeCodeGen.BuiltInParameterNames`（生成期保留名校验也读它）；
> 真正导出时 `levelRoot` 由导出器现场取（Prefab 资产根 / Scene 临时加载根），不再有 Authoring 聚合入口。

> 「扫描 prefab / 反填字段」已移除：那是给旧手工组件做迁移用的，新功能从零设计、
> 没有旧组件要迁移。若想查看「哪些 prefab / 场景上挂了哪些脚本、值是多少」，
> 用 **内容总览** 页签（3.6）——它是只读发现，不改写任何定义。

### 3.5 导出

导出器**直接对关卡资产（Prefab 或 Scene）调用**，Blob 以资产 `.meta` 的 GUID 命名，
**不需要在关卡根挂任何描述器组件、不需要手填 levelId、也不用关卡名**；
运行期身份由业务 key 经 3.5.1 的映射清单解析，无需接入方持有 GUID。

在 Project 里选中关卡 **Prefab 资产**或 **Scene 资产**，用以下任一入口导出：

- 菜单 **HYC Framework/Scene Content/Export Blob (Selected)**（pri 72）；
- Scene Content Window 工具栏 **全量导出**（扫全部含标记组件的 Prefab/Scene 资产）或
  **选中导出**（需先在 Project 选中目标资产）；
- 程序化：`SceneContentExporter.Export(Object levelAsset, string alias = null)`
  （Prefab `GameObject` 或 `SceneAsset` 均可；`alias` 可选，见 3.5.1）。

导出器取 `AssetDatabase.GetAssetPath` → `AssetPathToGUID` 得到关卡 GUID，对资产内 GO 扫描
`GetComponentsInChildren<ISceneContentMarker>(true)`，两侧产物分别落：

- 客户端：`StreamingAssets/SceneContent/<guid>.blob`
- 服务端：`StreamingAssets/SceneContent/server/<guid>.blob`

纯单机工程可在 `SceneContentExporter` 侧关掉服务端产物（默认两都写）。
导出后做 TryRead 往返自校验：读回 Blob 内 `LevelGuid` 与资产 GUID 比对，不一致报 Error。
Scene 资产不可挂运行时组件，导出时以 Additive 方式临时加载取根 GO，扫完即关，不影响当前场景。

#### 3.5.1 业务 key 与映射清单（运行期身份）

**问题**：Blob 以资产 GUID 命名，但 GUID 只在编辑器可得（`AssetDatabase` 属于 `UnityEditor`，
打包后不存在，WebGL 尤甚）；程序化重建关卡（先 `DeleteAsset` 再 `SaveAsPrefabAsset`）还会换 GUID。
而关卡加载器运行期手里只有**业务身份**——配置 id、`LevelConfig.MapAddress`（Addressables 地址）、资产名。

框架不要求接入方手抄 GUID，而是在**导出期**把两者绑起来：每次 `Export` 成功后
upsert 一条到 `StreamingAssets/SceneContent/index.json`：

```json
{
  "version": 1,
  "entries": [
    { "key": "Level/001", "guid": "1c8dd791...", "assetPath": "Assets/Prefabs/Levels/Level_001.prefab" }
  ]
}
```

| 项 | 规则 |
| --- | --- |
| key 优先级 | `Export(asset, alias)` 的显式 alias > 该资产的 **Addressables 地址** > **资产名**（不含扩展名） |
| 未显式传 alias | `Export(Object)` / `ExportAll()` 自动推断，源码兼容旧调用 |
| 同一 key | 后导出者覆盖（关卡重建换 GUID 后**重新导出即自愈**，无需改任何代码） |
| 不同 key → 同一 guid | 允许共存（一个关卡可有多个业务别名），共用同一份 blob |
| key 撞车 | 不同资产同名（不同目录）用资产名兜底时会撞 key，框架打 Warning 提示改用显式 alias |
| 未命中 | `LoadByKey` 报 Error 并**列出清单里所有可用 key**；清单缺失/为空/解析失败都有独立报错 |

运行期用 `SceneContentLoader.LoadByKey(key, spawner[, side])`：走 `StreamingAssets.ReadText`
读清单 → 查 key 换成 guid → 复用 `Load`。**`Load(guid, …)` 保留为底层通道**，仅当手里确实已有 GUID 时用。

- **WebGL / Android 无需额外接线**：`index.json` 是纯磁盘文件，构建期
  `StreamingAssetsManifestBuilder` 递归枚举 StreamingAssets（排除 `aa/`、`.meta`）把它写进
  `hyc_streaming_manifest.txt`，随 `FrameworkBootstrap.InitializeAsync` 的 `PreloadAsync()` 预取。
  ⚠️ 不要用 `File.ReadAllText` 读它——那两个平台读不到。
- **不改动 Blob**：`SceneContentBlob.BlobVersion` 仍为 3、`LevelGuid` 字段与 WriteSide 往返自校验都不动。
  存量已导出的 blob **零重导**；只是新增一个映射文件。
- **孤儿文件**：关卡重建换 GUID 后，旧 `<oldGuid>.blob` 会留在磁盘（清单已指向新 GUID，不影响正确性），
  但需要时手动清理 `StreamingAssets/SceneContent/` 下的历史 blob，避免被构建清单收录进包。
- 清单格式版本 `SceneContentManifest.ManifestVersion`（当前 1），结构变更时 +1。

### 3.6 编辑器窗口

菜单 `HYC Framework/Scene Content/`（priority 70–73）：

- **Scene Content Window**（pri 70）= **类型设计器 + 脚本生成器 + 只读内容总览**。
  窗口骨架**照抄**数据配置窗口 `ConfigDataWindow` 的 `OnGUI` 坐标系：工具条 `0..22`、搜索栏 `22..48`、
  左侧 `SceneContentTreeView`（Unity `TreeView`）`48..底`、右侧内容区占满剩余。
  左树与 `ConfigDataTree` 同一套行样式：`showAlternatingRowBackgrounds = true` ⇒ **隔行深浅底色
  铺满整块 rect，连没有数据的空行也铺**；`rowHeight = singleLineHeight + 6`、文件夹 / 资产图标、
  垂直居中标签、选中高亮；顶部有搜索框，右侧空态是居中灰字。**左右有明确分界线**（竖线 +
  两条横线），分隔条可拖动调宽。两个内层页签收在工具条左端，动作按钮集中在右端：
  `新建类型 / 生成全部代码 / 全量导出 / 给 GO 加组件 / 刷新`（总览页为「刷新扫描」）。
  - **类型设计**：左树**按显示名里的「/」拆成分类层级**列出全部 `SceneContentTypeDef`（对齐数据配置的资源树，类型定义即「类型资源」），
    例如 `场景内容/出生点` → `场景内容`（组）→ `出生点`（叶子）；没有分类段的归入「（无分类）」组；
    **未生成代码的类型在叶子末尾加「 (未生成)」标**（不再有顶层「已生成 / 未生成」两个分组）。
    右侧内嵌该类型的自定义 Inspector（`IsEmbedded = true`），**字段就在本窗口内设计**；
    同一编辑器在资产原生 Inspector 里主体留空，避免两处重复。
    每行可拖到 GO 加组件、行内 ＋ 给选中 GO 加组件、右键出操作菜单（见 3.4）；
    动作：保存 / 生成代码 / 校验。搜索框按显示名 / 类名过滤。
  - **内容总览（只读）**：点「刷新扫描」自动扫描工程内全部场景与单独 Prefab，
    左树按 `场景 / 单独 Prefab → 场景或资源 → 挂了内容脚本的 GO` 组织。
    点 GO，右侧显示 **用户填写值**、**内定参数（只读 · 自动导出）**、
    **客户端 payload 预览**（直接调 `ToPayloadJson`，可核对最终导出内容），
    以及「在 Project 中定位」。**只发现、不改写**：不修改定义资产、不写入任何字段。
    未打开的场景会临时以 Additive 方式加载、扫完即关，用户当前场景不受影响。
- **Export All Blobs**（pri 71）/ **Export Blob (Selected)**（pri 72）：批量 / 选中导出。
  选中导出需在 Project 里先选中一个关卡 Prefab 或 Scene 资产（见 3.5）。
- **New Scene Content Type**（pri 73）：新建类型定义资产。

---

## 四、运行期接入（游戏侧）

场景加载完成后（无论用 Unity SceneManager 还是 Addressables），调一次。
用关卡加载器**本来就持有**的业务 key（`LevelConfig.MapAddress` / 配置 id / 资产名），
框架查 `SceneContent/index.json` 换成资产 GUID 再加载（见 3.5.1）：

```csharp
// 游戏自己的关卡加载完成后（第三个参数默认 Client）
// levelKey 与导出时登记的 alias / Addressables 地址 / 资产名一致
SceneContentLoader.LoadByKey(levelKey, mySpawner);
// 专用服务器进程读另一份产物：
SceneContentLoader.LoadByKey(levelKey, serverSpawner, SceneContentSide.Server);
```

> **不要手抄资产 GUID**：`AssetDatabase` 打包后不存在，运行期也没有
> `gameObject → 资产 GUID` 的 API；程序化重建关卡还会换 GUID。
> 只有确实已持有 GUID 时，才用底层通道 `SceneContentLoader.Load(guid, spawner[, side])`。
> `LoadByKey` 查不到 key 会报 Error 并列出清单里所有可用 key。

`mySpawner` 实现 `ISceneContentSpawner`，按 `contentTypeId` 决定生成什么：

```csharp
public class MySpawner : ISceneContentSpawner
{
    public void Spawn(int contentTypeId, float3 pos, quaternion rot, float3 scale, NativeArray<byte> payload)
    {
        // 按 id 解析到 prefab / entity，从 payload（UTF8 JSON）读参数
        var json = System.Text.Encoding.UTF8.GetString(payload.ToArray());
        // ... 生成 GameObject 或 EntityManager.Instantiate(...) ...
    }
}
```

按 id 分派时优先用生成的常量表，别写魔法数字：

```csharp
switch (contentTypeId)
{
    case SceneContentTypes.MonsterSpawn: /* ... */ break;
}
```

payload 里的 GO 引用是层级路径，运行期这样反解：

```csharp
var door = SceneContentRefPath.Resolve(levelRoot, json.doorPath);
```

编辑器的 prefab→id 解析：**用生成组件时无需注册**（内置兜底读组件自己的 id）。
只有「条目是纯 prefab、上面没有标记组件」的旧用法才需要自定义解析器：

```csharp
SceneContentResolvers.Register(myResolver); // int GetContentTypeId(GameObject prefab)
```

---

## 五、关键约束

- **blittable**：Blob 内只用 `int / float（扁平 PosX..RotW..ScaleZ）/ BlobArray<byte>`，prefab 引用收成 int id。
  坐标刻意不用 `float3`/`quaternion`：导出器在 Editor 程序集（asmdef 无 Unity.Mathematics 引用），
  碰 math 字段会 CS0012 / EA0003；运行期由 `SceneContentLoader` 重组为 math 类型再交给 spawner。
- **Blob 落点**：`Assets/StreamingAssets/SceneContent/`（服务端加 `server/` 子目录），
  运行期只能从 StreamingAssets 读取（跨端一致）。
- **映射清单不是 Blob**：`SceneContent/index.json` 是纯文本 JSON，由导出器 upsert 维护，
  运行期必须走 `StreamingAssets.ReadText`（`LoadByKey` 已封装）——**禁止 `File.ReadAllText`**，
  WebGL / Android 读不到物理文件。构建期由 `StreamingAssetsManifestBuilder` 递归收录进预取清单，
  无需改 `FrameworkBootstrap`。清单结构变更抬 `SceneContentManifest.ManifestVersion`，
  **不要**为此改 `SceneContentBlob.BlobVersion`（那是 Blob 二进制格式版本，两者独立）。
- **运行期身份**：用业务 key（`LoadByKey`），不要手抄资产 GUID。程序化重建关卡换 GUID 后
  **重新导出即自愈**，但旧 `<oldGuid>.blob` 会成为孤儿文件，需要时手动清理。
- **版本**：`SceneContentBlob.BlobVersion`；改条目结构时 +1，运行期 `TryRead` 版本不符会拒绝。
- **payload 生成方式**：
  - 生成的标记组件 / 实现了 `ISceneContentMarkerPayload` 的组件 → 按侧产出 JSON
    （字段可裁剪、GO 引用可写成层级路径）；
  - 都没实现 → 退回 `JsonUtility.ToJson(component)`，此时**只能放标量**：
    那个 API 会把 `GameObject`/`Transform` 写成 `instanceID`，跨会话不稳定。
- **内定参数恒定导出**：`localPos` / `worldPos` / `path` / `localEuler` 由生成器在
  `ToPayloadJson` 里从 `transform` + 关卡根现场算出，**不属于 `SceneContentTypeDef.fields`**；
  四个名字是保留字段名（生成期校验拒绝用户字段撞名，避免重复 JSON key）。
  改了生成器内容后，**已生成的 `<类名>.cs` 必须点「生成全部代码」重新生成**才生效。
- **加组件依赖「已编译」**：Add Component 分组、窗口拖拽、行内 ＋、右键加组件走的都是
  「生成脚本 → Unity 编译 → `MonoScript.GetClass()` 非空」这条链。生成代码在宿主工程里、
  包内编译不到，所以未编译前一律置灰 / 拒绝，不能靠反射类型定义资产硬加。
  `[AddComponentMenu]` 写在生成代码里，**改了分组名必须重新生成全部类型代码**。
- **字段类型与配置模板共用一套枚举**：`SceneContentFieldDef.type` 是 `ConfigFieldType`
  （原先独立的 `SceneContentFieldType` 已删除）。这是**资产序列化层面的 Breaking**：
  早期手工建过的类型定义资产需重新选一次字段类型；本功能为新建，包内无存量资产。
  菜单展示最全清单，`SupportedTypes` 决定哪些能真正生成；新增支持项要同时改
  `SupportedTypes`、`FieldTypeName`、`AppendDirectExpression` 三处（`Validate` 会拦未支持项）。
- **内容类型 id**：类名的 FNV-1a 32 位哈希（`SceneContentTypeId.Compute`）。
  **改类名 = 换 id**，已导出的 blob 必须重导；不同定义撞 id 会在生成期被拦下。

---

## 六、示例

`Samples~/HYCSimple/Scene/` 提供最小示例：`HYCSimpleSpawnPoint`（标记组件示例）、
`HYCSimpleSceneContentSpawner`（spawner 示例）、`HYCSimpleSceneContentDemo`（注册 + 加载示例）。
