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
    }
}
