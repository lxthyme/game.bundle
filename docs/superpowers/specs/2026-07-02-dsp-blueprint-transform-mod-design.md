# DSP 蓝图变换 Mod 设计文档

- 日期：2026-07-02
- 状态：已确认，待写实施计划
- 来源需求：`dsp mod 功能说明.md`
- 参考实现：`3rd/edit-dspblue-print`（网页版蓝图编辑工具，Vue2）

## 背景与约束

戴森球计划（DSP）网页版蓝图编辑工具 `3rd/edit-dspblue-print` 已实现坐标偏移、水平翻转、线性变换（缩放+旋转）三类蓝图操作。本项目要把这三类能力复刻进游戏内 mod，玩家无需跳出游戏到网页即可完成蓝图变换。

约束：

- **运行平台分离**：游戏本体运行在 Windows 机器，开发在 macOS（本仓库所在机器）。当前 macOS 上未安装 dotnet SDK，也没有游戏本体（无 `Managed/` DLL 引用）。
- **Mod 框架**：BepInEx 5.4.23.5，已在 `3rd/BepInEx/BepInEx_{win,linux,macos}_x64_5.4.23.5/` 解压就绪，`BepInEx/core` 下核心程序集（`BepInEx.dll`、`0Harmony.dll` 等）齐全，可直接引用。
- **蓝图字符串格式**：只处理**当前游戏版本**格式（`parser.js` 中 `-102` 前缀分支对应的最新格式），不移植 `-100`/`-101`/更早版本的历史兼容分支——功能说明未要求兼容旧存档蓝图码，属于网页版为兼容历史积累的负担，本项目 YAGNI。
- **翻转特殊补偿**：网页版对火力发电厂(2204)、微型聚变发电站(2211)、化工厂(2309)、量子化工厂(2317)等分拣器接口不对称的建筑做了硬编码偏移补偿（见 `Home.vue` `linearTransformation` 中 `isOverturn` 分支），这是踩坑总结出的修正，原样移植，不简化。

## 目标与非目标

**目标**：游戏内悬浮窗口，粘贴蓝图码 → 设置偏移/翻转/缩放/旋转参数 → 应用 → 变换结果写回系统剪贴板 → 玩家用游戏原生粘贴（Ctrl+V）功能放置。

**非目标（本期不做）**：
- 不做蓝图粘贴预览（重影）阶段的实时联动变换。
- 不做历史蓝图格式兼容。
- 不做参数跨会话持久化（每次打开窗口重置为默认值）。
- 不做蓝图可视化预览/画布渲染（网页版有 Canvas 预览，游戏内不做）。

## 总体架构与工作流

```
BepInEx Plugin (Plugin.cs)
  └─ 注册可配置热键（默认 F7）→ 开关悬浮窗
  └─ TransformWindow（OnGUI/IMGUI 悬浮窗，MonoBehaviour）
       ├─ 蓝图码输入框（粘贴/手动输入）+ 解析状态/报错提示（红字）
       ├─ 分组1 坐标偏移：横向偏移X / 纵向偏移Y / 垂直偏移Z
       ├─ 分组2 水平翻转：横向翻转 / 纵向翻转（按钮，各自独立触发一次线性变换）
       ├─ 分组3 线性变换：横向缩放量 / 纵向缩放量 / 旋转角度(-360~360)
       └─ 应用按钮 → 调用 Blueprint 模块变换 → 结果写入 `GUIUtility.systemCopyBuffer`
  └─ Blueprint/（纯 C#，不依赖 UnityEngine，可独立单测）
       ├─ BlueprintData.cs      数据模型
       ├─ BlueprintParser.cs    字符串 <-> BlueprintData（对应 parser.js fromStr/toStr，仅当前版本）
       ├─ BlueprintTransform.cs 变换算法（对应 Home.vue 的 horizontalOffset/verticalOffset/linearTransformation）
       └─ BuildingMeta.cs       建筑翻转元数据表（对应 itemsUtil.js 的 inserterSlotBuildInfos/beltSlotBuildInfos 等）
```

**工作流**：玩家在游戏里选中建筑 Ctrl+C 复制蓝图 → 打开 mod 窗口，粘贴蓝图码到输入框（或直接读剪贴板自动填充） → 调整参数 → 点击应用 → 窗口内显示新蓝图码并自动写回剪贴板 → 玩家 Ctrl+V 到建造网络放置。

## 模块划分与目录结构

```
dsp-mod/
  DspBlueprintTransform.csproj
  Plugin.cs                    # BepInEx 入口，热键注册，窗口生命周期
  UI/
    TransformWindow.cs         # OnGUI 悬浮窗：三个分组 + 应用按钮 + 状态提示
  Blueprint/
    BlueprintData.cs           # Header/Meta/Area/Building 等数据模型
    BlueprintParser.cs         # FromStr/ToStr
    BlueprintTransform.cs      # HorizontalOffset/VerticalOffset/LinearTransformation
    BuildingMeta.cs            # 建筑翻转元数据表 + 查询方法
  Blueprint.Tests/              # 纯 C# 单元测试项目（round-trip 校验），不依赖 Unity
    ParserRoundTripTests.cs
    TransformTests.cs
```

对应关系（网页版 → mod）：

| 网页版文件/函数 | mod 对应 |
|---|---|
| `parser.js: fromStr/toStr` | `BlueprintParser.FromStr/ToStr`（仅当前版本分支） |
| `parser.js: BufferReader/BufferWriter` | `BlueprintData.cs` 内部按需实现或用 `System.IO.BinaryReader/Writer`（.NET 原生字符串读写已内置 7-bit encoded int 前缀，等价于 JS 手写的 `getString/setString`） |
| `parser.js: digest (md5.js)` | `BlueprintChecksum.Digest`（**不能用 `System.Security.Cryptography.MD5`**——`md5.js` 的初始向量 `INIT_MD5F` 被改过，不是标准 MD5，需逐字节移植自定义实现，否则校验和会被游戏判定不匹配） |
| `pako.gzip/ungzip` | `System.IO.Compression.GZipStream` |
| `Home.vue: horizontalOffset` | `BlueprintTransform.HorizontalOffset` |
| `Home.vue: verticalOffset` | `BlueprintTransform.VerticalOffset` |
| `Home.vue: linearTransformation` | `BlueprintTransform.LinearTransformation`（含翻转特殊补偿分支原样移植） |
| `itemsUtil.js: inserterSlotBuildInfos/beltSlotBuildInfos` 等 | `BuildingMeta.cs` 静态表 + `IsInserterSlotBuild/AlterInserterSlot/IsBeltSlotBuild/AlterBeltSlot` 等查询方法 |

## 关键数据结构

`BlueprintData`：
- `Header { Layout, Icons[5], Time, GameVersion, ShortDesc, Author, CustomVersion, ExternalFields, Desc }`
- `CursorOffset{X,Y}`, `CursorTargetArea`, `DragBoxSize{X,Y}`, `PrimaryAreaIdx`
- `Area[] Areas { Index, ParentIndex, TropicAnchor, AreaSegments, AnchorLocalOffset{X,Y}, Size{X,Y} }`
- `Building[] Buildings { Index, AreaIndex, LocalOffset[2]{X,Y,Z}, Yaw[2], Tilt, Tilt2, Pitch, Pitch2, ItemId, ModelIndex, OutputObjIdx, InputObjIdx, OutputToSlot, InputFromSlot, OutputFromSlot, InputToSlot, OutputOffset, InputOffset, RecipeId, FilterId, Parameters, Content }`
- `ReformData`（地基数据，version>=2 时存在）

只解析/序列化当前版本对应字段布局，不做多版本分支判断。

## UI 设计细节

- 实现方式：`OnGUI` + `GUILayout`（IMGUI），理由见下方"备选方案"。
- 窗口内容自上而下：
  1. 蓝图码多行文本框（输入/粘贴）
  2. 解析状态：成功显示建筑数量摘要，失败显示红字错误原因（格式错误/校验和不匹配等，来自 `BlueprintParser.FromStr` 抛出的异常信息）
  3. 分组"坐标偏移"：横向偏移X / 纵向偏移Y / 垂直偏移Z 三个数值输入框
  4. 分组"水平翻转"：横向翻转 / 纵向翻转 两个按钮（点击即触发一次变换并刷新结果，等价于网页版 zoomX=-1 或 zoomY=-1 的线性变换）
  5. 分组"线性变换"：横向缩放量 / 纵向缩放量（默认1，负数即翻转）/ 旋转角度（-360~360）+ 应用按钮
  6. 输出蓝图码只读文本框 + "复制到剪贴板"按钮（应用后自动执行一次复制，也支持手动再次复制）
- 剪贴板读写：`UnityEngine.GUIUtility.systemCopyBuffer`（跨平台，无需额外依赖）。
- 热键：BepInEx `ConfigEntry<KeyboardShortcut>`，默认 F7，可在 BepInEx 配置文件里改。

## 错误处理

- `BlueprintParser.FromStr` 校验失败（起始标记不对/表头字段不足/校验和不匹配/解析异常）时捕获异常，窗口内红字展示错误信息，不清空玩家已输入的蓝图码和参数，不崩溃。
- 变换过程本身（偏移/翻转/线性变换）不做用户输入合法性校验之外的容错——数值输入框限制为数字，其余按网页版逻辑直接计算。

## 测试策略

- `Blueprint/` 下的解析与变换逻辑不依赖 `UnityEngine`，可独立放进 `Blueprint.Tests` 用 `dotnet test` 跑单元测试，在没有游戏本体的 macOS 开发机上就能验证：
  - Round-trip 测试：真实蓝图码 `FromStr` → `ToStr` → 再 `FromStr`，比对关键字段一致（复用原始蓝图码作为测试夹具，可从网页版本地跑一份或用玩家提供的样例）。
  - 变换正确性：对已知蓝图码做偏移/翻转/缩放/旋转后，人工核对建筑坐标/朝向变化是否符合预期（可与网页版跑同一份蓝图码做交叉比对）。
- UI、热键、剪贴板集成、游戏内实际粘贴效果，必须在装有游戏的 Windows 机器上手动测试，无法在当前 macOS 环境验证，需要用户在 Windows 端配合验证。

## 备选方案及取舍（含被否决方案）

| 决策点 | 选定方案 | 被否决方案 | 取舍理由 |
|---|---|---|---|
| 交互方式 | 独立悬浮窗，剪贴板文本进出 | 实时预览联动（Hook 游戏粘贴预览重影对象直接调参） | 后者需要 Harmony patch 游戏内部私有蓝图对象模型，逆向成本和后续随游戏更新失效的风险高得多；功能说明未要求所见即所得，YAGNI |
| 蓝图数据获取方式 | 自实现二进制解析（照抄 parser.js 逻辑） | 反射/patch 游戏内部 BlueprintData 类做解析 | 自实现逻辑已经过网页版实战验证（4个历史版本兼容分支说明它扛住过多次游戏更新）；反射游戏私有类型需要先拿到 Windows 机器上的 `Assembly-CSharp.dll` 反编译摸索字段名，且不随源码走版本管理，脆弱 |
| 格式兼容范围 | 仅当前游戏版本格式 | 移植 parser.js 全部4个历史版本分支 | 功能说明不要求兼容旧存档/旧蓝图码；mod 运行时游戏版本已固定，玩家复制出来的蓝图码必然是当前格式 |
| 翻转特殊补偿（不对称建筑） | 原样移植硬编码补偿 | 简化/暂不处理 | 这是网页版踩坑总结的修正，跳过会导致火电/化工厂等建筑翻转后接口错位、连接失效，属于已知必现问题，不是边角情况 |
| UI 实现 | OnGUI/IMGUI | uGUI（Canvas+EventSystem+Prefab） | 需求是"简单UI"，IMGUI 几行 GUILayout 代码即可完成，构建和联调成本远低于 uGUI；样式简陋可接受 |

## 开放问题/风险

1. **跨机器构建依赖**：macOS 开发机需要从 Windows 游戏机拷贝 `DSPGAME_Data/Managed/` 下的 UnityEngine 相关 DLL（`UnityEngine.CoreModule.dll`、`UnityEngine.IMGUIModule.dll` 等；本设计不依赖游戏私有类型，不需要 `Assembly-CSharp.dll`）才能编译通过，用户需要提供这份目录或建立共享。
2. **产物同步方式未定**：macOS 编译出的插件 DLL 如何送到 Windows `BepInEx/plugins/` 目录（局域网共享/云盘/git）待用户决定，影响实施阶段的联调节奏，不阻塞设计和编码本身。
3. **dotnet SDK 未安装**：macOS 开发机当前没有 `dotnet`/`mono`/`msbuild`，需要在实施阶段之前安装（如 `brew install --cask dotnet-sdk`）。
4. **热键默认值**：F7 为建议默认值，是否和游戏原生快捷键冲突需要在 Windows 端实测确认。
5. **测试夹具**：单元测试需要至少一份真实蓝图码作为 round-trip 测试样例，需要用户从游戏内复制提供，或从本地跑起网页版临时生成。

## 涉及的文件路径/接口/数据结构（汇总）

- 新建：`dsp-mod/DspBlueprintTransform.csproj`、`dsp-mod/Plugin.cs`
- 新建：`dsp-mod/UI/TransformWindow.cs`
- 新建：`dsp-mod/Blueprint/BlueprintData.cs`、`BlueprintParser.cs`、`BlueprintTransform.cs`、`BuildingMeta.cs`
- 新建：`dsp-mod/Blueprint.Tests/ParserRoundTripTests.cs`、`TransformTests.cs`
- 参考（不修改）：`3rd/edit-dspblue-print/src/utils/parser.js`、`src/utils/itemsUtil.js`、`src/views/Home.vue`（`linearTransformation`/`horizontalOffset`/`verticalOffset` 方法体）
- 依赖：`3rd/BepInEx/BepInEx_win_x64_5.4.23.5/BepInEx/core/*.dll`（编译期引用）；Windows 游戏机 `DSPGAME_Data/Managed/UnityEngine.*.dll`（待用户提供）

---

# UI 紧凑化优化（2026-07-05 追加）

- 日期：2026-07-05
- 状态：已确认
- 基于：`2026-07-02-dsp-blueprint-transform-mod-design.md`

## 背景与约束

TransformWindow 初版 UI 按功能分组纵向排列，每组有独立的"应用"按钮，翻转由按钮即时触发。使用中发现以下问题：

1. **操作分散**：偏移、翻转、线性变换各有一个独立按钮，调整多个参数需多次点击
2. **翻转不可逆**：点击翻转按钮后立即生效，无法和其他参数一起审视调整
3. **纵向空间浪费**：每个字段独占一行（label + textbox），窗口纵向空间紧张
4. **传送带序号限制**：只能指定单个序号，无法同时偏移多条指定传送带
5. **剪贴板自动读取时机**：`OnEnable` 无条件覆盖，可能导致玩家已输入内容被意外替换

约束：

- 保持 IMGUI（OnGUI + GUILayout），不引入 uGUI
- 不改变现有 BlueprintTransform 的变换算法逻辑，只改接口签名（单 targetIndex → 多 targetIndices）
- 窗口已有功能（四角拖拽缩放、双击标题栏重置尺寸、默认宽高自定义）不受影响

## 目标

- UI 紧凑化：字段同行排列，减少纵向空间占用
- 统一 Apply：一次性按序执行所有变更，按分组跟踪脏状态
- 翻转交互：从即时按钮改为 checkbox + Apply 统一生效
- 多传送带序号：支持逗号分隔，同时偏移多条传送带
- 剪贴板读取策略：仅输入框为空时自动读取，非空时只通过按钮更新
- Reset：一键清空所有参数、输出和状态（不包含蓝图码和窗口尺寸）

## 关键决策

| 决策点 | 选定方案 | 理由 |
|--------|---------|------|
| Apply 执行策略 | 按分组跟踪脏状态，只执行脏分组 | 避免重复执行未变更操作（如连续两次 Apply 不会重复翻转）；用户可能只想调偏移不改翻转 |
| 脏状态粒度 | 按分组（偏移/翻转/线性变换），非按字段 | 分组内字段高度关联（偏移 X/Y/Z 总是一起生效），按字段粒度增复杂度不增实用价值 |
| Apply 执行顺序 | 偏移 → 翻转 → 线性变换 | 与网页版操作顺序一致；翻转本质上是一个 zoomX=-1/zoomY=-1 的线性变换，放在偏移之后符合直觉 |
| 翻转交互 | checkbox + Apply 统一生效 | 替代即时按钮，用户可在 Apply 前任意勾选/取消，与其他参数一起审视；和其余参数统一交互模式 |
| Reset 范围 | 清空参数+输出+状态+_parsed，保留蓝图码和窗口默认尺寸 | 蓝图码是"输入数据"不应被 Reset 丢弃；窗口尺寸是用户偏好设置 |
| 多传送带序号格式 | 逗号分隔文本（如 "0,3,7"） | 最简实现，无需额外 UI 控件；和网页版输入习惯一致 |
| BlueprintTransform 接口 | `targetIndex: int` → `targetIndices: HashSet<int>?` | 保持重载简洁（null=全部），HashSet 查询 O(1)；单序号 "5" 解析为 `{5}` 统一走集合路径 |

## 备选方案（被否决）

| 方案 | 否决理由 |
|------|---------|
| Apply 始终执行全部三组操作 | 未改动的分组会被重复执行，如翻转已应用再点 Apply 会再翻一次 |
| Reset 也清空蓝图码 | 玩家通常只想重置参数重新调，蓝图码需要保留 |
| 多传送带序号用多选列表 UI | IMGUI 下实现复杂，且传送带数量通常不多（当前限制<20），逗号文本输入更快 |
| 翻转保留即时按钮 + 新增 checkbox | 两套触发机制并存增加认知负担，checkbox 统一后不再需要按钮 |

## UI 布局（紧凑化后）

```
┌─ 蓝图变换 ──────────────────────────────┐
│ 窗口默认宽高: [宽度: 420] [高度: 560] [设置] │
│                                          │
│ 蓝图码            [从剪贴板读取] [解析]    │
│ ┌──────────────────────────────────────┐ │
│ │  (固定高度 TextArea)                  │ │
│ └──────────────────────────────────────┘ │
│ ✓ 解析成功：N 个建筑                      │
│                                          │
│ 偏移:  X: [0]  Y: [0]  Z: [0]           │
│ 水平翻转: ☐横向  ☐纵向                   │
│ 线性变换:  横向: [1]  纵向: [1]           │
│ 旋转角度(-360~360): [0]                  │
│ 传送带序号(留空=全部): []                 │
│                                          │
│ [应用] [重置]                             │
│                                          │
│ 输出蓝图码                                │
│ ┌──────────────────────────────────────┐ │
│ │  (固定高度 TextArea, 只读)             │ │
│ └──────────────────────────────────────┘ │
│ [复制到剪贴板]                            │
└──────────────────────────────────────────┘
```

## 数据流与状态管理

```
OnEnable:
  if (_inputCode == "") → _inputCode = GUIUtility.systemCopyBuffer

[解析] 按钮:
  _parsed = BlueprintParser.FromStr(_inputCode)
  CaptureBaseline()  ← 记录当前字段值作为脏状态基线

[应用] 按钮:
  data = _parsed.Clone()
  if IsOffsetDirty()  → data = HorizontalOffset(data, x, y, targetIndices)
                          data = VerticalOffset(data, z, targetIndices)  // if z != 0
  if IsFlipDirty()    → zoomX = _flipH ? -1 : 1; zoomY = _flipV ? -1 : 1
                          data = LinearTransformation(data, zoomX, zoomY, 0)
  if IsLinearDirty()  → data = LinearTransformation(data, _zoomX, _zoomY, _rotate)
  序列化 → _outputCode, 写入剪贴板
  CaptureBaseline()  ← 更新基线，防止重复应用

[重置] 按钮:
  偏移恢复 "0"/"0"/"0"/""
  翻转 ☐☐
  线性变换恢复 "1"/"1"/"0"
  _outputCode = ""
  _statusMessage = ""
  _parsed = null

CaptureBaseline():
  _baselineOffsetX = _offsetX; ... (所有参数字段)
  _baselineFlipH = _flipH; _baselineFlipV = _flipV;
  _baselineZoomX = _zoomX; _baselineZoomY = _zoomY; _baselineRotate = _rotate;
```

## 接口变更

`BlueprintTransform.cs`:

```csharp
// Before
public static BlueprintData HorizontalOffset(BlueprintData bp, double offsetX, double offsetY, int targetIndex = -1)
public static BlueprintData VerticalOffset(BlueprintData bp, double offsetZ, int targetIndex = -1)

// After
public static BlueprintData HorizontalOffset(BlueprintData bp, double offsetX, double offsetY, HashSet<int>? targetIndices = null)
public static BlueprintData VerticalOffset(BlueprintData bp, double offsetZ, HashSet<int>? targetIndices = null)
```

匹配逻辑：`targetIndex >= 0 && b.Index != targetIndex` → `targetIndices != null && !targetIndices.Contains(b.Index)`。

## 涉及的文件路径

- **修改**：`dsp-mod/Plugin/TransformWindow.cs` — UI 布局重排、脏状态跟踪、Reset/Apply 逻辑、多序号解析
- **修改**：`dsp-mod/Blueprint/BlueprintTransform.cs` — `HorizontalOffset`/`VerticalOffset` 签名变更（int → HashSet<int>?）
- **修改**：`dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs` — 适配新接口 + 新增多序号测试用例

## 开放问题/风险

1. **IsBeltOnlySmall 限制**：当前传送带序号功能限制"蓝图全部为传送带且数量<20"才能指定序号。改为多序号后这个限制是否保留？如果保留，多序号场景下的校验逻辑需更新（所有指定序号均须合法）。
2. **翻转 + 线性变换的叠加**：用户同时勾选翻转和设置线性变换 zoomX=-2，两次 LinearTransformation 调用叠加后的行为需和网页版交叉验证。
3. **基线在窗口重开时丢失**：`CaptureBaseline` 数据存在字段里，窗口关闭（GameObject Destroy）后丢失。玩家关闭再打开窗口时，之前 Apply 过的操作会被视为"新变更"再次执行——这是预期行为（字段值还在、基线重置），但需在 UI 上保证字段值不会被意外清空。

---

# 纯传送带方向反转与原地转向（2026-07-05 追加）

- 日期：2026-07-05
- 状态：已确认，待写实施计划
- 基于：本文档 `IsBeltOnlySmall` / 传送带序号功能

## 背景与约束

`IsBeltOnlySmall()`（`TransformWindow.cs:343`）已允许在"蓝图全部为传送带且数量<20"时，通过 `offsetIndex`（逗号分隔序号，留空=全部）选定传送带做 X/Y/Z 坐标偏移。玩家提出两个新增能力，均在同一前提下、复用同一序号选择：

1. 改变传送带方向——反转物流方向（不是改朝向贴图那么简单，是让选中传送带整体掉头：原来 A→B 变成 B→A）。
2. Z 轴旋转——让选中传送带绕自身原地转向（改变 `Yaw`，不移动坐标），区别于现有 `LinearTransformation` 对整张蓝图做的绕竖直轴几何旋转（该参数不支持按序号筛选，且会连坐标一起转）。

约束：
- 复用现有 `IsBeltOnlySmall` 前提和 `offsetIndex` 序号选择，不引入新的适用范围判断。
- 不改变现有偏移/翻转/线性变换的行为和接口。
- macOS 开发机无法跑游戏验证，本节的字段级语义推断需要玩家在游戏内粘贴测试蓝图验证。

## 目标

- 新增 `BlueprintTransform.ReverseBeltDirection(bp, targetIndices)`：反转选中传送带的物流方向。
- 新增 `BlueprintTransform.RotateInPlace(bp, degrees, targetIndices)`：让选中传送带绕自身原地转向。
- `TransformWindow` 在"传送带序号"行新增一个 checkbox（反转方向）和一个文本框（转向角度），仅在 `IsBeltOnlySmall()` 为真时可用。

## 关键决策

| 决策点 | 选定方案 | 理由 |
|--------|---------|------|
| 方向反转语义 | 反转物流方向（拓扑 + 朝向 + 坡度同步） | 玩家明确要"掉头"效果，不是纯视觉调整；否则渲染箭头和实际流向会脱节 |
| 反转算法 | `Output*` 四元组与 `Input*` 四元组整体互换 + `Yaw += 180` + `Tilt` 取反 | `BlueprintBuilding` 字段按互换对顺序声明（`OutputObjIdx`/`InputObjIdx`、`OutputToSlot`/`InputFromSlot`、`OutputFromSlot`/`InputToSlot`、`OutputOffset`/`InputOffset`），结构上支持该假设；断头（`-1`）互换后自然变成另一端断头，无需特判边界 |
| 反转作用范围 | 仅对 `offsetIndex` 选中的集合反转（留空=全部） | 复用现有序号选择机制，不新增输入控件，和偏移功能行为一致 |
| 原地转向语义 | 仅改 `Yaw`，不改 `LocalOffset`、不改拓扑、不改 `Tilt` | `Tilt` 由输入/输出端高度差决定，与朝向无关；坐标不变意味着不需要像 `LinearTransformation` 那样重算蓝图整体 `Area.Size`/`DragBoxSize`/`CursorOffset` |
| 转向角度输入方式 | 复用自由角度文本框（同现有"旋转角度"风格，非固定步进按钮） | 与既有交互风格一致，实现最简；代价是允许非 90° 倍数角度，可能导致模型与实际连接不匹配（见风险） |
| UI 位置 | 在"传送带序号"行新增两个控件，而非独立分组 | 三者都依赖同一 `IsBeltOnlySmall` 前提和同一 `targetIndices`，放在一起认知负担最小 |
| 是否要求 IsBeltOnlySmall 前提 | 是，复用现有前提，不放开混合蓝图 | 与偏移序号功能保持同一护栏，避免在混合蓝图里单独识别/约束传送带子集的额外复杂度 |
| Apply 执行顺序 | 偏移 → 传送带方向反转/转向 → 翻转 → 线性变换 | 传送带专属操作在坐标偏移之后、全局几何变换（翻转/线性变换）之前执行，使全局变换基于最终坐标计算，且方向反转后的 `Yaw` 能被后续全局翻转正确叠加 |

## 备选方案（被否决）

| 方案 | 否决理由 |
|------|---------|
| 方向反转仅调整 Yaw（不改拓扑） | 物流方向（游戏逻辑上的实际流向）不变，只是视觉朝向变了，达不到"掉头"效果，且可能导致朝向与拓扑连接不一致 |
| 反转/转向始终作用于蓝图全部传送带 | 玩家可能只想反转/旋转一段支线，强制全体操作达不到诉求 |
| 原地转向做坐标旋转（像 LinearTransformation 一样绕中心点转） | 需要额外确定旋转中心（局部质心/全局中心），语义和实现都更复杂，玩家明确只要"原地转向"不移动位置 |
| 转向角度用固定步进选择（90°/180°/270°） | 需要额外 UI 控件（按钮/下拉），且与玩家选择的"复用自由角度输入框"不一致 |
| 反转/转向放开混合蓝图限制 | 需要在混合蓝图里单独识别、校验哪些序号是传送带，校验逻辑复杂度显著上升，收益不明确 |

## 接口变更

`BlueprintTransform.cs` 新增（不改动现有方法签名）：

```csharp
public static BlueprintData ReverseBeltDirection(BlueprintData bp, HashSet<int>? targetIndices = null)
public static BlueprintData RotateInPlace(BlueprintData bp, double degrees, HashSet<int>? targetIndices = null)
```

`ReverseBeltDirection` 对 `targetIndices == null || targetIndices.Contains(b.Index)` 的每个 `BlueprintBuilding`：

```csharp
(b.OutputObjIdx, b.InputObjIdx) = (b.InputObjIdx, b.OutputObjIdx);
(b.OutputToSlot, b.InputFromSlot) = (b.InputFromSlot, b.OutputToSlot);
(b.OutputFromSlot, b.InputToSlot) = (b.InputToSlot, b.OutputFromSlot);
(b.OutputOffset, b.InputOffset) = (b.InputOffset, b.OutputOffset);
b.Yaw[0] += 180; b.Yaw[1] += 180;
b.Tilt = -b.Tilt; b.Tilt2 = -b.Tilt2;
```

`RotateInPlace` 对目标建筑：

```csharp
b.Yaw[0] += degrees;
b.Yaw[1] += degrees;
```

## UI 变更

`TransformWindow.cs`：

```
传送带序号(留空=全部): []   ☐ 反转方向   转向: [0]
```

新增字段：`_beltReverse: bool = false`、`_beltRotate: string = "0"`。二者仅在 `IsBeltOnlySmall()` 为真时可交互；`IsBeltOnlySmall()` 不成立且这两个字段被设置为非默认值时，复用现有"仅当蓝图全部为传送带且数量小于 20 时可指定序号"报错分支（追加反转/转向到同一条校验里）。

`ApplyAll` 新增步骤（插入在偏移之后、翻转之前）：

```csharp
double beltRotateDeg = ParseOrZero(_beltRotate);
bool usesBeltOnlyFeature = indices != null || _beltReverse || beltRotateDeg != 0;
if (usesBeltOnlyFeature && !IsBeltOnlySmall())
{
    _statusMessage = "仅当蓝图全部为传送带且数量小于 20 时可指定序号/反转方向/转向";
    _statusIsError = true;
    return;
}

if (_beltReverse)
    data = BlueprintTransform.ReverseBeltDirection(data, indices);
if (beltRotateDeg != 0)
    data = BlueprintTransform.RotateInPlace(data, beltRotateDeg, indices);
```

这条校验替换（而非追加）现有 `if (indices != null && !IsBeltOnlySmall())` 分支，覆盖范围从"仅序号"扩展到"序号/反转/转向任一被使用"。

`ResetAll` 新增：`_beltReverse = false; _beltRotate = "0";`

## 涉及的文件路径

- **修改**：`dsp-mod/Blueprint/BlueprintTransform.cs` — 新增 `ReverseBeltDirection`、`RotateInPlace`
- **修改**：`dsp-mod/Plugin/TransformWindow.cs` — UI 新增 checkbox + 文本框、`ApplyAll`/`ResetAll` 逻辑
- **修改**：`dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs` — 新增 `ReverseBeltDirection`/`RotateInPlace` 测试用例（含断头边界、`targetIndices=null` 全选、多序号子集）

## 开放问题/风险

1. **反转算法未经游戏内验证**：`Output*`/`Input*` 四元组互换语义是基于字段声明顺序推断的，参考网页版（`3rd/edit-dspblue-print`）未实现此功能、无先例可核对。需要玩家在游戏里粘贴测试蓝图，确认反转后传送带物流方向和朝向渲染正确。
2. **任意角度转向的游戏内有效性未知**：传送带贴图/寻路是否接受非 90° 倍数的 `Yaw`，或是否需要与相邻建筑的连接槽位重新对齐，尚不确定。若实测发现异常，后续可能需要收紧为 90° 步进或增加角度校验。
3. **转向与拓扑不同步的风险**：`RotateInPlace` 只改 `Yaw` 不改 `InputObjIdx`/`OutputObjIdx`，若游戏实际按拓扑而非 `Yaw` 渲染路径，转向可能仅影响外观、不影响流向；这与玩家的预期（"原地转向"）一致，但需要在验收时明确告知这一点，避免误以为转向也能改变连接关系。
