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
