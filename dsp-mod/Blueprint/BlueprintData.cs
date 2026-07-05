using System;
using System.Collections.Generic;

namespace DspBlueprintTransform.Blueprint
{
    public sealed class Vec2I
    {
        public int X;
        public int Y;
    }

    public sealed class Vec3D
    {
        public double X;
        public double Y;
        public double Z;
    }

    public sealed class BlueprintHeader
    {
        public int Layout;
        public int[] Icons = new int[5];
        public DateTime Time = DateTime.MinValue;
        public string GameVersion = "";
        public string ShortDesc = "";
        public string Author = "";
        public string CustomVersion = "";
        public string ExternalFields = "";
        public string Desc = "";
    }

    public sealed class BlueprintArea
    {
        public sbyte Index;
        public sbyte ParentIndex;
        public short TropicAnchor;
        public short AreaSegments;
        public Vec2I AnchorLocalOffset = new Vec2I();
        public Vec2I Size = new Vec2I();
    }

    public sealed class BlueprintBuilding
    {
        public int Index;
        public sbyte AreaIndex;
        public Vec3D[] LocalOffset = { new Vec3D(), new Vec3D() };
        public double[] Yaw = new double[2];
        public double Tilt;
        public double Tilt2;
        public double Pitch;
        public double Pitch2;
        public short ItemId;
        public short ModelIndex;
        public int OutputObjIdx = -1;
        public int InputObjIdx = -1;
        public sbyte OutputToSlot;
        public sbyte InputFromSlot;
        public sbyte OutputFromSlot;
        public sbyte InputToSlot;
        public sbyte OutputOffset;
        public sbyte InputOffset;
        public short RecipeId;
        public short FilterId;

        // 原始字节透传，不解析内部结构（见 Global Constraints）
        public byte[]? Parameters;
        public string? Content;
    }

    public sealed class BlueprintData
    {
        public int Version;
        public BlueprintHeader Header = new BlueprintHeader();
        public Vec2I CursorOffset = new Vec2I();
        public int CursorTargetArea;
        public Vec2I DragBoxSize = new Vec2I();
        public int PrimaryAreaIdx;
        public List<BlueprintArea> Areas = new List<BlueprintArea>();
        public List<BlueprintBuilding> Buildings = new List<BlueprintBuilding>();
        public int Patch;

        // 地基/地形改造数据，原始字节透传，不解析内部结构（同 Parameters/Content，见 Global Constraints）
        public byte[]? ReformData;

        public BlueprintData Clone()
        {
            var clone = new BlueprintData
            {
                Version = Version,
                Header = new BlueprintHeader
                {
                    Layout = Header.Layout,
                    Icons = (int[])Header.Icons.Clone(),
                    Time = Header.Time,
                    GameVersion = Header.GameVersion,
                    ShortDesc = Header.ShortDesc,
                    Author = Header.Author,
                    CustomVersion = Header.CustomVersion,
                    ExternalFields = Header.ExternalFields,
                    Desc = Header.Desc,
                },
                CursorOffset = new Vec2I { X = CursorOffset.X, Y = CursorOffset.Y },
                CursorTargetArea = CursorTargetArea,
                DragBoxSize = new Vec2I { X = DragBoxSize.X, Y = DragBoxSize.Y },
                PrimaryAreaIdx = PrimaryAreaIdx,
                Patch = Patch,
                ReformData = ReformData == null ? null : (byte[])ReformData.Clone(),
            };

            foreach (var a in Areas)
            {
                clone.Areas.Add(new BlueprintArea
                {
                    Index = a.Index,
                    ParentIndex = a.ParentIndex,
                    TropicAnchor = a.TropicAnchor,
                    AreaSegments = a.AreaSegments,
                    AnchorLocalOffset = new Vec2I { X = a.AnchorLocalOffset.X, Y = a.AnchorLocalOffset.Y },
                    Size = new Vec2I { X = a.Size.X, Y = a.Size.Y },
                });
            }

            foreach (var b in Buildings)
            {
                clone.Buildings.Add(new BlueprintBuilding
                {
                    Index = b.Index,
                    AreaIndex = b.AreaIndex,
                    LocalOffset = new[]
                    {
                        new Vec3D { X = b.LocalOffset[0].X, Y = b.LocalOffset[0].Y, Z = b.LocalOffset[0].Z },
                        new Vec3D { X = b.LocalOffset[1].X, Y = b.LocalOffset[1].Y, Z = b.LocalOffset[1].Z },
                    },
                    Yaw = new[] { b.Yaw[0], b.Yaw[1] },
                    Tilt = b.Tilt,
                    Tilt2 = b.Tilt2,
                    Pitch = b.Pitch,
                    Pitch2 = b.Pitch2,
                    ItemId = b.ItemId,
                    ModelIndex = b.ModelIndex,
                    OutputObjIdx = b.OutputObjIdx,
                    InputObjIdx = b.InputObjIdx,
                    OutputToSlot = b.OutputToSlot,
                    InputFromSlot = b.InputFromSlot,
                    OutputFromSlot = b.OutputFromSlot,
                    InputToSlot = b.InputToSlot,
                    OutputOffset = b.OutputOffset,
                    InputOffset = b.InputOffset,
                    RecipeId = b.RecipeId,
                    FilterId = b.FilterId,
                    Parameters = b.Parameters == null ? null : (byte[])b.Parameters.Clone(),
                    Content = b.Content,
                });
            }

            return clone;
        }
    }
}
