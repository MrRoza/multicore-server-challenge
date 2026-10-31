using System;
using System.Collections.Generic;

namespace Bare;

/// All the reads one Postgres connection is answering at once, in up to three round trips:
/// A resolves what requests name (slugs, users, pages of ids), B reads each distinct article once, and C reads the
/// (viewer, author) pairs once the authors are known. Requests asking for the same thing share its rows, for this batch
/// only: everything is dropped when the last response has gone out.
sealed unsafe class ReadBatch
{
    readonly Loop _loop;
    public Pg Pg;
    Job[] _jobs = new Job[256];
    int _count;

    readonly KeySet _usersById = new KeySet(), _usersByEmail = new KeySet(), _profiles = new KeySet(), _slugs = new KeySet();
    readonly KeySet[] _pages = { new KeySet(), new KeySet(), new KeySet(), new KeySet(), new KeySet() };
    readonly KeySet[] _counts = { new KeySet(), new KeySet(), new KeySet(), new KeySet(), new KeySet() };
    readonly KeySet _info = new KeySet(), _bodies = new KeySet(), _favorited = new KeySet(), _comments = new KeySet(), _following = new KeySet();
    readonly LongIndex _tagRank = new LongIndex();
    nint[] _tagName = new nint[512];
    int[] _tagLen = new int[512];
    int _tagCount;
    bool _needTags;
    readonly Buf _tagsJson = new Buf(1 << 14);
    bool _tagsJsonBuilt;
    int[] _sort = new int[64];

    readonly List<(KeySet set, int stmt)> _sent = new List<(KeySet, int)>();
    enum Round { A, B, C }
    Round _round;

    public ReadBatch(Loop loop) { _loop = loop; }

    public void Add(Job j)
    {
        if (_count == _jobs.Length) Array.Resize(ref _jobs, _count * 2);
        _jobs[_count++] = j;
    }

    static int Kind(Op op) => op switch
    {
        Op.ListGlobal => 0,
        Op.ListTag => 1,
        Op.ListAuthor => 2,
        Op.ListFavorited => 3,
        _ => 4,
    };

    public void Begin()
    {
        _needTags = false;
        _tagsJsonBuilt = false;
        for (int i = 0; i < _count; i++)
        {
            Job j = _jobs[i];
            switch (j.Op)
            {
                case Op.GetUser: j.I1 = _usersById.Add(j.Viewer); break;
                case Op.Login: j.I1 = _usersByEmail.Add(j.Key, j.KeyLen, 0, 0); break;
                case Op.Profile: j.I1 = _profiles.Add(j.Key, j.KeyLen, 0, 0); break;
                case Op.Article:
                case Op.Comments: j.I1 = _slugs.Add(j.Key, j.KeyLen, 0, 0); break;
                case Op.Tags: _needTags = true; break;
                case Op.ArticleById: break;
                default:
                {
                    int k = Kind(j.Op);
                    long lo = (long)j.Limit << 32 | (uint)j.Offset;
                    long a = j.Op == Op.Feed ? j.Viewer : 0;
                    j.I1 = _pages[k].Add(j.Key, j.KeyLen, a, lo);
                    j.I2 = _counts[k].Add(j.Key, j.KeyLen, a, 0);
                    break;
                }
            }
        }

        _sent.Clear();
        Send(_usersById, Sql.UsersById, Shape.Longs);
        Send(_usersByEmail, Sql.UsersByEmail, Shape.Texts);
        Send(_profiles, Sql.Profiles, Shape.Texts);
        Send(_slugs, Sql.Slugs, Shape.Texts);
        Send(_pages[0], Sql.PageGlobal, Shape.Limits);
        Send(_pages[1], Sql.PageTag, Shape.TextLimits);
        Send(_pages[2], Sql.PageAuthor, Shape.TextLimits);
        Send(_pages[3], Sql.PageFavorited, Shape.TextLimits);
        Send(_pages[4], Sql.PageFeed, Shape.LongLimits);
        Send(_counts[0], Sql.CountGlobal, Shape.None);
        Send(_counts[1], Sql.CountTag, Shape.Texts);
        Send(_counts[2], Sql.CountAuthor, Shape.Texts);
        Send(_counts[3], Sql.CountFavorited, Shape.Texts);
        Send(_counts[4], Sql.CountFeed, Shape.Longs);

        if (_sent.Count > 0) Launch(Round.A);
        else AfterA();
    }

    enum Shape { None, Longs, Texts, Limits, TextLimits, LongLimits, Pairs }

    void Send(KeySet k, int stmt, Shape shape)
    {
        if (k.Count == 0) return;
        Pg p = Pg;
        _sent.Add((k, stmt));
        switch (shape)
        {
            case Shape.None: p.Bind(Sql.ReadName[stmt], 0); break;
            case Shape.Longs: p.Bind(Sql.ReadName[stmt], 1); p.PLongArrayA(k); break;
            case Shape.Texts: p.Bind(Sql.ReadName[stmt], 1); p.PTextArray(k); break;
            case Shape.Limits: p.Bind(Sql.ReadName[stmt], 2); p.PLimits(k); p.POffsets(k); break;
            case Shape.TextLimits: p.Bind(Sql.ReadName[stmt], 3); p.PTextArray(k); p.PLimits(k); p.POffsets(k); break;
            case Shape.LongLimits: p.Bind(Sql.ReadName[stmt], 3); p.PLongArrayA(k); p.PLimits(k); p.POffsets(k); break;
            case Shape.Pairs: p.Bind(Sql.ReadName[stmt], 2); p.PLongArrayA(k); p.PLongArrayB(k); break;
        }
        p.Exec();
    }

    void Launch(Round r)
    {
        _round = r;
        Pg.Sync();
        Pg.BeginRound((int)r);
        _loop.KickPg(Pg);
    }

    public void OnReply()
    {
        if (Pg.ErrorCount > 0)
        {
            Console.Error.WriteLine("read batch failed, SQLSTATE code " + Pg.FirstError);
            Pg.Finish();
            FailAll(500, "database error"u8);
            return;
        }
        Pg.Finish();
        byte* cur = Pg.In[(int)_round].P;
        for (int i = 0; i < _sent.Count; i++) cur = Fill(cur, _sent[i].set, _sent[i].stmt);
        switch (_round)
        {
            case Round.A: AfterA(); break;
            case Round.B: AfterB(); break;
            default: Finish(); break;
        }
    }

    /// Walks one statement's replies and records its rows. Comment rows of one key are contiguous.
    byte* Fill(byte* p, KeySet k, int stmt)
    {
        while (true)
        {
            byte t = *p;
            if (t == (byte)'C') return Wire.NextMsg(p);
            if (t == (byte)'D')
            {
                if (stmt == Sql.Tags) AddTag(p);
                else
                {
                    byte* cols = Wire.Cols(p);
                    long ord = Wire.NextI64(ref cols);
                    int i = (int)ord - 1;
                    if (k.RowN[i]++ == 0) k.Row[i] = (nint)p;
                }
            }
            p = Wire.NextMsg(p);
        }
    }

    void AddTag(byte* msg)
    {
        byte* c = Wire.Cols(msg);
        byte* idp = Wire.Next(ref c, out int _);
        int id = Wire.I32(idp);
        byte* name = Wire.Next(ref c, out int nl);
        if (_tagCount == _tagName.Length)
        {
            Array.Resize(ref _tagName, _tagCount * 2);
            Array.Resize(ref _tagLen, _tagCount * 2);
        }
        _tagName[_tagCount] = (nint)name;
        _tagLen[_tagCount] = nl;
        _tagRank.Set(id, _tagCount);
        _tagCount++;
    }

    // ---------- after each round ----------

    void AfterA()
    {
        // Compacted in place: answered jobs leave the batch.
        int kept = 0;
        for (int i = 0; i < _count; i++)
        {
            Job j = _jobs[i];
            if (ResolveA(j)) _jobs[kept++] = j;
        }
        _count = kept;

        _sent.Clear();
        Send(_info, Sql.Info, Shape.Longs);
        Send(_bodies, Sql.Bodies, Shape.Longs);
        Send(_favorited, Sql.Favorited, Shape.Pairs);
        Send(_comments, Sql.Comments, Shape.Longs);
        if (_needTags || _info.Count > 0)
        {
            _needTags = true;
            _tagCount = 0;
            _tagRank.Clear();
            _sent.Add((null, Sql.Tags));
            Pg.Bind(Sql.ReadName[Sql.Tags], 0);
            Pg.Exec();
        }
        if (_sent.Count > 0) Launch(Round.B);
        else AfterB();
    }

    /// True if the job needs another round.
    bool ResolveA(Job j)
    {
        switch (j.Op)
        {
            case Op.GetUser:
            {
                byte* row = (byte*)_usersById.Row[j.I1];
                if (row == null) { _loop.FailJob(j, 401, "user no longer exists"u8); return false; }
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                _loop.AnswerUserPresented(j, c, 200);
                return false;
            }
            case Op.Login:
                _loop.LoginRow(j, (byte*)_usersByEmail.Row[j.I1]);
                return false;
            case Op.Profile:
            {
                byte* row = (byte*)_profiles.Row[j.I1];
                if (row == null) { _loop.FailJob(j, 404, "no such user"u8); return false; }
                if (j.Viewer != 0)
                {
                    byte* c = Wire.Cols(row);
                    Wire.Skip(ref c);
                    _following.Add(j.Viewer, Wire.NextI64(ref c));
                }
                return true;
            }
            case Op.Article:
            case Op.Comments:
            {
                byte* row = (byte*)_slugs.Row[j.I1];
                if (row == null) { _loop.FailJob(j, 404, "no such article"u8); return false; }
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                j.ArticleId = Wire.NextI64(ref c);
                if (j.Op == Op.Comments) _comments.Add(j.ArticleId);
                else WantArticle(j);
                return true;
            }
            case Op.ArticleById:
                WantArticle(j);
                return true;
            case Op.Tags:
                return true;
            default:
            {
                int kind = Kind(j.Op);
                byte* row = (byte*)_pages[kind].Row[j.I1];
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                byte* arr = Wire.Next(ref c, out int _);
                j.PageIds = arr;
                int n = Wire.ArrayItems(arr, out byte* item);
                for (int i = 0; i < n; i++, item += 12)
                {
                    long id = Wire.I64(item + 4);
                    _info.Add(id);
                    if (j.Viewer != 0) _favorited.Add(j.Viewer, id);
                }
                return true;
            }
        }
    }

    void WantArticle(Job j)
    {
        _info.Add(j.ArticleId);
        _bodies.Add(j.ArticleId);
        if (j.Viewer != 0) _favorited.Add(j.Viewer, j.ArticleId);
    }

    /// The columns of an article row we need early: its author.
    static long AuthorOf(byte* infoRow)
    {
        byte* c = Wire.Cols(infoRow);
        Wire.Skip(ref c);
        return Wire.NextI64(ref c);
    }

    void AfterB()
    {
        int kept = 0;
        for (int i = 0; i < _count; i++)
        {
            Job j = _jobs[i];
            if (ResolveB(j)) _jobs[kept++] = j;
        }
        _count = kept;

        _sent.Clear();
        Send(_following, Sql.Following, Shape.Pairs);
        if (_sent.Count > 0) Launch(Round.C);
        else Finish();
    }

    bool ResolveB(Job j)
    {
        switch (j.Op)
        {
            case Op.Tags:
                AnswerTags(j);
                return false;
            case Op.Profile:
                return true;
            case Op.Article:
            case Op.ArticleById:
            {
                byte* info = _info.RowOf(j.ArticleId);
                if (info == null) { _loop.FailJob(j, 404, "no such article"u8); return false; }
                if (j.Viewer != 0) _following.Add(j.Viewer, AuthorOf(info));
                return true;
            }
            case Op.Comments:
            {
                if (j.Viewer == 0) return true;
                int ci = _comments.Find(j.ArticleId);
                byte* row = (byte*)_comments.Row[ci];
                int n = _comments.RowN[ci];
                for (int i = 0; i < n; i++, row = Wire.NextMsg(row))
                {
                    byte* c = Wire.Cols(row);
                    Wire.Skip(ref c); Wire.Skip(ref c); Wire.Skip(ref c); Wire.Skip(ref c); Wire.Skip(ref c);
                    _following.Add(j.Viewer, Wire.NextI64(ref c));
                }
                return true;
            }
            default:
            {
                if (j.Viewer == 0) return true;
                int n = Wire.ArrayItems(j.PageIds, out byte* item);
                for (int i = 0; i < n; i++, item += 12)
                {
                    byte* info = _info.RowOf(Wire.I64(item + 4));
                    if (info != null) _following.Add(j.Viewer, AuthorOf(info));
                }
                return true;
            }
        }
    }

    bool Follows(long viewer, long author) => viewer != 0 && _following.RowOf(viewer, author) != null;
    bool Favorited(long viewer, long article) => viewer != 0 && _favorited.RowOf(viewer, article) != null;

    void Finish()
    {
        for (int i = 0; i < _count; i++) Answer(_jobs[i]);
        Release();
    }

    void Answer(Job j)
    {
        if (!_loop.Alive(j)) { _loop.JobDone(j); return; }
        switch (j.Op)
        {
            case Op.Profile:
            {
                byte* row = (byte*)_profiles.Row[j.I1];
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                long id = Wire.NextI64(ref c);
                _loop.AnswerProfile(j, c, Follows(j.Viewer, id));
                return;
            }
            case Op.Article:
            case Op.ArticleById:
            {
                _loop.Begin(j.Conn, j.Op == Op.ArticleById ? j.Status : 200);
                Buf w = _loop.W;
                w.Put("{\"article\":"u8);
                ArticleObj(w, j.ArticleId, j.Viewer, true);
                w.Put((byte)'}');
                _loop.End(j.Conn);
                _loop.JobDone(j);
                return;
            }
            case Op.Comments:
                AnswerComments(j);
                return;
            default:
                AnswerList(j);
                return;
        }
    }

    void AnswerTags(Job j)
    {
        if (!_tagsJsonBuilt)
        {
            Buf t = _tagsJson;
            t.Len = 0;
            t.Put("{\"tags\":["u8);
            for (int i = 0; i < _tagCount; i++)
            {
                if (i > 0) t.Put((byte)',');
                t.JsonStr((byte*)_tagName[i], _tagLen[i]);
            }
            t.Put("]}"u8);
            _tagsJsonBuilt = true;
        }
        if (_loop.Alive(j))
        {
            _loop.Begin(j.Conn, 200);
            _loop.W.Put(_tagsJson.P, _tagsJson.Len);
            _loop.End(j.Conn);
        }
        _loop.JobDone(j);
    }

    void ArticleObj(Buf w, long id, long viewer, bool withBody)
    {
        byte* row = _info.RowOf(id);
        byte* c = Wire.Cols(row);
        Wire.Skip(ref c);
        long author = Wire.NextI64(ref c);
        byte* slug = Wire.Next(ref c, out int sl);
        byte* title = Wire.Next(ref c, out int tl);
        byte* desc = Wire.Next(ref c, out int dl);
        long created = Wire.NextI64(ref c);
        long updated = Wire.NextI64(ref c);
        byte* uname = Wire.Next(ref c, out int ul);
        byte* bio = Wire.Next(ref c, out int bl);
        byte* image = Wire.Next(ref c, out int il);
        byte* tags = Wire.Next(ref c, out int _);
        long favs = Wire.NextI64(ref c);

        w.Put("{\"slug\":"u8);
        w.JsonStr(slug, sl);
        w.Put(",\"title\":"u8);
        w.JsonStr(title, tl);
        w.Put(",\"description\":"u8);
        w.JsonStr(desc, dl);
        if (withBody)
        {
            byte* b = _bodies.RowOf(id);
            if (b != null)
            {
                byte* bc = Wire.Cols(b);
                Wire.Skip(ref bc);
                byte* body = Wire.Next(ref bc, out int bn);
                w.Put(",\"body\":"u8);
                w.JsonStr(body, bn);
            }
            else
            {
                w.Put(",\"body\":\"\""u8);
            }
        }
        w.Put(",\"tagList\":["u8);
        TagList(w, tags);
        w.Put("],\"createdAt\":"u8);
        w.Timestamp(created);
        w.Put(",\"updatedAt\":"u8);
        w.Timestamp(updated);
        w.Put(Favorited(viewer, id) ? ",\"favorited\":true,\"favoritesCount\":"u8 : ",\"favorited\":false,\"favoritesCount\":"u8);
        w.Dec(favs);
        w.Put(",\"author\":"u8);
        _loop.ProfileBody(uname, ul, bio, bl, image, il, Follows(viewer, author));
        w.Put((byte)'}');
    }

    /// Tag ids to names, in name order: the tags table was read sorted, so a tag's position there is its order.
    void TagList(Buf w, byte* arr)
    {
        int n = Wire.ArrayItems(arr, out byte* item);
        if (n == 0) return;
        if (n > _sort.Length) _sort = new int[n * 2];
        int have = 0;
        for (int i = 0; i < n; i++, item += 8)
        {
            int rank = _tagRank.Get(Wire.I32(item + 4));
            if (rank >= 0) _sort[have++] = rank;
        }
        if (have > 1) Array.Sort(_sort, 0, have);
        for (int i = 0; i < have; i++)
        {
            if (i > 0) w.Put((byte)',');
            w.JsonStr((byte*)_tagName[_sort[i]], _tagLen[_sort[i]]);
        }
    }

    void AnswerComments(Job j)
    {
        int ci = _comments.Find(j.ArticleId);
        byte* row = (byte*)_comments.Row[ci];
        int n = _comments.RowN[ci];
        _loop.Begin(j.Conn, 200);
        Buf w = _loop.W;
        w.Put("{\"comments\":["u8);
        for (int i = 0; i < n; i++, row = Wire.NextMsg(row))
        {
            if (i > 0) w.Put((byte)',');
            byte* c = Wire.Cols(row);
            Wire.Skip(ref c);
            long id = Wire.NextI64(ref c);
            byte* body = Wire.Next(ref c, out int bn);
            long created = Wire.NextI64(ref c);
            long updated = Wire.NextI64(ref c);
            long author = Wire.NextI64(ref c);
            byte* un = Wire.Next(ref c, out int ul);
            byte* bio = Wire.Next(ref c, out int bl);
            byte* image = Wire.Next(ref c, out int il);
            _loop.CommentObj(id, body, bn, created, updated, un, ul, bio, bl, image, il, Follows(j.Viewer, author));
        }
        w.Put("]}"u8);
        _loop.End(j.Conn);
        _loop.JobDone(j);
    }

    void AnswerList(Job j)
    {
        int kind = Kind(j.Op);
        _loop.Begin(j.Conn, 200);
        Buf w = _loop.W;
        w.Put("{\"articles\":["u8);
        int n = Wire.ArrayItems(j.PageIds, out byte* item);
        bool first = true;
        for (int i = 0; i < n; i++, item += 12)
        {
            long id = Wire.I64(item + 4);
            if (_info.RowOf(id) == null) continue;      // deleted between the rounds
            if (!first) w.Put((byte)',');
            first = false;
            ArticleObj(w, id, j.Viewer, false);
        }
        w.Put("],\"articlesCount\":"u8);
        byte* row = (byte*)_counts[kind].Row[j.I2];
        byte* c = Wire.Cols(row);
        Wire.Skip(ref c);
        w.Dec(Wire.NextI64(ref c));
        w.Put((byte)'}');
        _loop.End(j.Conn);
        _loop.JobDone(j);
    }

    // ---------- the end ----------

    void Release()
    {
        Array.Clear(_jobs, 0, _count);
        _count = 0;
        _usersById.Clear(); _usersByEmail.Clear(); _profiles.Clear(); _slugs.Clear();
        for (int i = 0; i < 5; i++) { _pages[i].Clear(); _counts[i].Clear(); }
        _info.Clear(); _bodies.Clear(); _favorited.Clear(); _comments.Clear(); _following.Clear();
        _tagRank.Clear();
        _tagCount = 0;
        Pg pg = Pg;
        for (int i = 0; i < 3; i++) pg.In[i].Shrink(16 << 20);
        _loop.BatchDone(this);
    }

    void FailAll(int status, ReadOnlySpan<byte> msg)
    {
        for (int i = 0; i < _count; i++) _loop.FailJob(_jobs[i], status, msg);
        Release();
    }

    public void Fail() => FailAll(503, "database connection lost"u8);
}

/// Small map from an integer to an integer.
sealed class LongIndex
{
    long[] _k = new long[1024];
    int[] _v = new int[1024];
    int _mask = 1023, _n;

    public void Clear()
    {
        if (_n == 0) return;
        Array.Clear(_v);
        _n = 0;
    }

    public void Set(long k, int v)
    {
        if ((_n + 1) * 2 > _mask) Grow();
        int s = (int)((ulong)k * 0x9E3779B97F4A7C15 >> 40) & _mask;
        while (_v[s] != 0 && _k[s] != k) s = (s + 1) & _mask;
        if (_v[s] == 0) _n++;
        _k[s] = k;
        _v[s] = v + 1;
    }

    public int Get(long k)
    {
        int s = (int)((ulong)k * 0x9E3779B97F4A7C15 >> 40) & _mask;
        while (_v[s] != 0)
        {
            if (_k[s] == k) return _v[s] - 1;
            s = (s + 1) & _mask;
        }
        return -1;
    }

    void Grow()
    {
        long[] ok = _k;
        int[] ov = _v;
        int size = (_mask + 1) * 2;
        _k = new long[size];
        _v = new int[size];
        _mask = size - 1;
        _n = 0;
        for (int i = 0; i < ok.Length; i++) if (ov[i] != 0) Set(ok[i], ov[i] - 1);
    }
}
