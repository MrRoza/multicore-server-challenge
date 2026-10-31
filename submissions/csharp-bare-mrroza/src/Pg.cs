using System;
using System.Collections.Generic;
using System.Text;

namespace Bare;

/// DATABASE_URL, split up: postgres://user:password@host:port/db
sealed class PgConfig
{
    public string User, Password, Host, Db;
    public int Port = 5432;

    public static PgConfig Parse(string url)
    {
        int s = url.IndexOf("://", StringComparison.Ordinal);
        if (s < 0) throw new ArgumentException("DATABASE_URL must look like postgres://user:password@host:port/db");
        string rest = url.Substring(s + 3);
        int at = rest.LastIndexOf('@');
        if (at < 0) throw new ArgumentException("DATABASE_URL has no user");
        string cred = rest.Substring(0, at);
        rest = rest.Substring(at + 1);
        var c = new PgConfig();
        int colon = cred.IndexOf(':');
        c.User = Uri.UnescapeDataString(colon < 0 ? cred : cred.Substring(0, colon));
        c.Password = colon < 0 ? "" : Uri.UnescapeDataString(cred.Substring(colon + 1));
        int slash = rest.IndexOf('/');
        c.Db = slash < 0 ? c.User : rest.Substring(slash + 1);
        int q = c.Db.IndexOf('?');
        if (q >= 0) c.Db = c.Db.Substring(0, q);
        string hp = slash < 0 ? rest : rest.Substring(0, slash);
        int pc = hp.LastIndexOf(':');
        c.Host = pc < 0 ? hp : hp.Substring(0, pc);
        if (pc >= 0) c.Port = int.Parse(hp.Substring(pc + 1));
        return c;
    }
}

/// One Postgres connection, driven by the loop that owns it: a pipeline of Bind/Execute ending in one Sync goes out,
/// and everything up to its ReadyForQuery is gathered in one buffer, so rows are read in place and never copied.
sealed unsafe class Pg
{
    public int Fd = -1;
    public int Index;
    public readonly bool IsWriter;
    public readonly Buf Out = new Buf(1 << 16);
    readonly Buf[] _own = { new Buf(1 << 12) };
    public Buf[] In;                // whichever batch or group is running owns them; replies outlive this connection's turn
    public int Phase;               // which In buffer the current reply goes to
    int _sent, _scan;
    public bool Busy;
    public bool Ready;              // a whole reply is in In[Phase]
    public bool Dead = true;
    public long RetryAt;
    public int ErrorCount;
    public int FirstError;          // SQLSTATE as five bytes packed in an int; 0 for none
    public ReadBatch Batch;
    public WriteGroup Group;

    int _lenAt;

    public Pg(bool writer)
    {
        IsWriter = writer;
        In = _own;
    }

    public void UseOwnBuffers() => In = _own;

    public static int SqlState(string s) => s[0] * 16777216 + s[1] * 65536 + s[2] * 256 + s[3] * 16 + s[4];

    // ---------- pipeline building ----------

    /// Starts a Bind for a prepared statement; all parameters and results are binary.
    public void Bind(byte[] name, int nParams)
    {
        Buf o = Out;
        o.Put((byte)'B');
        _lenAt = o.Len;
        o.Be32(0);
        o.Put(0);
        o.Put(name);
        o.Be16(1); o.Be16(1);
        o.Be16(nParams);
    }

    /// Ends the Bind and adds its Execute.
    public void Exec()
    {
        Buf o = Out;
        o.Be16(1); o.Be16(1);
        o.PatchBe32(_lenAt, o.Len - _lenAt);
        o.Put("E\0\0\0\t\0\0\0\0\0"u8);
    }

    public void Sync() => Out.Put("S\0\0\0\u0004"u8);

    public void PNull() => Out.Be32(-1);

    public void PText(byte* p, int n)
    {
        Out.Be32(n);
        Out.Put(p, n);
    }

    public void PLong(long v)
    {
        Out.Be32(8);
        Out.Be64(v);
    }

    public void PSlot(byte* p, int n)
    {
        if (n < 0) PNull();
        else PText(p, n);
    }

    const int OidText = 25, OidInt8 = 20, OidInt4 = 23;

    int ArrayStart(int oid, int n)
    {
        Buf o = Out;
        int at = o.Len;
        o.Be32(0);
        o.Be32(1); o.Be32(0); o.Be32(oid);
        o.Be32(n); o.Be32(1);
        return at;
    }

    void ArrayEnd(int at) => Out.PatchBe32(at, Out.Len - at - 4);

    public void PTextArray(KeySet k)
    {
        int at = ArrayStart(OidText, k.Count);
        for (int i = 0; i < k.Count; i++)
        {
            Out.Be32(k.L[i]);
            Out.Put((byte*)k.P[i], k.L[i]);
        }
        ArrayEnd(at);
    }

    /// Column a of the set as int8[].
    public void PLongArrayA(KeySet k)
    {
        int at = ArrayStart(OidInt8, k.Count);
        for (int i = 0; i < k.Count; i++) { Out.Be32(8); Out.Be64(k.A[i]); }
        ArrayEnd(at);
    }

    public void PLongArrayB(KeySet k)
    {
        int at = ArrayStart(OidInt8, k.Count);
        for (int i = 0; i < k.Count; i++) { Out.Be32(8); Out.Be64(k.B[i]); }
        ArrayEnd(at);
    }

    /// Limit and offset are packed in column b.
    public void PLimits(KeySet k)
    {
        int at = ArrayStart(OidInt4, k.Count);
        for (int i = 0; i < k.Count; i++) { Out.Be32(4); Out.Be32((int)(k.B[i] >> 32)); }
        ArrayEnd(at);
    }

    public void POffsets(KeySet k)
    {
        int at = ArrayStart(OidInt4, k.Count);
        for (int i = 0; i < k.Count; i++) { Out.Be32(4); Out.Be32((int)k.B[i]); }
        ArrayEnd(at);
    }

    /// int8[] of the first n values, or only the first of each run of equal values (count of them given).
    public void PLongs(long[] v, int n, int count, bool distinct)
    {
        int at = ArrayStart(OidInt8, count);
        for (int i = 0; i < n; i++)
        {
            if (distinct && i > 0 && v[i] == v[i - 1]) continue;
            Out.Be32(8);
            Out.Be64(v[i]);
        }
        ArrayEnd(at);
    }

    public void PInts(int[] v, int n)
    {
        int at = ArrayStart(OidInt4, n);
        for (int i = 0; i < n; i++) { Out.Be32(4); Out.Be32(v[i]); }
        ArrayEnd(at);
    }

    /// int4[] of where each run of equal values starts, or ends, 1-based.
    public void PSliceBounds(long[] v, int n, int count, bool ends)
    {
        int at = ArrayStart(OidInt4, count);
        for (int i = 0; i < n; i++)
        {
            bool edge = ends ? i == n - 1 || v[i + 1] != v[i] : i == 0 || v[i - 1] != v[i];
            if (!edge) continue;
            Out.Be32(4);
            Out.Be32(i + 1);
        }
        ArrayEnd(at);
    }

    /// A text[] whose elements are already in array format.
    public void PTagArray(byte* items, int bytes, int count)
    {
        int at = ArrayStart(OidText, count);
        Out.Put(items, bytes);
        ArrayEnd(at);
    }

    // ---------- socket ----------

    public void BeginRound(int phase)
    {
        Phase = phase;
        In[phase].Len = 0;
        _scan = 0;
        _sent = 0;
        Ready = false;
        ErrorCount = 0;
        FirstError = 0;
        Busy = true;
    }

    /// False if the connection broke.
    public bool Flush()
    {
        Buf o = Out;
        while (_sent < o.Len)
        {
            nint n = Sys.send(Fd, o.P + _sent, o.Len - _sent, Sys.MSG_NOSIGNAL);
            if (n > 0) { _sent += (int)n; continue; }
            int e = Sys.Errno;
            if (n < 0 && e == Sys.EAGAIN) return true;
            if (n < 0 && e == Sys.EINTR) continue;
            return false;
        }
        o.Len = 0;
        _sent = 0;
        return true;
    }

    /// Reads what is there. False if the connection broke. Ready says whether the reply is complete.
    public bool Receive()
    {
        Buf b = In[Phase];
        while (true)
        {
            b.Ensure(1 << 16);
            int room = b.Cap - b.Len;
            nint n = Sys.recv(Fd, b.P + b.Len, room, 0);
            if (n > 0)
            {
                b.Len += (int)n;
                Scan(b);
                if (Ready || n < room) return true;
                continue;
            }
            if (n == 0) return false;
            int e = Sys.Errno;
            if (e == Sys.EAGAIN) return true;
            if (e != Sys.EINTR) return false;
        }
    }

    void Scan(Buf b)
    {
        while (b.Len - _scan >= 5)
        {
            int len = Wire.I32(b.P + _scan + 1);
            if (b.Len - _scan < 1 + len) return;
            byte t = b.P[_scan];
            if (t == (byte)'Z') { Ready = true; return; }
            if (t == (byte)'E')
            {
                ErrorCount++;
                if (FirstError == 0) FirstError = ErrorField(b.P + _scan + 5, len - 4);
            }
            _scan += 1 + len;
        }
    }

    static int ErrorField(byte* p, int n)
    {
        int i = 0;
        while (i < n && p[i] != 0)
        {
            byte f = p[i++];
            int s = i;
            while (i < n && p[i] != 0) i++;
            if (f == (byte)'C' && i - s == 5)
                return p[s] * 16777216 + p[s + 1] * 65536 + p[s + 2] * 256 + p[s + 3] * 16 + p[s + 4];
            i++;
        }
        return -1;
    }

    public void Finish()
    {
        Busy = false;
        Ready = false;
    }

    public void CloseSocket()
    {
        if (Fd >= 0) Sys.close(Fd);
        Fd = -1;
        Dead = true;
        Busy = false;
        Ready = false;
        Out.Len = 0;
        _sent = 0;
    }

    // ---------- connect (blocking, at startup and after a lost connection) ----------

    /// Connects, logs in and prepares this connection's statements. The socket ends up non-blocking.
    public void Connect(PgConfig cfg, List<string> settings, string[] sql, string prefix)
    {
        int fd = Sys.socket(Sys.AF_INET, Sys.SOCK_STREAM | Sys.SOCK_CLOEXEC, 0);
        if (fd < 0) throw new InvalidOperationException("socket failed");
        try
        {
            byte* addr = stackalloc byte[16];
            Sys.Ipv4(addr, Sys.ParseIp(cfg.Host), cfg.Port);
            if (Sys.connect(fd, addr, 16) != 0) throw new InvalidOperationException("cannot connect to Postgres, errno " + Sys.Errno);
            int one = 1;
            Sys.setsockopt(fd, Sys.IPPROTO_TCP, Sys.TCP_NODELAY, &one, 4);

            Login(fd, cfg, settings);
            var o = new Buf(1 << 16);
            for (int i = 0; i < sql.Length; i++)
            {
                o.Put((byte)'P');
                int at = o.Len;
                o.Be32(0);
                o.Put(Encoding.ASCII.GetBytes(prefix + i)); o.Put(0);
                o.Put(Encoding.UTF8.GetBytes(sql[i])); o.Put(0);
                o.Be16(0);
                o.PatchBe32(at, o.Len - at);
            }
            o.Put("S\0\0\0\u0004"u8);
            SendAll(fd, o.P, o.Len);
            while (true)
            {
                byte[] m = ReadMsg(fd, out byte t);
                if (t == (byte)'E') throw new InvalidOperationException("prepare failed: " + ErrorText(m));
                if (t == (byte)'Z') break;
            }

            int fl = 0x800; // O_NONBLOCK
            Fcntl.SetFlags(fd, fl);
            Fd = fd;
            Dead = false;
        }
        catch
        {
            Sys.close(fd);
            throw;
        }
    }

    static void Login(int fd, PgConfig cfg, List<string> settings)
    {
        var o = new Buf(1024);
        o.Be32(0);
        o.Be32(196608);
        void Kv(string k, string v)
        {
            o.Put(Encoding.UTF8.GetBytes(k)); o.Put(0);
            o.Put(Encoding.UTF8.GetBytes(v)); o.Put(0);
        }
        Kv("user", cfg.User);
        Kv("database", cfg.Db);
        Kv("client_encoding", "UTF8");
        Kv("application_name", "conduit-bare");
        for (int i = 0; i + 1 < settings.Count; i += 2) Kv(settings[i], settings[i + 1]);
        o.Put(0);
        o.PatchBe32(0, o.Len);
        SendAll(fd, o.P, o.Len);

        string clientFirstBare = null, nonce = null;
        while (true)
        {
            byte[] m = ReadMsg(fd, out byte t);
            if (t == (byte)'E') throw new InvalidOperationException("Postgres login failed: " + ErrorText(m));
            if (t == (byte)'Z') return;
            if (t != (byte)'R') continue;
            int kind = m[0] << 24 | m[1] << 16 | m[2] << 8 | m[3];
            switch (kind)
            {
                case 0: break;
                case 3:
                {
                    var r = new Buf(256);
                    r.Put((byte)'p');
                    r.Be32(4 + Encoding.UTF8.GetByteCount(cfg.Password) + 1);
                    r.Put(Encoding.UTF8.GetBytes(cfg.Password)); r.Put(0);
                    SendAll(fd, r.P, r.Len);
                    break;
                }
                case 10:
                {
                    byte* rnd = stackalloc byte[18];
                    Sys.Random(rnd, 18);
                    nonce = Convert.ToBase64String(new ReadOnlySpan<byte>(rnd, 18));
                    clientFirstBare = "n=,r=" + nonce;
                    byte[] first = Encoding.ASCII.GetBytes("n,," + clientFirstBare);
                    var r = new Buf(256);
                    r.Put((byte)'p');
                    int at = r.Len;
                    r.Be32(0);
                    r.Put("SCRAM-SHA-256\0"u8);
                    r.Be32(first.Length);
                    r.Put(first);
                    r.PatchBe32(at, r.Len - at);
                    SendAll(fd, r.P, r.Len);
                    break;
                }
                case 11:
                {
                    string serverFirst = Encoding.UTF8.GetString(m, 4, m.Length - 4);
                    byte[] fin = Encoding.ASCII.GetBytes(Scram.Final(cfg.Password, clientFirstBare, serverFirst, nonce));
                    var r = new Buf(256);
                    r.Put((byte)'p');
                    r.Be32(4 + fin.Length);
                    r.Put(fin);
                    SendAll(fd, r.P, r.Len);
                    break;
                }
                case 12: break;
                default:
                    throw new InvalidOperationException("unsupported Postgres auth method " + kind);
            }
        }
    }

    static string ErrorText(byte[] m)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < m.Length && m[i] != 0)
        {
            byte f = m[i++];
            int s = i;
            while (i < m.Length && m[i] != 0) i++;
            if (f == (byte)'M' || f == (byte)'C') sb.Append(Encoding.UTF8.GetString(m, s, i - s)).Append(' ');
            i++;
        }
        return sb.ToString();
    }

    static void SendAll(int fd, byte* p, int n)
    {
        while (n > 0)
        {
            nint w = Sys.send(fd, p, n, Sys.MSG_NOSIGNAL);
            if (w <= 0)
            {
                if (w < 0 && Sys.Errno == Sys.EINTR) continue;
                throw new InvalidOperationException("Postgres write failed");
            }
            p += w;
            n -= (int)w;
        }
    }

    static void RecvAll(int fd, byte* p, int n)
    {
        while (n > 0)
        {
            nint r = Fcntl.BlockingRecv(fd, p, n);
            if (r <= 0)
            {
                if (r < 0 && Sys.Errno == Sys.EINTR) continue;
                throw new InvalidOperationException("Postgres closed the connection");
            }
            p += r;
            n -= (int)r;
        }
    }

    static byte[] ReadMsg(int fd, out byte type)
    {
        byte* h = stackalloc byte[5];
        RecvAll(fd, h, 5);
        type = h[0];
        int len = Wire.I32(h + 1) - 4;
        var m = new byte[len];
        fixed (byte* pm = m) if (len > 0) RecvAll(fd, pm, len);
        return m;
    }
}

static unsafe class Fcntl
{
    [System.Runtime.InteropServices.DllImport("libc")] static extern int fcntl(int fd, int cmd, long arg);
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "recv")] public static extern nint BlockingRecv(int fd, byte* p, nint n, int flags = 0);

    public static void SetFlags(int fd, int flags) => fcntl(fd, 4, flags);
}
