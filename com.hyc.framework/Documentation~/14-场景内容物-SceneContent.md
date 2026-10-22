# 14 · 场景内容物（Scene Content）

> 一套**框架级通用能力**，让「关卡里有什么」成为可编辑、可导出、可加载的数据，
> 而不是散落在 prefab 里的隐式引用。与 Config / BT / Attr 平级，不绑定任何具体玩法。

---

## 一、它解决什么

做关卡时通常有两类内容：

| 来源 | 例子 | 形式 |
| --- | --- | --- |
| **A. 显式实例条目** | 道具箱、装饰物、可复用 prefab | 拖 prefab + 摆 transform + 填参数 |
| **B. 标记组件** | 角色出生点、怪物出生点、触发区 | 直接挂在关卡 prefab 上的 MonoBehaviour |

两者都应能：在编辑器里**列出来看**（几个出生点、什么类型）、**导出成 Blob**、
**运行期在场景加载后加载并生成**。本模块把这两类合并成一份 Blob，用同一套加载。

---

## 二、核心概念

- **`SceneContentAuthoring`**（MonoBehaviour，挂关卡 prefab 根）= 接入 HYC 的「钩子」，同时是
  A（实例条目列表）与 B（标记组件）的聚合容器。
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
关卡 prefab (SceneContentAuthoring)
├─ A. 实例条目 List<SceneContentEntry>  ─┐
└─ B. 标记组件 ISceneContentMarker     ─┤→ 合并 → SceneContentBlob
                                        │      StreamingAssets/SceneContent/<levelId>.blob            (客户端)
                                        │      StreamingAssets/SceneContent/server/<levelId>.blob     (服务端)
                                        │      (运行期) SceneContentLoader.Load(levelId, spawner, side) → spawner.Spawn(...)
```

---

## 三、策划工作流（类型定义 → 生成组件 → 挂点 → 导出）

### 3.1 新建「场景内容类型」

菜单 **HYC Framework/Scene Content/New Scene Content Type** →
生成 `Assets/HYC/SceneContentTypes/NewSceneContent.asset` 并自动选中。
之后在它的 **Inspector** 里配（见 3.2）。

> **为什么不在配置数据编辑器里**：`SceneContentTypeDef` 不是配置数据——没有 ID/GUID、
> 不参与发号、不参与配置导出。它**不标 `[CfgAsset]`**，所以既不出现在「配置创建器」窗口，
> 也不出现在配置数据树。配置创建器只服务「带 ID/GUID、要发号」的配置资产
> （它必走 `ConfigIdService.TryWriteId`，没有 ID/GUID 字段的类型会被弹窗拒绝）。
> 它的定位更像「代码模板/生成源」，入口放本模块菜单，编辑走自定义 Inspector。

### 3.2 配字段

选中刚建的类型定义，右侧面板：显示名、类名、**字段列表**、**扫描 prefab**。

每个字段配四项：字段名 / 类型 / 是否列表 / 两侧（客户端、服务端）导出方式。

| 字段类型 | 可用的导出方式 |
| --- | --- |
| `Int` / `Float` / `Bool` / `String` | `Direct`（原样）、`Text`（转字符串）、`None` |
| `Enum`（引用数据编辑器的枚举定义） | `Direct`（写成 int）、`Text`（写成名字）、`None` |
| `GameObject`（关卡内的门、挂点…） | `HierarchyPath`（**默认**，相对关卡根的层级路径）、`AssetName`、`AssetPath`、`AssetAddressable`、`None` |

- **两侧独立**：服务端默认 `None`——服务端通常不需要可视对象引用；需要什么就逐字段开。
- **GO 引用必须选路径族**：`Direct`/`Text` 对 GameObject 是非法的（生成前就会拦下来），
  因为 `JsonUtility` 只会写成 `instanceID`，跨会话不稳定。

### 3.3 生成代码

点定义 Inspector 底部的 **生成代码**，或 Scene Content 窗口顶部的 **生成全部类型代码**（全量 + id 常量表）。产出两个文件：

| 产物 | 内容 |
| --- | --- |
| `Assets/HYC/Generated/SceneContent/<类名>.cs` | 标记组件：`MonoBehaviour + ISceneContentMarker + ISceneContentMarkerPayload`，含字段声明与按侧写 payload 的 `ToPayloadJson` |
| `Assets/HYC/Generated/SceneContent/SceneContentTypes.g.cs` | 全类型 id 常量表 `SceneContentTypes.<类名>` + `SceneContentTypeNames.Get(id)` |

命名空间由「命名空间 root」派生：`{root}.scene`（见 HYC 设置）。
生成物带「自动生成、勿手改」头，改字段请回类型定义 Inspector 改完再重新生成。

### 3.4 挂到关卡 prefab

组件在 **Add Component → HYC/SceneContent/<类名>**。把它挂到出生点/开关等 GO 上，
摆好位置（`localPosition/rotation/scale` 就是导出坐标）。

想把已有手工组件的字段直接搬进定义：把关卡 prefab 拖到定义的 **扫描 prefab** 上 →
**扫描** → 面板列出该 prefab 上所有标记组件（`★` 是本类型）→
**按本类型的组件反填字段**。

### 3.5 导出

关卡根挂 `SceneContentAuthoring`（`levelId` 唯一）→ 工具栏 **导出 Blob**（或菜单 Export All）。
两侧产物分别落 `StreamingAssets/SceneContent/<levelId>.blob` 与 `…/server/<levelId>.blob`；
纯单机工程可关掉 `exportServer`。

### 3.6 编辑器窗口

菜单 `HYC Framework/Scene Content/`（priority 70–73）：

- **Scene Content Window**：顶部常驻 **生成全部类型代码** / **导出全部 Blob**；
  下方选取一个 `SceneContentAuthoring`，内嵌其 Inspector；「列出工程内全部」快速选择。
- **自定义 Inspector**：编辑 A 的实例条目列表（ReorderableList）+ **B 标记组件内省面板**
  （按「类型名 #id」分组显示计数与实例坐标——即「列出来」）+ 工具栏按钮：
  - **刷新到场景**：把 A 的实例条目实例化进当前场景预览（仅场景内 prefab 实例生效，避免改脏 prefab 资产）。
  - **采集自场景**：把带 `SceneContentInstance` 标记的 GO 读回 A 的实例条目（默认覆盖，可追加）。
  - **导出 Blob**：写 `StreamingAssets/SceneContent/<levelId>.blob` 并做 TryRead 往返自校验。
- **Export All Blobs** / **Export Blob (Selected)**：批量 / 选中导出。

---

## 四、运行期接入（游戏侧）

场景加载完成后（无论用 Unity SceneManager 还是 Addressables），调一次：

```csharp
// 游戏自己的关卡加载完成后（第三个参数默认 Client）
SceneContentLoader.Load(levelId, mySpawner);
// 专用服务器进程读另一份产物：
SceneContentLoader.Load(levelId, serverSpawner, SceneContentSide.Server);
```

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
- **版本**：`SceneContentBlob.BlobVersion`；改条目结构时 +1，运行期 `TryRead` 版本不符会拒绝。
- **payload 生成方式**：
  - 生成的标记组件 / 实现了 `ISceneContentMarkerPayload` 的组件 → 按侧产出 JSON
    （字段可裁剪、GO 引用可写成层级路径）；
  - 都没实现 → 退回 `JsonUtility.ToJson(component)`，此时**只能放标量**：
    那个 API 会把 `GameObject`/`Transform` 写成 `instanceID`，跨会话不稳定。
- **内容类型 id**：类名的 FNV-1a 32 位哈希（`SceneContentTypeId.Compute`）。
  **改类名 = 换 id**，已导出的 blob 必须重导；不同定义撞 id 会在生成期被拦下。
- **A 类手写条目**只有一个 `payloadJson`，客户端/服务端共用——手写内容不受字段级导出设置管辖。

---

## 六、示例

`Samples~/HYCSimple/Scene/` 提供最小示例：`HYCSimpleSpawnPoint`（标记组件示例）、
`HYCSimpleSceneContentSpawner`（spawner 示例）、`HYCSimpleSceneContentDemo`（注册 + 加载示例）。
