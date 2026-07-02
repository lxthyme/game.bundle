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
