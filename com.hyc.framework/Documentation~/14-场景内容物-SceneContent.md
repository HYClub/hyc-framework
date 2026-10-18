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
- **`ISceneContentMarker`**：任何标记组件实现它即被自动发现；
  `ContentTypeId` 是游戏定义的**稳定 int**（prefab/配置引用不能进 Blob，一律收成 id）。
- **`ISceneContentSpawner`**：运行期生成实例的注入点。框架只管读 Blob + 解析，
  具体生成 GameObject 还是 Entity 由游戏决定。
- **`ISceneContentPrefabResolver`**（编辑器）：把实例条目的 prefab 引用解析成 int id 写进 Blob。

### 数据流

```
关卡 prefab (SceneContentAuthoring)
├─ A. 实例条目 List<SceneContentEntry>  ─┐
└─ B. 标记组件 ISceneContentMarker     ─┤→ 合并 → SceneContentBlob → StreamingAssets/SceneContent/<levelId>.blob
                                        │      (运行期) SceneContentLoader.Load(levelId, spawner) → spawner.Spawn(...)
```

---

## 三、编辑器（菜单 `HYC Framework/Scene Content/`，priority 70–72）

- **Scene Content Window**：选取一个 `SceneContentAuthoring`，内嵌其 Inspector；
  「列出工程内全部」快速选择。
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
// 游戏自己的关卡加载完成后
SceneContentLoader.Load(levelId, mySpawner);
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

编辑器导出前，游戏需注册 prefab→id 解析器（一次性）：

```csharp
SceneContentResolvers.Register(myResolver); // int GetContentTypeId(GameObject prefab)
```

---

## 五、关键约束

- **blittable**：Blob 内只用 `int / float3 / quaternion / BlobArray<byte>`，prefab 引用收成 int id。
- **Blob 落点**：`Assets/StreamingAssets/SceneContent/`，运行期只能从 StreamingAssets 读取（跨端一致）。
- **版本**：`SceneContentBlob.BlobVersion`；改条目结构时 +1，运行期 `TryRead` 版本不符会拒绝。
- **标记组件 payload**：`JsonUtility.ToJson(component)` 序列化其字段，运行期按 id 反序列化；
  标记组件内不要放 Unity 对象引用（不进 Blob）。

---

## 六、示例

`Samples~/HYCSimple/Scene/` 提供最小示例：`HYCSimpleSpawnPoint`（标记组件示例）、
`HYCSimpleSceneContentSpawner`（spawner 示例）、`HYCSimpleSceneContentDemo`（注册 + 加载示例）。
