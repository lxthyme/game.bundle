# TransformWindow UI 紧凑化 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** TransformWindow UI 紧凑化重排 + 统一 Apply/Reset + 多传送带序号支持

**Architecture:** 分两层——底层 BlueprintTransform 接口从单 `targetIndex: int` 改为多 `targetIndices: HashSet<int>?`；上层 TransformWindow 重排 GUILayout、新增脏状态跟踪和统一 Apply/Reset 逻辑。

**Tech Stack:** C# (netstandard2.0/net472), Unity IMGUI (OnGUI + GUILayout), xunit

## Global Constraints

- IMGUI only，不引入 uGUI
- BlueprintTransform 变换算法逻辑不变，只改接口签名
- 窗口已有功能（四角拖拽缩放、双击标题栏重置尺寸、默认宽高自定义）不受影响
- `Blueprint/` 下代码不依赖 UnityEngine，保持可独立单测

---

### Task 1: BlueprintTransform 多传送带接口变更

**Files:**
- Modify: `dsp-mod/Blueprint/BlueprintTransform.cs`

**Interfaces:**
- Produces: `HorizontalOffset(BlueprintData bp, double offsetX, double offsetY, HashSet<int>? targetIndices = null)`
- Produces: `VerticalOffset(BlueprintData bp, double offsetZ, HashSet<int>? targetIndices = null)`

- [ ] **Step 1: 修改 HorizontalOffset 签名和内部逻辑**

将 `HorizontalOffset` 方法的参数 `int targetIndex = -1` 改为 `HashSet<int>? targetIndices = null`，内部匹配逻辑相应调整：

```csharp
public static BlueprintData HorizontalOffset(BlueprintData bp, double offsetX, double offsetY, HashSet<int>? targetIndices = null)
{
    var res = bp.Clone();
    foreach (var b in res.Buildings)
    {
        if (targetIndices != null && !targetIndices.Contains(b.Index)) continue;
        b.LocalOffset[0].X += offsetX;
        b.LocalOffset[1].X += offsetX;
        b.LocalOffset[0].Y += offsetY;
        b.LocalOffset[1].Y += offsetY;
    }
    return res;
}
```

- [ ] **Step 2: 修改 VerticalOffset 签名和内部逻辑**

将 `VerticalOffset` 方法的参数 `int targetIndex = -1` 改为 `HashSet<int>? targetIndices = null`：

```csharp
public static BlueprintData VerticalOffset(BlueprintData bp, double offsetZ, HashSet<int>? targetIndices = null)
{
    var res = bp.Clone();
    bool needBase = false;
    bool changeIndex = false;
    var newBuildings = new System.Collections.Generic.List<BlueprintBuilding>();

    foreach (var v in res.Buildings)
    {
        if (targetIndices == null || targetIndices.Contains(v.Index))
        {
            v.LocalOffset[0].Z += offsetZ;
            v.LocalOffset[1].Z += offsetZ;

            if (v.ItemId == 1131)
            {
                v.LocalOffset[0].Z = -10;
                v.LocalOffset[1].Z = -10;
            }
            else if ((v.LocalOffset[0].Z > 0.22 || v.LocalOffset[1].Z > 0.22)
                     && v.InputObjIdx == -1
                     && !BuildingMeta.IsHanging(v.ItemId))
            {
                v.InputObjIdx = res.Buildings.Count;
                needBase = true;
                if (BuildingMeta.IsInserterSlotBuild(v.ItemId))
                {
                    newBuildings.Insert(0, v);
                    changeIndex = true;
                    continue;
                }
            }
        }
        newBuildings.Add(v);
    }

    if (needBase)
    {
        newBuildings.Add(new BlueprintBuilding
        {
            Index = newBuildings.Count,
            AreaIndex = 0,
            LocalOffset = new[] { new Vec3D { X = 0, Y = 0, Z = -10 }, new Vec3D { X = 0, Y = 0, Z = -10 } },
            Yaw = new double[] { 0, 0 },
            ItemId = 1131,
            ModelIndex = 37,
            OutputObjIdx = -1,
            InputObjIdx = -1,
            OutputToSlot = 0,
            InputFromSlot = 0,
            OutputFromSlot = 0,
            InputToSlot = 1,
            OutputOffset = 0,
            InputOffset = 0,
            RecipeId = 0,
            FilterId = 0,
            Parameters = null,
        });
    }

    if (changeIndex)
        FormatIndex(newBuildings);

    res.Buildings = newBuildings;
    return res;
}
```

- [ ] **Step 3: 确认编译通过**

```bash
cd dsp-mod && dotnet build Blueprint/Blueprint.csproj
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add dsp-mod/Blueprint/BlueprintTransform.cs
git commit -m "refactor(dsp-mod): HorizontalOffset/VerticalOffset targetIndex 改为 targetIndices 集合

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 2: BlueprintTransformTests 适配 + 多序号测试

**Files:**
- Modify: `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs`

**Interfaces:**
- Consumes: `HorizontalOffset(bp, x, y, HashSet<int>?)`, `VerticalOffset(bp, z, HashSet<int>?)` (from Task 1)

- [ ] **Step 1: 更新现有测试用例适配新签名**

将 `targetIndex: 0` 改为 `new HashSet<int> { 0 }`，将 `targetIndex: 1` 改为 `new HashSet<int> { 1 }`：

`HorizontalOffset_WithTargetIndex_OnlyMovesThatBuilding` 方法中的调用：
```csharp
var result = BlueprintTransform.HorizontalOffset(bp, 5, -3, targetIndices: new HashSet<int> { 0 });
```

`VerticalOffset_WithTargetIndex_OnlyLiftsThatBuildingAndStillAddsBaseIfNeeded` 方法中的调用：
```csharp
var result = BlueprintTransform.VerticalOffset(bp, 2, targetIndices: new HashSet<int> { 1 });
```

- [ ] **Step 2: 运行现有测试确认全部通过**

```bash
cd dsp-mod && dotnet test Blueprint.Tests/Blueprint.Tests.csproj
```
Expected: All 8 existing tests PASS.

- [ ] **Step 3: 新增多序号 HorizontalOffset 测试**

在 `BlueprintTransformTests` 类末尾追加：

```csharp
[Fact]
public void HorizontalOffset_WithMultipleTargetIndices_OnlyMovesSpecifiedBuildings()
{
    var bp = BuildFixture();
    var result = BlueprintTransform.HorizontalOffset(bp, 5, -3, targetIndices: new HashSet<int> { 0, 1 });

    Assert.Equal(5.5, result.Buildings[0].LocalOffset[0].X, 4);
    Assert.Equal(-2.5, result.Buildings[0].LocalOffset[0].Y, 4);
    Assert.Equal(3.5, result.Buildings[1].LocalOffset[0].X, 4);
    Assert.Equal(-0.75, result.Buildings[1].LocalOffset[0].Y, 4);
}

[Fact]
public void HorizontalOffset_WithEmptyTargetIndices_MovesNone()
{
    var bp = BuildFixture();
    var result = BlueprintTransform.HorizontalOffset(bp, 5, -3, targetIndices: new HashSet<int>());

    Assert.Equal(0.5, result.Buildings[0].LocalOffset[0].X, 4);
    Assert.Equal(0.5, result.Buildings[0].LocalOffset[0].Y, 4);
    Assert.Equal(-1.5, result.Buildings[1].LocalOffset[0].X, 4);
    Assert.Equal(2.25, result.Buildings[1].LocalOffset[0].Y, 4);
}

[Fact]
public void HorizontalOffset_WithNullTargetIndices_MovesAll()
{
    var bp = BuildFixture();
    var result = BlueprintTransform.HorizontalOffset(bp, 5, -3, targetIndices: null);

    Assert.Equal(5.5, result.Buildings[0].LocalOffset[0].X, 4);
    Assert.Equal(3.5, result.Buildings[1].LocalOffset[0].X, 4);
}
```

- [ ] **Step 4: 运行全部测试**

```bash
cd dsp-mod && dotnet test Blueprint.Tests/Blueprint.Tests.csproj
```
Expected: All 11 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs
git commit -m "test(dsp-mod): 适配多传送带序号接口，新增多序号/空集合/null 测试

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 3: TransformWindow UI 布局重排

**Files:**
- Modify: `dsp-mod/Plugin/TransformWindow.cs`

**Interfaces:**
- Consumes: `HorizontalOffset(bp, x, y, HashSet<int>?)`, `VerticalOffset(bp, z, HashSet<int>?)` (from Task 1)

- [ ] **Step 1: 新增字段——翻转勾选框、基线快照**

在现有字段声明区（`_rotate` 之后、`_parsed` 之前）插入：

```csharp
private bool _flipH = false;
private bool _flipV = false;

// 脏状态基线快照
private string _baselineOffsetX = "0";
private string _baselineOffsetY = "0";
private string _baselineOffsetZ = "0";
private string _baselineOffsetIndex = "";
private bool _baselineFlipH = false;
private bool _baselineFlipV = false;
private string _baselineZoomX = "1";
private string _baselineZoomY = "1";
private string _baselineRotate = "0";
```

- [ ] **Step 2: 新增 ParseBeltIndices 辅助方法**

在 `ParseOrZero` 方法之后追加：

```csharp
private static HashSet<int>? ParseBeltIndices(string s)
{
    if (string.IsNullOrWhiteSpace(s)) return null;
    var set = new HashSet<int>();
    foreach (var part in s.Split(','))
    {
        if (int.TryParse(part.Trim(), out var idx))
            set.Add(idx);
    }
    return set.Count > 0 ? set : null;
}
```

- [ ] **Step 3: 重写 DrawWindow 方法——UI 布局紧凑化**

用以下完整实现替换现有 `DrawWindow` 方法（保留 `DrawResizeHandles`、`HandleResize` 等其余方法不变）：

```csharp
private void DrawWindow(int id)
{
    float viewportHeight = Mathf.Max(50f, _windowRect.height - ContentAreaPadding);
    _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(viewportHeight));

    // ---- 窗口默认宽高 ----
    GUILayout.Label("窗口默认宽高（双击标题栏应用，或点设置立即生效）");
    GUILayout.BeginHorizontal();
    GUILayout.Label("宽度", GUILayout.Width(30));
    _defaultWidthText = GUILayout.TextField(_defaultWidthText, GUILayout.Width(50));
    GUILayout.Label("高度", GUILayout.Width(30));
    _defaultHeightText = GUILayout.TextField(_defaultHeightText, GUILayout.Width(50));
    if (GUILayout.Button("设置")) ApplyDefaultWindowSize();
    GUILayout.EndHorizontal();
    GUILayout.Space(8);

    // ---- 蓝图码输入 ----
    GUILayout.Label("蓝图码（粘贴或从剪贴板读取）");
    _inputCode = GUILayout.TextArea(_inputCode, GUILayout.Height(TextAreaHeight), GUILayout.Width(TextAreaWidth));

    GUILayout.BeginHorizontal();
    if (GUILayout.Button("从剪贴板读取")) _inputCode = GUIUtility.systemCopyBuffer;
    if (GUILayout.Button("解析")) TryParse();
    GUILayout.EndHorizontal();

    if (!string.IsNullOrEmpty(_statusMessage))
    {
        Color prevColor = GUI.color;
        GUI.color = _statusIsError ? Color.red : Color.green;
        GUILayout.Label(_statusMessage);
        GUI.color = prevColor;
    }

    GUILayout.Space(8);

    // ---- 偏移：X/Y/Z 同行 ----
    GUILayout.Label("偏移");
    GUILayout.BeginHorizontal();
    GUILayout.Label("X", GUILayout.Width(12));
    _offsetX = GUILayout.TextField(_offsetX, GUILayout.Width(60));
    GUILayout.Label("Y", GUILayout.Width(12));
    _offsetY = GUILayout.TextField(_offsetY, GUILayout.Width(60));
    GUILayout.Label("Z", GUILayout.Width(12));
    _offsetZ = GUILayout.TextField(_offsetZ, GUILayout.Width(60));
    GUILayout.EndHorizontal();

    // ---- 水平翻转：checkbox ----
    GUILayout.BeginHorizontal();
    GUILayout.Label("水平翻转", GUILayout.Width(60));
    _flipH = GUILayout.Toggle(_flipH, "横向");
    _flipV = GUILayout.Toggle(_flipV, "纵向");
    GUILayout.EndHorizontal();

    // ---- 线性变换：横向/纵向同行，旋转另起一行 ----
    GUILayout.BeginHorizontal();
    GUILayout.Label("线性变换", GUILayout.Width(60));
    GUILayout.Label("横向", GUILayout.Width(30));
    _zoomX = GUILayout.TextField(_zoomX, GUILayout.Width(60));
    GUILayout.Label("纵向", GUILayout.Width(30));
    _zoomY = GUILayout.TextField(_zoomY, GUILayout.Width(60));
    GUILayout.EndHorizontal();

    GUILayout.BeginHorizontal();
    GUILayout.Label("旋转角度(-360~360)", GUILayout.Width(130));
    _rotate = GUILayout.TextField(_rotate, GUILayout.Width(60));
    GUILayout.EndHorizontal();

    // ---- 传送带序号 ----
    GUILayout.BeginHorizontal();
    GUILayout.Label("传送带序号(留空=全部)", GUILayout.Width(150));
    _offsetIndex = GUILayout.TextField(_offsetIndex);
    GUILayout.EndHorizontal();

    GUILayout.Space(4);

    // ---- 应用 / 重置 ----
    GUILayout.BeginHorizontal();
    if (GUILayout.Button("应用")) ApplyAll();
    if (GUILayout.Button("重置")) ResetAll();
    GUILayout.EndHorizontal();

    GUILayout.Space(8);

    // ---- 输出蓝图码 ----
    GUILayout.Label("输出蓝图码");
    GUILayout.TextArea(_outputCode, GUILayout.Height(TextAreaHeight), GUILayout.Width(TextAreaWidth));
    if (GUILayout.Button("复制到剪贴板")) GUIUtility.systemCopyBuffer = _outputCode;

    GUILayout.EndScrollView();

    GUI.DragWindow();
    DrawResizeHandles();
}
```

- [ ] **Step 4: 确认编译通过**

```bash
cd dsp-mod && dotnet build Plugin/Plugin.csproj
```
Expected: Build succeeded. 注意可能有 warning（`ApplyAll`、`ResetAll`、`CaptureBaseline` 方法尚未定义，在 Task 4 补齐）。

- [ ] **Step 5: Commit**

```bash
git add dsp-mod/Plugin/TransformWindow.cs
git commit -m "feat(dsp-mod): TransformWindow UI 紧凑化重排

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 4: TransformWindow 脏状态跟踪 + Apply/Reset 逻辑

**Files:**
- Modify: `dsp-mod/Plugin/TransformWindow.cs`

**Interfaces:**
- Consumes: UI 布局（from Task 3）、`HorizontalOffset`/`VerticalOffset` 新签名（from Task 1）
- Produces: `CaptureBaseline()`, `ApplyAll()`, `ResetAll()`

- [ ] **Step 1: 删除旧的 Apply 方法和 DrawLabeledField**

删除以下不再需要的方法：
- `DrawLabeledField`（静态辅助，布局已内联）
- `ApplyOffset`（被 `ApplyAll` 替代）
- `ApplyLinearTransformationFromFields`（被 `ApplyAll` 替代）
- `ApplyLinearTransformation(double, double, double)`（被 `ApplyAll` 内联）

- [ ] **Step 2: 新增 CaptureBaseline 和脏状态判定方法**

在 `ParseBeltIndices` 之后追加：

```csharp
private void CaptureBaseline()
{
    _baselineOffsetX = _offsetX;
    _baselineOffsetY = _offsetY;
    _baselineOffsetZ = _offsetZ;
    _baselineOffsetIndex = _offsetIndex;
    _baselineFlipH = _flipH;
    _baselineFlipV = _flipV;
    _baselineZoomX = _zoomX;
    _baselineZoomY = _zoomY;
    _baselineRotate = _rotate;
}

private bool IsOffsetDirty()
    => _offsetX != _baselineOffsetX
    || _offsetY != _baselineOffsetY
    || _offsetZ != _baselineOffsetZ
    || _offsetIndex != _baselineOffsetIndex;

private bool IsFlipDirty()
    => _flipH != _baselineFlipH || _flipV != _baselineFlipV;

private bool IsLinearDirty()
    => _zoomX != _baselineZoomX || _zoomY != _baselineZoomY || _rotate != _baselineRotate;
```

- [ ] **Step 3: 新增 ApplyAll 方法**

在 `IsLinearDirty` 之后追加：

```csharp
private void ApplyAll()
{
    if (!EnsureParsed()) return;

    bool anyDirty = false;
    var data = _parsed!.Clone();

    if (IsOffsetDirty())
    {
        double x = ParseOrZero(_offsetX);
        double y = ParseOrZero(_offsetY);
        double z = ParseOrZero(_offsetZ);
        var indices = ParseBeltIndices(_offsetIndex);

        // 指定传送带序号时需满足全为传送带且数量<20（与网页版限制一致；spec 开放问题待定）
        if (indices != null && !IsBeltOnlySmall())
        {
            _statusMessage = "仅当蓝图全部为传送带且数量小于 20 时可指定序号";
            _statusIsError = true;
            return;
        }

        data = BlueprintTransform.HorizontalOffset(data, x, y, indices);
        if (z != 0)
        {
            var afterVert = BlueprintTransform.VerticalOffset(data, z, indices);
            bool addedBase = afterVert.Buildings.Count > data.Buildings.Count;
            data = afterVert;
            if (addedBase) _statusMessage = "检测到悬空建筑，已自动加地基";
        }
        anyDirty = true;
    }

    if (IsFlipDirty())
    {
        double zoomX = _flipH ? -1 : 1;
        double zoomY = _flipV ? -1 : 1;
        data = BlueprintTransform.LinearTransformation(data, zoomX, zoomY, 0);
        anyDirty = true;
    }

    if (IsLinearDirty())
    {
        double zoomX = ParseOrZero(_zoomX, 1);
        double zoomY = ParseOrZero(_zoomY, 1);
        double rotate = ParseOrZero(_rotate, 0);
        data = BlueprintTransform.LinearTransformation(data, zoomX, zoomY, rotate);
        anyDirty = true;
    }

    if (!anyDirty)
    {
        _statusMessage = "无变更";
        _statusIsError = true;
        return;
    }

    _parsed = data;
    _outputCode = BlueprintParser.ToStr(data);
    GUIUtility.systemCopyBuffer = _outputCode;
    if (string.IsNullOrEmpty(_statusMessage))
        _statusMessage = "已应用变换并复制到剪贴板";
    _statusIsError = false;
    CaptureBaseline();
}
```

- [ ] **Step 4: 新增 ResetAll 方法**

在 `ApplyAll` 之后追加：

```csharp
private void ResetAll()
{
    _offsetX = "0";
    _offsetY = "0";
    _offsetZ = "0";
    _offsetIndex = "";
    _flipH = false;
    _flipV = false;
    _zoomX = "1";
    _zoomY = "1";
    _rotate = "0";
    _outputCode = "";
    _statusMessage = "";
    _statusIsError = false;
    _parsed = null;
}
```

- [ ] **Step 5: 修改 TryParse 方法——解析成功后调用 CaptureBaseline**

在 `TryParse` 方法的成功分支末尾添加 `CaptureBaseline()` 调用。找到：

```csharp
_statusMessage = $"解析成功：{_parsed.Buildings.Count} 个建筑";
_statusIsError = false;
```

改为：

```csharp
_statusMessage = $"解析成功：{_parsed.Buildings.Count} 个建筑";
_statusIsError = false;
CaptureBaseline();
```

- [ ] **Step 6: 确认编译通过**

```bash
cd dsp-mod && dotnet build Plugin/Plugin.csproj
```
Expected: Build succeeded, zero errors.

- [ ] **Step 7: Commit**

```bash
git add dsp-mod/Plugin/TransformWindow.cs
git commit -m "feat(dsp-mod): TransformWindow 统一 Apply/Reset + 脏状态跟踪

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

### Task 5: 集成验证

**Files:**
- 验证: `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs`

- [ ] **Step 1: 运行全部单元测试**

```bash
cd dsp-mod && dotnet test Blueprint.Tests/Blueprint.Tests.csproj
```
Expected: All tests PASS.

- [ ] **Step 2: 确认 Plugin 项目编译通过**

```bash
cd dsp-mod && dotnet build Plugin/Plugin.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: 人工检查点清单**（需在 Windows 游戏环境验证）

以下项目在当前 macOS 环境无法验证，需用户在 Windows 端确认：
- 窗口 UI 布局是否与设计图一致
- 偏移 X/Y/Z 同行排列是否正确
- 翻转 checkbox 勾选 → Apply 后是否生效
- 线性变换 + 旋转是否正常
- 传送带多序号（如 "0,2"）是否只偏移指定传送带
- Apply 后再次点击是否提示"无变更"
- Reset 是否正确清空参数/输出/状态，保留蓝图码
- 窗口缩放、双击重置等已有功能是否不受影响

- [ ] **Step 4: Commit（如有检查点调整）**

```bash
git add -A
git commit -m "chore(dsp-mod): 集成验证通过

Co-Authored-By: Claude <noreply@anthropic.com>"
```
