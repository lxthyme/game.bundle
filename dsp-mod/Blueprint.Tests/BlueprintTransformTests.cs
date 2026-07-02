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
    }
}
