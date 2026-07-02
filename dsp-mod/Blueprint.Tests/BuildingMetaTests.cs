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
