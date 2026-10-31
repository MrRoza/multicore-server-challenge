using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bare;

/// bcrypt, five passwords at a time. Blowfish spends its time on S-box loads, so a core can keep five independent
/// chains in flight in one loop. The 1042 boxes' words are the hex digits of pi; they are computed at startup with
/// Machin's formula rather than kept as a table.
static unsafe class Blowfish
{
    public const int Words = 18 + 1024;
    static uint* _pi;

    public static void Init()
    {
        const int bits = Words * 32;
        int guard = 64;
        BigInteger one = BigInteger.One << (bits + guard + 2);
        // pi = 16 atan(1/5) - 4 atan(1/239)
        BigInteger pi = 16 * Atan(5, one) - 4 * Atan(239, one);
        pi >>= guard + 2;
        pi -= 3 * (BigInteger.One << bits);   // the fraction
        _pi = (uint*)NativeMemory.Alloc(Words * 4);
        BigInteger mask = (BigInteger.One << 32) - 1;
        for (int i = Words - 1; i >= 0; i--)
        {
            _pi[i] = (uint)(pi & mask);
            pi >>= 32;
        }
        if (_pi[0] != 0x243F6A88) throw new InvalidOperationException("pi digits are wrong");
    }

    static BigInteger Atan(int x, BigInteger one)
    {
        BigInteger term = one / x, sum = term;
        int x2 = x * x;
        for (int k = 3; !term.IsZero; k += 2)
        {
            term /= x2;
            BigInteger t = term / k;
            sum = (k & 2) != 0 ? sum - t : sum + t;
        }
        return sum;
    }

    public static void Reset(uint* st) => Buffer.MemoryCopy(_pi, st, Words * 4, Words * 4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint F(uint x, uint* s) => ((s[x >> 24] + s[256 + (x >> 16 & 255)]) ^ s[512 + (x >> 8 & 255)]) + s[768 + (x & 255)];

    public static void Encrypt(uint* st, ref uint lr, ref uint rr)
    {
        uint* s = st + 18;
        uint l = lr ^ st[0], r = rr;
        for (int i = 1; i <= 16; i += 2)
        {
            r ^= F(l, s) ^ st[i];
            l ^= F(r, s) ^ st[i + 1];
        }
        lr = r ^ st[17];
        rr = l;
    }

    /// One pass of the key schedule: XOR the key stream into P, then re-encrypt the whole state as one chain,
    /// mixing in the salt when there is one.
    public static void Expand(uint* st, byte* key, int keyLen, uint* salt)
    {
        XorKey(st, key, keyLen);
        uint l = 0, r = 0;
        int si = 0;
        for (int i = 0; i < Words; i += 2)
        {
            if (salt != null)
            {
                l ^= salt[si];
                r ^= salt[si + 1];
                si = (si + 2) & 3;
            }
            Encrypt(st, ref l, ref r);
            st[i] = l;
            st[i + 1] = r;
        }
    }

    public static void XorKey(uint* st, byte* key, int keyLen)
    {
        int k = 0;
        for (int i = 0; i < 18; i++)
        {
            uint w = 0;
            for (int j = 0; j < 4; j++)
            {
                w = w << 8 | key[k];
                if (++k == keyLen) k = 0;
            }
            st[i] ^= w;
        }
    }

    /// Lanes sit at fixed distances from one base pointer, so every load is base + index * 4 + constant.
    public const int LaneWords = Words + 22;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint L(uint* b, int lane, uint x)
    {
        nint s = lane * LaneWords + 18;
        return ((b[(nint)(x >> 24) + s] + b[(nint)(x >> 16 & 255) + s + 256]) ^ b[(nint)(x >> 8 & 255) + s + 512]) + b[(nint)(x & 255) + s + 768];
    }

    /// The saltless chain over five lanes at once. Each round is written out with its P index as a constant: with one
    /// base, one counter and ten halves, everything fits in registers, and that is what lets the core overlap the
    /// lanes' S-box loads. With a pointer per lane and a round counter the halves spill and a lane costs what a lone
    /// hash does.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Chain5(uint* p)
    {
        const int W1 = LaneWords, W2 = LaneWords * 2, W3 = LaneWords * 3, W4 = LaneWords * 4;
        uint al = 0, ar = 0, bl = 0, br = 0, cl = 0, cr = 0, dl = 0, dr = 0, el = 0, er = 0, t;
        for (nint w = 0; w < Words; w += 2)
        {
            al ^= p[0]; bl ^= p[W1]; cl ^= p[W2]; dl ^= p[W3]; el ^= p[W4];
            ar ^= L(p, 0, al) ^ p[1]; br ^= L(p, 1, bl) ^ p[W1 + 1]; cr ^= L(p, 2, cl) ^ p[W2 + 1]; dr ^= L(p, 3, dl) ^ p[W3 + 1]; er ^= L(p, 4, el) ^ p[W4 + 1];
            al ^= L(p, 0, ar) ^ p[2]; bl ^= L(p, 1, br) ^ p[W1 + 2]; cl ^= L(p, 2, cr) ^ p[W2 + 2]; dl ^= L(p, 3, dr) ^ p[W3 + 2]; el ^= L(p, 4, er) ^ p[W4 + 2];
            ar ^= L(p, 0, al) ^ p[3]; br ^= L(p, 1, bl) ^ p[W1 + 3]; cr ^= L(p, 2, cl) ^ p[W2 + 3]; dr ^= L(p, 3, dl) ^ p[W3 + 3]; er ^= L(p, 4, el) ^ p[W4 + 3];
            al ^= L(p, 0, ar) ^ p[4]; bl ^= L(p, 1, br) ^ p[W1 + 4]; cl ^= L(p, 2, cr) ^ p[W2 + 4]; dl ^= L(p, 3, dr) ^ p[W3 + 4]; el ^= L(p, 4, er) ^ p[W4 + 4];
            ar ^= L(p, 0, al) ^ p[5]; br ^= L(p, 1, bl) ^ p[W1 + 5]; cr ^= L(p, 2, cl) ^ p[W2 + 5]; dr ^= L(p, 3, dl) ^ p[W3 + 5]; er ^= L(p, 4, el) ^ p[W4 + 5];
            al ^= L(p, 0, ar) ^ p[6]; bl ^= L(p, 1, br) ^ p[W1 + 6]; cl ^= L(p, 2, cr) ^ p[W2 + 6]; dl ^= L(p, 3, dr) ^ p[W3 + 6]; el ^= L(p, 4, er) ^ p[W4 + 6];
            ar ^= L(p, 0, al) ^ p[7]; br ^= L(p, 1, bl) ^ p[W1 + 7]; cr ^= L(p, 2, cl) ^ p[W2 + 7]; dr ^= L(p, 3, dl) ^ p[W3 + 7]; er ^= L(p, 4, el) ^ p[W4 + 7];
            al ^= L(p, 0, ar) ^ p[8]; bl ^= L(p, 1, br) ^ p[W1 + 8]; cl ^= L(p, 2, cr) ^ p[W2 + 8]; dl ^= L(p, 3, dr) ^ p[W3 + 8]; el ^= L(p, 4, er) ^ p[W4 + 8];
            ar ^= L(p, 0, al) ^ p[9]; br ^= L(p, 1, bl) ^ p[W1 + 9]; cr ^= L(p, 2, cl) ^ p[W2 + 9]; dr ^= L(p, 3, dl) ^ p[W3 + 9]; er ^= L(p, 4, el) ^ p[W4 + 9];
            al ^= L(p, 0, ar) ^ p[10]; bl ^= L(p, 1, br) ^ p[W1 + 10]; cl ^= L(p, 2, cr) ^ p[W2 + 10]; dl ^= L(p, 3, dr) ^ p[W3 + 10]; el ^= L(p, 4, er) ^ p[W4 + 10];
            ar ^= L(p, 0, al) ^ p[11]; br ^= L(p, 1, bl) ^ p[W1 + 11]; cr ^= L(p, 2, cl) ^ p[W2 + 11]; dr ^= L(p, 3, dl) ^ p[W3 + 11]; er ^= L(p, 4, el) ^ p[W4 + 11];
            al ^= L(p, 0, ar) ^ p[12]; bl ^= L(p, 1, br) ^ p[W1 + 12]; cl ^= L(p, 2, cr) ^ p[W2 + 12]; dl ^= L(p, 3, dr) ^ p[W3 + 12]; el ^= L(p, 4, er) ^ p[W4 + 12];
            ar ^= L(p, 0, al) ^ p[13]; br ^= L(p, 1, bl) ^ p[W1 + 13]; cr ^= L(p, 2, cl) ^ p[W2 + 13]; dr ^= L(p, 3, dl) ^ p[W3 + 13]; er ^= L(p, 4, el) ^ p[W4 + 13];
            al ^= L(p, 0, ar) ^ p[14]; bl ^= L(p, 1, br) ^ p[W1 + 14]; cl ^= L(p, 2, cr) ^ p[W2 + 14]; dl ^= L(p, 3, dr) ^ p[W3 + 14]; el ^= L(p, 4, er) ^ p[W4 + 14];
            ar ^= L(p, 0, al) ^ p[15]; br ^= L(p, 1, bl) ^ p[W1 + 15]; cr ^= L(p, 2, cl) ^ p[W2 + 15]; dr ^= L(p, 3, dl) ^ p[W3 + 15]; er ^= L(p, 4, el) ^ p[W4 + 15];
            al ^= L(p, 0, ar) ^ p[16]; bl ^= L(p, 1, br) ^ p[W1 + 16]; cl ^= L(p, 2, cr) ^ p[W2 + 16]; dl ^= L(p, 3, dr) ^ p[W3 + 16]; el ^= L(p, 4, er) ^ p[W4 + 16];
            t = ar ^ p[17]; ar = al; al = t;
            t = br ^ p[W1 + 17]; br = bl; bl = t;
            t = cr ^ p[W2 + 17]; cr = cl; cl = t;
            t = dr ^ p[W3 + 17]; dr = dl; dl = t;
            t = er ^ p[W4 + 17]; er = el; el = t;
            p[w] = al; p[w + 1] = ar;
            p[w + W1] = bl; p[w + W1 + 1] = br;
            p[w + W2] = cl; p[w + W2 + 1] = cr;
            p[w + W3] = dl; p[w + W3 + 1] = dr;
            p[w + W4] = el; p[w + W4 + 1] = er;
        }
    }
}

/// One password to hash or check. Key points into the job's memory, which stays put while the job is here.
sealed unsafe class BcryptTask
{
    public Job Job;
    public byte* Password;      // the key without its trailing NUL, at most 72 bytes used
    public int PasswordLen;
    public byte* Hash;          // verify: the stored hash (60 bytes)
    public byte* Output;        // hash: where the new 60-byte hash goes
    public long Enqueued;
    public int Cost;
}

/// The bcrypt threads and their queue. Nothing here touches a loop's state: results go back through its inbox.
static unsafe class Bcrypt
{
    const string Alpha = "./ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    const int QueueLimit = 8192;
    const int Cost = 10;
    const int Lanes = 5;

    static readonly Queue<BcryptTask> Q = new Queue<BcryptTask>();
    static int _waitMs, _nice;

    public static void Start(int threads, int waitMs, int nice)
    {
        _waitMs = waitMs;
        _nice = nice;
        for (int i = 0; i < threads; i++)
        {
            var t = new Thread(Worker, 1 << 20) { IsBackground = true, Name = "bcrypt" };
            t.Start();
        }
    }

    /// False if the queue is full: the caller answers 503 right away rather than let the wait grow without bound.
    public static bool Hash(Job j, byte* password, int n, byte* output) =>
        Enqueue(new BcryptTask { Job = j, Password = password, PasswordLen = n, Output = output, Cost = Cost });

    public static bool Verify(Job j, byte* password, int n, byte* hash)
    {
        int cost = ParseCost(hash);
        if (cost < 0) { j.PwOk = false; j.Loop.Post(j); return true; }
        return Enqueue(new BcryptTask { Job = j, Password = password, PasswordLen = n, Hash = hash, Cost = cost });
    }

    static int ParseCost(byte* h)
    {
        if (h[0] != '$' || h[1] != '2' || h[3] != '$' || h[6] != '$') return -1;
        if (h[2] != 'a' && h[2] != 'b' && h[2] != 'y') return -1;
        int c = (h[4] - '0') * 10 + (h[5] - '0');
        return c >= 4 && c <= 31 ? c : -1;
    }

    static bool Enqueue(BcryptTask t)
    {
        t.Enqueued = Sys.MonoMs();
        lock (Q)
        {
            if (Q.Count >= QueueLimit) return false;
            Q.Enqueue(t);
            if (Q.Count == 1 || Q.Count >= Lanes) Monitor.PulseAll(Q);
        }
        return true;
    }

    static bool SameCost(BcryptTask[] b)
    {
        for (int i = 1; i < b.Length; i++) if (b[i].Cost != b[0].Cost) return false;
        return true;
    }

    static void Worker()
    {
        if (_nice != 0) Sys.setpriority(0, 0, _nice);
        uint* lanes = (uint*)NativeMemory.AlignedAlloc(Lanes * Blowfish.LaneWords * 4, 64);
        var batch = new BcryptTask[Lanes];
        while (true)
        {
            int n = Take(batch);
            if (n == Lanes && SameCost(batch))
            {
                RunLanes(batch, lanes);
            }
            else
            {
                for (int i = 0; i < n; i++) Run1(batch[i], lanes);
            }
            for (int i = 0; i < n; i++)
            {
                batch[i].Job.Loop.Post(batch[i].Job);
                batch[i] = null;
            }
        }
    }

    /// A lone password waits a little for company, since four cost barely more than one.
    static int Take(BcryptTask[] batch)
    {
        lock (Q)
        {
            while (true)
            {
                if (Q.Count >= Lanes) break;
                if (Q.Count == 0) { Monitor.Wait(Q); continue; }
                long left = Q.Peek().Enqueued + _waitMs - Sys.MonoMs();
                if (left <= 0) break;
                Monitor.Wait(Q, (int)left);
            }
            int n = 0;
            while (n < Lanes && Q.Count > 0) batch[n++] = Q.Dequeue();
            return n;
        }
    }

    /// Key material: the password and its terminating NUL, cut to 72 bytes.
    static int Key(BcryptTask t, byte* key)
    {
        int n = Math.Min(t.PasswordLen, 72);
        Buffer.MemoryCopy(t.Password, key, n, n);
        if (n < 72) key[n++] = 0;
        return n;
    }

    static void Salt(BcryptTask t, byte* salt)
    {
        if (t.Hash != null) DecodeB64(t.Hash + 7, 22, salt, 16);
        else Sys.Random(salt, 16);
    }

    static void SaltWords(byte* salt, uint* w)
    {
        for (int i = 0; i < 4; i++) w[i] = (uint)Wire.I32(salt + i * 4);
    }

    static void Run1(BcryptTask t, uint* st)
    {
        byte* key = stackalloc byte[72];
        byte* salt = stackalloc byte[16];
        uint* sw = stackalloc uint[4];
        int kl = Key(t, key);
        Salt(t, salt);
        SaltWords(salt, sw);
        Blowfish.Reset(st);
        Blowfish.Expand(st, key, kl, sw);
        long rounds = 1L << t.Cost;
        for (long r = 0; r < rounds; r++)
        {
            Blowfish.Expand(st, key, kl, null);
            Blowfish.Expand(st, salt, 16, null);
        }
        Finish(t, st, salt);
    }

    static void RunLanes(BcryptTask[] b, uint* lanes)
    {
        byte* keys = stackalloc byte[72 * Lanes];
        byte* salts = stackalloc byte[16 * Lanes];
        int* lens = stackalloc int[Lanes];
        uint* sw = stackalloc uint[4];
        for (int i = 0; i < Lanes; i++)
        {
            uint* st = lanes + i * Blowfish.LaneWords;
            lens[i] = Key(b[i], keys + i * 72);
            Salt(b[i], salts + i * 16);
            SaltWords(salts + i * 16, sw);
            Blowfish.Reset(st);
            Blowfish.Expand(st, keys + i * 72, lens[i], sw);
        }
        long rounds = 1L << b[0].Cost;
        for (long r = 0; r < rounds; r++)
        {
            for (int i = 0; i < Lanes; i++) Blowfish.XorKey(lanes + i * Blowfish.LaneWords, keys + i * 72, lens[i]);
            Blowfish.Chain5(lanes);
            for (int i = 0; i < Lanes; i++) Blowfish.XorKey(lanes + i * Blowfish.LaneWords, salts + i * 16, 16);
            Blowfish.Chain5(lanes);
        }
        for (int i = 0; i < Lanes; i++) Finish(b[i], lanes + i * Blowfish.LaneWords, salts + i * 16);
    }

    static void Finish(BcryptTask t, uint* st, byte* salt)
    {
        // "OrpheanBeholderScryDoubt", each block encrypted 64 times.
        uint* c = stackalloc uint[6] { 0x4F727068, 0x65616E42, 0x65686F6C, 0x64657253, 0x63727944, 0x6F756274 };
        for (int b = 0; b < 6; b += 2)
            for (int i = 0; i < 64; i++)
                Blowfish.Encrypt(st, ref c[b], ref c[b + 1]);
        byte* raw = stackalloc byte[24];
        for (int i = 0; i < 6; i++)
        {
            raw[i * 4] = (byte)(c[i] >> 24);
            raw[i * 4 + 1] = (byte)(c[i] >> 16);
            raw[i * 4 + 2] = (byte)(c[i] >> 8);
            raw[i * 4 + 3] = (byte)c[i];
        }
        byte* enc = stackalloc byte[32];
        EncodeB64(raw, 23, enc);
        if (t.Hash != null)
        {
            int diff = 0;
            for (int i = 0; i < 31; i++) diff |= enc[i] ^ t.Hash[29 + i];
            t.Job.PwOk = diff == 0;
        }
        else
        {
            byte* o = t.Output;
            o[0] = (byte)'$'; o[1] = (byte)'2'; o[2] = (byte)'a'; o[3] = (byte)'$';
            o[4] = (byte)('0' + t.Cost / 10); o[5] = (byte)('0' + t.Cost % 10); o[6] = (byte)'$';
            EncodeB64(salt, 16, o + 7);
            for (int i = 0; i < 31; i++) o[29 + i] = enc[i];
            t.Job.PwOk = true;
        }
    }

    static void EncodeB64(byte* s, int n, byte* o)
    {
        int i = 0;
        while (i < n)
        {
            int c1 = s[i++];
            *o++ = (byte)Alpha[c1 >> 2];
            c1 = (c1 & 3) << 4;
            if (i >= n) { *o++ = (byte)Alpha[c1]; break; }
            int c2 = s[i++];
            c1 |= c2 >> 4;
            *o++ = (byte)Alpha[c1];
            c1 = (c2 & 15) << 2;
            if (i >= n) { *o++ = (byte)Alpha[c1]; break; }
            c2 = s[i++];
            c1 |= c2 >> 6;
            *o++ = (byte)Alpha[c1];
            *o++ = (byte)Alpha[c2 & 63];
        }
    }

    static void DecodeB64(byte* s, int chars, byte* o, int n)
    {
        int acc = 0, bits = 0, w = 0;
        for (int i = 0; i < chars && w < n; i++)
        {
            int v = Alpha.IndexOf((char)s[i]);
            if (v < 0) v = 0;
            acc = acc << 6 | v;
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                o[w++] = (byte)(acc >> bits);
                acc &= (1 << bits) - 1;
            }
        }
    }
}
