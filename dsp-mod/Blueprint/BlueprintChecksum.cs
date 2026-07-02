using System;

namespace DspBlueprintTransform.Blueprint
{
    // 蓝图校验和用的是自定义 MD5 变体：初始向量与标准 MD5(RFC1321)不同
    // （对照 3rd/edit-dspblue-print/src/utils/md5.js 的 INIT_MD5F 字节序列：
    //  01 23 45 67 89 ab DC ef fe dc ba 98 46 57 32 10，
    //  标准 MD5 是 ...89 ab CD ef... 76 54...），必须逐字节移植，
    // 不能用 System.Security.Cryptography.MD5。
    public static class BlueprintChecksum
    {
        private static readonly uint[] K =
        {
            0xd76aa478, 0xe8d7b756, 0x242070db, 0xc1bdceee, 0xf57c0faf, 0x4787c62a, 0xa8304623, 0xfd469501,
            0x698098d8, 0x8b44f7af, 0xffff5bb1, 0x895cd7be, 0x6b9f1122, 0xfd987193, 0xa679438e, 0x39b40821,
            0xf61e2562, 0xc040b340, 0x265e5a51, 0xc9b6c7aa, 0xd62f105d, 0x02443453, 0xd8a1e681, 0xe7d3fbc8,
            0x21f1cde6, 0xc33707d6, 0xf4d50d87, 0x475a14ed, 0xa9e3e905, 0xfcefa3f8, 0x676f02d9, 0x8d2a4c8a,
            0xfffa3942, 0x8771f681, 0x6d9d6122, 0xfde5380c, 0xa4beea44, 0x4bdecfa9, 0xf6bb4b60, 0xbebfbc70,
            0x289b7ec6, 0xeaa127fa, 0xd4ef3085, 0x04881d05, 0xd9d4d039, 0xe6db99e5, 0x1fa27cf8, 0xc4ac5665,
            0xf4292244, 0x432aff97, 0xab9423a7, 0xfc93a039, 0x655b59c3, 0x8f0ccc92, 0xffeff47d, 0x85845dd1,
            0x6fa87e4f, 0xfe2ce6e0, 0xa3014314, 0x4e0811a1, 0xf7537e82, 0xbd3af235, 0x2ad7d2bb, 0xeb86d391,
        };

        private static readonly int[] S =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
        };

        private static readonly uint[] InitState = { 0x67452301, 0xefdcab89, 0x98badcfe, 0x10325746 };

        private const int BlockSize = 64;

        public static byte[] Digest(byte[] data)
        {
            uint[] s = (uint[])InitState.Clone();
            int i = 0;
            for (; i <= data.Length - BlockSize; i += BlockSize)
                UpdateBlock(s, data, i);

            int remaining = data.Length - i;
            int paddedLen = ((remaining + 9 + BlockSize - 1) / BlockSize) * BlockSize;
            byte[] last = new byte[paddedLen];
            Array.Copy(data, i, last, 0, remaining);
            last[remaining] = 0x80;
            uint bitLenLow = unchecked((uint)((ulong)data.Length * 8 & 0xFFFFFFFF));
            BitConverter.GetBytes(bitLenLow).CopyTo(last, last.Length - 8);

            for (int j = 0; j <= last.Length - BlockSize; j += BlockSize)
                UpdateBlock(s, last, j);

            byte[] result = new byte[16];
            for (int k = 0; k < 4; k++)
                BitConverter.GetBytes(s[k]).CopyTo(result, k * 4);
            return result;
        }

        public static string HexDigest(byte[] data)
        {
            byte[] hash = Digest(data);
            var chars = new char[32];
            for (int i = 0; i < 16; i++)
            {
                chars[i * 2] = HexChar(hash[i] >> 4);
                chars[i * 2 + 1] = HexChar(hash[i] & 0xF);
            }
            return new string(chars);
        }

        private static char HexChar(int v) => (char)(v < 10 ? '0' + v : 'A' + (v - 10));

        private static uint RotateLeft(uint x, int s) => (x << s) | (x >> (32 - s));

        private static void UpdateBlock(uint[] s, byte[] buf, int offset)
        {
            uint a = s[0], b = s[1], c = s[2], d = s[3];
            for (int i = 0; i < 64; i++)
            {
                uint f;
                int g;
                if (i < 16) { f = (b & c) | (~b & d); g = i; }
                else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
                else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
                else { f = c ^ (b | ~d); g = (7 * i) % 16; }

                uint chunk = BitConverter.ToUInt32(buf, offset + g * 4);
                f = unchecked(f + a + K[i] + chunk);
                a = d;
                d = c;
                c = b;
                b = unchecked(b + RotateLeft(f, S[i]));
            }
            s[0] = unchecked(s[0] + a);
            s[1] = unchecked(s[1] + b);
            s[2] = unchecked(s[2] + c);
            s[3] = unchecked(s[3] + d);
        }
    }
}
