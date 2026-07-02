using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DspBlueprintTransform.Blueprint
{
    public static class BlueprintParser
    {
        private const string Start = "BLUEPRINT:";

        public static BlueprintData FromStr(string strData)
        {
            if (!strData.StartsWith(Start, StringComparison.Ordinal))
                throw new FormatException("Invalid start");

            int p1 = strData.IndexOf('"', Start.Length);
            if (p1 < 0)
                throw new FormatException("Header terminator not found");

            string headerPart = strData.Substring(Start.Length, p1 - Start.Length);
            string[] cells = headerPart.Split(',');
            if (cells.Length < 15)
                throw new FormatException("Header too short");

            var header = new BlueprintHeader
            {
                Layout = int.Parse(cells[1], CultureInfo.InvariantCulture),
                Icons = new[]
                {
                    int.Parse(cells[2], CultureInfo.InvariantCulture),
                    int.Parse(cells[3], CultureInfo.InvariantCulture),
                    int.Parse(cells[4], CultureInfo.InvariantCulture),
                    int.Parse(cells[5], CultureInfo.InvariantCulture),
                    int.Parse(cells[6], CultureInfo.InvariantCulture),
                },
                // cells[8] 是 .NET DateTime.Ticks（游戏本身用 C# 写的，直接原生对应，不用走 JS 版的 epoch 换算）
                Time = new DateTime(long.Parse(cells[8], CultureInfo.InvariantCulture), DateTimeKind.Utc),
                GameVersion = cells[9],
                ShortDesc = Uri.UnescapeDataString(cells[10]),
                Author = Uri.UnescapeDataString(cells[11]),
                CustomVersion = Uri.UnescapeDataString(cells[12]),
                ExternalFields = Uri.UnescapeDataString(cells[13]),
                Desc = Uri.UnescapeDataString(cells[14]),
            };

            int p2 = strData.Length - 33;
            if (p2 < p1 || strData[p2] != '"')
                throw new FormatException("Checksum marker not found");

            string forChecksum = strData.Substring(0, p2);
            string expected = strData.Substring(p2 + 1);
            string actual = BlueprintChecksum.HexDigest(Encoding.ASCII.GetBytes(forChecksum));
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new FormatException($"Checksum mismatch: expected {expected}, got {actual}");

            string encoded = strData.Substring(p1 + 1, p2 - (p1 + 1));
            byte[] gzipped = Convert.FromBase64String(encoded);
            byte[] decoded = Gunzip(gzipped);

            using var ms = new MemoryStream(decoded);
            using var r = new BinaryReader(ms);

            var bp = new BlueprintData { Header = header };
            bp.Version = r.ReadInt32();
            bp.CursorOffset = new Vec2I { X = r.ReadInt32(), Y = r.ReadInt32() };
            bp.CursorTargetArea = r.ReadInt32();
            bp.DragBoxSize = new Vec2I { X = r.ReadInt32(), Y = r.ReadInt32() };
            bp.PrimaryAreaIdx = r.ReadInt32();

            int numAreas = r.ReadByte();
            for (int i = 0; i < numAreas; i++)
                bp.Areas.Add(ReadArea(r));

            int numBuildings = r.ReadInt32();
            for (int i = 0; i < numBuildings; i++)
                bp.Buildings.Add(ReadBuilding(r));

            if (bp.Version >= 2)
            {
                bp.Patch = r.ReadInt32();
                r.ReadByte(); // 是否存在地基数据；本项目不解析地基，读掉这个标记字节即可
            }

            return bp;
        }

        public static string ToStr(BlueprintData bp)
        {
            var sb = new StringBuilder();
            sb.Append(Start);
            sb.Append("1,");
            sb.Append(bp.Header.Layout).Append(',');
            foreach (int icon in bp.Header.Icons)
                sb.Append(icon).Append(',');
            sb.Append("0,");
            sb.Append(bp.Header.Time.Ticks).Append(',');
            sb.Append(bp.Header.GameVersion).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.ShortDesc)).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.Author)).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.CustomVersion)).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.ExternalFields)).Append(',');
            sb.Append(Uri.EscapeDataString(bp.Header.Desc));
            sb.Append('"');

            byte[] rawBytes;
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(bp.Version);
                    w.Write(bp.CursorOffset.X);
                    w.Write(bp.CursorOffset.Y);
                    w.Write(bp.CursorTargetArea);
                    w.Write(bp.DragBoxSize.X);
                    w.Write(bp.DragBoxSize.Y);
                    w.Write(bp.PrimaryAreaIdx);
                    w.Write((byte)bp.Areas.Count);
                    foreach (var a in bp.Areas)
                        WriteArea(w, a);
                    w.Write(bp.Buildings.Count);
                    foreach (var b in bp.Buildings)
                        WriteBuilding(w, b);
                    if (bp.Version >= 2)
                    {
                        w.Write(bp.Patch);
                        w.Write((byte)0); // 本项目不写出地基数据
                    }
                }
                rawBytes = ms.ToArray();
            }

            sb.Append(Convert.ToBase64String(Gzip(rawBytes)));

            string body = sb.ToString();
            string checksum = BlueprintChecksum.HexDigest(Encoding.ASCII.GetBytes(body));
            sb.Append('"').Append(checksum);
            return sb.ToString();
        }

        private static BlueprintArea ReadArea(BinaryReader r) => new BlueprintArea
        {
            Index = r.ReadSByte(),
            ParentIndex = r.ReadSByte(),
            TropicAnchor = r.ReadInt16(),
            AreaSegments = r.ReadInt16(),
            AnchorLocalOffset = new Vec2I { X = r.ReadInt16(), Y = r.ReadInt16() },
            Size = new Vec2I { X = r.ReadInt16(), Y = r.ReadInt16() },
        };

        private static void WriteArea(BinaryWriter w, BlueprintArea a)
        {
            w.Write(a.Index);
            w.Write(a.ParentIndex);
            w.Write(a.TropicAnchor);
            w.Write(a.AreaSegments);
            w.Write((short)a.AnchorLocalOffset.X);
            w.Write((short)a.AnchorLocalOffset.Y);
            w.Write((short)a.Size.X);
            w.Write((short)a.Size.Y);
        }

        private static double Fix(float v) => Math.Round((double)v, 4, MidpointRounding.AwayFromZero);

        private static Vec3D ReadXyz(BinaryReader r) => new Vec3D
        {
            X = Fix(r.ReadSingle()),
            Y = Fix(r.ReadSingle()),
            Z = Fix(r.ReadSingle()),
        };

        private static void WriteXyz(BinaryWriter w, Vec3D v)
        {
            w.Write((float)v.X);
            w.Write((float)v.Y);
            w.Write((float)v.Z);
        }

        private static BlueprintBuilding ReadBuilding(BinaryReader r)
        {
            int prefix = r.ReadInt32();
            if (prefix != -102)
                throw new FormatException(
                    $"仅支持当前游戏版本蓝图格式(-102 前缀)，实际读到前缀 {prefix}，可能是旧版本蓝图码或游戏已更新格式");

            var b = new BlueprintBuilding
            {
                Index = r.ReadInt32(),
                ItemId = r.ReadInt16(),
                ModelIndex = r.ReadInt16(),
                AreaIndex = r.ReadSByte(),
            };
            b.LocalOffset[0] = ReadXyz(r);
            b.Yaw[0] = Fix(r.ReadSingle());

            if (b.ItemId > 2000 && b.ItemId < 2010)
            {
                b.Tilt = r.ReadSingle();
                b.Pitch = 0;
                b.LocalOffset[1] = new Vec3D { X = b.LocalOffset[0].X, Y = b.LocalOffset[0].Y, Z = b.LocalOffset[0].Z };
                b.Yaw[1] = b.Yaw[0];
                b.Tilt2 = b.Tilt;
                b.Pitch2 = 0;
            }
            else if (b.ItemId > 2010 && b.ItemId < 2020)
            {
                b.Tilt = r.ReadSingle();
                b.Pitch = r.ReadSingle();
                b.LocalOffset[1] = ReadXyz(r);
                b.Yaw[1] = Fix(r.ReadSingle());
                b.Tilt2 = r.ReadSingle();
                b.Pitch2 = r.ReadSingle();
            }
            else
            {
                b.Tilt = 0;
                b.Pitch = 0;
                b.LocalOffset[1] = new Vec3D { X = b.LocalOffset[0].X, Y = b.LocalOffset[0].Y, Z = b.LocalOffset[0].Z };
                b.Yaw[1] = b.Yaw[0];
                b.Tilt2 = 0;
                b.Pitch2 = 0;
            }

            b.OutputObjIdx = r.ReadInt32();
            b.InputObjIdx = r.ReadInt32();
            b.OutputToSlot = r.ReadSByte();
            b.InputFromSlot = r.ReadSByte();
            b.OutputFromSlot = r.ReadSByte();
            b.InputToSlot = r.ReadSByte();
            b.OutputOffset = r.ReadSByte();
            b.InputOffset = r.ReadSByte();
            b.RecipeId = r.ReadInt16();
            b.FilterId = r.ReadInt16();

            short paramLen = r.ReadInt16();
            b.Parameters = paramLen > 0 ? r.ReadBytes(paramLen * 4) : null;

            int contentMarker = r.ReadInt32();
            b.Content = contentMarker > 0 ? r.ReadString() : null;

            return b;
        }

        private static void WriteBuilding(BinaryWriter w, BlueprintBuilding b)
        {
            w.Write(-102);
            w.Write(b.Index);
            w.Write(b.ItemId);
            w.Write(b.ModelIndex);
            w.Write(b.AreaIndex);
            WriteXyz(w, b.LocalOffset[0]);
            w.Write((float)b.Yaw[0]);

            if (b.ItemId > 2000 && b.ItemId < 2010)
            {
                w.Write((float)b.Tilt);
            }
            else if (b.ItemId > 2010 && b.ItemId < 2020)
            {
                w.Write((float)b.Tilt);
                w.Write((float)b.Pitch);
                WriteXyz(w, b.LocalOffset[1]);
                w.Write((float)b.Yaw[1]);
                w.Write((float)b.Tilt2);
                w.Write((float)b.Pitch2);
            }

            w.Write(b.OutputObjIdx);
            w.Write(b.InputObjIdx);
            w.Write(b.OutputToSlot);
            w.Write(b.InputFromSlot);
            w.Write(b.OutputFromSlot);
            w.Write(b.InputToSlot);
            w.Write(b.OutputOffset);
            w.Write(b.InputOffset);
            w.Write(b.RecipeId);
            w.Write(b.FilterId);

            if (b.Parameters != null && b.Parameters.Length > 0)
            {
                w.Write((short)(b.Parameters.Length / 4));
                w.Write(b.Parameters);
            }
            else
            {
                w.Write((short)0);
            }

            if (!string.IsNullOrEmpty(b.Content))
            {
                w.Write(b.Content!.Length); // 字符数标记，实际字符串靠下面 w.Write(string) 自带的 7-bit 长度前缀读回
                w.Write(b.Content);
            }
            else
            {
                w.Write(0);
            }
        }

        private static byte[] Gzip(byte[] data)
        {
            using var output = new MemoryStream();
            using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                gz.Write(data, 0, data.Length);
            return output.ToArray();
        }

        private static byte[] Gunzip(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gz.CopyTo(output);
            return output.ToArray();
        }
    }
}
