using System.IO;
using System.Text;

namespace FovApp.Services;

/// <summary>
/// PSO CHKS checksum, byte-for-byte the algorithm of CodeWalker's
/// PsoChecksum.Update: both fields of the trailing CHKS section are zeroed,
/// the whole file is hashed with a Jenkins one-at-a-time that treats each byte
/// as signed, then length and checksum are written back.
/// </summary>
public static class PsoChecksum
{
    private const uint Salt = 0x3FAC7125;

    private static int FindChunk(byte[] data)
    {
        var pos = data.Length - 8;
        while (pos >= 0)
        {
            if (data[pos] == 0x43 && data[pos + 1] == 0x48 && data[pos + 2] == 0x4B && data[pos + 3] == 0x53)
                return pos;
            pos--;
        }
        return -1;
    }

    private static uint ReadBe(byte[] d, int p) =>
        (uint)(d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]);

    private static void WriteBe(byte[] d, int p, uint v)
    {
        d[p] = (byte)(v >> 24);
        d[p + 1] = (byte)(v >> 16);
        d[p + 2] = (byte)(v >> 8);
        d[p + 3] = (byte)v;
    }

    private static uint Hash(byte[] data, int length, uint salt)
    {
        unchecked
        {
            uint h = salt;
            for (int i = 0; i < length; i++)
            {
                int b = (sbyte)data[i];
                h += (uint)b;
                h += h << 10;
                h ^= h >> 6;
            }
            h += h << 3;
            h ^= h >> 11;
            h += h << 15;
            return h;
        }
    }

    public static uint Update(byte[] data)
    {
        var ch = FindChunk(data);
        if (ch < 0)
            throw new InvalidDataException("CHKS section not found");
        var ver = ReadBe(data, ch + 4);
        var flen = (uint)data.Length;
        WriteBe(data, ch + 8, flen);
        WriteBe(data, ch + 12, 0);
        WriteBe(data, ch + 8, 0);
        var salt = ver == 20 ? Salt : 0;
        var chk = Hash(data, data.Length, salt);
        WriteBe(data, ch + 8, flen);
        WriteBe(data, ch + 12, chk);
        return chk;
    }
}
