# 纯传送带方向反转与原地转向 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `IsBeltOnlySmall()` 前提下（蓝图全部为传送带且数量<20），为选中的传送带集合新增"反转物流方向"和"原地转向"两个能力，复用现有传送带序号选择与 Apply 执行流程。

**Architecture:** `Blueprint/BlueprintTransform.cs` 新增两个纯函数 `ReverseBeltDirection`/`RotateInPlace`（不依赖 UnityEngine，可独立单测）；`Plugin/TransformWindow.cs` 新增两个 UI 控件并把两个新函数接入 `ApplyAll`/`ResetAll` 现有流程。

**Tech Stack:** C# / .NET（`Blueprint` 项目 `netstandard2.0`，`Blueprint.Tests` 用 xUnit，`Plugin` 项目 `net472` 依赖 BepInEx/UnityEngine）。

## Global Constraints

- 复用现有 `IsBeltOnlySmall()` 前提和 `ParseBeltIndices`/`offsetIndex` 序号选择机制，不新增适用范围判断。
- 不改变现有 `HorizontalOffset`/`VerticalOffset`/`LinearTransformation` 的签名和行为。
- `Blueprint`/`Blueprint.Tests` 项目在当前 macOS 开发机可编译并跑测试（已验证：`dotnet test Blueprint.Tests/Blueprint.Tests.csproj` 当前 26/26 通过）；`Plugin` 项目在 macOS 上**可编译**（已验证：`dotnet build Plugin/Plugin.csproj` 0 错误 0 警告）但**无法运行游戏验证实际效果**，UI 交互和游戏内物流/朝向表现需要 Windows 游戏环境实测。
- 所有 dotnet 命令的工作目录为 `/Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod/dsp-mod`（含 `.sln` 的目录）。

---

### Task 1: BlueprintTransform.ReverseBeltDirection

**Files:**
- Modify: `dsp-mod/Blueprint/BlueprintTransform.cs`
- Test: `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs`

**Interfaces:**
- Consumes：`BlueprintData`/`BlueprintBuilding`（`dsp-mod/Blueprint/BlueprintData.cs`）现有字段：`OutputObjIdx`/`InputObjIdx`/`OutputToSlot`/`InputFromSlot`/`OutputFromSlot`/`InputToSlot`/`OutputOffset`/`InputOffset`（均为 `int`/`sbyte`，默认 `OutputObjIdx`/`InputObjIdx = -1`）、`Yaw`（`double[2]`）、`Tilt`/`Tilt2`（`double`）。`BlueprintData.Clone()` 已存在，深拷贝所有建筑字段。
- Produces：`public static BlueprintData ReverseBeltDirection(BlueprintData bp, HashSet<int>? targetIndices = null)`，供 Task 3 的 `TransformWindow.ApplyAll` 调用。

- [x] **Step 1: 写失败测试**

在 `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs` 中，最后一个现有测试方法 `HorizontalOffset_WithNullTargetIndices_MovesAll`（第 223-230 行）的结束 `}`（第 230 行）之后、类的结束 `}`（第 231 行）之前追加：

```csharp
        [Fact]
        public void ReverseBeltDirection_SwapsOutputInputQuadrupleAndFacing_OnTargetBuilding()
        {
            var bp = BuildFixture();
            bp.Buildings[0].OutputObjIdx = 1;
            bp.Buildings[0].InputObjIdx = -1;
            bp.Buildings[0].OutputToSlot = 3;
            bp.Buildings[0].InputFromSlot = 7;
            bp.Buildings[0].OutputFromSlot = 2;
            bp.Buildings[0].InputToSlot = 5;
            bp.Buildings[0].OutputOffset = 1;
            bp.Buildings[0].InputOffset = 0;
            bp.Buildings[0].Tilt = 12.5;
            bp.Buildings[0].Tilt2 = -3.5;

            var result = BlueprintTransform.ReverseBeltDirection(bp, new HashSet<int> { 0 });
            var b = result.Buildings[0];

            Assert.Equal(-1, b.OutputObjIdx);
            Assert.Equal(1, b.InputObjIdx);
            Assert.Equal(7, b.OutputToSlot);
            Assert.Equal(3, b.InputFromSlot);
            Assert.Equal(5, b.OutputFromSlot);
            Assert.Equal(2, b.InputToSlot);
            Assert.Equal(0, b.OutputOffset);
            Assert.Equal(1, b.InputOffset);
            Assert.Equal(270, b.Yaw[0], 6); // 原 90 + 180
            Assert.Equal(270, b.Yaw[1], 6);
            Assert.Equal(-12.5, b.Tilt, 6);
            Assert.Equal(3.5, b.Tilt2, 6);

            // 未指定的建筑保持原样（默认 OutputObjIdx/InputObjIdx = -1）
            Assert.Equal(-1, result.Buildings[1].OutputObjIdx);
            Assert.Equal(-1, result.Buildings[1].InputObjIdx);
            Assert.Equal(0, result.Buildings[1].Yaw[0], 6);
        }

        [Fact]
        public void ReverseBeltDirection_DanglingEndFlipsToOtherEnd_NoSpecialCasing()
        {
            var bp = BuildFixture();
            // 链路起点：无上游(InputObjIdx=-1)，只有下游(OutputObjIdx=1)
            bp.Buildings[0].InputObjIdx = -1;
            bp.Buildings[0].OutputObjIdx = 1;
            bp.Buildings[0].InputFromSlot = 0;
            bp.Buildings[0].OutputToSlot = 0;

            var result = BlueprintTransform.ReverseBeltDirection(bp, new HashSet<int> { 0 });
            var b = result.Buildings[0];

            // 反转后应变为链路终点：无下游(OutputObjIdx=-1)，只有上游(InputObjIdx=1)
            Assert.Equal(-1, b.OutputObjIdx);
            Assert.Equal(1, b.InputObjIdx);
        }

        [Fact]
        public void ReverseBeltDirection_WithNullTargetIndices_ReversesAll()
        {
            var bp = BuildFixture();
            bp.Buildings[0].OutputObjIdx = 1;
            bp.Buildings[1].OutputObjIdx = 0;

            var result = BlueprintTransform.ReverseBeltDirection(bp, targetIndices: null);

            Assert.Equal(-1, result.Buildings[0].OutputObjIdx);
            Assert.Equal(1, result.Buildings[0].InputObjIdx);
            Assert.Equal(-1, result.Buildings[1].OutputObjIdx);
            Assert.Equal(0, result.Buildings[1].InputObjIdx);
        }

        [Fact]
        public void ReverseBeltDirection_WithMultipleTargetIndices_OnlyReversesSpecifiedBuildings()
        {
            var bp = BuildFixture();
            bp.Buildings[0].OutputObjIdx = 1;
            bp.Buildings[1].OutputObjIdx = 0;

            var result = BlueprintTransform.ReverseBeltDirection(bp, new HashSet<int> { 0 });

            // index 0 已反转
            Assert.Equal(-1, result.Buildings[0].OutputObjIdx);
            Assert.Equal(1, result.Buildings[0].InputObjIdx);
            // index 1 未被选中，保持原样
            Assert.Equal(0, result.Buildings[1].OutputObjIdx);
            Assert.Equal(-1, result.Buildings[1].InputObjIdx);
        }
```

- [x] **Step 2: 运行测试确认失败**

Run: `cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod/dsp-mod && dotnet test Blueprint.Tests/Blueprint.Tests.csproj --filter ReverseBeltDirection`
Expected: 编译失败（`BlueprintTransform` 不含 `ReverseBeltDirection` 定义）或 4 个测试全部 FAIL。

- [x] **Step 3: 实现 ReverseBeltDirection**

在 `dsp-mod/Blueprint/BlueprintTransform.cs` 的 `VerticalOffset` 方法之后（`LinearTransformation` 方法之前，即第 86-87 行之间）插入：

```csharp
        public static BlueprintData ReverseBeltDirection(BlueprintData bp, System.Collections.Generic.HashSet<int>? targetIndices = null)
        {
            var res = bp.Clone();
            foreach (var b in res.Buildings)
            {
                if (targetIndices != null && !targetIndices.Contains(b.Index)) continue;

                (b.OutputObjIdx, b.InputObjIdx) = (b.InputObjIdx, b.OutputObjIdx);
                (b.OutputToSlot, b.InputFromSlot) = (b.InputFromSlot, b.OutputToSlot);
                (b.OutputFromSlot, b.InputToSlot) = (b.InputToSlot, b.OutputFromSlot);
                (b.OutputOffset, b.InputOffset) = (b.InputOffset, b.OutputOffset);

                b.Yaw[0] += 180;
                b.Yaw[1] += 180;
                b.Tilt = -b.Tilt;
                b.Tilt2 = -b.Tilt2;
            }
            return res;
        }

```

- [x] **Step 4: 运行测试确认通过**

Run: `cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod/dsp-mod && dotnet test Blueprint.Tests/Blueprint.Tests.csproj --filter ReverseBeltDirection`
Expected: `Passed! - Failed: 0, Passed: 4, ...`

- [x] **Step 5: 提交**

```bash
cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod
git add dsp-mod/Blueprint/BlueprintTransform.cs dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs
git commit -m "feat(dsp-mod): 新增传送带方向反转 ReverseBeltDirection"
```

---

### Task 2: BlueprintTransform.RotateInPlace

**Files:**
- Modify: `dsp-mod/Blueprint/BlueprintTransform.cs`
- Test: `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs`

**Interfaces:**
- Consumes：同 Task 1 的 `BlueprintBuilding.Yaw`（`double[2]`）；不涉及 `LocalOffset`/`OutputObjIdx`/`InputObjIdx`/`Tilt`。
- Produces：`public static BlueprintData RotateInPlace(BlueprintData bp, double degrees, HashSet<int>? targetIndices = null)`，供 Task 3 的 `TransformWindow.ApplyAll` 调用。

- [ ] **Step 1: 写失败测试**

在 `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs` 中，紧接 Task 1 新增的四个测试之后追加：

```csharp
        [Fact]
        public void RotateInPlace_AddsDegreesToYaw_OnTargetBuilding()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.RotateInPlace(bp, 45, new HashSet<int> { 0 });

            Assert.Equal(135, result.Buildings[0].Yaw[0], 6); // 原 90 + 45
            Assert.Equal(135, result.Buildings[0].Yaw[1], 6);
            // 未指定的建筑保持原样
            Assert.Equal(0, result.Buildings[1].Yaw[0], 6);
        }

        [Fact]
        public void RotateInPlace_WithNullTargetIndices_RotatesAll()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.RotateInPlace(bp, 30, targetIndices: null);

            Assert.Equal(120, result.Buildings[0].Yaw[0], 6); // 90 + 30
            Assert.Equal(30, result.Buildings[1].Yaw[0], 6); // 0 + 30
        }

        [Fact]
        public void RotateInPlace_DoesNotChangeCoordinatesOrTopology()
        {
            var bp = BuildFixture();
            bp.Buildings[0].OutputObjIdx = 1;
            bp.Buildings[0].InputObjIdx = -1;

            var result = BlueprintTransform.RotateInPlace(bp, 90, new HashSet<int> { 0 });

            Assert.Equal(0.5, result.Buildings[0].LocalOffset[0].X, 6);
            Assert.Equal(0.5, result.Buildings[0].LocalOffset[0].Y, 6);
            Assert.Equal(1, result.Buildings[0].OutputObjIdx);
            Assert.Equal(-1, result.Buildings[0].InputObjIdx);
            Assert.Equal(0, result.Buildings[0].Tilt, 6);
        }
```

- [ ] **Step 2: 运行测试确认失败**

Run: `cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod/dsp-mod && dotnet test Blueprint.Tests/Blueprint.Tests.csproj --filter RotateInPlace`
Expected: 编译失败（`RotateInPlace` 未定义）或 3 个测试全部 FAIL。

- [ ] **Step 3: 实现 RotateInPlace**

紧接 Task 1 插入的 `ReverseBeltDirection` 方法之后（仍在 `LinearTransformation` 之前）插入：

```csharp
        public static BlueprintData RotateInPlace(BlueprintData bp, double degrees, System.Collections.Generic.HashSet<int>? targetIndices = null)
        {
            var res = bp.Clone();
            foreach (var b in res.Buildings)
            {
                if (targetIndices != null && !targetIndices.Contains(b.Index)) continue;

                b.Yaw[0] += degrees;
                b.Yaw[1] += degrees;
            }
            return res;
        }

```

- [ ] **Step 4: 运行测试确认通过**

Run: `cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod/dsp-mod && dotnet test Blueprint.Tests/Blueprint.Tests.csproj --filter RotateInPlace`
Expected: `Passed! - Failed: 0, Passed: 3, ...`

- [ ] **Step 5: 提交**

```bash
cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod
git add dsp-mod/Blueprint/BlueprintTransform.cs dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs
git commit -m "feat(dsp-mod): 新增传送带原地转向 RotateInPlace"
```

---

### Task 3: TransformWindow UI 集成

**Files:**
- Modify: `dsp-mod/Plugin/TransformWindow.cs`

**Interfaces:**
- Consumes：Task 1 的 `BlueprintTransform.ReverseBeltDirection(BlueprintData, HashSet<int>?)`、Task 2 的 `BlueprintTransform.RotateInPlace(BlueprintData, double, HashSet<int>?)`；现有 `IsBeltOnlySmall()`（`TransformWindow.cs:343`）、`ParseBeltIndices(string)`（`TransformWindow.cs:433`）、`ParseOrZero(string, double)`（`TransformWindow.cs:430`）。
- Produces：无新公开接口（`TransformWindow` 是 UI 层终端，无下游任务消费）。

- [ ] **Step 1: 新增字段**

在 `dsp-mod/Plugin/TransformWindow.cs` 第 37 行 `private string _offsetIndex = "";` 之后插入：

```csharp
        private bool _beltReverse = false;
        private string _beltRotate = "0";
```

- [ ] **Step 2: 新增 UI 控件**

将第 285-289 行的传送带序号区块：

```csharp
            // ---- 传送带序号 ----
            GUILayout.BeginHorizontal();
            GUILayout.Label("传送带序号(留空=全部)", GUILayout.Width(150));
            _offsetIndex = GUILayout.TextField(_offsetIndex);
            GUILayout.EndHorizontal();
```

替换为：

```csharp
            // ---- 传送带序号 ----
            GUILayout.BeginHorizontal();
            GUILayout.Label("传送带序号(留空=全部)", GUILayout.Width(150));
            _offsetIndex = GUILayout.TextField(_offsetIndex);
            _beltReverse = GUILayout.Toggle(_beltReverse, "反转方向", GUILayout.Width(70));
            GUILayout.Label("转向", GUILayout.Width(30));
            _beltRotate = GUILayout.TextField(_beltRotate, GUILayout.Width(50));
            GUILayout.EndHorizontal();
```

- [ ] **Step 3: 更新 ApplyAll 校验与执行顺序**

将第 371-377 行的校验分支：

```csharp
            var indices = ParseBeltIndices(_offsetIndex);
            if (indices != null && !IsBeltOnlySmall())
            {
                _statusMessage = "仅当蓝图全部为传送带且数量小于 20 时可指定序号";
                _statusIsError = true;
                return;
            }
```

替换为：

```csharp
            var indices = ParseBeltIndices(_offsetIndex);
            double beltRotateDeg = ParseOrZero(_beltRotate);
            bool usesBeltOnlyFeature = indices != null || _beltReverse || beltRotateDeg != 0;
            if (usesBeltOnlyFeature && !IsBeltOnlySmall())
            {
                _statusMessage = "仅当蓝图全部为传送带且数量小于 20 时可指定序号/反转方向/转向";
                _statusIsError = true;
                return;
            }
```

再将第 393 行 `if (_flipH || _flipV)` 之前（紧接 `VerticalOffset` 的 `if (oz != 0) { ... }` 代码块之后）插入：

```csharp

            if (_beltReverse)
                data = BlueprintTransform.ReverseBeltDirection(data, indices);
            if (beltRotateDeg != 0)
                data = BlueprintTransform.RotateInPlace(data, beltRotateDeg, indices);
```

- [ ] **Step 4: 更新 ResetAll**

`_offsetIndex = "";` 在文件里出现两处（字段声明和 `ResetAll` 方法体），必须带上下文精确定位 `ResetAll`（第 413-428 行）里的那一处。将：

```csharp
            _offsetZ = "0";
            _offsetIndex = "";
            _flipH = false;
```

替换为（注意 4 空格缩进层级为方法体内语句，与字段声明处的 `private string _offsetIndex = "";` 不同，不会误匹配）：

```csharp
            _offsetZ = "0";
            _offsetIndex = "";
            _beltReverse = false;
            _beltRotate = "0";
            _flipH = false;
```

- [ ] **Step 5: 编译验证**

Run: `cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod/dsp-mod && dotnet build Plugin/Plugin.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: 提交**

```bash
cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod
git add dsp-mod/Plugin/TransformWindow.cs
git commit -m "feat(dsp-mod): TransformWindow 接入传送带方向反转与原地转向"
```

---

### Task 4: 全量回归验证与游戏内测试清单

**Files:**
- 无代码改动（纯验证任务）

**Interfaces:**
- Consumes：Task 1-3 全部产出
- Produces：无（本任务是本计划的验收关口）

- [ ] **Step 1: 运行完整测试套件**

Run: `cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod/dsp-mod && dotnet test Blueprint.Tests/Blueprint.Tests.csproj`
Expected: `Passed! - Failed: 0, Passed: 33, ...`（26 现有 + 4 ReverseBeltDirection + 3 RotateInPlace）

- [ ] **Step 2: 确认 Plugin 项目整体可编译**

Run: `cd /Users/lxthyme/Desktop/Lucky/lxthyme.Game/dsp/mod/dsp-mod/dsp-mod && dotnet build DspBlueprintTransform.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: 记录游戏内验证清单（需 Windows 游戏环境，本计划范围外，仅记录待办）**

在设计文档 `docs/superpowers/specs/2026-07-02-dsp-blueprint-transform-mod-design.md` 的"开放问题/风险"小节确认以下三项已列出（无需改动文件，仅核对）：
1. 反转算法的 `Output*`/`Input*` 四元组互换语义需要玩家在游戏里粘贴测试蓝图，确认反转后物流方向和朝向渲染正确。
2. 任意角度转向（非 90° 倍数）在游戏内是否有效未知，需实测；若异常需后续收紧为 90° 步进。
3. `RotateInPlace` 只改朝向不改拓扑——验收时需向玩家说明"转向"不会改变实际连接关系，仅改变视觉朝向。

- [ ] **Step 4: 提交（若前序 Step 有文档补充）**

若 Step 3 核对中发现设计文档遗漏上述任一项，补充后提交；若已全部存在，本步骤跳过（无需空提交）。
