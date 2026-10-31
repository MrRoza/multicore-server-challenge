using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Bare;

/// Something whose rows the network threads turn into responses: a finished read batch or write group. Each network
/// thread gets a packet with its own jobs. The rows stay valid and unchanged until the last packet is done, so the threads
/// read them without any locks, and then the owner goes back to the Postgres thread to be reused.
abstract class RenderSource
{
    public sealed class Packet
    {
        public RenderSource Source;
        public Job Head;
        public Packet Next;
    }

    readonly Packet[] _packets;
    int _pending;
    public RenderSource NextFree;
    protected readonly Db Db;

    protected RenderSource(Db db)
    {
        Db = db;
        _packets = new Packet[db.Loops.Length];
        for (int i = 0; i < _packets.Length; i++) _packets[i] = new Packet { Source = this };
    }

    public abstract void Answer(Job j, Loop io);

    protected void Gather(Job j)
    {
        Packet p = _packets[j.Loop.Index];
        j.Next = p.Head;
        p.Head = j;
    }

    /// Sends the gathered jobs out. The owner may be reused as soon as this returns and the Postgres thread looks again.
    protected void Publish()
    {
        int n = 0;
        for (int i = 0; i < _packets.Length; i++) if (_packets[i].Head != null) n++;
        if (n == 0) { Db.Free(this); return; }
        Volatile.Write(ref _pending, n);
        Loop[] loops = Db.Loops;
        for (int i = 0; i < _packets.Length; i++)
        {
            if (_packets[i].Head != null) loops[i].PostPacket(_packets[i]);
        }
    }

    /// On a network thread, after its packet's jobs are answered.
    public void PacketDone(Packet p)
    {
        p.Head = null;
        if (Interlocked.Decrement(ref _pending) == 0) Db.Free(this);
    }
}

/// The one thread that talks to Postgres: every read of every network thread queues here, so a batch holds as much as
/// possible, and every write goes down one connection, so a group commit holds as much as possible.
sealed unsafe class Db
{
    public readonly Loop[] Loops;
    readonly Settings _s;
    readonly PgConfig _cfg;
    Pg[] _pgs;
    int _ep, _wakeFd;
    long _nowMs;
    public long Now => _nowMs;

    const ulong TagWake = ulong.MaxValue;

    Job _readInbox, _writeInbox;
    RenderSource _freeInbox;
    readonly Stack<ReadBatch> _batches = new Stack<ReadBatch>();
    readonly Stack<WriteGroup> _groups = new Stack<WriteGroup>();

    Job _readHead, _readTail;
    int _readCount;
    long _readOldest;
    Job _writeHead, _writeTail;
    int _writeCount;

    // 0: running. 1: asleep, but a reply or the gather timer will wake it. 2: asleep with nothing to wake it.
    int _state;

    public Db(Settings s, PgConfig cfg, Loop[] loops)
    {
        _s = s;
        _cfg = cfg;
        Loops = loops;
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
        "enable_hashagg", "off",
    };

    static readonly List<string> WriteSettings = new List<string>
    {
        "plan_cache_mode", "force_generic_plan",
        "jit", "off",
        "max_parallel_workers_per_gather", "0",
    };

    public void Setup()
    {
        _ep = Sys.epoll_create1(0x80000);
        _wakeFd = Sys.eventfd(0, 0x800 | 0x80000);
        Add(_wakeFd, Sys.EPOLLIN | Sys.EPOLLET, TagWake);
        _pgs = new Pg[_s.ReadConns + _s.WriteConns];
        for (int i = 0; i < _pgs.Length; i++)
        {
            var pg = new Pg(i >= _s.ReadConns) { Index = i };
            _pgs[i] = pg;
            ConnectPg(pg, throwOnFail: true);
        }
    }

    void Add(int fd, uint events, ulong data)
    {
        EpollEvent ev;
        ev.Events = events;
        ev.Data = data;
        Sys.epoll_ctl(_ep, Sys.EPOLL_CTL_ADD, fd, &ev);
    }

    bool ConnectPg(Pg pg, bool throwOnFail)
    {
        try
        {
            if (pg.IsWriter) pg.Connect(_cfg, WriteSettings, Sql.Write, "w");
            else pg.Connect(_cfg, ReadSettings, Sql.Read, "r");
            Add(pg.Fd, Sys.EPOLLIN | Sys.EPOLLOUT | Sys.EPOLLET, (ulong)pg.Index);
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

    // ---------- from the network threads ----------

    /// A chain of jobs, tail.Next free. Reads only wake this thread if nothing else is going to.
    public void PushReads(Job head, Job tail)
    {
        Job old;
        do
        {
            old = Volatile.Read(ref _readInbox);
            tail.Next = old;
        } while (Interlocked.CompareExchange(ref _readInbox, head, old) != old);
        if (Volatile.Read(ref _state) == 2) Wake();
    }

    public void PushWrites(Job head, Job tail)
    {
        Job old;
        do
        {
            old = Volatile.Read(ref _writeInbox);
            tail.Next = old;
        } while (Interlocked.CompareExchange(ref _writeInbox, head, old) != old);
        if (Volatile.Read(ref _state) != 0) Wake();
    }

    void Wake()
    {
        if (Interlocked.Exchange(ref _state, 0) == 0) return;
        ulong one = 1;
        Sys.write(_wakeFd, (byte*)&one, 8);
    }

    public void Free(RenderSource r)
    {
        RenderSource old;
        do
        {
            old = Volatile.Read(ref _freeInbox);
            r.NextFree = old;
        } while (Interlocked.CompareExchange(ref _freeInbox, r, old) != old);
    }

    // ---------- the thread ----------

    public void Run()
    {
        const int MaxEvents = 128;
        EpollEvent* evs = (EpollEvent*)NativeMemory.Alloc(MaxEvents * (nuint)sizeof(EpollEvent));
        while (true)
        {
            Drain();
            _nowMs = Sys.MonoMs();
            Pump();
            int t = NextTimeout();
            if (t != 0)
            {
                // Sleeping is announced first and the inboxes looked at after, so a push can't slip between.
                Interlocked.Exchange(ref _state, _readHead == null && t < 0 ? 2 : 1);
                if (Volatile.Read(ref _readInbox) != null || Volatile.Read(ref _writeInbox) != null) t = 0;
            }
            int n = Sys.epoll_wait(_ep, evs, MaxEvents, t);
            Volatile.Write(ref _state, 0);
            _nowMs = Sys.MonoMs();
            for (int i = 0; i < n; i++)
            {
                ulong d = evs[i].Data;
                if (d == TagWake)
                {
                    ulong v;
                    Sys.read(_wakeFd, (byte*)&v, 8);
                }
                else PgEvent(_pgs[(int)d], evs[i].Events);
            }
        }
    }

    void Drain()
    {
        Job j = Interlocked.Exchange(ref _readInbox, null);
        while (j != null)
        {
            Job next = j.Next;
            EnqueueRead(j);
            j = next;
        }
        j = Interlocked.Exchange(ref _writeInbox, null);
        while (j != null)
        {
            Job next = j.Next;
            j.Next = null;
            if (_writeTail == null) _writeHead = j;
            else _writeTail.Next = j;
            _writeTail = j;
            _writeCount++;
            j = next;
        }
        RenderSource r = Interlocked.Exchange(ref _freeInbox, null);
        while (r != null)
        {
            RenderSource next = r.NextFree;
            r.NextFree = null;
            if (r is ReadBatch b)
            {
                b.Reset();
                _batches.Push(b);
            }
            else
            {
                var g = (WriteGroup)r;
                g.Reset();
                _groups.Push(g);
            }
            r = next;
        }
    }

    /// Also for reads that follow a commit: they must start after it, so they go through the queue like any other.
    public void EnqueueRead(Job j)
    {
        j.Next = null;
        if (_readTail == null)
        {
            _readHead = j;
            _readOldest = j.Enqueued;
        }
        else
        {
            _readTail.Next = j;
            if (j.Enqueued < _readOldest) _readOldest = j.Enqueued;
        }
        _readTail = j;
        _readCount++;
    }

    int NextTimeout()
    {
        int t = -1;
        if (_readHead != null && HasIdleReader())
        {
            long left = _readOldest + _s.GatherMs - Sys.MonoMs();
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
            else if (_readHead != null && (_readCount >= _s.BatchMax || _nowMs - _readOldest >= _s.GatherMs))
            {
                StartReadBatch(p);
            }
        }
    }

    void StartReadBatch(Pg pg)
    {
        ReadBatch b = _batches.Count > 0 ? _batches.Pop() : new ReadBatch(this);
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
        else _readOldest = _nowMs;      // close enough: what is left was pushed while the batch filled
        _readCount -= n;
        pg.Batch = b;
        b.Start(pg);
    }

    void StartWriteGroup(Pg pg)
    {
        WriteGroup g = _groups.Count > 0 ? _groups.Pop() : new WriteGroup(this);
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
        pg.Group = g;
        g.Start(pg);
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
                pg.UseOwnBuffers();
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
        if (pg.IsWriter)
        {
            pg.Group?.Fail();
            pg.Group = null;
        }
        else if (pg.Batch != null)
        {
            pg.Batch.Fail();
            pg.Batch = null;
        }
    }
}
