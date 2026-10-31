using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Bare;

/// SHA-256 and HMAC, no OpenSSL. The round constants are derived from their definition at startup
/// (cube roots of the first 64 primes, square roots of the first 8), so there is no table to mistype.
static unsafe class Sha256
{
    static uint* K;
    static uint* H0;

    public static void Init()
    {
        K = (uint*)NativeMemory.Alloc(64 * 4);
        H0 = (uint*)NativeMemory.Alloc(8 * 4);
        int found = 0;
        for (int p = 2; found < 64; p++)
        {
            bool prime = true;
            for (int d = 2; d * d <= p; d++) if (p % d == 0) { prime = false; break; }
            if (!prime) continue;
            K[found] = (uint)(Root(new BigInteger(p) << 96, 3) & 0xFFFFFFFF);
            if (found < 8) H0[found] = (uint)(Root(new BigInteger(p) << 64, 2) & 0xFFFFFFFF);
            found++;
        }
    }

    /// Integer k-th root by Newton's method.
    static BigInteger Root(BigInteger v, int k)
    {
        BigInteger x = BigInteger.One << (int)(v.GetBitLength() / k + 1);
        while (true)
        {
            BigInteger y = ((k - 1) * x + v / BigInteger.Pow(x, k - 1)) / k;
            if (y >= x) return x;
            x = y;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static uint R(uint x, int n) => BitOperations.RotateRight(x, n);

    public static void Compress(uint* st, byte* block)
    {
        uint* w = stackalloc uint[64];
        for (int i = 0; i < 16; i++) w[i] = (uint)Wire.I32(block + i * 4);
        for (int i = 16; i < 64; i++)
        {
            uint a15 = w[i - 15], a2 = w[i - 2];
            w[i] = w[i - 16] + (R(a15, 7) ^ R(a15, 18) ^ a15 >> 3) + w[i - 7] + (R(a2, 17) ^ R(a2, 19) ^ a2 >> 10);
        }
        uint a = st[0], b = st[1], c = st[2], d = st[3], e = st[4], f = st[5], g = st[6], h = st[7];
        uint* k = K;
        for (int i = 0; i < 64; i++)
        {
            uint t1 = h + (R(e, 6) ^ R(e, 11) ^ R(e, 25)) + (e & f ^ ~e & g) + k[i] + w[i];
            uint t2 = (R(a, 2) ^ R(a, 13) ^ R(a, 22)) + (a & b ^ a & c ^ b & c);
            h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
        }
        st[0] += a; st[1] += b; st[2] += c; st[3] += d; st[4] += e; st[5] += f; st[6] += g; st[7] += h;
    }

    public static void InitState(uint* st) { for (int i = 0; i < 8; i++) st[i] = H0[i]; }

    /// Finishes a hash whose state already covers `prefix` bytes (a multiple of 64), over msg.
    public static void Finish(uint* st, long prefix, byte* msg, int n, byte* out32)
    {
        long total = prefix + n;
        while (n >= 64) { Compress(st, msg); msg += 64; n -= 64; }
        byte* tail = stackalloc byte[128];
        for (int i = 0; i < n; i++) tail[i] = msg[i];
        tail[n] = 0x80;
        int padded = n < 56 ? 64 : 128;
        for (int i = n + 1; i < padded - 8; i++) tail[i] = 0;
        long bits = total * 8;
        for (int i = 0; i < 8; i++) tail[padded - 1 - i] = (byte)(bits >> (8 * i));
        Compress(st, tail);
        if (padded == 128) Compress(st, tail + 64);
        for (int i = 0; i < 8; i++)
        {
            uint v = st[i];
            out32[i * 4] = (byte)(v >> 24);
            out32[i * 4 + 1] = (byte)(v >> 16);
            out32[i * 4 + 2] = (byte)(v >> 8);
            out32[i * 4 + 3] = (byte)v;
        }
    }

    public static byte[] Hash(ReadOnlySpan<byte> m)
    {
        var o = new byte[32];
        uint* st = stackalloc uint[8];
        InitState(st);
        fixed (byte* pm = m, po = o) Finish(st, 0, pm, m.Length, po);
        return o;
    }

    public static byte[] Hmac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> msg)
    {
        var o = new byte[32];
        var h = new HmacKey(key);
        fixed (byte* pm = msg, po = o) h.Sign(pm, msg.Length, po);
        return o;
    }
}

/// HMAC-SHA256 with the key's two pad blocks already compressed: signing a token costs only the message's blocks.
unsafe struct HmacKey
{
    fixed uint _i[8];
    fixed uint _o[8];

    public HmacKey(ReadOnlySpan<byte> key)
    {
        byte* pad = stackalloc byte[64];
        ReadOnlySpan<byte> k = key.Length > 64 ? Sha256.Hash(key) : key;
        for (int i = 0; i < 64; i++) pad[i] = (byte)((i < k.Length ? k[i] : 0) ^ 0x36);
        fixed (uint* s = _i) { Sha256.InitState(s); Sha256.Compress(s, pad); }
        for (int i = 0; i < 64; i++) pad[i] ^= 0x36 ^ 0x5c;
        fixed (uint* s = _o) { Sha256.InitState(s); Sha256.Compress(s, pad); }
    }

    public void Sign(byte* msg, int n, byte* out32)
    {
        uint* st = stackalloc uint[8];
        byte* inner = stackalloc byte[32];
        fixed (uint* s = _i) for (int i = 0; i < 8; i++) st[i] = s[i];
        Sha256.Finish(st, 64, msg, n, inner);
        fixed (uint* s = _o) for (int i = 0; i < 8; i++) st[i] = s[i];
        Sha256.Finish(st, 64, inner, 32, out32);
    }
}

/// HS256 tokens. The signature is compared in its encoded form, so a token with altered trailing bits never passes.
static unsafe class Jwt
{
    public const int Thirty = 30 * 24 * 3600;
    public const int MaxToken = 2048;
    static HmacKey _key;
    const string HeaderB64 = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
    static readonly byte[] Header = Encoding.ASCII.GetBytes(HeaderB64);
    static readonly byte[] B64 = Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    public static void Init(string secret) => _key = new HmacKey(Encoding.UTF8.GetBytes(secret));

    static int Encode(byte* s, int n, byte* o)
    {
        fixed (byte* t = B64)
        {
            byte* start = o;
            int i = 0;
            for (; i + 3 <= n; i += 3)
            {
                uint v = (uint)s[i] << 16 | (uint)s[i + 1] << 8 | s[i + 2];
                *o++ = t[v >> 18]; *o++ = t[v >> 12 & 63]; *o++ = t[v >> 6 & 63]; *o++ = t[v & 63];
            }
            if (n - i == 1)
            {
                uint v = (uint)s[i] << 16;
                *o++ = t[v >> 18]; *o++ = t[v >> 12 & 63];
            }
            else if (n - i == 2)
            {
                uint v = (uint)s[i] << 16 | (uint)s[i + 1] << 8;
                *o++ = t[v >> 18]; *o++ = t[v >> 12 & 63]; *o++ = t[v >> 6 & 63];
            }
            return (int)(o - start);
        }
    }

    static int Decode(byte* s, int n, byte* o)
    {
        uint acc = 0;
        int bits = 0, len = 0;
        for (int i = 0; i < n; i++)
        {
            byte c = s[i];
            int v = c >= 'A' && c <= 'Z' ? c - 'A'
                : c >= 'a' && c <= 'z' ? c - 'a' + 26
                : c >= '0' && c <= '9' ? c - '0' + 52
                : c == '-' ? 62 : c == '_' ? 63 : -1;
            if (v < 0) return -1;
            acc = acc << 6 | (uint)v;
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                o[len++] = (byte)(acc >> bits);
            }
        }
        return len;
    }

    /// True if the signature, the algorithm and exp hold. sub is the user id, always above 0.
    public static bool Verify(byte* t, int n, long now, out long sub)
    {
        sub = 0;
        if (n <= 0 || n > MaxToken) return false;
        var tok = new ReadOnlySpan<byte>(t, n);
        int d1 = tok.IndexOf((byte)'.');
        if (d1 <= 0) return false;
        int d2 = tok.Slice(d1 + 1).IndexOf((byte)'.');
        if (d2 <= 0) return false;
        d2 += d1 + 1;
        if (n - d2 - 1 != 43) return false;

        if (!tok.Slice(0, d1).SequenceEqual(Header))
        {
            byte* hdr = stackalloc byte[MaxToken];
            int hn = Decode(t, d1, hdr);
            if (hn <= 0 || !JsonIn.ClaimStringIs(hdr, hn, "alg"u8, "HS256"u8)) return false;
        }

        byte* mac = stackalloc byte[32];
        byte* enc = stackalloc byte[44];
        _key.Sign(t, d2, mac);
        Encode(mac, 32, enc);
        int diff = 0;
        for (int i = 0; i < 43; i++) diff |= enc[i] ^ t[d2 + 1 + i];
        if (diff != 0) return false;

        byte* pl = stackalloc byte[MaxToken];
        int pn = Decode(t + d1 + 1, d2 - d1 - 1, pl);
        if (pn <= 0) return false;
        if (!JsonIn.ReadClaims(pl, pn, out sub, out double exp)) return false;
        return exp > now;
    }

    /// Writes a fresh token for the user to o and returns its length (at most 256 bytes).
    public static int Mint(long id, long now, byte* o)
    {
        byte* json = stackalloc byte[96];
        var pj = new Span<byte>(json, 96);
        int n = 0;
        "{\"sub\":\""u8.CopyTo(pj); n += 8;
        System.Buffers.Text.Utf8Formatter.TryFormat(id, pj.Slice(n), out int w); n += w;
        "\",\"iat\":"u8.CopyTo(pj.Slice(n)); n += 8;
        System.Buffers.Text.Utf8Formatter.TryFormat(now, pj.Slice(n), out w); n += w;
        ",\"exp\":"u8.CopyTo(pj.Slice(n)); n += 7;
        System.Buffers.Text.Utf8Formatter.TryFormat(now + Thirty, pj.Slice(n), out w); n += w;
        pj[n++] = (byte)'}';

        int len = Header.Length;
        Header.AsSpan().CopyTo(new Span<byte>(o, len));
        o[len++] = (byte)'.';
        len += Encode(json, n, o + len);
        byte* mac = stackalloc byte[32];
        _key.Sign(o, len, mac);
        o[len++] = (byte)'.';
        len += Encode(mac, 32, o + len);
        return len;
    }
}

/// SCRAM-SHA-256 client (RFC 5802/7677), for the Postgres login. Runs only at connect time.
static class Scram
{
    public static byte[] Pbkdf2(byte[] password, byte[] salt, int iter)
    {
        var first = new byte[salt.Length + 4];
        salt.CopyTo(first, 0);
        first[^1] = 1;
        byte[] u = Sha256.Hmac(password, first);
        var res = (byte[])u.Clone();
        for (int i = 1; i < iter; i++)
        {
            u = Sha256.Hmac(password, u);
            for (int j = 0; j < 32; j++) res[j] ^= u[j];
        }
        return res;
    }

    /// The client-final message for a server-first message.
    public static string Final(string password, string clientFirstBare, string serverFirst, string nonce)
    {
        string r = null, s = null;
        int iter = 0;
        foreach (var part in serverFirst.Split(','))
        {
            if (part.StartsWith("r=")) r = part.Substring(2);
            else if (part.StartsWith("s=")) s = part.Substring(2);
            else if (part.StartsWith("i=")) iter = int.Parse(part.Substring(2));
        }
        if (r == null || s == null || iter <= 0 || !r.StartsWith(nonce)) throw new InvalidOperationException("bad SCRAM server message");
        byte[] salted = Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(s), iter);
        byte[] clientKey = Sha256.Hmac(salted, "Client Key"u8);
        byte[] stored = Sha256.Hash(clientKey);
        string without = "c=biws,r=" + r;
        byte[] sig = Sha256.Hmac(stored, Encoding.UTF8.GetBytes(clientFirstBare + "," + serverFirst + "," + without));
        var proof = new byte[32];
        for (int i = 0; i < 32; i++) proof[i] = (byte)(clientKey[i] ^ sig[i]);
        return without + ",p=" + Convert.ToBase64String(proof);
    }
}
