using System;
using System.IO;
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

        [Fact]
        public void ToStr_FromStr_RoundTripsReformDataWithoutLoss()
        {
            // 构造一份携带地基/地形改造数据的蓝图，验证 ToStr -> FromStr 不会丢失该数据
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)0); // 预留字段
                w.Write(1); // rectLen
                w.Write((byte)0); // rect 预留字段
                w.Write((short)3); // x
                w.Write((short)4); // y
                w.Write((byte)2); // w
                w.Write((byte)2); // h
                w.Write((byte)0x21); // type/color
                w.Write((byte)0); // areaIndex
                w.Write((uint)0); // customReformColorMask
                w.Write(2); // colorLen
                w.Write((uint)1);
                w.Write((uint)2);
            }
            byte[] reformData = ms.ToArray();

            var bp = new BlueprintData
            {
                Version = 2,
                Header = new BlueprintHeader { GameVersion = "0.10.34.28281" },
                ReformData = reformData,
            };
            bp.Areas.Add(new BlueprintArea { Index = 0, ParentIndex = -1, Size = new Vec2I { X = 1, Y = 1 } });

            string encoded = BlueprintParser.ToStr(bp);
            var reDecoded = BlueprintParser.FromStr(encoded);

            Assert.Equal(reformData, reDecoded.ReformData);
        }

        [Fact]
        public void ToStr_FromStr_RoundTripsWithoutReformData()
        {
            var bp = new BlueprintData
            {
                Version = 2,
                Header = new BlueprintHeader { GameVersion = "0.10.34.28281" },
            };
            bp.Areas.Add(new BlueprintArea { Index = 0, ParentIndex = -1, Size = new Vec2I { X = 1, Y = 1 } });

            string encoded = BlueprintParser.ToStr(bp);
            var reDecoded = BlueprintParser.FromStr(encoded);

            Assert.Null(reDecoded.ReformData);
        }
    }
}
