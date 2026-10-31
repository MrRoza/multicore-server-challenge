using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bare;

/// bcrypt, four passwords at a time. Blowfish spends its time on S-box loads, so a core can keep four independent
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

    /// The saltless chain over four states at once.
    public static void Chain4(uint* a, uint* b, uint* c, uint* d)
    {
        uint al = 0, ar = 0, bl = 0, br = 0, cl = 0, cr = 0, dl = 0, dr = 0;
        uint* sa = a + 18, sb = b + 18, sc = c + 18, sd = d + 18;
        for (int w = 0; w < Words; w += 2)
        {
            al ^= a[0]; bl ^= b[0]; cl ^= c[0]; dl ^= d[0];
            for (int i = 1; i <= 16; i += 2)
            {
                ar ^= F(al, sa) ^ a[i];
                br ^= F(bl, sb) ^ b[i];
                cr ^= F(cl, sc) ^ c[i];
                dr ^= F(dl, sd) ^ d[i];
                al ^= F(ar, sa) ^ a[i + 1];
                bl ^= F(br, sb) ^ b[i + 1];
                cl ^= F(cr, sc) ^ c[i + 1];
                dl ^= F(dr, sd) ^ d[i + 1];
            }
            uint t;
            t = ar ^ a[17]; ar = al; al = t;
            t = br ^ b[17]; br = bl; bl = t;
            t = cr ^ c[17]; cr = cl; cl = t;
            t = dr ^ d[17]; dr = dl; dl = t;
            a[w] = al; a[w + 1] = ar;
            b[w] = bl; b[w + 1] = br;
            c[w] = cl; c[w + 1] = cr;
            d[w] = dl; d[w + 1] = dr;
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

    static readonly Queue<BcryptTask> Q = new Queue<BcryptTask>();
    static int _waitMs;

    public static void Start(int threads, int waitMs)
    {
        _waitMs = waitMs;
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
            if (Q.Count == 1 || Q.Count >= 4) Monitor.PulseAll(Q);
        }
        return true;
    }

    static void Worker()
    {
        uint** states = stackalloc uint*[4];
        for (int i = 0; i < 4; i++) states[i] = (uint*)NativeMemory.Alloc(Blowfish.Words * 4);
        var batch = new BcryptTask[4];
        while (true)
        {
            int n = Take(batch);
            if (n == 4 && batch[0].Cost == batch[1].Cost && batch[0].Cost == batch[2].Cost && batch[0].Cost == batch[3].Cost)
            {
                Run4(batch, states);
            }
            else
            {
                for (int i = 0; i < n; i++) Run1(batch[i], states[0]);
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
                if (Q.Count >= 4) break;
                if (Q.Count == 0) { Monitor.Wait(Q); continue; }
                long left = Q.Peek().Enqueued + _waitMs - Sys.MonoMs();
                if (left <= 0) break;
                Monitor.Wait(Q, (int)left);
            }
            int n = 0;
            while (n < 4 && Q.Count > 0) batch[n++] = Q.Dequeue();
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

    static void Run4(BcryptTask[] b, uint** st)
    {
        byte* keys = stackalloc byte[72 * 4];
        byte* salts = stackalloc byte[16 * 4];
        int* lens = stackalloc int[4];
        uint* sw = stackalloc uint[4];
        for (int i = 0; i < 4; i++)
        {
            lens[i] = Key(b[i], keys + i * 72);
            Salt(b[i], salts + i * 16);
            SaltWords(salts + i * 16, sw);
            Blowfish.Reset(st[i]);
            Blowfish.Expand(st[i], keys + i * 72, lens[i], sw);
        }
        long rounds = 1L << b[0].Cost;
        for (long r = 0; r < rounds; r++)
        {
            for (int i = 0; i < 4; i++) Blowfish.XorKey(st[i], keys + i * 72, lens[i]);
            Blowfish.Chain4(st[0], st[1], st[2], st[3]);
            for (int i = 0; i < 4; i++) Blowfish.XorKey(st[i], salts + i * 16, 16);
            Blowfish.Chain4(st[0], st[1], st[2], st[3]);
        }
        for (int i = 0; i < 4; i++) Finish(b[i], st[i], salts + i * 16);
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
