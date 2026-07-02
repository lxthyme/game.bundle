# DSP 蓝图变换 Mod 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把网页版 `3rd/edit-dspblue-print` 的坐标偏移/水平翻转/线性变换能力移植成 DSP 游戏内 BepInEx mod：悬浮窗粘贴蓝图码 → 调参 → 应用 → 结果写回剪贴板。

**Architecture:** 纯 C#、不依赖 UnityEngine 的 `Blueprint` 核心库（校验和、数据模型、蓝图字符串编解码、三个变换算法、建筑翻转元数据表）+ 依赖 UnityEngine/BepInEx 的 `Plugin` 工程（OnGUI 悬浮窗、剪贴板读写、热键）。核心库可在无游戏环境下用 `dotnet test` 完整验证；`Plugin` 工程需要 Windows 游戏机的 `Managed` DLL 才能编译。

**Tech Stack:** C# / .NET，`Blueprint` 库 target `netstandard2.0`，`Blueprint.Tests` target `net8.0`（用现代 dotnet SDK 跑测试，不需要 net472 交叉编译工具链），`Plugin` target `net472`（匹配 BepInEx 5 / Unity Mono），xunit，BepInEx 5.4.23.5。

## Global Constraints

- 蓝图字符串解析只支持**当前游戏版本格式**（`-102` 前缀分支），遇到其他前缀直接抛异常，不做历史版本兼容。
- 校验和是**自定义 MD5 变体**（初始向量与标准 MD5 不同），必须逐字节移植 `3rd/edit-dspblue-print/src/utils/md5.js` 的 `digest()`，禁止使用 `System.Security.Cryptography.MD5`。
- 翻转时对火力发电厂(2204)/微型聚变发电站(2211)/化工厂(2309)/量子化工厂(2317)的不对称偏移补偿，原样移植，不简化。
- `Building.Parameters`（分拣器优先级、运输站插槽配置等建筑专属参数）按**原始字节透传**，不解析内部结构；因此翻转时网页版对四向分流器(2020)/运输站(2103/2104/2316) `parameters.priority`/`parameters.slots` 的插槽调换**本项目不实现**（已知限制，不影响连接口本身的翻转修正，只影响这几类建筑内部的"优先级"配置在翻转后是否保持指向；已记录在案，不属于本计划遗漏）。
- UI 用 `OnGUI`/`GUILayout`（IMGUI），不用 uGUI。
- 剪贴板读写用 `UnityEngine.GUIUtility.systemCopyBuffer`。
- 所有需要真实数据校验的测试，用本计划里提供的 golden fixture（由真实 `parser.js`/`md5.js`/`Home.vue` 变换函数在 Node 环境下生成，非手造数据）。

---

### Task 1: 安装 dotnet SDK，搭建解决方案骨架

**Files:**
- Create: `dsp-mod/DspBlueprintTransform.sln`
- Create: `dsp-mod/Blueprint/Blueprint.csproj`
- Create: `dsp-mod/Blueprint.Tests/Blueprint.Tests.csproj`
- Create: `dsp-mod/.gitignore`

**Interfaces:**
- Produces: 一个能跑 `dotnet test` 的空解决方案，后续任务在 `Blueprint/` 下加代码、在 `Blueprint.Tests/` 下加测试。

- [x] **Step 1: 安装 dotnet SDK**

```bash
brew install --cask dotnet-sdk
dotnet --version
```

Expected: 输出一个 `8.x` 或更高的版本号。

- [x] **Step 2: 创建 Blueprint 核心库工程**

```bash
mkdir -p "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet new classlib -n Blueprint -o Blueprint --framework netstandard2.0
rm Blueprint/Class1.cs
```

- [x] **Step 3: 创建测试工程并建立引用**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet new xunit -n Blueprint.Tests -o Blueprint.Tests
rm Blueprint.Tests/UnitTest1.cs
dotnet add Blueprint.Tests reference Blueprint/Blueprint.csproj
```

- [x] **Step 4: 创建解决方案并加入工程**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet new sln -n DspBlueprintTransform
dotnet sln add Blueprint/Blueprint.csproj Blueprint.Tests/Blueprint.Tests.csproj
```

- [x] **Step 5: 加 .gitignore**

写入 `dsp-mod/.gitignore`：

```
bin/
obj/
local.props
```

- [x] **Step 6: 验证空解决方案可构建**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet build DspBlueprintTransform.sln
```

Expected: `Build succeeded.`（此时 `Blueprint.Tests` 里还没有任何测试文件，属正常）

- [x] **Step 7: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/DspBlueprintTransform.sln dsp-mod/Blueprint/Blueprint.csproj dsp-mod/Blueprint.Tests/Blueprint.Tests.csproj dsp-mod/.gitignore
git commit -m "chore(dsp-mod): 搭建 Blueprint 核心库与测试工程骨架"
```

---

### Task 2: 自定义 MD5 摘要算法移植

**Files:**
- Create: `dsp-mod/Blueprint/BlueprintChecksum.cs`
- Test: `dsp-mod/Blueprint.Tests/BlueprintChecksumTests.cs`

**Interfaces:**
- Produces: `BlueprintChecksum.Digest(byte[] data) -> byte[]`（16 字节），`BlueprintChecksum.HexDigest(byte[] data) -> string`（32 位大写十六进制），供 Task 4 的 `BlueprintParser` 使用。

参考向量由真实 `3rd/edit-dspblue-print/src/utils/md5.js` 的 `digest()` 函数在 Node 环境下对以下输入实际计算得出（非标准 MD5，已验证与 `System.Security.Cryptography.MD5` 结果不同）：

| 输入 | 期望十六进制（大写） |
|---|---|
| `""` | `84D1CE3BD68F49AB26EB0F96416617CF` |
| `"a"` | `F10BDDAECB62E5A92433757867EE06DB` |
| `"abc"` | `F8D437E8A2D3C2138BC18EF62D8CFC64` |
| `"BLUEPRINT:1,10,0,0,0,0,0,0,0,123456,0.10.34.28281,test"` | `AB8CAA6FF98424011783D6C7FCF5E28C` |
| `"The quick brown fox jumps over the lazy dog"` | `86DCC27D895972046BC51C8EACA17F64` |

- [x] **Step 1: 写失败的测试**

创建 `dsp-mod/Blueprint.Tests/BlueprintChecksumTests.cs`：

```csharp
using System.Text;
using Xunit;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Blueprint.Tests
{
    public class BlueprintChecksumTests
    {
        [Theory]
        [InlineData("", "84D1CE3BD68F49AB26EB0F96416617CF")]
        [InlineData("a", "F10BDDAECB62E5A92433757867EE06DB")]
        [InlineData("abc", "F8D437E8A2D3C2138BC18EF62D8CFC64")]
        [InlineData("BLUEPRINT:1,10,0,0,0,0,0,0,0,123456,0.10.34.28281,test", "AB8CAA6FF98424011783D6C7FCF5E28C")]
        [InlineData("The quick brown fox jumps over the lazy dog", "86DCC27D895972046BC51C8EACA17F64")]
        public void HexDigest_MatchesReferenceVectors(string input, string expectedHex)
        {
            string actual = BlueprintChecksum.HexDigest(Encoding.ASCII.GetBytes(input));
            Assert.Equal(expectedHex, actual);
        }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintChecksumTests
```

Expected: 编译失败（找不到 `BlueprintChecksum` 类型）。

- [x] **Step 3: 实现 BlueprintChecksum**

创建 `dsp-mod/Blueprint/BlueprintChecksum.cs`：

```csharp
using System;

namespace DspBlueprintTransform.Blueprint
{
    // 蓝图校验和用的是自定义 MD5 变体：初始向量与标准 MD5(RFC1321)不同
    // （对照 3rd/edit-dspblue-print/src/utils/md5.js 的 INIT_MD5F 字节序列：
    //  01 23 45 67 89 ab DC ef fe dc ba 98 46 57 32 10，
    //  标准 MD5 是 ...89 ab CD ef... 76 54...），必须逐字节移植，
    // 不能用 System.Security.Cryptography.MD5。
    public static class BlueprintChecksum
    {
        private static readonly uint[] K =
        {
            0xd76aa478, 0xe8d7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304623, 0xfd469501,
            0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b9f1122, 0xfd987193, 0xa679438e, 0x39b40821,
            0xf61e2562, 0xc040b340, 0x265e5a51, 0xc9b6c7aa, 0xd62f105d, 0x02443453, 0xd8a1e681, 0xe7d3fbc8,
            0x21f1cde6, 0xc33707d6, 0xf4d50d87, 0x475a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
            0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
            0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
            0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
            0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
        };

        private static readonly int[] S =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        };

        private static readonly uint[] InitState = { 0x67452301, 0xefdcab89, 0x98badcfe, 0x10325746 };

        private const int BlockSize = 64;

        public static byte[] Digest(byte[] data)
        {
            uint[] s = (uint[])InitState.Clone();
            int i = 0;
            for (; i <= data.Length - BlockSize; i += BlockSize)
                UpdateBlock(s, data, i);

            int remaining = data.Length - i;
            int paddedLen = ((remaining + 9 + BlockSize - 1) / BlockSize) * BlockSize;
            byte[] last = new byte[paddedLen];
            Array.Copy(data, i, last, 0, remaining);
            last[remaining] = 0x80;
            uint bitLenLow = unchecked((uint)((ulong)data.Length * 8 & 0xFFFFFFFF));
            BitConverter.GetBytes(bitLenLow).CopyTo(last, last.Length - 8);

            for (int j = 0; j <= last.Length - BlockSize; j += BlockSize)
                UpdateBlock(s, last, j);

            byte[] result = new byte[16];
            for (int k = 0; k < 4; k++)
                BitConverter.GetBytes(s[k]).CopyTo(result, k * 4);
            return result;
        }

        public static string HexDigest(byte[] data)
        {
            byte[] hash = Digest(data);
            var chars = new char[32];
            for (int i = 0; i < 16; i++)
            {
                chars[i * 2] = HexChar(hash[i] >> 4);
                chars[i * 2 + 1] = HexChar(hash[i] & 0xF);
            }
            return new string(chars);
        }

        private static char HexChar(int v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));

        private static uint RotateLeft(uint x, int s) => (x << s) | (x >> (32 - s));

        private static void UpdateBlock(uint[] s, byte[] buf, int offset)
        {
            uint a = s[0], b = s[1], c = s[2], d = s[3];
            for (int i = 0; i < 64; i++)
            {
                uint f;
                int g;
                if (i < 16) { f = (b & c) | (~b & d); g = i; }
                else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
                else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
                else { f = c ^ (b | ~d); g = (7 * i) % 16; }

                uint chunk = BitConverter.ToUInt32(buf, offset + g * 4);
                f = unchecked(f + a + K[i] + chunk);
                a = d;
                d = c;
                c = b;
                b = unchecked(b + RotateLeft(f, S[i]));
            }
            s[0] = unchecked(s[0] + a);
            s[1] = unchecked(s[1] + b);
            s[2] = unchecked(s[2] + c);
            s[3] = unchecked(s[3] + d);
        }
    }
}
```

- [x] **Step 4: 运行测试确认通过**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintChecksumTests
```

Expected: 5 个用例全部 PASS。**如果不通过**，逐字节核对 `3rd/edit-dspblue-print/src/utils/md5.js` 里的 `K`/`S`/`INIT_MD5F` 常量，不要相信本文件里的手抄版本——上表的期望值才是唯一真相来源（由该文件在 Node 里实际跑出）。

- [x] **Step 5: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/Blueprint/BlueprintChecksum.cs dsp-mod/Blueprint.Tests/BlueprintChecksumTests.cs
git commit -m "feat(dsp-mod): 移植蓝图校验和自定义 MD5 变体"
```

---

### Task 3: BlueprintData 数据模型

**Files:**
- Create: `dsp-mod/Blueprint/BlueprintData.cs`
- Test: `dsp-mod/Blueprint.Tests/BlueprintDataTests.cs`

**Interfaces:**
- Produces: `Vec2I`、`Vec3D`、`BlueprintHeader`、`BlueprintArea`、`BlueprintBuilding`、`BlueprintData`（含 `Clone()` 深拷贝方法），供 Task 4（解析）、Task 6-8（变换）使用。
- 数值字段用 `double`（对应 JS 里读出 Float32 后按双精度做运算的行为），仅在 Task 4 读写二进制时才在 `float`/`double` 之间转换。

- [x] **Step 1: 写失败的测试**

创建 `dsp-mod/Blueprint.Tests/BlueprintDataTests.cs`：

```csharp
using Xunit;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Blueprint.Tests
{
    public class BlueprintDataTests
    {
        [Fact]
        public void Clone_ProducesIndependentDeepCopy()
        {
            var original = new BlueprintData
            {
                Version = 2,
                Header = new BlueprintHeader { Layout = 10, Icons = new[] { 1, 2, 3, 4, 5 }, GameVersion = "0.10.34.28281" },
            };
            original.Areas.Add(new BlueprintArea { Index = 0, Size = new Vec2I { X = 3, Y = 3 } });
            original.Buildings.Add(new BlueprintBuilding
            {
                Index = 0,
                ItemId = 2001,
                LocalOffset = new[] { new Vec3D { X = 1, Y = 2, Z = 3 }, new Vec3D { X = 1, Y = 2, Z = 3 } },
                Yaw = new double[] { 90, 90 },
            });

            var clone = original.Clone();
            clone.Header.Layout = 99;
            clone.Areas[0].Size.X = 100;
            clone.Buildings[0].LocalOffset[0].X = 999;

            Assert.Equal(10, original.Header.Layout);
            Assert.Equal(3, original.Areas[0].Size.X);
            Assert.Equal(1, original.Buildings[0].LocalOffset[0].X);
        }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintDataTests
```

Expected: 编译失败（找不到相关类型）。

- [x] **Step 3: 实现数据模型**

创建 `dsp-mod/Blueprint/BlueprintData.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace DspBlueprintTransform.Blueprint
{
    public sealed class Vec2I
    {
        public int X;
        public int Y;
    }

    public sealed class Vec3D
    {
        public double X;
        public double Y;
        public double Z;
    }

    public sealed class BlueprintHeader
    {
        public int Layout;
        public int[] Icons = new int[5];
        public DateTime Time = DateTime.MinValue;
        public string GameVersion = "";
        public string ShortDesc = "";
        public string Author = "";
        public string CustomVersion = "";
        public string ExternalFields = "";
        public string Desc = "";
    }

    public sealed class BlueprintArea
    {
        public sbyte Index;
        public sbyte ParentIndex;
        public short TropicAnchor;
        public short AreaSegments;
        public Vec2I AnchorLocalOffset = new Vec2I();
        public Vec2I Size = new Vec2I();
    }

    public sealed class BlueprintBuilding
    {
        public int Index;
        public sbyte AreaIndex;
        public Vec3D[] LocalOffset = { new Vec3D(), new Vec3D() };
        public double[] Yaw = new double[2];
        public double Tilt;
        public double Tilt2;
        public double Pitch;
        public double Pitch2;
        public short ItemId;
        public short ModelIndex;
        public int OutputObjIdx = -1;
        public int InputObjIdx = -1;
        public sbyte OutputToSlot;
        public sbyte InputFromSlot;
        public sbyte OutputFromSlot;
        public sbyte InputToSlot;
        public sbyte OutputOffset;
        public sbyte InputOffset;
        public short RecipeId;
        public short FilterId;

        // 原始字节透传，不解析内部结构（见 Global Constraints）
        public byte[]? Parameters;
        public string? Content;
    }

    public sealed class BlueprintData
    {
        public int Version;
        public BlueprintHeader Header = new BlueprintHeader();
        public Vec2I CursorOffset = new Vec2I();
        public int CursorTargetArea;
        public Vec2I DragBoxSize = new Vec2I();
        public int PrimaryAreaIdx;
        public List<BlueprintArea> Areas = new List<BlueprintArea>();
        public List<BlueprintBuilding> Buildings = new List<BlueprintBuilding>();
        public int Patch;

        public BlueprintData Clone()
        {
            var clone = new BlueprintData
            {
                Version = Version,
                Header = new BlueprintHeader
                {
                    Layout = Header.Layout,
                    Icons = (int[])Header.Icons.Clone(),
                    Time = Header.Time,
                    GameVersion = Header.GameVersion,
                    ShortDesc = Header.ShortDesc,
                    Author = Header.Author,
                    CustomVersion = Header.CustomVersion,
                    ExternalFields = Header.ExternalFields,
                    Desc = Header.Desc,
                },
                CursorOffset = new Vec2I { X = CursorOffset.X, Y = CursorOffset.Y },
                CursorTargetArea = CursorTargetArea,
                DragBoxSize = new Vec2I { X = DragBoxSize.X, Y = DragBoxSize.Y },
                PrimaryAreaIdx = PrimaryAreaIdx,
                Patch = Patch,
            };

            foreach (var a in Areas)
            {
                clone.Areas.Add(new BlueprintArea
                {
                    Index = a.Index,
                    ParentIndex = a.ParentIndex,
                    TropicAnchor = a.TropicAnchor,
                    AreaSegments = a.AreaSegments,
                    AnchorLocalOffset = new Vec2I { X = a.AnchorLocalOffset.X, Y = a.AnchorLocalOffset.Y },
                    Size = new Vec2I { X = a.Size.X, Y = a.Size.Y },
                });
            }

            foreach (var b in Buildings)
            {
                clone.Buildings.Add(new BlueprintBuilding
                {
                    Index = b.Index,
                    AreaIndex = b.AreaIndex,
                    LocalOffset = new[]
                    {
                        new Vec3D { X = b.LocalOffset[0].X, Y = b.LocalOffset[0].Y, Z = b.LocalOffset[0].Z },
                        new Vec3D { X = b.LocalOffset[1].X, Y = b.LocalOffset[1].Y, Z = b.LocalOffset[1].Z },
                    },
                    Yaw = new[] { b.Yaw[0], b.Yaw[1] },
                    Tilt = b.Tilt,
                    Tilt2 = b.Tilt2,
                    Pitch = b.Pitch,
                    Pitch2 = b.Pitch2,
                    ItemId = b.ItemId,
                    ModelIndex = b.ModelIndex,
                    OutputObjIdx = b.OutputObjIdx,
                    InputObjIdx = b.InputObjIdx,
                    OutputToSlot = b.OutputToSlot,
                    InputFromSlot = b.InputFromSlot,
                    OutputFromSlot = b.OutputFromSlot,
                    InputToSlot = b.InputToSlot,
                    OutputOffset = b.OutputOffset,
                    InputOffset = b.InputOffset,
                    RecipeId = b.RecipeId,
                    FilterId = b.FilterId,
                    Parameters = b.Parameters == null ? null : (byte[])b.Parameters.Clone(),
                    Content = b.Content,
                });
            }

            return clone;
        }
    }
}
```

在 `dsp-mod/Blueprint/Blueprint.csproj` 的 `<PropertyGroup>` 里加上 `<Nullable>enable</Nullable>`（用到了 `byte[]?`/`string?`）；同样也给 `Blueprint.Tests.csproj` 加上。

- [x] **Step 4: 运行测试确认通过**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintDataTests
```

Expected: PASS。

- [x] **Step 5: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/Blueprint/BlueprintData.cs dsp-mod/Blueprint.Tests/BlueprintDataTests.cs dsp-mod/Blueprint/Blueprint.csproj dsp-mod/Blueprint.Tests/Blueprint.Tests.csproj
git commit -m "feat(dsp-mod): 添加蓝图数据模型"
```

---

### Task 4: BlueprintParser（字符串 <-> BlueprintData）

**Files:**
- Create: `dsp-mod/Blueprint/BlueprintParser.cs`
- Test: `dsp-mod/Blueprint.Tests/BlueprintParserTests.cs`

**Interfaces:**
- Consumes: `BlueprintChecksum.HexDigest(byte[]) -> string`（Task 2），`BlueprintData`/`BlueprintHeader`/`BlueprintArea`/`BlueprintBuilding`/`Vec2I`/`Vec3D`（Task 3）。
- Produces: `BlueprintParser.FromStr(string) -> BlueprintData`，`BlueprintParser.ToStr(BlueprintData) -> string`，供 Task 9-10（UI）调用。

Golden fixture（由真实 `parser.js`/`md5.js` 在 Node 环境下对一个含 1 区域 2 建筑的最小蓝图对象实际编码得出，用于 round-trip 校验）：

```
BLUEPRINT:1,10,0,0,0,0,0,0,12345600000000,0.10.34.28281,%E6%B5%8B%E8%AF%95%E8%93%9D%E5%9B%BE,claude,1.0,,fixture%20for%20csharp%20port"H4sIAAAAAAAAA2NiYGBgZGBgYGKAAGYoZgCL/2dgOAEVZgYrmfX//38Q/yK7GljcHooZGBi2OIHI/1DAgAZAGkH2/OdwAXEP7GdgEHCASaJq4kfXSz4AAHBlcareAAAA"3E92023FAEB2B1E24C88F8C39C51A8A8
```

解码期望：`version=2`、`cursorOffset={1,2}`、`dragBoxSize={3,3}`、`primaryAreaIdx=0`、1 个 area（`index=0, parentIndex=-1, areaSegments=200, size={3,3}`）、2 个 building（`itemId=2001`（传送带）`localOffset[0]={0.5,0.5,0}, yaw[0]=90`；`itemId=2303`（制造台）`localOffset[0]={-1.5,2.25,0}, recipeId=15`），`header.shortDesc="测试蓝图"`、`author="claude"`、`customVersion="1.0"`、`desc="fixture for csharp port"`、`gameVersion="0.10.34.28281"`。

- [x] **Step 1: 写失败的测试**

创建 `dsp-mod/Blueprint.Tests/BlueprintParserTests.cs`：

```csharp
using System;
using Xunit;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Blueprint.Tests
{
    public class BlueprintParserTests
    {
        private const string GoldenFixture =
            "BLUEPRINT:1,10,0,0,0,0,0,0,12345600000000,0.10.34.28281,%E6%B5%8B%E8%AF%95%E8%93%9D%E5%9B%BE,claude,1.0,,fixture%20for%20csharp%20port\"H4sIAAAAAAAAA2NiYGBgZGBgYGKAAGYoZgCL/2dgOAEVZgYrmfX//38Q/yK7GljcHooZGBi2OIHI/1DAgAZAGkH2/OdwAXEP7GdgEHCASaJq4kfXSz4AAHBlcareAAAA\"3E92023FAEB2B1E24C88F8C39C51A8A8";

        [Fact]
        public void FromStr_DecodesGoldenFixture()
        {
            var bp = BlueprintParser.FromStr(GoldenFixture);

            Assert.Equal(2, bp.Version);
            Assert.Equal(1, bp.CursorOffset.X);
            Assert.Equal(2, bp.CursorOffset.Y);
            Assert.Equal(3, bp.DragBoxSize.X);
            Assert.Equal(3, bp.DragBoxSize.Y);
            Assert.Equal(0, bp.PrimaryAreaIdx);

            Assert.Single(bp.Areas);
            Assert.Equal(0, bp.Areas[0].Index);
            Assert.Equal(-1, bp.Areas[0].ParentIndex);
            Assert.Equal(200, bp.Areas[0].AreaSegments);
            Assert.Equal(3, bp.Areas[0].Size.X);
            Assert.Equal(3, bp.Areas[0].Size.Y);

            Assert.Equal(2, bp.Buildings.Count);
            Assert.Equal(2001, bp.Buildings[0].ItemId);
            Assert.Equal(0.5, bp.Buildings[0].LocalOffset[0].X, 4);
            Assert.Equal(0.5, bp.Buildings[0].LocalOffset[0].Y, 4);
            Assert.Equal(90, bp.Buildings[0].Yaw[0], 4);

            Assert.Equal(2303, bp.Buildings[1].ItemId);
            Assert.Equal(-1.5, bp.Buildings[1].LocalOffset[0].X, 4);
            Assert.Equal(2.25, bp.Buildings[1].LocalOffset[0].Y, 4);
            Assert.Equal(15, bp.Buildings[1].RecipeId);

            Assert.Equal("测试蓝图", bp.Header.ShortDesc);
            Assert.Equal("claude", bp.Header.Author);
            Assert.Equal("1.0", bp.Header.CustomVersion);
            Assert.Equal("fixture for csharp port", bp.Header.Desc);
            Assert.Equal("0.10.34.28281", bp.Header.GameVersion);
        }

        [Fact]
        public void ToStr_RoundTripsThroughFromStr()
        {
            var original = BlueprintParser.FromStr(GoldenFixture);
            string reEncoded = BlueprintParser.ToStr(original);
            var reDecoded = BlueprintParser.FromStr(reEncoded);

            Assert.Equal(original.Version, reDecoded.Version);
            Assert.Equal(original.Buildings.Count, reDecoded.Buildings.Count);
            for (int i = 0; i < original.Buildings.Count; i++)
            {
                Assert.Equal(original.Buildings[i].ItemId, reDecoded.Buildings[i].ItemId);
                Assert.Equal(original.Buildings[i].LocalOffset[0].X, reDecoded.Buildings[i].LocalOffset[0].X, 4);
                Assert.Equal(original.Buildings[i].LocalOffset[0].Y, reDecoded.Buildings[i].LocalOffset[0].Y, 4);
                Assert.Equal(original.Buildings[i].Yaw[0], reDecoded.Buildings[i].Yaw[0], 4);
            }
        }

        [Fact]
        public void FromStr_ThrowsOnTamperedChecksum()
        {
            string tampered = GoldenFixture.Substring(0, GoldenFixture.Length - 1) + "0";
            Assert.Throws<FormatException>(() => BlueprintParser.FromStr(tampered));
        }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintParserTests
```

Expected: 编译失败（找不到 `BlueprintParser`）。

- [x] **Step 3: 实现 BlueprintParser**

创建 `dsp-mod/Blueprint/BlueprintParser.cs`：

```csharp
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DspBlueprintTransform.Blueprint
{
    public static class BlueprintParser
    {
        private const string Start = "BLUEPRINT:";

        public static BlueprintData FromStr(string strData)
        {
            if (!strData.StartsWith(Start, StringComparison.Ordinal))
                throw new FormatException("Invalid start");

            int p1 = strData.IndexOf('"', Start.Length);
            if (p1 < 0)
                throw new FormatException("Header terminator not found");

            string headerPart = strData.Substring(Start.Length, p1 - Start.Length);
            string[] cells = headerPart.Split(',');
            if (cells.Length < 15)
                throw new FormatException("Header too short");

            var header = new BlueprintHeader
            {
                Layout = int.Parse(cells[1], CultureInfo.InvariantCulture),
                Icons = new[]
                {
                    int.Parse(cells[2], CultureInfo.InvariantCulture),
                    int.Parse(cells[3], CultureInfo.InvariantCulture),
                    int.Parse(cells[4], CultureInfo.InvariantCulture),
                    int.Parse(cells[5], CultureInfo.InvariantCulture),
                    int.Parse(cells[6], CultureInfo.InvariantCulture),
                },
                // cells[8] 是 .NET DateTime.Ticks（游戏本身用 C# 写的，直接原生对应，不用走 JS 版的 epoch 换算）
                Time = new DateTime(long.Parse(cells[8], CultureInfo.InvariantCulture), DateTimeKind.Utc),
                GameVersion = cells[9],
                ShortDesc = Uri.UnescapeDataString(cells[10]),
                Author = Uri.UnescapeDataString(cells[11]),
                CustomVersion = Uri.UnescapeDataString(cells[12]),
                ExternalFields = Uri.UnescapeDataString(cells[13]),
                Desc = Uri.UnescapeDataString(cells[14]),
            };

            int p2 = strData.Length - 33;
            if (p2 < p1 || strData[p2] != '"')
                throw new FormatException("Checksum marker not found");

            string forChecksum = strData.Substring(0, p2);
            string expected = strData.Substring(p2 + 1);
            string actual = BlueprintChecksum.HexDigest(Encoding.ASCII.GetBytes(forChecksum));
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new FormatException($"Checksum mismatch: expected {expected}, got {actual}");

            string encoded = strData.Substring(p1 + 1, p2 - (p1 + 1));
            byte[] gzipped = Convert.FromBase64String(encoded);
            byte[] decoded = Gunzip(gzipped);

            using var ms = new MemoryStream(decoded);
            using var r = new BinaryReader(ms);

            var bp = new BlueprintData { Header = header };
            bp.Version = r.ReadInt32();
            bp.CursorOffset = new Vec2I { X = r.ReadInt32(), Y = r.ReadInt32() };
            bp.CursorTargetArea = r.ReadInt32();
            bp.DragBoxSize = new Vec2I { X = r.ReadInt32(), Y = r.ReadInt32() };
            bp.PrimaryAreaIdx = r.ReadInt32();

            int numAreas = r.ReadByte();
            for (int i = 0; i < numAreas; i++)
                bp.Areas.Add(ReadArea(r));

            int numBuildings = r.ReadInt32();
            for (int i = 0; i < numBuildings; i++)
                bp.Buildings.Add(ReadBuilding(r));

            if (bp.Version >= 2)
            {
                bp.Patch = r.ReadInt32();
                r.ReadByte(); // 是否存在地基数据；本项目不解析地基，读掉这个标记字节即可
            }

            return bp;
        }

        public static string ToStr(BlueprintData bp)
        {
            var sb = new StringBuilder();
            sb.Append(Start);
            sb.Append("1,");
            sb.Append(bp.Header.Layout).Append(',');
            foreach (int icon in bp.Header.Icons)
                sb.Append(icon).Append(',');
            sb.Append("0,");
            sb.Append(bp.Header.Time.Ticks).Append(',');
            sb.Append(bp.Header.GameVersion).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.ShortDesc)).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.Author)).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.CustomVersion)).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.ExternalFields)).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.Desc));
            sb.Append('"');

            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(bp.Version);
                    w.Write(bp.CursorOffset.X);
                    w.Write(bp.CursorOffset.Y);
                    w.Write(bp.CursorTargetArea);
                    w.Write(bp.DragBoxSize.X);
                    w.Write(bp.DragBoxSize.Y);
                    w.Write(bp.PrimaryAreaIdx);
                    w.Write((byte)bp.Areas.Count);
                    foreach (var a in bp.Areas)
                        WriteArea(w, a);
                    w.Write(bp.Buildings.Count);
                    foreach (var b in bp.Buildings)
                        WriteBuilding(w, b);
                    if (bp.Version >= 2)
                    {
                        w.Write(bp.Patch);
                        w.Write((byte)0); // 本项目不写出地基数据
                    }
                }
                rawBytes = ms.ToArray();
            }

            sb.Append(Convert.ToBase64String(Gzip(rawBytes)));

            string body = sb.ToString();
            string checksum = BlueprintChecksum.HexDigest(Encoding.ASCII.GetBytes(body));
            sb.Append('"').Append(checksum);
            return sb.ToString();
        }

        private static BlueprintArea ReadArea(BinaryReader r) => new BlueprintArea
        {
            Index = r.ReadSByte(),
            ParentIndex = r.ReadSByte(),
            TropicAnchor = r.ReadInt16(),
            AreaSegments = r.ReadInt16(),
            AnchorLocalOffset = new Vec2I { X = r.ReadInt16(), Y = r.ReadInt16() },
            Size = new Vec2I { X = r.ReadInt16(), Y = r.ReadInt16() },
        };

        private static void WriteArea(BinaryWriter w, BlueprintArea a)
        {
            w.Write(a.Index);
            w.Write(a.ParentIndex);
            w.Write(a.TropicAnchor);
            w.Write(a.AreaSegments);
            w.Write((short)a.AnchorLocalOffset.X);
            w.Write((short)a.AnchorLocalOffset.Y);
            w.Write((short)a.Size.X);
            w.Write((short)a.Size.Y);
        }

        private static double Fix(float v) => Math.Round((double)v, 4, MidpointRounding.AwayFromZero);

        private static Vec3D ReadXyz(BinaryReader r) => new Vec3D
        {
            X = Fix(r.ReadSingle()),
            Y = Fix(r.ReadSingle()),
            Z = Fix(r.ReadSingle()),
        };

        private static void WriteXyz(BinaryWriter w, Vec3D v)
        {
            w.Write((float)v.X);
            w.Write((float)v.Y);
            w.Write((float)v.Z);
        }

        private static BlueprintBuilding ReadBuilding(BinaryReader r)
        {
            int prefix = r.ReadInt32();
            if (prefix != -102)
                throw new FormatException(
                    $"仅支持当前游戏版本蓝图格式(-102 前缀)，实际读到前缀 {prefix}，可能是旧版本蓝图码或游戏已更新格式");

            var b = new BlueprintBuilding
            {
                Index = r.ReadInt32(),
                ItemId = r.ReadInt16(),
                ModelIndex = r.ReadInt16(),
                AreaIndex = r.ReadSByte(),
            };
            b.LocalOffset[0] = ReadXyz(r);
            b.Yaw[0] = Fix(r.ReadSingle());

            if (b.ItemId > 2000 && b.ItemId < 2010)
            {
                b.Tilt = r.ReadSingle();
                b.Pitch = 0;
                b.LocalOffset[1] = new Vec3D { X = b.LocalOffset[0].X, Y = b.LocalOffset[0].Y, Z = b.LocalOffset[0].Z };
                b.Yaw[1] = b.Yaw[0];
                b.Tilt2 = b.Tilt;
                b.Pitch2 = 0;
            }
            else if (b.ItemId > 2010 && b.ItemId < 2020)
            {
                b.Tilt = r.ReadSingle();
                b.Pitch = r.ReadSingle();
                b.LocalOffset[1] = ReadXyz(r);
                b.Yaw[1] = Fix(r.ReadSingle());
                b.Tilt2 = r.ReadSingle();
                b.Pitch2 = r.ReadSingle();
            }
            else
            {
                b.Tilt = 0;
                b.Pitch = 0;
                b.LocalOffset[1] = new Vec3D { X = b.LocalOffset[0].X, Y = b.LocalOffset[0].Y, Z = b.LocalOffset[0].Z };
                b.Yaw[1] = b.Yaw[0];
                b.Tilt2 = 0;
                b.Pitch2 = 0;
            }

            b.OutputObjIdx = r.ReadInt32();
            b.InputObjIdx = r.ReadInt32();
            b.OutputToSlot = r.ReadSByte();
            b.InputFromSlot = r.ReadSByte();
            b.OutputFromSlot = r.ReadSByte();
            b.InputToSlot = r.ReadSByte();
            b.OutputOffset = r.ReadSByte();
            b.InputOffset = r.ReadSByte();
            b.RecipeId = r.ReadInt16();
            b.FilterId = r.ReadInt16();

            short paramLen = r.ReadInt16();
            b.Parameters = paramLen > 0 ? r.ReadBytes(paramLen * 4) : null;

            int contentMarker = r.ReadInt32();
            b.Content = contentMarker > 0 ? r.ReadString() : null;

            return b;
        }

        private static void WriteBuilding(BinaryWriter w, BlueprintBuilding b)
        {
            w.Write(-102);
            w.Write(b.Index);
            w.Write(b.ItemId);
            w.Write(b.ModelIndex);
            w.Write(b.AreaIndex);
            WriteXyz(w, b.LocalOffset[0]);
            w.Write((float)b.Yaw[0]);

            if (b.ItemId > 2000 && b.ItemId < 2010)
            {
                w.Write((float)b.Tilt);
            }
            else if (b.ItemId > 2010 && b.ItemId < 2020)
            {
                w.Write((float)b.Tilt);
                w.Write((float)b.Pitch);
                WriteXyz(w, b.LocalOffset[1]);
                w.Write((float)b.Yaw[1]);
                w.Write((float)b.Tilt2);
                w.Write((float)b.Pitch2);
            }

            w.Write(b.OutputObjIdx);
            w.Write(b.InputObjIdx);
            w.Write(b.OutputToSlot);
            w.Write(b.InputFromSlot);
            w.Write(b.OutputFromSlot);
            w.Write(b.InputToSlot);
            w.Write(b.OutputOffset);
            w.Write(b.InputOffset);
            w.Write(b.RecipeId);
            w.Write(b.FilterId);

            if (b.Parameters != null && b.Parameters.Length > 0)
            {
                w.Write((short)(b.Parameters.Length / 4));
                w.Write(b.Parameters);
            }
            else
            {
                w.Write((short)0);
            }

            if (!string.IsNullOrEmpty(b.Content))
            {
                w.Write(b.Content!.Length); // 字符数标记，实际字符串靠下面 w.Write(string) 自带的 7-bit 长度前缀读回
                w.Write(b.Content);
            }
            else
            {
                w.Write(0);
            }
        }

        private static byte[] Gzip(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                gz.Write(data, 0, data.Length);
            return output.ToArray();
        }

        private static byte[] Gunzip(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gz.CopyTo(output);
            return output.ToArray();
        }
    }
}
```

- [x] **Step 4: 运行测试确认通过**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintParserTests
```

Expected: 3 个用例全部 PASS。

- [x] **Step 5: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/Blueprint/BlueprintParser.cs dsp-mod/Blueprint.Tests/BlueprintParserTests.cs
git commit -m "feat(dsp-mod): 移植蓝图字符串编解码(仅当前版本格式)"
```

---

### Task 5: BuildingMeta 建筑翻转元数据表

**Files:**
- Create: `dsp-mod/Blueprint/BuildingMeta.cs`
- Test: `dsp-mod/Blueprint.Tests/BuildingMetaTests.cs`

**Interfaces:**
- Produces: `BuildingMeta.IsBelt/IsInserter/IsStation/IsHanging(short) -> bool`，`BuildingMeta.IsInserterSlotBuild/IsBeltSlotBuild(short) -> bool`，`BuildingMeta.GetInserterSlotBuildAxis(short) -> SlotAxis?`，`BuildingMeta.GetBeltSlotBuildAxis(short, short) -> SlotAxis?`，`BuildingMeta.AlterInserterSlot(short, int) -> int?`，`BuildingMeta.AlterBeltSlot(short, short, int) -> int?`，`SlotAxis` 枚举（`X`/`Y`）。供 Task 8（`LinearTransformation`）使用。

数据表逐条照抄 `3rd/edit-dspblue-print/src/utils/itemsUtil.js` 的 `inserterSlotBuildInfos`/`beltSlotBuildInfos`。

- [x] **Step 1: 写失败的测试**

创建 `dsp-mod/Blueprint.Tests/BuildingMetaTests.cs`：

```csharp
using Xunit;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Blueprint.Tests
{
    public class BuildingMetaTests
    {
        [Fact]
        public void IsBelt_IsInserter_IsStation_MatchKnownIds()
        {
            Assert.True(BuildingMeta.IsBelt(2001));
            Assert.False(BuildingMeta.IsBelt(2011));
            Assert.True(BuildingMeta.IsInserter(2011));
            Assert.True(BuildingMeta.IsStation(2103));
            Assert.True(BuildingMeta.IsHanging(2001)); // 传送带可悬空
            Assert.False(BuildingMeta.IsHanging(2303)); // 制造台不可悬空
        }

        [Fact]
        public void InserterSlotBuild_SmallStorage_SwapsSymmetricSlots()
        {
            Assert.True(BuildingMeta.IsInserterSlotBuild(2101)); // 小型储物仓
            Assert.Equal(SlotAxis.Y, BuildingMeta.GetInserterSlotBuildAxis(2101));
            Assert.Equal(2, BuildingMeta.AlterInserterSlot(2101, 0));
            Assert.Equal(0, BuildingMeta.AlterInserterSlot(2101, 2));
            Assert.Null(BuildingMeta.AlterInserterSlot(2101, 1)); // 1 不在对称表里，原样保留
        }

        [Fact]
        public void InserterSlotBuild_ThermalPowerPlant_HasAsymmetricSlot()
        {
            // 火力发电厂：0<->4, 1<->3，2 号插槽不对称，翻转后无法调换
            Assert.Equal(SlotAxis.Y, BuildingMeta.GetInserterSlotBuildAxis(2204));
            Assert.Equal(4, BuildingMeta.AlterInserterSlot(2204, 0));
            Assert.Null(BuildingMeta.AlterInserterSlot(2204, 2));
        }

        [Fact]
        public void BeltSlotBuild_Splitter_MultiModelLookup()
        {
            Assert.True(BuildingMeta.IsBeltSlotBuild(2020)); // 四向分流器
            Assert.Equal(SlotAxis.Y, BuildingMeta.GetBeltSlotBuildAxis(2020, 38));
            Assert.Equal(3, BuildingMeta.AlterBeltSlot(2020, 38, 1));
            Assert.Null(BuildingMeta.AlterBeltSlot(2020, 39, 1)); // 一字双层模型没有可调换插槽
        }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BuildingMetaTests
```

Expected: 编译失败（找不到 `BuildingMeta`/`SlotAxis`）。

- [x] **Step 3: 实现 BuildingMeta**

创建 `dsp-mod/Blueprint/BuildingMeta.cs`：

```csharp
using System.Collections.Generic;
using System.Linq;

namespace DspBlueprintTransform.Blueprint
{
    public enum SlotAxis { X, Y }

    internal sealed class SlotFlipInfo
    {
        public SlotAxis Axis;
        public (int, int)[] AlterSlot = System.Array.Empty<(int, int)>();
    }

    public static class BuildingMeta
    {
        public static readonly HashSet<short> BeltBuildIds = new HashSet<short> { 2001, 2002, 2003 };
        public static bool IsBelt(short id) => BeltBuildIds.Contains(id);

        public static readonly HashSet<short> InserterBuildIds = new HashSet<short> { 2011, 2012, 2013, 2014 };
        public static bool IsInserter(short id) => InserterBuildIds.Contains(id);

        public static bool IsStation(short id) => id == 2103 || id == 2104 || id == 2316;

        public static readonly HashSet<short> HangingBuildIds =
            new HashSet<short>(BeltBuildIds.Concat(InserterBuildIds)) { 2030, 2313 };
        public static bool IsHanging(short id) => HangingBuildIds.Contains(id);

        private static readonly Dictionary<short, SlotFlipInfo> InserterSlotBuildInfos = new Dictionary<short, SlotFlipInfo>
        {
            [2101] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2102] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2204] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 4), (1, 3) } },
            [2211] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 4), (1, 3) } },
            [2302] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2315] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2319] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2303] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2304] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2305] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2318] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2308] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 5), (1, 4), (2, 3), (6, 8) } },
            [2309] = new SlotFlipInfo { Axis = SlotAxis.X, AlterSlot = new (int, int)[] { (0, 6), (1, 5), (2, 4), (3, 7) } },
            [2317] = new SlotFlipInfo { Axis = SlotAxis.X, AlterSlot = new (int, int)[] { (0, 6), (1, 5), (2, 4), (3, 7) } },
            [2310] = new SlotFlipInfo { Axis = SlotAxis.X, AlterSlot = new (int, int)[] { (0, 8), (1, 7), (2, 6), (3, 5) } },
            [2311] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 1) } },
            [2210] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (1, 3) } },
            [2312] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (1, 2) } },
            [2901] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [2902] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } },
            [3009] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 8), (1, 7), (2, 6), (3, 5) } },
        };

        public static bool IsInserterSlotBuild(short id) => InserterSlotBuildInfos.ContainsKey(id);

        public static SlotAxis? GetInserterSlotBuildAxis(short id) =>
            InserterSlotBuildInfos.TryGetValue(id, out var info) ? info.Axis : (SlotAxis?)null;

        public static int? AlterInserterSlot(short id, int originSlot)
        {
            if (!InserterSlotBuildInfos.TryGetValue(id, out var info)) return null;
            foreach (var (a, b) in info.AlterSlot)
            {
                if (a == originSlot) return b;
                if (b == originSlot) return a;
            }
            return null;
        }

        private sealed class BeltSlotInfo
        {
            public bool MultiModel;
            public Dictionary<short, SlotFlipInfo>? Models;
            public SlotFlipInfo? Single;
        }

        private static readonly Dictionary<short, BeltSlotInfo> BeltSlotBuildInfos = new Dictionary<short, BeltSlotInfo>
        {
            [2020] = new BeltSlotInfo
            {
                MultiModel = true,
                Models = new Dictionary<short, SlotFlipInfo>
                {
                    [38] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (1, 3) } },
                    [39] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = System.Array.Empty<(int, int)>() },
                    [40] = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (1, 3) } },
                },
            },
            [2103] = new BeltSlotInfo { Single = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } } },
            [2104] = new BeltSlotInfo { Single = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 11), (4, 10), (5, 9), (6, 8) } } },
            [2316] = new BeltSlotInfo { Single = new SlotFlipInfo { Axis = SlotAxis.Y, AlterSlot = new (int, int)[] { (0, 2), (3, 8), (4, 7), (5, 6) } } },
        };

        public static bool IsBeltSlotBuild(short id) => BeltSlotBuildInfos.ContainsKey(id);

        private static SlotFlipInfo? GetBeltSlotBuildInfo(short id, short modelIndex)
        {
            if (!BeltSlotBuildInfos.TryGetValue(id, out var info)) return null;
            if (info.MultiModel) return info.Models!.TryGetValue(modelIndex, out var m) ? m : null;
            return info.Single;
        }

        public static SlotAxis? GetBeltSlotBuildAxis(short id, short modelIndex) =>
            GetBeltSlotBuildInfo(id, modelIndex)?.Axis;

        public static int? AlterBeltSlot(short id, short modelIndex, int originSlot)
        {
            var info = GetBeltSlotBuildInfo(id, modelIndex);
            if (info == null) return null;
            foreach (var (a, b) in info.AlterSlot)
            {
                if (a == originSlot) return b;
                if (b == originSlot) return a;
            }
            return null;
        }
    }
}
```

- [x] **Step 4: 运行测试确认通过**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BuildingMetaTests
```

Expected: 4 个用例全部 PASS。

- [x] **Step 5: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/Blueprint/BuildingMeta.cs dsp-mod/Blueprint.Tests/BuildingMetaTests.cs docs/superpowers/plans/2026-07-02-dsp-blueprint-transform-mod.md
git commit -m "feat(dsp-mod): 移植建筑翻转元数据表"
```

---

### Task 6: BlueprintTransform.HorizontalOffset（水平偏移）

**Files:**
- Create: `dsp-mod/Blueprint/BlueprintTransform.cs`
- Test: `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs`

**Interfaces:**
- Consumes: `BlueprintData.Clone()`（Task 3）。
- Produces: `BlueprintTransform.HorizontalOffset(BlueprintData, double offsetX, double offsetY) -> BlueprintData`，供 UI（Task 10）调用；后续 Task 7/8 会在同一个 `BlueprintTransform.cs` 里追加方法。

固定测试用蓝图（对应下方所有 Task 6/7/8 测试共用）：1 个 area（`size={3,3}`），2 个 building：`{index:0, itemId:2001(传送带), localOffset:[{0.5,0.5,0},{0.5,0.5,0}], yaw:[90,90]}`，`{index:1, itemId:2303(制造台), localOffset:[{-1.5,2.25,0},{-1.5,2.25,0}], yaw:[0,0], recipeId:15}`。该固定蓝图和以下期望值均由真实 `Home.vue` 的 `horizontalOffset`/`verticalOffset`/`linearTransformation` 函数在 Node 环境下对同一份数据实际跑出。

- [x] **Step 1: 写失败的测试**

创建 `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs`：

```csharp
using Xunit;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Blueprint.Tests
{
    public class BlueprintTransformTests
    {
        private static BlueprintData BuildFixture()
        {
            var bp = new BlueprintData();
            bp.Areas.Add(new BlueprintArea { Index = 0, ParentIndex = -1, AreaSegments = 200, Size = new Vec2I { X = 3, Y = 3 } });
            bp.DragBoxSize = new Vec2I { X = 3, Y = 3 };
            bp.CursorOffset = new Vec2I { X = 1, Y = 2 };

            bp.Buildings.Add(new BlueprintBuilding
            {
                Index = 0,
                ItemId = 2001,
                ModelIndex = 38,
                LocalOffset = new[] { new Vec3D { X = 0.5, Y = 0.5, Z = 0 }, new Vec3D { X = 0.5, Y = 0.5, Z = 0 } },
                Yaw = new double[] { 90, 90 },
            });
            bp.Buildings.Add(new BlueprintBuilding
            {
                Index = 1,
                ItemId = 2303,
                ModelIndex = 68,
                LocalOffset = new[] { new Vec3D { X = -1.5, Y = 2.25, Z = 0 }, new Vec3D { X = -1.5, Y = 2.25, Z = 0 } },
                Yaw = new double[] { 0, 0 },
                RecipeId = 15,
            });
            return bp;
        }

        [Fact]
        public void HorizontalOffset_ShiftsXAndYOnAllBuildings()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.HorizontalOffset(bp, 5, -3);

            Assert.Equal(5.5, result.Buildings[0].LocalOffset[0].X, 4);
            Assert.Equal(-2.5, result.Buildings[0].LocalOffset[0].Y, 4);
            Assert.Equal(3.5, result.Buildings[1].LocalOffset[0].X, 4);
            Assert.Equal(-0.75, result.Buildings[1].LocalOffset[0].Y, 4);

            // 原对象不受影响（不可变风格）
            Assert.Equal(0.5, bp.Buildings[0].LocalOffset[0].X, 4);
        }
    }
}
```

- [x] **Step 2: 运行测试确认失败**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintTransformTests
```

Expected: 编译失败（找不到 `BlueprintTransform`）。

- [x] **Step 3: 实现 HorizontalOffset**

创建 `dsp-mod/Blueprint/BlueprintTransform.cs`：

```csharp
namespace DspBlueprintTransform.Blueprint
{
    public static class BlueprintTransform
    {
        public static BlueprintData HorizontalOffset(BlueprintData bp, double offsetX, double offsetY)
        {
            var res = bp.Clone();
            foreach (var b in res.Buildings)
            {
                b.LocalOffset[0].X += offsetX;
                b.LocalOffset[1].X += offsetX;
                b.LocalOffset[0].Y += offsetY;
                b.LocalOffset[1].Y += offsetY;
            }
            return res;
        }
    }
}
```

- [x] **Step 4: 运行测试确认通过**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintTransformTests
```

Expected: PASS。

- [x] **Step 5: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/Blueprint/BlueprintTransform.cs dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs
git commit -m "feat(dsp-mod): 实现水平偏移变换"
```

---

### Task 7: BlueprintTransform.VerticalOffset（垂直偏移，含卡地基浮空）

**Files:**
- Modify: `dsp-mod/Blueprint/BlueprintTransform.cs`
- Modify: `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs`

**Interfaces:**
- Consumes: `BuildingMeta.IsHanging/IsInserterSlotBuild`（Task 5）。
- Produces: `BlueprintTransform.VerticalOffset(BlueprintData, double offsetZ) -> BlueprintData`。

- [x] **Step 1: 写失败的测试**

在 `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs` 里追加（`BuildFixture` 复用 Task 6 已有的私有方法）：

```csharp
        [Fact]
        public void VerticalOffset_AddsBaseAndReordersWhenFloating()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.VerticalOffset(bp, 2);

            // 制造台(itemId 2303)悬空超过阈值且不可悬空建造，需要卡地基，
            // 又因为它是"带分拣器插槽的建筑"要挪到最前面，原真实 Home.vue 逻辑跑出的顺序如下：
            Assert.Equal(3, result.Buildings.Count);
            Assert.Equal(2303, result.Buildings[0].ItemId);
            Assert.Equal(2, result.Buildings[0].InputObjIdx);
            Assert.Equal(2001, result.Buildings[1].ItemId);
            Assert.Equal(-1, result.Buildings[1].InputObjIdx); // 传送带可悬空，不需要地基
            Assert.Equal(1131, result.Buildings[2].ItemId); // 自动补的地基
            Assert.Equal(-10, result.Buildings[2].LocalOffset[0].Z, 4);

            // index 已按新顺序重新映射
            Assert.Equal(0, result.Buildings[0].Index);
            Assert.Equal(1, result.Buildings[1].Index);
            Assert.Equal(2, result.Buildings[2].Index);
        }
```

- [x] **Step 2: 运行测试确认失败**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter VerticalOffset_AddsBaseAndReordersWhenFloating
```

Expected: 编译失败（找不到 `VerticalOffset`）。

- [x] **Step 3: 实现 VerticalOffset**

在 `dsp-mod/Blueprint/BlueprintTransform.cs` 的 `BlueprintTransform` 类里追加：

```csharp
        public static BlueprintData VerticalOffset(BlueprintData bp, double offsetZ)
        {
            var res = bp.Clone();
            bool needBase = false;
            bool changeIndex = false;
            var newBuildings = new System.Collections.Generic.List<BlueprintBuilding>();

            foreach (var v in res.Buildings)
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
                    v.InputObjIdx = res.Buildings.Count; // 卡浮空，底指向即将追加的地基
                    needBase = true;
                    if (BuildingMeta.IsInserterSlotBuild(v.ItemId))
                    {
                        // 卡浮空的建筑如果先建分拣器会导致输出端连接失效，挪到最前面确保比分拣器先创建
                        newBuildings.Insert(0, v);
                        changeIndex = true;
                        continue;
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

        private static void FormatIndex(System.Collections.Generic.List<BlueprintBuilding> buildings)
        {
            var indexMap = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < buildings.Count; i++)
                indexMap[buildings[i].Index] = i;

            foreach (var v in buildings)
            {
                v.Index = indexMap[v.Index];
                if (v.OutputObjIdx != -1) v.OutputObjIdx = indexMap[v.OutputObjIdx];
                if (v.InputObjIdx != -1) v.InputObjIdx = indexMap[v.InputObjIdx];
            }
        }
```

（`BlueprintData.Buildings` 的 setter 需要是可写属性；Task 3 里已经用 `public List<BlueprintBuilding> Buildings = ...` 字段形式，可直接赋值 `res.Buildings = newBuildings;`，无需额外改动。）

- [x] **Step 4: 运行测试确认通过**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter BlueprintTransformTests
```

Expected: 全部 PASS（包括 Task 6 的用例）。

- [x] **Step 5: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/Blueprint/BlueprintTransform.cs dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs
git commit -m "feat(dsp-mod): 实现垂直偏移变换(含卡地基浮空)"
```

---

### Task 8: BlueprintTransform.LinearTransformation（线性变换：缩放/翻转/旋转）

**Files:**
- Modify: `dsp-mod/Blueprint/BlueprintTransform.cs`
- Modify: `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs`

**Interfaces:**
- Consumes: `BuildingMeta.*`（Task 5）。
- Produces: `BlueprintTransform.LinearTransformation(BlueprintData, double zoomX, double zoomY, double rotateDeg) -> BlueprintData`，供 UI（Task 10）的"横向翻转/纵向翻转/线性变换"调用（翻转就是 `zoomX`或`zoomY`传 `-1`，`rotate`传 `0`）。

- [ ] **Step 1: 写失败的测试**

在 `dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs` 里追加：

```csharp
        [Fact]
        public void LinearTransformation_ScaleAndRotate_MatchesReferenceOutput()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.LinearTransformation(bp, 2, 1, 45);

            Assert.Equal(7, result.Areas[0].Size.X);
            Assert.Equal(7, result.Areas[0].Size.Y);
            Assert.Equal(7, result.DragBoxSize.X);
            Assert.Equal(7, result.DragBoxSize.Y);
            Assert.Equal(3, result.CursorOffset.X);
            Assert.Equal(3, result.CursorOffset.Y);

            Assert.Equal(0.35355339059327384, result.Buildings[0].LocalOffset[0].X, 10);
            Assert.Equal(1.0606601717798212, result.Buildings[0].LocalOffset[0].Y, 10);
            Assert.Equal(45, result.Buildings[0].Yaw[0], 6);

            Assert.Equal(-3.7123106012293747, result.Buildings[1].LocalOffset[0].X, 10);
            Assert.Equal(-0.5303300858899103, result.Buildings[1].LocalOffset[0].Y, 10);
            Assert.Equal(-45, result.Buildings[1].Yaw[0], 6);
        }

        [Fact]
        public void LinearTransformation_HorizontalFlip_MatchesReferenceOutput()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.LinearTransformation(bp, -1, 1, 0);

            Assert.Equal(3, result.Areas[0].Size.X);
            Assert.Equal(3, result.Areas[0].Size.Y);

            // 传送带(2001)：横向翻转后 x 取反，朝向取反，倾斜角取反
            Assert.Equal(-0.5, result.Buildings[0].LocalOffset[0].X, 6);
            Assert.Equal(0.5, result.Buildings[0].LocalOffset[0].Y, 6);
            Assert.Equal(-90, result.Buildings[0].Yaw[0], 6);
            Assert.Equal(0, result.Buildings[0].Tilt, 6);

            // 制造台(2303)：横向翻转后 x 取反，朝向取反(0 保持 0)
            Assert.Equal(1.5, result.Buildings[1].LocalOffset[0].X, 6);
            Assert.Equal(2.25, result.Buildings[1].LocalOffset[0].Y, 6);
            Assert.Equal(0, result.Buildings[1].Yaw[0], 6);
        }
```

- [ ] **Step 2: 运行测试确认失败**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests --filter LinearTransformation
```

Expected: 编译失败（找不到 `LinearTransformation`）。

- [ ] **Step 3: 实现 LinearTransformation**

在 `dsp-mod/Blueprint/BlueprintTransform.cs` 的 `BlueprintTransform` 类里追加：

```csharp
        public static BlueprintData LinearTransformation(BlueprintData bp, double zoomX, double zoomY, double rotateDeg)
        {
            var res = bp.Clone();

            double w = res.Areas[0].Size.X * System.Math.Abs(zoomX);
            double h = res.Areas[0].Size.Y * System.Math.Abs(zoomY);
            double absRotRad = System.Math.Abs(rotateDeg) * System.Math.PI / 180.0;
            int wSize = (int)System.Math.Ceiling(w * System.Math.Cos(absRotRad) + h * System.Math.Sin(absRotRad));
            int hSize = (int)System.Math.Ceiling(w * System.Math.Sin(absRotRad) + h * System.Math.Cos(absRotRad));
            res.Areas[0].Size.X = wSize;
            res.Areas[0].Size.Y = hSize;
            res.DragBoxSize.X = wSize;
            res.DragBoxSize.Y = hSize;
            res.CursorOffset.X = wSize / 2; // 整数除法截断，对应 JS 的 ~~(W/2)
            res.CursorOffset.Y = hSize / 2;

            bool overturnX = zoomX < 0;
            bool overturnY = zoomY < 0;
            bool isOverturn = overturnX ^ overturnY;

            var beltSlotBuildIndexes = new System.Collections.Generic.HashSet<int>();
            var inserterSlotBuildIndexes = new System.Collections.Generic.HashSet<int>();

            if (isOverturn)
            {
                foreach (var v in res.Buildings)
                {
                    if (BuildingMeta.IsBeltSlotBuild(v.ItemId))
                    {
                        if (BuildingMeta.GetBeltSlotBuildAxis(v.ItemId, v.ModelIndex) == SlotAxis.X)
                        {
                            v.Yaw[0] -= 180;
                            v.Yaw[1] -= 180;
                        }
                        // 网页版此处还会调换 parameters.priority/slots，本项目 Parameters 按原样透传，见 Global Constraints
                        beltSlotBuildIndexes.Add(v.Index);
                    }
                    if (BuildingMeta.IsInserterSlotBuild(v.ItemId))
                    {
                        if (BuildingMeta.GetInserterSlotBuildAxis(v.ItemId) == SlotAxis.X)
                        {
                            v.Yaw[0] -= 180;
                            v.Yaw[1] -= 180;
                        }
                        inserterSlotBuildIndexes.Add(v.Index);
                    }
                }
            }

            double rotateRad = rotateDeg * System.Math.PI / 180.0;
            foreach (var v in res.Buildings)
            {
                double x = zoomX * v.LocalOffset[0].X, x2 = zoomX * v.LocalOffset[1].X;
                double y = zoomY * v.LocalOffset[0].Y, y2 = zoomY * v.LocalOffset[1].Y;

                v.LocalOffset[0].X = x * System.Math.Cos(rotateRad) - y * System.Math.Sin(rotateRad);
                v.LocalOffset[1].X = x2 * System.Math.Cos(rotateRad) - y2 * System.Math.Sin(rotateRad);
                v.LocalOffset[0].Y = x * System.Math.Sin(rotateRad) + y * System.Math.Cos(rotateRad);
                v.LocalOffset[1].Y = x2 * System.Math.Sin(rotateRad) + y2 * System.Math.Cos(rotateRad);

                if (overturnX) { v.Yaw[0] = -v.Yaw[0]; v.Yaw[1] = -v.Yaw[1]; }
                if (overturnY) { v.Yaw[0] = 180 - v.Yaw[0]; v.Yaw[1] = 180 - v.Yaw[1]; }
                v.Yaw[0] -= rotateDeg;
                v.Yaw[1] -= rotateDeg;

                if (isOverturn)
                {
                    if (v.ItemId == 2204 || v.ItemId == 2211)
                    {
                        const double offsetX = 1, offsetY = -1;
                        v.LocalOffset[0].X += offsetX * System.Math.Cos(v.Yaw[0] * System.Math.PI / 180.0);
                        v.LocalOffset[1].X += offsetX * System.Math.Cos(v.Yaw[1] * System.Math.PI / 180.0);
                        v.LocalOffset[0].Y += offsetY * System.Math.Sin(v.Yaw[0] * System.Math.PI / 180.0);
                        v.LocalOffset[1].Y += offsetY * System.Math.Sin(v.Yaw[1] * System.Math.PI / 180.0);
                    }
                    else if (v.ItemId == 2309 || v.ItemId == 2317)
                    {
                        const double offsetX = -1, offsetY = -1;
                        v.LocalOffset[0].X += offsetX * System.Math.Sin(v.Yaw[0] * System.Math.PI / 180.0);
                        v.LocalOffset[1].X += offsetX * System.Math.Sin(v.Yaw[1] * System.Math.PI / 180.0);
                        v.LocalOffset[0].Y += offsetY * System.Math.Cos(v.Yaw[0] * System.Math.PI / 180.0);
                        v.LocalOffset[1].Y += offsetY * System.Math.Cos(v.Yaw[1] * System.Math.PI / 180.0);
                    }
                    else if (BuildingMeta.IsBelt(v.ItemId))
                    {
                        v.Tilt = -v.Tilt;
                        v.Tilt2 = -v.Tilt2;
                        if (beltSlotBuildIndexes.Contains(v.InputObjIdx))
                        {
                            var inputBuild = res.Buildings[v.InputObjIdx];
                            var newInputSlot = BuildingMeta.AlterBeltSlot(inputBuild.ItemId, inputBuild.ModelIndex, v.InputFromSlot);
                            if (newInputSlot.HasValue) v.InputFromSlot = (sbyte)newInputSlot.Value;
                        }
                        if (beltSlotBuildIndexes.Contains(v.OutputObjIdx))
                        {
                            var outputBuild = res.Buildings[v.OutputObjIdx];
                            var newOutputSlot = BuildingMeta.AlterBeltSlot(outputBuild.ItemId, outputBuild.ModelIndex, v.OutputToSlot);
                            if (newOutputSlot.HasValue) v.OutputToSlot = (sbyte)newOutputSlot.Value;
                        }
                    }
                    else if (BuildingMeta.IsInserter(v.ItemId))
                    {
                        if (inserterSlotBuildIndexes.Contains(v.InputObjIdx))
                        {
                            var inputItemId = res.Buildings[v.InputObjIdx].ItemId;
                            var newInputSlot = BuildingMeta.AlterInserterSlot(inputItemId, v.InputFromSlot);
                            if (newInputSlot.HasValue) v.InputFromSlot = (sbyte)newInputSlot.Value;
                        }
                        if (inserterSlotBuildIndexes.Contains(v.OutputObjIdx))
                        {
                            var outputItemId = res.Buildings[v.OutputObjIdx].ItemId;
                            var newOutputSlot = BuildingMeta.AlterInserterSlot(outputItemId, v.OutputToSlot);
                            if (newOutputSlot.HasValue) v.OutputToSlot = (sbyte)newOutputSlot.Value;
                        }
                    }
                }
            }

            return res;
        }
```

- [ ] **Step 4: 运行测试确认通过**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet test Blueprint.Tests
```

Expected: 全部测试（Task 2-8 累计）PASS。

- [ ] **Step 5: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/Blueprint/BlueprintTransform.cs dsp-mod/Blueprint.Tests/BlueprintTransformTests.cs
git commit -m "feat(dsp-mod): 实现线性变换(缩放/翻转/旋转)"
```

> 到此为止，`Blueprint` 核心库功能完整且已用 golden fixture 验证，不依赖游戏本体，可在当前 macOS 环境完全跑通。Task 9 起需要 Windows 游戏机的 `Managed` DLL，见下方说明。

---

### Task 9: BepInEx Plugin 工程骨架

**Files:**
- Create: `dsp-mod/local.props.example`
- Create: `dsp-mod/Plugin/Plugin.csproj`
- Create: `dsp-mod/Plugin/Plugin.cs`

**Interfaces:**
- Consumes: `Blueprint.csproj`（Task 1-8 产出的核心库，作为 ProjectReference）。
- Produces: 一个可被 BepInEx 5.4.23.5 加载的插件骨架（`BlueprintTransformPlugin`），供 Task 10 挂载 `TransformWindow`。

**前置条件（阻塞点）**：需要 Windows 游戏机上 `DSP 安装目录/DSPGAME_Data/Managed/` 的路径，本设计不需要 `Assembly-CSharp.dll`，只需要通用 `UnityEngine.*.dll`。如果当前机器还没有这份目录（比如需要从 Windows 机器拷贝或建共享），先完成本任务的代码编写，**编译验证步骤（Step 4）留到拿到 DLL 后再跑**，不要因为编译不过而回退已写好的代码。

- [ ] **Step 1: 创建 local.props 模板**

创建 `dsp-mod/local.props.example`（提交到 git，作为团队协作的模板）：

```xml
<Project>
  <PropertyGroup>
    <!-- 复制为同目录下的 local.props（已在 .gitignore 里排除），
         改成你机器上 DSP 游戏的 DSPGAME_Data/Managed 目录实际路径 -->
    <DspManagedDir>C:\Path\To\SteamLibrary\steamapps\common\Dyson Sphere Program\DSPGAME_Data\Managed</DspManagedDir>
  </PropertyGroup>
</Project>
```

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
cp local.props.example local.props
# 然后手动编辑 local.props，把 DspManagedDir 改成你机器上的真实路径
```

- [ ] **Step 2: 创建 Plugin 工程**

创建 `dsp-mod/Plugin/Plugin.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AssemblyName>DspBlueprintTransform</AssemblyName>
    <RootNamespace>DspBlueprintTransform.Plugin</RootNamespace>
  </PropertyGroup>

  <Import Project="..\local.props" Condition="Exists('..\local.props')" />

  <Target Name="EnsureLocalProps" BeforeTargets="Build">
    <Error Condition="'$(DspManagedDir)' == ''"
           Text="缺少 dsp-mod/local.props：请复制 local.props.example 为 local.props，并填入 DSP 游戏的 DSPGAME_Data/Managed 目录路径。" />
  </Target>

  <ItemGroup>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(DspManagedDir)\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.IMGUIModule">
      <HintPath>$(DspManagedDir)\UnityEngine.IMGUIModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.InputLegacyModule">
      <HintPath>$(DspManagedDir)\UnityEngine.InputLegacyModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx">
      <HintPath>..\..\3rd\BepInEx\BepInEx_win_x64_5.4.23.5\BepInEx\core\BepInEx.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>..\..\3rd\BepInEx\BepInEx_win_x64_5.4.23.5\BepInEx\core\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Blueprint\Blueprint.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: 写 Plugin 入口**

创建 `dsp-mod/Plugin/Plugin.cs`：

```csharp
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace DspBlueprintTransform.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class BlueprintTransformPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.lxthyme.dspblueprinttransform";
        public const string PluginName = "DSP Blueprint Transform";
        public const string PluginVersion = "0.1.0";

        private ConfigEntry<KeyboardShortcut> _toggleKey = null!;
        private TransformWindow _window = null!;

        private void Awake()
        {
            _toggleKey = Config.Bind(
                "General",
                "ToggleWindowKey",
                new KeyboardShortcut(KeyCode.F7),
                "打开/关闭蓝图变换窗口的快捷键");

            _window = gameObject.AddComponent<TransformWindow>();
            _window.enabled = false;

            Logger.LogInfo($"{PluginName} v{PluginVersion} 已加载");
        }

        private void Update()
        {
            if (_toggleKey.Value.IsDown())
                _window.enabled = !_window.enabled;
        }
    }
}
```

将 `dsp-mod/Plugin/Plugin.csproj` 加入解决方案：

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet sln add Plugin/Plugin.csproj
```

- [ ] **Step 4: 编译验证（需要 local.props 配好真实路径后才能跑通）**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet build Plugin/Plugin.csproj
```

Expected（`local.props` 未配置时）：MSBuild 报错 `缺少 dsp-mod/local.props...`，这是预期行为，说明前置条件检查生效。
Expected（`local.props` 配置了真实 `DspManagedDir` 后）：`Build succeeded.`（此时 `TransformWindow` 还不存在，会在 Task 10 补上——如果 Task 9/10 是连续执行，可以先跳过本步骤的最终验证，等 Task 10 完成后一起编译）。

- [ ] **Step 5: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/local.props.example dsp-mod/Plugin/Plugin.csproj dsp-mod/Plugin/Plugin.cs dsp-mod/DspBlueprintTransform.sln
git commit -m "feat(dsp-mod): 搭建 BepInEx Plugin 工程骨架"
```

---

### Task 10: TransformWindow（OnGUI 悬浮窗）

**Files:**
- Create: `dsp-mod/Plugin/TransformWindow.cs`

**Interfaces:**
- Consumes: `BlueprintParser.FromStr/ToStr`（Task 4）、`BlueprintTransform.HorizontalOffset/VerticalOffset/LinearTransformation`（Task 6-8）。
- Produces: `TransformWindow`（`MonoBehaviour`），由 Task 9 的 `Plugin.cs` 通过 `gameObject.AddComponent<TransformWindow>()` 挂载。

**前置条件**：同 Task 9，需要 `local.props` 配好真实 `DspManagedDir` 才能编译验证；本任务只负责写代码。

- [ ] **Step 1: 实现 TransformWindow**

创建 `dsp-mod/Plugin/TransformWindow.cs`：

```csharp
using System;
using UnityEngine;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Plugin
{
    public class TransformWindow : MonoBehaviour
    {
        private Rect _windowRect = new Rect(100, 100, 420, 560);
        private string _inputCode = "";
        private string _outputCode = "";
        private string _statusMessage = "";
        private bool _statusIsError;

        private string _offsetX = "0";
        private string _offsetY = "0";
        private string _offsetZ = "0";
        private string _zoomX = "1";
        private string _zoomY = "1";
        private string _rotate = "0";

        private BlueprintData? _parsed;

        private void OnGUI()
        {
            _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, DrawWindow, "蓝图变换");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("蓝图码（粘贴或从剪贴板读取）");
            _inputCode = GUILayout.TextArea(_inputCode, GUILayout.Height(80));

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
            GUILayout.Label("坐标偏移");
            DrawLabeledField("横向偏移 X", ref _offsetX);
            DrawLabeledField("纵向偏移 Y", ref _offsetY);
            DrawLabeledField("垂直偏移 Z", ref _offsetZ);
            if (GUILayout.Button("应用坐标偏移")) ApplyOffset();

            GUILayout.Space(8);
            GUILayout.Label("水平翻转");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("横向翻转")) ApplyLinearTransformation(-1, 1, 0);
            if (GUILayout.Button("纵向翻转")) ApplyLinearTransformation(1, -1, 0);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("线性变换");
            DrawLabeledField("横向缩放量", ref _zoomX);
            DrawLabeledField("纵向缩放量", ref _zoomY);
            DrawLabeledField("旋转角度(-360~360)", ref _rotate);
            if (GUILayout.Button("应用线性变换")) ApplyLinearTransformationFromFields();

            GUILayout.Space(8);
            GUILayout.Label("输出蓝图码");
            GUILayout.TextArea(_outputCode, GUILayout.Height(80));
            if (GUILayout.Button("复制到剪贴板")) GUIUtility.systemCopyBuffer = _outputCode;

            GUI.DragWindow();
        }

        private static void DrawLabeledField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150));
            value = GUILayout.TextField(value);
            GUILayout.EndHorizontal();
        }

        private void TryParse()
        {
            try
            {
                _parsed = BlueprintParser.FromStr(_inputCode);
                _statusMessage = $"解析成功：{_parsed.Buildings.Count} 个建筑";
                _statusIsError = false;
            }
            catch (Exception ex)
            {
                _parsed = null;
                _statusMessage = $"解析失败：{ex.Message}";
                _statusIsError = true;
            }
        }

        private void ApplyOffset()
        {
            if (!EnsureParsed()) return;
            double x = ParseOrZero(_offsetX);
            double y = ParseOrZero(_offsetY);
            double z = ParseOrZero(_offsetZ);
            var afterHorizontal = BlueprintTransform.HorizontalOffset(_parsed!, x, y);
            var afterVertical = BlueprintTransform.VerticalOffset(afterHorizontal, z);
            Finish(afterVertical);
        }

        private void ApplyLinearTransformationFromFields()
        {
            if (!EnsureParsed()) return;
            double zoomX = ParseOrZero(_zoomX, 1);
            double zoomY = ParseOrZero(_zoomY, 1);
            double rotate = ParseOrZero(_rotate, 0);
            ApplyLinearTransformation(zoomX, zoomY, rotate);
        }

        private void ApplyLinearTransformation(double zoomX, double zoomY, double rotate)
        {
            if (!EnsureParsed()) return;
            var result = BlueprintTransform.LinearTransformation(_parsed!, zoomX, zoomY, rotate);
            Finish(result);
        }

        private bool EnsureParsed()
        {
            if (_parsed != null) return true;
            _statusMessage = "请先粘贴蓝图码并点击「解析」";
            _statusIsError = true;
            return false;
        }

        private void Finish(BlueprintData result)
        {
            _parsed = result;
            _outputCode = BlueprintParser.ToStr(result);
            GUIUtility.systemCopyBuffer = _outputCode;
            _statusMessage = "已应用变换并复制到剪贴板";
            _statusIsError = false;
        }

        private static double ParseOrZero(string s, double fallback = 0)
            => double.TryParse(s, out var v) ? v : fallback;
    }
}
```

- [ ] **Step 2: 编译验证（需要 local.props 配好真实路径）**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp/dsp-mod"
dotnet build Plugin/Plugin.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/Plugin/TransformWindow.cs
git commit -m "feat(dsp-mod): 实现蓝图变换悬浮窗 UI"
```

- [ ] **Step 4: 游戏内手动验证（必须在 Windows 机器上做，本计划范围外）**

在 Windows 机器上把编译产物（见 Task 11）放进 `BepInEx/plugins/`，启动游戏，验证：
1. 按 F7 能打开/关闭悬浮窗
2. 游戏内复制一个真实蓝图，粘贴进窗口，点「解析」不报错
3. 依次测试坐标偏移、横向/纵向翻转、线性变换（含负数缩放），每次「应用」后剪贴板里的新蓝图码能在游戏里正常粘贴，建筑位置/朝向符合预期，翻转后火电/化工厂等建筑分拣器接口仍能连接

---

### Task 11: 打包与部署说明

**Files:**
- Create: `dsp-mod/README.md`

**Interfaces:**
- 无代码接口，纯文档，指导如何把 Task 1-10 的产物部署到 Windows 游戏机。

- [ ] **Step 1: 编写 README**

创建 `dsp-mod/README.md`：

```markdown
# DSP Blueprint Transform

戴森球计划(DSP)游戏内蓝图变换 mod：坐标偏移、水平翻转、线性变换（缩放+旋转）。
移植自网页版 [edit-dspblue-print](https://github.com/cying314/edit-dspblue-print)。

## 开发环境准备

1. 安装 dotnet SDK：`brew install --cask dotnet-sdk`（macOS）
2. 复制 `local.props.example` 为 `local.props`，把 `DspManagedDir` 改成你机器上
   `DSP 安装目录/DSPGAME_Data/Managed` 的实际路径（需要从装有游戏的 Windows 机器拷贝这个目录，
   或者建立局域网共享）

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
```

- [ ] **Step 2: Commit**

```bash
cd "/Users/lxthyme/Desktop/Lucky/Obsidian/dsp"
git add dsp-mod/README.md
git commit -m "docs(dsp-mod): 添加构建与部署说明"
```
