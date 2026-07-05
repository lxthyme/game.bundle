using System.Collections.Generic;
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

        [Fact]
        public void ChainedHorizontalThenVerticalOffset_PreservesIndexIntegrity()
        {
            // 模拟 TransformWindow.ApplyOffset：先水平偏移，再把结果喂给垂直偏移
            var bp = BuildFixture();
            var afterHorizontal = BlueprintTransform.HorizontalOffset(bp, 5, -3);
            var result = BlueprintTransform.VerticalOffset(afterHorizontal, 2);

            Assert.Equal(3, result.Buildings.Count);

            // X/Y 应反映水平偏移后的坐标(5.5,-2.5 / 3.5,-0.75)，而非原始坐标，
            // 说明水平偏移确实先于垂直偏移生效
            Assert.Equal(2303, result.Buildings[0].ItemId);
            Assert.Equal(3.5, result.Buildings[0].LocalOffset[0].X, 4);
            Assert.Equal(-0.75, result.Buildings[0].LocalOffset[0].Y, 4);
            Assert.Equal(2, result.Buildings[0].InputObjIdx);
            Assert.Equal(0, result.Buildings[0].Index);

            Assert.Equal(2001, result.Buildings[1].ItemId);
            Assert.Equal(5.5, result.Buildings[1].LocalOffset[0].X, 4);
            Assert.Equal(-2.5, result.Buildings[1].LocalOffset[0].Y, 4);
            Assert.Equal(-1, result.Buildings[1].InputObjIdx); // 传送带可悬空，不需要地基
            Assert.Equal(1, result.Buildings[1].Index);

            Assert.Equal(1131, result.Buildings[2].ItemId); // 自动补的地基
            Assert.Equal(-10, result.Buildings[2].LocalOffset[0].Z, 4);
            Assert.Equal(2, result.Buildings[2].Index);
        }

        [Fact]
        public void HorizontalOffset_WithTargetIndex_OnlyMovesThatBuilding()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.HorizontalOffset(bp, 5, -3, targetIndices: new HashSet<int> { 0 });

            Assert.Equal(5.5, result.Buildings[0].LocalOffset[0].X, 4);
            Assert.Equal(-2.5, result.Buildings[0].LocalOffset[0].Y, 4);

            // 未指定的建筑保持原样
            Assert.Equal(-1.5, result.Buildings[1].LocalOffset[0].X, 4);
            Assert.Equal(2.25, result.Buildings[1].LocalOffset[0].Y, 4);
        }

        [Fact]
        public void VerticalOffset_WithTargetIndex_OnlyLiftsThatBuildingAndStillAddsBaseIfNeeded()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.VerticalOffset(bp, 2, targetIndices: new HashSet<int> { 1 });

            // 只有 index 1（制造台 2303）被抬升，触发悬空检测并补地基；
            // 2303 带分拣器插槽，需挪到最前面（与全量抬升时的重排规则一致）
            Assert.Equal(3, result.Buildings.Count);
            Assert.Equal(2303, result.Buildings[0].ItemId);
            Assert.Equal(2, result.Buildings[0].LocalOffset[0].Z, 4);
            Assert.Equal(2001, result.Buildings[1].ItemId);
            Assert.Equal(0, result.Buildings[1].LocalOffset[0].Z, 4); // 未指定的建筑 Z 不变
            Assert.Equal(1131, result.Buildings[2].ItemId);
        }

        [Fact]
        public void LinearTransformation_OnPreviouslyOffsetBlueprint_PreservesConnectionIndices()
        {
            var bp = BuildFixture();
            var afterOffset = BlueprintTransform.HorizontalOffset(bp, 5, -3);
            var result = BlueprintTransform.LinearTransformation(afterOffset, -1, 1, 0);

            // 传送带(2001)：翻转基于偏移后坐标(5.5,-2.5)计算，而非原始坐标(0.5,0.5)
            Assert.Equal(-5.5, result.Buildings[0].LocalOffset[0].X, 6);
            Assert.Equal(-2.5, result.Buildings[0].LocalOffset[0].Y, 6);
            Assert.Equal(-90, result.Buildings[0].Yaw[0], 6);
            Assert.Equal(0, result.Buildings[0].Tilt, 6);
            Assert.Equal(-1, result.Buildings[0].InputObjIdx);
            Assert.Equal(-1, result.Buildings[0].OutputObjIdx);

            // 制造台(2303)：翻转基于偏移后坐标(3.5,-0.75)计算
            Assert.Equal(-3.5, result.Buildings[1].LocalOffset[0].X, 6);
            Assert.Equal(-0.75, result.Buildings[1].LocalOffset[0].Y, 6);
            Assert.Equal(0, result.Buildings[1].Yaw[0], 6);
            Assert.Equal(-1, result.Buildings[1].InputObjIdx);
            Assert.Equal(-1, result.Buildings[1].OutputObjIdx);
        }

        [Fact]
        public void HorizontalOffset_WithMultipleTargetIndices_OnlyMovesSpecifiedBuildings()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.HorizontalOffset(bp, 5, -3, targetIndices: new HashSet<int> { 0, 1 });

            Assert.Equal(5.5, result.Buildings[0].LocalOffset[0].X, 4);
            Assert.Equal(-2.5, result.Buildings[0].LocalOffset[0].Y, 4);
            Assert.Equal(3.5, result.Buildings[1].LocalOffset[0].X, 4);
            Assert.Equal(-0.75, result.Buildings[1].LocalOffset[0].Y, 4);
        }

        [Fact]
        public void HorizontalOffset_WithEmptyTargetIndices_MovesNone()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.HorizontalOffset(bp, 5, -3, targetIndices: new HashSet<int>());

            Assert.Equal(0.5, result.Buildings[0].LocalOffset[0].X, 4);
            Assert.Equal(0.5, result.Buildings[0].LocalOffset[0].Y, 4);
            Assert.Equal(-1.5, result.Buildings[1].LocalOffset[0].X, 4);
            Assert.Equal(2.25, result.Buildings[1].LocalOffset[0].Y, 4);
        }

        [Fact]
        public void HorizontalOffset_WithNullTargetIndices_MovesAll()
        {
            var bp = BuildFixture();
            var result = BlueprintTransform.HorizontalOffset(bp, 5, -3, targetIndices: null);

            Assert.Equal(5.5, result.Buildings[0].LocalOffset[0].X, 4);
            Assert.Equal(3.5, result.Buildings[1].LocalOffset[0].X, 4);
        }

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
    }
}
