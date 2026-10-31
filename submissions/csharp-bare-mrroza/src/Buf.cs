using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bare;

/// Growable native byte buffer: responses and Postgres messages are written straight into one of these.
sealed unsafe class Buf
{
    public byte* P;
    public int Len, Cap;

    static readonly SearchValues<byte> Esc = SearchValues.Create(
        "\"\\\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000a\u000b\u000c\u000d\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f"u8);

    public Buf(int cap)
    {
        P = (byte*)NativeMemory.Alloc((nuint)cap);
        Cap = cap;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Ensure(int extra)
    {
        if (Cap - Len < extra) Grow(extra);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Grow(int extra)
    {
        long want = Math.Max((long)Cap * 2, (long)Len + extra);
        if (want > int.MaxValue) throw new OutOfMemoryException();
        P = (byte*)NativeMemory.Realloc(P, (nuint)want);
        Cap = (int)want;
    }

    /// Gives back a buffer one huge batch inflated.
    public void Shrink(int cap)
    {
        if (Cap <= cap || Len > cap) return;
        P = (byte*)NativeMemory.Realloc(P, (nuint)cap);
        Cap = cap;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Put(byte b)
    {
        Ensure(1);
        P[Len++] = b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Put(ReadOnlySpan<byte> s)
    {
        Ensure(s.Length);
        s.CopyTo(new Span<byte>(P + Len, s.Length));
        Len += s.Length;
    }

    public void Put(byte* s, int n)
    {
        Ensure(n);
        Buffer.MemoryCopy(s, P + Len, n, n);
        Len += n;
    }

    public void Be16(int v)
    {
        Ensure(2);
        P[Len++] = (byte)(v >> 8);
        P[Len++] = (byte)v;
    }

    public void Be32(int v)
    {
        Ensure(4);
        Unsafe.WriteUnaligned(P + Len, BitConverter.IsLittleEndian ? BitOperations_Swap(v) : v);
        Len += 4;
    }

    public void Be64(long v)
    {
        Ensure(8);
        Unsafe.WriteUnaligned(P + Len, System.Net.IPAddress.HostToNetworkOrder(v));
        Len += 8;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int BitOperations_Swap(int v) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v);

    public void PatchBe32(int at, int v) =>
        Unsafe.WriteUnaligned(P + at, System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(v));

    public void Dec(long v)
    {
        Ensure(20);
        if (v < 0) { P[Len++] = (byte)'-'; v = -v; }
        int d = v < 10 ? 1 : DigitCount((ulong)v);
        int end = Len + d;
        byte* o = P;
        for (int i = end - 1; i >= Len; i--)
        {
            o[i] = (byte)('0' + v % 10);
            v /= 10;
        }
        Len = end;
    }

    static int DigitCount(ulong v)
    {
        int n = 1;
        while (v >= 10) { v /= 10; n++; }
        return n;
    }

    /// A JSON string with its quotes. Text columns are valid UTF-8 already, so only the JSON specials need work.
    public void JsonStr(byte* s, int n)
    {
        Ensure(n * 6 + 2);
        byte* o = P + Len;
        *o++ = (byte)'"';
        var rest = new ReadOnlySpan<byte>(s, n);
        while (true)
        {
            int i = rest.IndexOfAny(Esc);
            if (i < 0)
            {
                rest.CopyTo(new Span<byte>(o, rest.Length));
                o += rest.Length;
                break;
            }
            rest.Slice(0, i).CopyTo(new Span<byte>(o, i));
            o += i;
            byte c = rest[i];
            *o++ = (byte)'\\';
            switch (c)
            {
                case (byte)'"': *o++ = (byte)'"'; break;
                case (byte)'\\': *o++ = (byte)'\\'; break;
                case (byte)'\n': *o++ = (byte)'n'; break;
                case (byte)'\r': *o++ = (byte)'r'; break;
                case (byte)'\t': *o++ = (byte)'t'; break;
                default:
                    *o++ = (byte)'u'; *o++ = (byte)'0'; *o++ = (byte)'0';
                    *o++ = (byte)"0123456789abcdef"[c >> 4];
                    *o++ = (byte)"0123456789abcdef"[c & 15];
                    break;
            }
            rest = rest.Slice(i + 1);
        }
        *o++ = (byte)'"';
        Len = (int)(o - P);
    }

    public void JsonStrOrNull(byte* s, int n)
    {
        if (n < 0) Put("null"u8);
        else JsonStr(s, n);
    }

    /// Postgres binary timestamptz (microseconds since 2000-01-01 UTC) as "YYYY-MM-DDTHH:MM:SS.mmmZ", truncated.
    public void Timestamp(long micros)
    {
        Ensure(26);
        long ms = micros >= 0 ? micros / 1000 : -((-micros + 999) / 1000);
        long secs = ms >= 0 ? ms / 1000 : -((-ms + 999) / 1000);
        int milli = (int)(ms - secs * 1000);
        long days = secs >= 0 ? secs / 86400 : -((-secs + 86399) / 86400);
        int sod = (int)(secs - days * 86400);

        // Howard Hinnant's civil_from_days; 10957 days from 1970-01-01 to 2000-01-01, 719468 from 0000-03-01 to 1970.
        long z = days + 10957 + 719468;
        long era = (z >= 0 ? z : z - 146096) / 146097;
        long doe = z - era * 146097;
        long yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;
        long doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
        long mp = (5 * doy + 2) / 153;
        int day = (int)(doy - (153 * mp + 2) / 5 + 1);
        int mon = (int)(mp < 10 ? mp + 3 : mp - 9);
        int year = (int)(yoe + era * 400 + (mon <= 2 ? 1 : 0));

        byte* o = P + Len;
        *o++ = (byte)'"';
        o[0] = (byte)('0' + year / 1000 % 10);
        o[1] = (byte)('0' + year / 100 % 10);
        o[2] = (byte)('0' + year / 10 % 10);
        o[3] = (byte)('0' + year % 10);
        o[4] = (byte)'-';
        Two(o + 5, mon);
        o[7] = (byte)'-';
        Two(o + 8, day);
        o[10] = (byte)'T';
        Two(o + 11, sod / 3600);
        o[13] = (byte)':';
        Two(o + 14, sod / 60 % 60);
        o[16] = (byte)':';
        Two(o + 17, sod % 60);
        o[19] = (byte)'.';
        o[20] = (byte)('0' + milli / 100);
        Two(o + 21, milli % 100);
        o[23] = (byte)'Z';
        o[24] = (byte)'"';
        Len += 26;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void Two(byte* o, int v)
    {
        o[0] = (byte)('0' + v / 10);
        o[1] = (byte)('0' + v % 10);
    }
}

/// Reads the wire format: big-endian integers and the columns of a DataRow.
static unsafe class Wire
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int I32(byte* p) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<int>(p));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long I64(byte* p) => System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(Unsafe.ReadUnaligned<long>(p));

    /// First column of the DataRow message at p.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* Cols(byte* msg) => msg + 7;

    /// Current column's data and length (-1 for NULL), and moves to the next column.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* Next(ref byte* c, out int len)
    {
        len = I32(c);
        byte* d = c + 4;
        c = d + (len < 0 ? 0 : len);
        return d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long NextI64(ref byte* c)
    {
        byte* d = Next(ref c, out int _);
        return I64(d);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Skip(ref byte* c)
    {
        int len = I32(c);
        c += 4 + (len < 0 ? 0 : len);
    }

    /// Next DataRow after the message at msg.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* NextMsg(byte* msg) => msg + 1 + I32(msg + 1);

    /// Element count and first element of a one-dimensional binary array. Each element is (len, bytes).
    public static int ArrayItems(byte* a, out byte* first)
    {
        int ndim = I32(a);
        if (ndim == 0) { first = null; return 0; }
        first = a + 20;
        return I32(a + 12);
    }
}

/// Distinct keys of one batch statement (text, integers or both), and the rows Postgres returned for each.
sealed unsafe class KeySet
{
    public nint[] P = new nint[64];
    public int[] L = new int[64];
    public long[] A = new long[64];
    public long[] B = new long[64];
    public nint[] Row = new nint[64];
    public nint[] Row2 = new nint[64];
    public int[] RowN = new int[64];
    public int Count;
    int[] _table = new int[256];
    int _mask = 255;

    public void Clear()
    {
        if (Count == 0) return;
        Array.Clear(_table);
        Array.Clear(Row, 0, Count);
        Array.Clear(Row2, 0, Count);
        Array.Clear(RowN, 0, Count);
        Count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static ulong Hash(byte* p, int n, long a, long b)
    {
        const ulong K = 0x9E3779B97F4A7C15;
        ulong h = (ulong)n * K ^ (ulong)a * 0xC2B2AE3D27D4EB4F ^ (ulong)b * 0x165667B19E3779F9;
        while (n >= 8)
        {
            h = BitOperations.RotateLeft((h ^ Unsafe.ReadUnaligned<ulong>(p)) * K, 23);
            p += 8;
            n -= 8;
        }
        if (n > 0)
        {
            ulong t = 0;
            for (int i = 0; i < n; i++) t = t << 8 | p[i];
            h = BitOperations.RotateLeft((h ^ t) * K, 23);
        }
        h *= K;
        return h ^ h >> 29;
    }

    bool Same(int i, byte* p, int n, long a, long b) =>
        A[i] == a && B[i] == b && L[i] == n &&
        (n == 0 || new ReadOnlySpan<byte>((byte*)P[i], n).SequenceEqual(new ReadOnlySpan<byte>(p, n)));

    /// Index of the key, adding it if new. The key bytes must stay valid until the batch ends.
    public int Add(byte* p, int n, long a, long b)
    {
        uint s = (uint)Hash(p, n, a, b) & (uint)_mask;
        int[] t = _table;
        while (true)
        {
            int e = t[s];
            if (e == 0) break;
            if (Same(e - 1, p, n, a, b)) return e - 1;
            s = (s + 1) & (uint)_mask;
        }
        int idx = Count;
        if (idx == P.Length) GrowEntries();
        P[idx] = (nint)p; L[idx] = n; A[idx] = a; B[idx] = b; Row[idx] = 0; Row2[idx] = 0; RowN[idx] = 0;
        Count = idx + 1;
        t[s] = idx + 1;
        if (Count * 2 > _mask) Rehash();
        return idx;
    }

    public int Add(long a, long b = 0) => Add(null, 0, a, b);

    public int Find(long a, long b = 0)
    {
        uint s = (uint)Hash(null, 0, a, b) & (uint)_mask;
        int[] t = _table;
        while (true)
        {
            int e = t[s];
            if (e == 0) return -1;
            if (A[e - 1] == a && B[e - 1] == b) return e - 1;
            s = (s + 1) & (uint)_mask;
        }
    }

    /// Row of a key added earlier, or null.
    public byte* RowOf(long a, long b = 0)
    {
        int i = Find(a, b);
        return i < 0 ? null : (byte*)Row[i];
    }

    void GrowEntries()
    {
        int n = P.Length * 2;
        Array.Resize(ref P, n); Array.Resize(ref L, n); Array.Resize(ref A, n); Array.Resize(ref B, n);
        Array.Resize(ref Row, n); Array.Resize(ref Row2, n); Array.Resize(ref RowN, n);
    }

    void Rehash()
    {
        int size = (_mask + 1) * 2;
        var t = new int[size];
        uint mask = (uint)size - 1;
        for (int i = 0; i < Count; i++)
        {
            uint s = (uint)Hash((byte*)P[i], L[i], A[i], B[i]) & mask;
            while (t[s] != 0) s = (s + 1) & mask;
            t[s] = i + 1;
        }
        _table = t;
        _mask = size - 1;
    }
}
