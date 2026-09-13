# 50 · 过场（Cutscene）

## 职责

配置驱动的过场/时间线播放队列。**框架只提供骨架，真正的播放要宿主实现。**

命名空间：**`HYC.Framework.Timeline`**

一句话：**一个 FIFO 队列 + 一个 `NotifyFinished()` 回调契约。**

---

## 核心类型

### `Cutscene`（sealed class）

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `Id` | `long` | 过场 ID（通常来自配置表） |
| `Name` | `string` | 显示名，`Play(id)` 默认生成 `"cut_" + id` |
| `AssetKey` | `string` | **Addressable key**（TimelineAsset 或预制体） |
| `Loop` | `bool` | 是否循环 |
| `OnFinished` | `Action` | 单个过场完成回调 |

### `CutsceneDirector`

| 成员 | 说明 |
| --- | --- |
| `IsPlaying` | 是否正在播 |
| `Current` | 当前过场（播完为 null） |
| `CutsceneStarted` | `event Action<Cutscene>` |
| `CutsceneFinished` | `event Action<Cutscene>` |
| `Play(long id)` | virtual，入队 |
| `Enqueue(Cutscene)` | protected，入队 + 若空闲立刻开播 |
| `PlayClip(Cutscene)` | **protected virtual，宿主必须重写** |
| `NotifyFinished()` | 播完调用 |
| `Skip()` | 跳过当前（等同 `NotifyFinished`） |
| `Clear()` | 清空队列与当前 |

---

## 流程

```
Play(id)
  └─ Enqueue(new Cutscene{ Id = id, Name = "cut_" + id })
       └─ if (!IsPlaying) StartNext()
            ├─ Current = _queue.Dequeue()
            ├─ CutsceneStarted?.Invoke(Current)
            └─ PlayClip(Current)          ← 宿主在这里真正播 Timeline
                     ↓ （PlayableDirector 的 stopped 回调）
                NotifyFinished()
                     ├─ Current.OnFinished?.Invoke()
                     ├─ Current = null; IsPlaying = false
                     ├─ CutsceneFinished?.Invoke(done)
                     └─ if (_queue.Count > 0) StartNext()
```

---

## 用法

```csharp
public class MyCutsceneDirector : CutsceneDirector
{
    private PlayableDirector _director;

    // 1. 用配置表解析 id → AssetKey
    public override void Play(long id)
    {
        if (ConfigManager.TryGet<CutsceneRow>(id, out var row))
        {
            Enqueue(new Cutscene
            {
                Id = id,
                Name = row.Name,
                AssetKey = row.AssetKey,
                Loop = row.Loop,
                OnFinished = () => Debug.Log($"cut {id} done"),
            });
        }
    }

    // 2. 真正播放
    protected override void PlayClip(Cutscene clip)
    {
        Addressables.LoadAssetAsync<PlayableDirector>(clip.AssetKey).Completed += h =>
        {
            _director = h.Result;
            _director.stopped += _ => NotifyFinished();   // ← 关键
            _director.Play();
        };
    }
}
```

```csharp
var director = new MyCutsceneDirector();
director.CutsceneStarted  += c => UIManager.HideAllUI();
director.CutsceneFinished += c => UIManager.ShowAllUI();

director.Play(1001);
director.Play(1002);      // 排队，1001 播完自动接上
director.Skip();          // 跳过当前，直接进下一个
director.Clear();         // 全部取消
```

---

## 注意事项 / 坑

1. **`PlayClip` 的默认实现只是 `Debug.Log("CutsceneDirector playing: " + clip.Name)`**——不重写就永远不会有 `NotifyFinished`，队列会卡在第一条（`IsPlaying` 恒为 true）。
2. **`Loop` 字段框架完全不消费**：循环过场会永远不调 `NotifyFinished`，后面的队列全被堵住。要循环就自己控制何时 `NotifyFinished`。
3. **`Play(long id)` 默认只填 `Id` 和 `Name`**，`AssetKey` 是空的——必须重写它去查配置表，或者直接用 `Enqueue`（protected，子类可见）。
4. **`Skip()` 只在 `IsPlaying` 时有效**，空闲时调用是空操作。
5. **`NotifyFinished` 在 `Current == null` 时直接 return**——重复调用安全。
6. **`Clear()` 不会触发 `CutsceneFinished`**，也不会调 `Current.OnFinished`——是"硬清"，需要收尾就先 `Skip()` 再 `Clear()`。
7. **没有暂停/恢复**，没有优先级，没有打断策略——需要的话自己在子类里加（比如重写 `Enqueue` 做插队）。
8. **不是 ECS System**：`CutsceneDirector` 是普通 class，需要宿主自己持有并驱动；它也没有订阅任何 `UpdateGroup`。
