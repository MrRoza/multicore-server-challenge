using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bare;

/// A client connection. Holds no buffers while idle: 100k idle connections cost only this object and a socket.
sealed unsafe class Conn
{
    public int Fd;
    public uint Gen;
    public byte* In;
    public int InLen, InCap;
    public byte* Out;
    public int OutLen, OutPos, OutCap;
    public Job Cur;             // the request being answered; at most one at a time, later ones wait in In
    public bool CloseAfter;
    public bool Parsing;
}

public sealed class Settings
{
    public int Loops, ReadConns, WriteConns, BatchMax, GatherMs, WriteGroupMax;
}

/// One event loop thread: its clients, its Postgres connections, its queues. Loops share nothing, so nothing here takes
/// a lock except the inbox the bcrypt threads post to.
sealed unsafe partial class Loop
{
    const ulong TagOther = 1UL << 63;
    const ulong TagListener = TagOther, TagInbox = TagOther | 1, TagPgBase = TagOther | 2;
    const int RecvSize = 1 << 16;
    const int MaxHeader = 1 << 16;
    const int MaxBody = 8 << 20;

    readonly Settings _s;
    readonly PgConfig _pgCfg;
    readonly int _listenPort;
    readonly uint _listenIp;
    int _ep, _listen, _inboxFd, _spareFd = -1;
    byte* _recv;
    Conn[] _conns = new Conn[4096];
    readonly Stack<Conn> _connPool = new Stack<Conn>();
    readonly Stack<Job> _jobPool = new Stack<Job>();
    long _nowMs, _nowSec;

    readonly Buf _w = new Buf(1 << 20);
    int _bodyStart;

    Pg[] _pgs;
    readonly Stack<ReadBatch> _batchPool = new Stack<ReadBatch>();

    Job _readHead, _readTail;
    int _readCount;
    bool _readUrgent;
    Job _writeHead, _writeTail;
    int _writeCount;

    readonly object _inboxLock = new object();
    List<Job> _inbox = new List<Job>();
    List<Job> _inboxSwap = new List<Job>();

    public Loop(Settings s, PgConfig pg, uint ip, int port)
    {
        _s = s;
        _pgCfg = pg;
        _listenIp = ip;
        _listenPort = port;
    }

    // ---------- setup ----------

    public void Setup()
    {
        _ep = Sys.epoll_create1(0x80000);
        _recv = (byte*)NativeMemory.Alloc(RecvSize);

        _pgs = new Pg[_s.ReadConns + _s.WriteConns];
        for (int i = 0; i < _pgs.Length; i++)
        {
            var pg = new Pg(i >= _s.ReadConns) { Index = i };
            _pgs[i] = pg;
            ConnectPg(pg, throwOnFail: true);
        }

        _inboxFd = Sys.eventfd(0, 0x800 | 0x80000);
        Add(_inboxFd, Sys.EPOLLIN | Sys.EPOLLET, TagInbox);

        _listen = Sys.socket(Sys.AF_INET, Sys.SOCK_STREAM | Sys.SOCK_NONBLOCK | Sys.SOCK_CLOEXEC, 0);
        int one = 1;
        Sys.setsockopt(_listen, Sys.SOL_SOCKET, Sys.SO_REUSEADDR, &one, 4);
        Sys.setsockopt(_listen, Sys.SOL_SOCKET, Sys.SO_REUSEPORT, &one, 4);
        Sys.setsockopt(_listen, Sys.IPPROTO_TCP, Sys.TCP_NODELAY, &one, 4);   // accepted sockets inherit it
        byte* addr = stackalloc byte[16];
        Sys.Ipv4(addr, _listenIp, _listenPort);
        if (Sys.bind(_listen, addr, 16) != 0) throw new InvalidOperationException("bind failed, errno " + Sys.Errno);
        if (Sys.listen(_listen, 65535) != 0) throw new InvalidOperationException("listen failed, errno " + Sys.Errno);
        Add(_listen, Sys.EPOLLIN, TagListener);
        OpenSpareFd();
    }

    void OpenSpareFd()
    {
        fixed (byte* p = "/dev/null\0"u8) _spareFd = Sys.open(p, 0x80000);
    }

    void Add(int fd, uint events, ulong data)
    {
        EpollEvent ev;
        ev.Events = events;
        ev.Data = data;
        Sys.epoll_ctl(_ep, Sys.EPOLL_CTL_ADD, fd, &ev);
    }

    static readonly List<string> ReadSettings = new List<string>
    {
        // Every read is index lookups per key, so a plan with anything else in it would be the wrong plan.
        "plan_cache_mode", "force_generic_plan",
        "jit", "off",
        "max_parallel_workers_per_gather", "0",
        "enable_hashjoin", "off",
        "enable_mergejoin", "off",
        "enable_memoize", "off",
        "enable_material", "off",
        "enable_seqscan", "off",
        "enable_bitmapscan", "off",
    };

    static readonly List<string> WriteSettings = new List<string>
    {
        "plan_cache_mode", "force_generic_plan",
        "jit", "off",
        "max_parallel_workers_per_gather", "0",
    };

    bool ConnectPg(Pg pg, bool throwOnFail)
    {
        try
        {
            if (pg.IsWriter) pg.Connect(_pgCfg, WriteSettings, Sql.Write, "w");
            else pg.Connect(_pgCfg, ReadSettings, Sql.Read, "r");
            Add(pg.Fd, Sys.EPOLLIN | Sys.EPOLLOUT | Sys.EPOLLET, TagPgBase + (ulong)pg.Index);
            return true;
        }
        catch (Exception e)
        {
            if (throwOnFail) throw;
            Console.Error.WriteLine("postgres reconnect failed: " + e.Message);
            pg.RetryAt = Sys.MonoMs() + 1000;
            return false;
        }
    }

    // ---------- the loop ----------

    public void Run()
    {
        const int MaxEvents = 1024;
        EpollEvent* evs = (EpollEvent*)NativeMemory.Alloc(MaxEvents * (nuint)sizeof(EpollEvent));
        while (true)
        {
            int n = Sys.epoll_wait(_ep, evs, MaxEvents, NextTimeout());
            _nowMs = Sys.MonoMs();
            _nowSec = Sys.UnixSec();
            for (int i = 0; i < n; i++)
            {
                ulong d = evs[i].Data;
                uint e = evs[i].Events;
                if ((d & TagOther) == 0) ClientEvent(d, e);
                else if (d == TagListener) AcceptAll();
                else if (d == TagInbox) DrainInbox();
                else PgEvent(_pgs[(int)(d - TagPgBase)], e);
            }
            Pump();
        }
    }

    int NextTimeout()
    {
        int t = -1;
        if (_readHead != null && HasIdleReader())
        {
            long left = _readUrgent ? 0 : _readHead.Enqueued + _s.GatherMs - Sys.MonoMs();
            t = left <= 0 ? 0 : (int)left;
        }
        for (int i = 0; i < _pgs.Length; i++)
        {
            if (_pgs[i].Dead && (t < 0 || t > 1000)) t = 1000;
        }
        return t;
    }

    bool HasIdleReader()
    {
        for (int i = 0; i < _s.ReadConns; i++)
        {
            Pg p = _pgs[i];
            if (!p.Busy && !p.Dead) return true;
        }
        return false;
    }

    /// Starts whatever can start: a read batch once its oldest request has waited out the gather window (or it's full);
    /// a write group as soon as a writer is free.
    void Pump()
    {
        for (int i = 0; i < _pgs.Length; i++)
        {
            Pg p = _pgs[i];
            if (p.Dead)
            {
                if (_nowMs >= p.RetryAt) ConnectPg(p, throwOnFail: false);
                continue;
            }
            if (p.Busy) continue;
            if (p.IsWriter)
            {
                if (_writeHead != null) StartWriteGroup(p);
            }
            else if (_readHead != null &&
                     (_readUrgent || _readCount >= _s.BatchMax || _nowMs - _readHead.Enqueued >= _s.GatherMs))
            {
                StartReadBatch(p);
            }
        }
    }

    void StartReadBatch(Pg pg)
    {
        ReadBatch b = _batchPool.Count > 0 ? _batchPool.Pop() : new ReadBatch(this);
        int n = 0;
        while (_readHead != null && n < _s.BatchMax)
        {
            Job j = _readHead;
            _readHead = j.Next;
            j.Next = null;
            b.Add(j);
            n++;
        }
        if (_readHead == null) _readTail = null;
        _readCount -= n;
        _readUrgent = false;
        pg.Batch = b;
        b.Pg = pg;
        b.Begin();
    }

    void StartWriteGroup(Pg pg)
    {
        WriteGroup g = pg.Group ??= new WriteGroup(this, pg);
        int n = 0;
        while (_writeHead != null && n < _s.WriteGroupMax)
        {
            Job j = _writeHead;
            _writeHead = j.Next;
            j.Next = null;
            g.Add(j);
            n++;
        }
        if (_writeHead == null) _writeTail = null;
        _writeCount -= n;
        g.Begin();
    }

    public void BatchDone(ReadBatch b)
    {
        b.Pg.Batch = null;
        b.Pg = null;
        _batchPool.Push(b);
    }

    /// Sends what the pipeline built, and fails its requests if the connection is gone.
    public void KickPg(Pg pg)
    {
        if (!pg.Flush()) LostPg(pg);
    }

    void PgEvent(Pg pg, uint e)
    {
        if (pg.Dead) return;
        if ((e & (Sys.EPOLLERR | Sys.EPOLLHUP)) != 0) { LostPg(pg); return; }
        if ((e & Sys.EPOLLOUT) != 0 && !pg.Flush()) { LostPg(pg); return; }
        if ((e & Sys.EPOLLIN) != 0)
        {
            if (!pg.Busy)
            {
                // Nothing is expected, but the socket must be drained to notice a shutdown or a notice.
                pg.BeginRound(0);
                bool alive = pg.Receive();
                pg.Finish();
                if (!alive) LostPg(pg);
                return;
            }
            if (!pg.Receive()) { LostPg(pg); return; }
            if (pg.Ready)
            {
                if (pg.IsWriter) pg.Group.OnReply();
                else pg.Batch.OnReply();
            }
        }
    }

    void LostPg(Pg pg)
    {
        Console.Error.WriteLine("lost a Postgres connection");
        pg.CloseSocket();
        pg.RetryAt = _nowMs + 200;
        if (pg.IsWriter) pg.Group?.Fail();
        else if (pg.Batch != null) pg.Batch.Fail();
    }

    // ---------- bcrypt results ----------

    /// Called from a bcrypt thread.
    public void Post(Job j)
    {
        bool wake;
        lock (_inboxLock)
        {
            wake = _inbox.Count == 0;
            _inbox.Add(j);
        }
        if (wake)
        {
            ulong one = 1;
            Sys.write(_inboxFd, (byte*)&one, 8);
        }
    }

    void DrainInbox()
    {
        ulong v;
        Sys.read(_inboxFd, (byte*)&v, 8);
        List<Job> got;
        lock (_inboxLock)
        {
            got = _inbox;
            _inbox = _inboxSwap;
        }
        for (int i = 0; i < got.Count; i++) AfterBcrypt(got[i]);
        got.Clear();
        _inboxSwap = got;
    }

    // ---------- queues ----------

    public void EnqueueRead(Job j, bool urgent = false)
    {
        j.Enqueued = _nowMs;
        j.Next = null;
        if (_readTail == null) _readHead = j;
        else _readTail.Next = j;
        _readTail = j;
        _readCount++;
        if (urgent) _readUrgent = true;
    }

    public void EnqueueWrite(Job j)
    {
        j.Enqueued = _nowMs;
        j.Next = null;
        if (_writeTail == null) _writeHead = j;
        else _writeTail.Next = j;
        _writeTail = j;
        _writeCount++;
    }

    Job NewJob(Conn c, Op op)
    {
        Job j = _jobPool.Count > 0 ? _jobPool.Pop() : new Job { Loop = this };
        j.Reset();
        j.Op = op;
        j.Conn = c;
        j.ConnGen = c.Gen;
        c.Cur = j;
        return j;
    }

    public bool Alive(Job j) => j.Conn.Gen == j.ConnGen;

    /// The job's over, answered or abandoned: the connection moves on to any request waiting behind it.
    public void JobDone(Job j)
    {
        Conn c = j.Conn;
        bool alive = c.Gen == j.ConnGen;
        j.Conn = null;
        j.ShrinkAfterUse();
        _jobPool.Push(j);
        if (!alive) return;
        c.Cur = null;
        if (c.CloseAfter)
        {
            if (c.OutLen == c.OutPos) CloseConn(c);
            return;
        }
        if (c.InLen > 0 && !c.Parsing) Consume(c, c.In, c.InLen);
    }

    // ---------- clients ----------

    void AcceptAll()
    {
        while (true)
        {
            int fd = Sys.accept4(_listen, null, null, Sys.SOCK_NONBLOCK | Sys.SOCK_CLOEXEC);
            if (fd < 0)
            {
                int e = Sys.Errno;
                if (e == Sys.EINTR) continue;
                if ((e == Sys.EMFILE || e == Sys.ENFILE) && _spareFd >= 0)
                {
                    // Out of descriptors: drop this connection rather than spin on a listener that stays readable.
                    Sys.close(_spareFd);
                    _spareFd = -1;
                    int f = Sys.accept4(_listen, null, null, Sys.SOCK_CLOEXEC);
                    if (f >= 0) Sys.close(f);
                    OpenSpareFd();
                    continue;
                }
                return;
            }
            if (fd >= _conns.Length) Array.Resize(ref _conns, Math.Max(_conns.Length * 2, fd + 1));
            Conn c = _connPool.Count > 0 ? _connPool.Pop() : new Conn();
            c.Fd = fd;
            c.Gen = (c.Gen + 1) & 0x7FFFFFFF;
            _conns[fd] = c;
            Add(fd, Sys.EPOLLIN | Sys.EPOLLOUT | Sys.EPOLLET, (ulong)c.Gen << 32 | (uint)fd);
            OnReadable(c);
        }
    }

    void ClientEvent(ulong d, uint e)
    {
        int fd = (int)(uint)d;
        Conn c = fd < _conns.Length ? _conns[fd] : null;
        if (c == null || c.Gen != (uint)(d >> 32)) return;
        if ((e & (Sys.EPOLLERR | Sys.EPOLLHUP)) != 0) { CloseConn(c); return; }
        if ((e & Sys.EPOLLOUT) != 0 && c.OutLen > c.OutPos && !FlushOut(c)) return;
        if ((e & Sys.EPOLLIN) != 0) OnReadable(c);
    }

    void OnReadable(Conn c)
    {
        uint gen = c.Gen;
        while (c.Gen == gen)
        {
            byte* dst;
            int room;
            bool intoConn = c.InLen > 0 || c.Cur != null;
            if (intoConn)
            {
                EnsureIn(c, c.InLen + RecvSize);
                dst = c.In + c.InLen;
                room = c.InCap - c.InLen;
            }
            else
            {
                dst = _recv;
                room = RecvSize;
            }
            nint n = Sys.recv(c.Fd, dst, room, 0);
            if (n > 0)
            {
                if (intoConn)
                {
                    c.InLen += (int)n;
                    if (c.Cur == null) Consume(c, c.In, c.InLen);
                }
                else
                {
                    Consume(c, dst, (int)n);
                }
                if (n < room) return;   // the socket is drained; edge-triggered epoll reports anything new
                continue;
            }
            if (n == 0) { CloseConn(c); return; }
            int e = Sys.Errno;
            if (e == Sys.EAGAIN) return;
            if (e != Sys.EINTR) { CloseConn(c); return; }
        }
    }

    static void EnsureIn(Conn c, int cap)
    {
        if (c.InCap >= cap) return;
        int n = Math.Max(c.InCap * 2, cap);
        c.In = (byte*)NativeMemory.Realloc(c.In, (nuint)n);
        c.InCap = n;
    }

    /// Answers every complete request in buf that it can, and keeps the rest on the connection.
    void Consume(Conn c, byte* buf, int n)
    {
        uint gen = c.Gen;
        c.Parsing = true;
        int off = 0;
        while (off < n && c.Cur == null && !c.CloseAfter)
        {
            int used = OneRequest(c, buf + off, n - off);
            if (c.Gen != gen) return;       // closed underneath us; its buffers are gone
            if (used == 0) break;
            off += used;
        }
        c.Parsing = false;
        int left = n - off;
        if (c.CloseAfter && c.Cur == null)
        {
            if (c.OutLen == c.OutPos) CloseConn(c);
            return;
        }
        if (left == 0)
        {
            if (buf == c.In) { c.InLen = 0; ReleaseIn(c); }
            return;
        }
        if (buf == c.In)
        {
            if (off > 0) Buffer.MemoryCopy(buf + off, c.In, c.InCap, left);
            c.InLen = left;
        }
        else
        {
            EnsureIn(c, left);
            Buffer.MemoryCopy(buf + off, c.In, c.InCap, left);
            c.InLen = left;
        }
    }

    static void ReleaseIn(Conn c)
    {
        if (c.In == null) return;
        NativeMemory.Free(c.In);
        c.In = null;
        c.InCap = 0;
    }

    public void CloseConn(Conn c)
    {
        Sys.close(c.Fd);
        _conns[c.Fd] = null;
        c.Gen = (c.Gen + 1) & 0x7FFFFFFF;   // anything still in flight for it sees a different generation
        ReleaseIn(c);
        c.InLen = 0;
        if (c.Out != null)
        {
            NativeMemory.Free(c.Out);
            c.Out = null;
        }
        c.OutLen = c.OutPos = c.OutCap = 0;
        c.Cur = null;
        c.CloseAfter = false;
        c.Parsing = false;
        _connPool.Push(c);
    }

    // ---------- sending ----------

    /// Sends the response in _w, or queues what the socket won't take. False if the connection closed.
    bool SendResponse(Conn c)
    {
        byte* p = _w.P;
        int n = _w.Len;
        if (c.OutLen == c.OutPos)
        {
            while (n > 0)
            {
                nint s = Sys.send(c.Fd, p, n, Sys.MSG_NOSIGNAL);
                if (s > 0) { p += s; n -= (int)s; continue; }
                int e = Sys.Errno;
                if (s < 0 && e == Sys.EAGAIN) break;
                if (s < 0 && e == Sys.EINTR) continue;
                CloseConn(c);
                return false;
            }
            if (n == 0) return true;
            c.OutLen = c.OutPos = 0;
        }
        if (c.OutCap - c.OutLen < n)
        {
            int cap = Math.Max(c.OutCap * 2, c.OutLen + n);
            c.Out = (byte*)NativeMemory.Realloc(c.Out, (nuint)cap);
            c.OutCap = cap;
        }
        Buffer.MemoryCopy(p, c.Out + c.OutLen, c.OutCap - c.OutLen, n);
        c.OutLen += n;
        return true;
    }

    /// The socket took more. False if the connection closed.
    bool FlushOut(Conn c)
    {
        while (c.OutPos < c.OutLen)
        {
            nint s = Sys.send(c.Fd, c.Out + c.OutPos, c.OutLen - c.OutPos, Sys.MSG_NOSIGNAL);
            if (s > 0) { c.OutPos += (int)s; continue; }
            int e = Sys.Errno;
            if (s < 0 && e == Sys.EAGAIN) return true;
            if (s < 0 && e == Sys.EINTR) continue;
            CloseConn(c);
            return false;
        }
        NativeMemory.Free(c.Out);
        c.Out = null;
        c.OutCap = c.OutLen = c.OutPos = 0;
        if (c.CloseAfter && c.Cur == null)
        {
            CloseConn(c);
            return false;
        }
        return true;
    }
}

static class Server
{
    public static void Run(Settings s, PgConfig pg, uint ip, int port)
    {
        var loops = new Loop[s.Loops];
        for (int i = 0; i < loops.Length; i++)
        {
            loops[i] = new Loop(s, pg, ip, port);
            loops[i].Setup();
        }
        Console.Error.WriteLine($"conduit-bare: {s.Loops} loops, {s.Loops * (s.ReadConns + s.WriteConns)} Postgres connections, port {port}");
        for (int i = 1; i < loops.Length; i++)
        {
            var t = new Thread(loops[i].Run, 16 << 20) { Name = "loop" + i };
            t.Start();
        }
        loops[0].Run();
    }
}
