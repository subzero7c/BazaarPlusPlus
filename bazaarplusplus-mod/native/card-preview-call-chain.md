# 卡牌预览调用链文档

本文档描述 BazaarPlusPlus 中原生卡牌预览（`CardPreviewBase`）的完整创建、显示、回收流程，
涵盖图鉴（CollectionPanel）和十胜阵容预览（ItemBoardPreviewSurface）两条路径。

---

## 一、关键游戏类型（均通过 Krafs.Publicizer 公开）

| 游戏类 | 说明 |
|---|---|
| `AssetLoader` | 通过 `Services.Get<AssetLoader>()` 获取，负责异步加载/实例化 UI 卡牌 |
| `CardPreviewBase` | 卡牌预览 UI 组件基类（`CardPreviewItem` / `CardPreviewSkill` 继承它） |
| `CardPreviewBase.SetUp(TCardBase, bool, TCardInstance, CancellationToken)` | 异步方法：绑定数据、加载 frame/art、最后调用 `Show(false)` |
| `CardPreviewBase.Show(bool)` | 激活/隐藏 `_cardImage` 和 `_frameContainer`，**SetUp 完成后默认是隐藏状态** |
| `CardPreviewBase.Resize()` | 调整内部 `_cardRect` 大小 |
| `AssetLoader.InstantiateUICardAsync(TCardInstance, Transform?, CancellationToken)` | 内部：实例化 prefab → `SetUp` → `SetParent`，返回 `GameObject` |

> **注意**：`SetUp` 第 4 个参数 `CancellationToken` 是后来的游戏更新中加入的。
> `NativeCardPreviewRuntime.InvokeSetUpSafe` 通过反射在运行时检测参数数量来兼容新旧两个版本。

---

## 二、BPP 侧的层次结构

```
NativeCardPreviewPrefabResolver   ← 最底层：调用 InstantiateUICardAsync，返回 CardPreviewBase Component
        ↓
NativeCardPreviewPool / CollectionCardPool   ← 池管理：Take(创建或复用) / Return(回收)
        ↓
NativeCardPreviewFactory / CollectionCardFactory   ← 工厂：决定是否需要二次 SetUp
        ↓
ItemBoardPreviewSurface / CollectionGridVirtualizer   ← 上层调用方：负责显示、定位、回收
```

---

## 三、图鉴路径（CollectionPanel）

### 3.1 触发

`CollectionGridVirtualizer.Tick()` 每帧检查可见窗口，对新进入窗口的 index 调用 `TryRealize(index)`。

### 3.2 完整调用链

```
CollectionGridVirtualizer.TryRealize(index)
  → _ = TryRealizeAsync(index, generation)          // fire-and-forget

TryRealizeAsync
  → CollectionCardFactory.TryBindAsync(vm)
      → CollectionCardPool.TakeAsync(kind, instance, _parent)
          ├─ [pool hit]  card = queue.Dequeue()
          │              card.transform.SetParent(parent)
          │              card.SetActive(true)
          │              Resize()
          │              return (card, isNew=false)
          │
          └─ [pool miss] NativeCardPreviewPrefabResolver.TryCreateCardAsync(instance, parent)
                           → AssetLoader.InstantiateUICardAsync(instance, parent, token)
                               // 内部：Instantiate prefab → SetUp(绑定数据+LoadArt+Show(false)) → SetParent
                           → go.GetComponent<CardPreviewBase>()
                         card.AddComponent<CollectionPanelOwnedMarker>()   // 门控 LoadArt patch
                         card.AddComponent<CanvasGroup>()                  // fade-in 用
                         card.SetActive(true)
                         canvasGroup.alpha = 0f                            // 初始透明，等 TickFades ramp
                         Resize()
                         return (card, isNew=true)

      ← (card, isNew)
      isNew=true  → setUpTask = Task.CompletedTask   // InstantiateUICardAsync 内部已 SetUp，不重复
      isNew=false → setUpTask = InvokeSetUpSafe(card, template, instance)  // 复用卡需重新绑定数据
      return CollectionCardBinding(card, kind, setUpTask)

  ← binding
  cell = new RealizedCell(card, kind, setUpTask, ...)
  _realized[index] = cell
  Reposition(index, cell)        // 设置 anchoredPosition（anchor 固定点 (0,1)/(0,1)，pivot center）
  ApplyCellScale(index, cell)    // ⚠️ 见下方陷阱说明
  _ = ShowWhenReady(cell, generation)

ShowWhenReady(cell, generation)
  → await cell.SetUpTask         // pool hit 时等 InvokeSetUpSafe 完成；pool miss 时立即继续
  → cell.Card.SetActive(true)
  → NativeCardPreviewRuntime.Show(card, true)   // 调用 CardPreviewBase.Show(true)，激活 _cardImage/_frameContainer
  → cell.FadeAlpha = 0f
  → cell.FadeActive = true       // 交给 TickFades 做 alpha 0→1 动画
```

### 3.3 每帧 Update

```
CollectionPanel.Update()
  → virtualizer.Tick()           // 回收出窗口的 cell，realize 新进入的 cell
  → virtualizer.TickFades(dt)    // 对 FadeActive=true 的 cell 做 CanvasGroup.alpha 动画
```

### 3.4 回收

```
RecycleCell(cell)
  ├─ SetUpTask 未完成 → cell.PendingReturn = true（ShowWhenReady 末尾会 CompleteRecycle）
  └─ SetUpTask 已完成 → CompleteRecycle(cell)
      → CollectionCardFactory.Return(card, kind)
          → CollectionCardPool.Return(card, kind)
              → card.SetActive(false)
              → queue.Enqueue(card)   // 下次 TakeAsync 直接复用，走 pool hit 路径
```

---

## 四、十胜阵容路径（ItemBoardPreviewSurface）

### 4.1 触发

外部调用 `ItemBoardPreviewSurface.Render(cards, options)` 协程。

### 4.2 完整调用链

```
ItemBoardPreviewSurface.Render(cards, options)   // Unity IEnumerator 协程
  → SpawnCardsAsync(cards, snapshot)             // Task，内部并行创建所有卡牌
      foreach spec in cards:
          NativeCardPreviewFactory.TryCreateAsync(spec, socket, index)
              → NativeCardPreviewPool.TakeAsync(kind, instance, socket)
                  // 同 CollectionCardPool，但不添加 CollectionPanelOwnedMarker 和 CanvasGroup
                  // pool miss → TryCreateCardAsync → InstantiateUICardAsync → return (card, isNew=true)
              isNew=true  → setUpTask = Task.CompletedTask
              isNew=false → setUpTask = InvokeSetUpSafe(card, template, instance)
              return NativeCardPreviewHandle(card, rect, kind, setUpTask, spec)

  await Task.WhenAll(createTasks)       // 等所有卡牌并行创建完成
  yield return null × 2 + Canvas.ForceUpdateCanvases()

  ShowSetUpCards()
      foreach handle: if setUpTask.IsCompletedSuccessfully → NativeCardPreviewFactory.Show(handle)
                          → NativeCardPreviewRuntime.Show(card, true)   // CardPreviewBase.Show(true)

  LayoutCardsSlotGrid() / LayoutCardsPacked()   // 根据 LayoutMode 做位置调整
```

---

## 五、关键陷阱与坑点

### 5.1 InstantiateUICardAsync 内部已调用 SetUp

`ConstructAndInstantiateUICard`（游戏内部）的流程：
1. Addressables 实例化 prefab
2. 调用 `SetUp(template, false, instance, token)` → 绑定数据 + `LoadArt` + **`Show(false)`**
3. `SetParent(parent, false)`

**坑**：`SetUp` 结束时 `_cardImage.gameObject.activeSelf = false`，卡牌是隐藏的。
必须在外部手动调用 `Show(true)` 才能显示。

**坑**：pool miss 时绝对不能再调用第二次 `InvokeSetUpSafe`——`isNew=true` 时用 `Task.CompletedTask` 跳过。

### 5.2 prefab 根节点 sizeDelta = (0, 0)

通过 `InstantiateUICardAsync` 创建的 prefab，其根 `RectTransform` 设计为 anchor-stretch（`sizeDelta=(0,0)`），依靠游戏自己的卡槽容器的 anchor 来决定实际大小。

我们在 `Reposition` 里把 anchor 改为固定点 `(0,1)/(0,1)` 之后，anchor-stretch 失效，卡牌大小变为 `0×0`，完全不可见。

**修复**（`CollectionGridVirtualizer.ApplyCellScale`）：检测到 `sizeDelta=(0,0)` 时，直接把 `sizeDelta` 设为目标 cell 尺寸，`localScale=1`，让 prefab 内部的子节点 anchor-stretch 自动撑满。

### 5.3 SetUp 参数数量随游戏版本变化

游戏某次更新给 `CardPreviewBase.SetUp` 增加了第 4 个参数 `CancellationToken`。
`InvokeSetUpSafe` 通过 `method.GetParameters().Length >= 4` 在运行时判断，传 3 或 4 个参数。

### 5.4 CollectionPanelOwnedMarker 门控 LoadArt patch

`CollectionItemLoadArtPatch` 只对带有 `CollectionPanelOwnedMarker` 组件的卡牌生效。
该 marker 在 pool miss（首次创建）时添加到 card.gameObject，并在整个 Take/Return 生命周期内保留。

### 5.5 CanvasGroup 仅图鉴路径使用

`CollectionCardPool` 在 pool miss 时添加 `CanvasGroup`，并在每次 `TakeAsync` 时将 `alpha` 重置为 `0`。
`ShowWhenReady` 设置 `FadeActive=true`，`TickFades` 在每帧 Update 中将 alpha 从 `0` ramp 到 `1`。
`NativeCardPreviewPool`（十胜阵容）不使用此机制。

---

## 六、反射入口速查（NativeCardPreviewReflection）

| 字段/属性 | 对应游戏成员 | 用途 |
|---|---|---|
| `SetUpMethod` | `CardPreviewBase.SetUp` | `InvokeSetUpSafe` 调用 |
| `ShowMethod` | `CardPreviewBase.Show` | `NativeCardPreviewRuntime.Show` 调用 |
| `ResizeMethod` | `CardPreviewBase.Resize` | `NativeCardPreviewRuntime.Resize` 调用 |
| `SizeProperty` | `CardPreviewBase.Size` | 读取 `ECardSize` |

---

## 七、如果游戏更新后卡牌再次失效，检查清单

1. `AssetLoader` 是否还能通过 `Services.Get<AssetLoader>()` 获取？
2. `InstantiateUICardAsync` 方法签名是否变化？
3. `CardPreviewBase.SetUp` 参数数量是否再次变化？
4. `CardPreviewBase.Show` / `Resize` 是否还存在？
5. 用 `monodis Assembly-CSharp.dll | grep -A 10 "CardPreviewBase"` 确认成员列表。
