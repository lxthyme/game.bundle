namespace DspBlueprintTransform.Blueprint
{
    public static class BlueprintTransform
    {
        public static BlueprintData HorizontalOffset(BlueprintData bp, double offsetX, double offsetY, System.Collections.Generic.HashSet<int>? targetIndices = null)
        {
            var res = bp.Clone();
            foreach (var b in res.Buildings)
            {
                if (targetIndices != null && !targetIndices.Contains(b.Index)) continue;
                b.LocalOffset[0].X += offsetX;
                b.LocalOffset[1].X += offsetX;
                b.LocalOffset[0].Y += offsetY;
                b.LocalOffset[1].Y += offsetY;
            }
            return res;
        }

        public static BlueprintData VerticalOffset(BlueprintData bp, double offsetZ, System.Collections.Generic.HashSet<int>? targetIndices = null)
        {
            var res = bp.Clone();
            bool needBase = false;
            bool changeIndex = false;
            var newBuildings = new System.Collections.Generic.List<BlueprintBuilding>();

            foreach (var v in res.Buildings)
            {
                if (targetIndices == null || targetIndices.Contains(v.Index))
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

        public static BlueprintData ReverseBeltDirection(BlueprintData bp, System.Collections.Generic.HashSet<int>? targetIndices = null)
        {
            var res = bp.Clone();
            foreach (var b in res.Buildings)
            {
                if (targetIndices != null && !targetIndices.Contains(b.Index)) continue;

                (b.OutputObjIdx, b.InputObjIdx) = (b.InputObjIdx, b.OutputObjIdx);
                (b.OutputToSlot, b.InputFromSlot) = (b.InputFromSlot, b.OutputToSlot);
                (b.OutputFromSlot, b.InputToSlot) = (b.InputToSlot, b.OutputFromSlot);
                (b.OutputOffset, b.InputOffset) = (b.InputOffset, b.OutputOffset);

                b.Yaw[0] += 180;
                b.Yaw[1] += 180;
                b.Tilt = -b.Tilt;
                b.Tilt2 = -b.Tilt2;
            }
            return res;
        }

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
                        // 依赖 res.Buildings[i].Index == i（列表位置即对象索引）；该不变量由 parser 输出保证，本方法不做重排
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
