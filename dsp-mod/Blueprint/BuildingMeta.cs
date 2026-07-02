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
