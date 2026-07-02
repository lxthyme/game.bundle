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
