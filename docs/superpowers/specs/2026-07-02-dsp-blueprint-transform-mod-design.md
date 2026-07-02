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
