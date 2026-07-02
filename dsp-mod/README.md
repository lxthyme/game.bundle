# DSP Blueprint Transform

戴森球计划(DSP)游戏内蓝图变换 mod：坐标偏移、水平翻转、线性变换（缩放+旋转）。
移植自网页版 [edit-dspblue-print](https://github.com/cying314/edit-dspblue-print)。

## 开发环境准备

1. 安装 dotnet SDK：`brew install --cask dotnet-sdk`（macOS）
2. 复制 `local.props.example` 为 `local.props`，把 `DspManagedDir` 改成你机器上
   `DSP 安装目录/DSPGAME_Data/Managed` 的实际路径（需要从装有游戏的 Windows 机器拷贝这个目录，
   或者建立局域网共享）

## BepInEx 依赖

`Plugin.csproj` 通过硬编码相对路径引用 `BepInEx.dll` / `0Harmony.dll`，需要仓库根目录下存在
`3rd/BepInEx/BepInEx_win_x64_5.4.23.5/BepInEx/core/`（含这两个 DLL）。该目录不随仓库提交（已加入
`.gitignore`），需要自行下载：

1. 前往 [BepInEx releases](https://github.com/BepInEx/BepInEx/releases)，下载 `5.4.23.5` 版本的
   `BepInEx_win_x64_5.4.23.5.zip`
2. 解压到仓库根目录 `3rd/BepInEx/BepInEx_win_x64_5.4.23.5/`

## 构建

```bash
cd dsp-mod
dotnet test Blueprint.Tests        # 核心逻辑单元测试，不需要游戏本体
dotnet build Plugin/Plugin.csproj -c Release   # 需要 local.props 配好
```

## 部署到游戏

把以下两个 DLL 一起拷贝到 Windows 游戏机的 `BepInEx/plugins/DspBlueprintTransform/` 目录下
（**两个都要拷，缺 Blueprint.dll 会导致插件加载失败**）：

- `dsp-mod/Plugin/bin/Release/net472/DspBlueprintTransform.dll`
- `dsp-mod/Plugin/bin/Release/net472/Blueprint.dll`

启动游戏，默认按 `F7` 打开/关闭变换窗口（可在 BepInEx 配置文件里改快捷键）。

## 已知限制

- 只支持当前游戏版本的蓝图码格式，不兼容旧版本蓝图码。
- 四向分流器(2020)/物流运输站/大型采矿机(2103/2104/2316) 的"优先级/插槽"高级配置在翻转后不会自动调整
  （建筑本身的翻转、连接口修正正常，仅这几类建筑的内部优先级配置需要玩家翻转后手动检查）。
