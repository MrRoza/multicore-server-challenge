using System;
using System.Collections.Generic;

namespace Bare;

/// All the reads one Postgres connection is answering at once, in up to five round trips (feeds add two, see below):
/// A reads what requests name (users, articles by id, comments, pages of ids), B reads what each distinct article adds
/// (its row, tags and favorite count, the viewers' favorites), and C reads each distinct author and the viewers' follows.
/// Requests asking for the same thing share its rows, for this batch only: everything is dropped when the last response
/// has gone out. The rounds run on the Postgres thread, and the network threads write the responses from the same rows.
sealed unsafe class ReadBatch : RenderSource
{
    Pg _pg;
    Job[] _jobs = new Job[256];
    int _count;
    readonly Buf[] _in = { new Buf(1 << 18), new Buf(1 << 18), new Buf(1 << 18), new Buf(1 << 18), new Buf(1 << 18) };

    readonly KeySet _usersById = new KeySet(), _usersByEmail = new KeySet(), _profiles = new KeySet();
    readonly KeySet _full = new KeySet(), _slugOnly = new KeySet(), _comments = new KeySet();
    readonly KeySet[] _pages = { new KeySet(), new KeySet(), new KeySet(), new KeySet(), new KeySet() };
    readonly KeySet[] _counts = { new KeySet(), new KeySet(), new KeySet(), new KeySet() };
    readonly KeySet _rows = new KeySet(), _extra = new KeySet(), _favorited = new KeySet();
    readonly KeySet _authors = new KeySet(), _following = new KeySet();
    readonly LongIndex _tagRank = new LongIndex();
    nint[] _tagName = new nint[512];
    int[] _tagLen = new int[512];
    int _tagCount;
    bool _needTags, _tagsAsked;
    readonly Buf _tagsJson = new Buf(1 << 14);
    long[] _pairA = new long[256], _pairB = new long[256];

    // Feeds. _pages[4] holds each distinct (viewer, limit, offset); the arrays with p are per page, those with f per followee.
    readonly KeySet _feedViewers = new KeySet(), _followees = new KeySet(), _slowPages = new KeySet(), _slowCounts = new KeySet();
    const int MaxHubs = 64, BigChecks = 12, MaxHubDepth = 200;
    int[] _fRefs = new int[256], _fDepth = new int[256], _fHub = new int[256];
    long[] _hubKeys = new long[256], _hubIds = new long[MaxHubs];
    int[] _hubDepths = new int[MaxHubs], _hubBig = new int[MaxHubs];
    long[] _authorIds = new long[256];
    long[] _pFloor = new long[64], _pTotal = new long[64];
    int[] _pOff = new int[64], _pLen = new int[64], _pSlow = new int[64];
    bool[] _pBig = new bool[64];
    long[] _merge = new long[1024];
    readonly Buf _feedIds = new Buf(1 << 14);

    readonly List<(KeySet set, int stmt)> _sent = new List<(KeySet, int)>();
    enum Round { A, FeedHubs, FeedRest, B, C }
    Round _round;

    public ReadBatch(Db db) : base(db) { }

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

    public void Start(Pg pg)
    {
        _pg = pg;
        pg.In = _in;
        _needTags = false;
        _tagsAsked = false;
        for (int i = 0; i < _count; i++)
        {
            Job j = _jobs[i];
            switch (j.Op)
            {
                case Op.GetUser: j.I1 = _usersById.Add(j.Viewer); break;
                case Op.Login: j.I1 = _usersByEmail.Add(j.Key, j.KeyLen, 0, 0); break;
                case Op.Profile: j.I1 = _profiles.Add(j.Key, j.KeyLen, 0, 0); break;
                case Op.Article:
                case Op.ArticleById: j.I1 = _full.Add(j.ArticleId); break;
                case Op.Comments:
                    j.I1 = _slugOnly.Add(j.ArticleId);
                    j.I2 = _comments.Add(j.ArticleId);
                    break;
                case Op.Tags: _needTags = _tagsAsked = true; break;
                case Op.Feed:
                    j.I1 = _pages[4].Add(null, 0, j.Viewer, (long)j.Limit << 32 | (uint)j.Offset);
                    _feedViewers.Add(j.Viewer);
                    break;
                default:
                {
                    int k = Kind(j.Op);
                    j.I1 = _pages[k].Add(j.Key, j.KeyLen, 0, (long)j.Limit << 32 | (uint)j.Offset);
                    j.I2 = _counts[k].Add(j.Key, j.KeyLen, 0, 0);
                    break;
                }
            }
        }

        _sent.Clear();
        Send(_usersById, Sql.UsersById, Shape.Longs);
        Send(_usersByEmail, Sql.UsersByEmail, Shape.Texts);
        Send(_profiles, Sql.Profiles, Shape.Texts);
        Send(_full, Sql.ArticleFull, Shape.Longs);
        Send(_slugOnly, Sql.SlugOnly, Shape.Longs);
        Send(_comments, Sql.Comments, Shape.Longs);
        Send(_pages[0], Sql.PageGlobal, Shape.Limits);
        Send(_pages[1], Sql.PageTag, Shape.TextLimits);
        Send(_pages[2], Sql.PageAuthor, Shape.TextLimits);
        Send(_pages[3], Sql.PageFavorited, Shape.TextLimits);
        Send(_feedViewers, Sql.Followees, Shape.Longs);
        Send(_counts[0], Sql.CountGlobal, Shape.None);
        Send(_counts[1], Sql.CountTag, Shape.Texts);
        Send(_counts[2], Sql.CountAuthor, Shape.Texts);
        Send(_counts[3], Sql.CountFavorited, Shape.Texts);

        if (_sent.Count > 0) Launch(Round.A);
        else AfterPages();
    }

    enum Shape { None, Longs, Texts, Limits, TextLimits, LongLimits, Pairs }

    void Send(KeySet k, int stmt, Shape shape)
    {
        if (k.Count == 0) return;
        Pg p = _pg;
        _sent.Add((k, stmt));
        switch (shape)
        {
            case Shape.None: p.Bind(Sql.ReadName[stmt], 0); break;
            case Shape.Longs: p.Bind(Sql.ReadName[stmt], 1); p.PLongArrayA(k); break;
            case Shape.Texts: p.Bind(Sql.ReadName[stmt], 1); p.PTextArray(k); break;
            case Shape.Limits: p.Bind(Sql.ReadName[stmt], 2); p.PLimits(k); p.POffsets(k); break;
            case Shape.TextLimits: p.Bind(Sql.ReadName[stmt], 3); p.PTextArray(k); p.PLimits(k); p.POffsets(k); break;
            case Shape.LongLimits: p.Bind(Sql.ReadName[stmt], 3); p.PLongArrayA(k); p.PLimits(k); p.POffsets(k); break;
            case Shape.Pairs: p.Bind(Sql.ReadName[stmt], 4); PGrouped(k); break;
        }
        p.Exec();
    }

    /// (a, b) pairs as four parameters: the distinct a's, all b's grouped by a, and each a's 1-based slice of them.
    void PGrouped(KeySet k)
    {
        int n = k.Count;
        if (_pairA.Length < n)
        {
            _pairA = new long[n * 2];
            _pairB = new long[n * 2];
        }
        Array.Copy(k.A, _pairA, n);
        Array.Copy(k.B, _pairB, n);
        Array.Sort(_pairA, _pairB, 0, n);
        int groups = 1;
        for (int i = 1; i < n; i++) if (_pairA[i] != _pairA[i - 1]) groups++;
        Pg p = _pg;
        p.PLongs(_pairA, n, groups, distinct: true);
        p.PLongs(_pairB, n, n, distinct: false);
        p.PSliceBounds(_pairA, n, groups, ends: false);
        p.PSliceBounds(_pairA, n, groups, ends: true);
    }

    void Launch(Round r)
    {
        _round = r;
        _pg.Sync();
        _pg.BeginRound((int)r);
        Db.KickPg(_pg);
    }

    public void OnReply()
    {
        if (_pg.ErrorCount > 0)
        {
            Console.Error.WriteLine("read batch failed, SQLSTATE code " + _pg.FirstError);
            _pg.Finish();
            FailAll(Err.Database);
            return;
        }
        _pg.Finish();
        byte* cur = _in[(int)_round].P;
        for (int i = 0; i < _sent.Count; i++) cur = Fill(cur, _sent[i].set, _sent[i].stmt);
        switch (_round)
        {
            case Round.A:
                if (_feedViewers.Count > 0) PlanHubs();
                else AfterPages();
                break;
            case Round.FeedHubs: PlanRest(); break;
            case Round.FeedRest:
                BuildFeedPages();
                AfterPages();
                break;
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
                byte* cols = Wire.Cols(p);
                switch (stmt)
                {
                    case Sql.Tags:
                        AddTag(p);
                        break;
                    case Sql.Favorited:
                    case Sql.Following:
                    {
                        long a = Wire.NextI64(ref cols);
                        long b = Wire.NextI64(ref cols);
                        int i = k.Find(a, b);
                        if (i >= 0) k.Row[i] = (nint)p;
                        break;
                    }
                    case Sql.UsersById:
                    case Sql.ArticleFull:
                    case Sql.SlugOnly:
                    case Sql.ArticleRows:
                    case Sql.Authors:
                    case Sql.FeedHubs:
                    case Sql.ArticleTags:   // one key's rows are contiguous in these three: the scan runs in key order
                    case Sql.Followees:
                    case Sql.FeedOthers:
                    {
                        int i = k.Find(Wire.NextI64(ref cols));
                        if (k.RowN[i]++ == 0) k.Row[i] = (nint)p;
                        break;
                    }
                    case Sql.FavoriteCounts:
                        k.Row2[k.Find(Wire.NextI64(ref cols))] = (nint)p;
                        break;
                    default:
                    {
                        int i = (int)Wire.NextI64(ref cols) - 1;
                        if (k.RowN[i]++ == 0) k.Row[i] = (nint)p;
                        break;
                    }
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

    // A feed is planned here rather than in SQL, where looking through a viewer's followees for its shared ones costs far
    // more. Followees several viewers share are read once: the id of one's (limit + offset)-th newest article is a floor
    // for that viewer, because that followee alone fills the page with articles at least that new. All other followees
    // then come from one index scan down to the lowest floor, and each page is merged here.

    void PlanHubs()
    {
        KeySet pages = _pages[4];
        for (int p = 0; p < pages.Count; p++)
        {
            int depth = PageDepth(pages, p);
            byte* r = (byte*)FolloweesOf(pages.A[p], out int n);
            for (int i = 0; i < n; i++, r = Wire.NextMsg(r))
            {
                byte* c = Wire.Cols(r);
                Wire.Skip(ref c);
                int f = _followees.Add(Wire.NextI64(ref c));
                if (f >= _fRefs.Length)
                {
                    Array.Resize(ref _fRefs, f * 2);
                    Array.Resize(ref _fDepth, f * 2);
                    Array.Resize(ref _fHub, f * 2);
                    Array.Resize(ref _hubKeys, f * 2);
                    Array.Resize(ref _authorIds, f * 2);
                }
                if (_fRefs[f] == 0) _fDepth[f] = 0;
                _fRefs[f]++;
                if (depth > _fDepth[f]) _fDepth[f] = depth;
            }
        }

        int shared = 0;
        for (int f = 0; f < _followees.Count; f++)
        {
            _fHub[f] = 0;
            if (_fRefs[f] >= 2) _hubKeys[shared++] = (long)_fRefs[f] << 32 | (uint)f;
        }
        Array.Sort(_hubKeys, 0, shared);
        int hubs = Math.Min(shared, MaxHubs);
        for (int i = 0; i < hubs; i++)
        {
            int f = (int)(uint)_hubKeys[shared - 1 - i];
            _fHub[f] = i + 1;
            _hubIds[i] = _followees.A[f];
            _hubDepths[i] = Math.Min(_fDepth[f], MaxHubDepth);
            _hubBig[i] = i < BigChecks ? 1 : 0;
        }

        _sent.Clear();
        if (hubs > 0)
        {
            Pg p = _pg;
            _sent.Add((_followees, Sql.FeedHubs));
            p.Bind(Sql.ReadName[Sql.FeedHubs], 3);
            p.PLongs(_hubIds, hubs, hubs, distinct: false);
            p.PInts(_hubDepths, hubs);
            p.PInts(_hubBig, hubs);
            p.Exec();
            Launch(Round.FeedHubs);
        }
        else PlanRest();
    }

    static int PageDepth(KeySet pages, int p)
    {
        long depth = (pages.B[p] >> 32) + (uint)pages.B[p];
        return depth > int.MaxValue ? int.MaxValue : (int)depth;
    }

    nint FolloweesOf(long viewer, out int n)
    {
        int v = _feedViewers.Find(viewer);
        n = _feedViewers.RowN[v];
        return _feedViewers.Row[v];
    }

    void PlanRest()
    {
        KeySet pages = _pages[4];
        if (_pFloor.Length < pages.Count)
        {
            int size = pages.Count * 2;
            _pFloor = new long[size]; _pTotal = new long[size]; _pOff = new int[size]; _pLen = new int[size];
            _pSlow = new int[size]; _pBig = new bool[size];
        }
        long lowest = long.MaxValue;
        int others = 0, floors = 0;
        for (int p = 0; p < pages.Count; p++)
        {
            int depth = PageDepth(pages, p);
            byte* r = (byte*)FolloweesOf(pages.A[p], out int n);
            long floor = 0;
            bool big = false;
            for (int i = 0; i < n; i++, r = Wire.NextMsg(r))
            {
                byte* c = Wire.Cols(r);
                Wire.Skip(ref c);
                int f = _followees.Find(Wire.NextI64(ref c));
                if (_fHub[f] == 0) continue;
                byte* h = Wire.Cols((byte*)_followees.Row[f]);
                Wire.Skip(ref h);
                byte* arr = Wire.Next(ref h, out int _);
                byte* flag = Wire.Next(ref h, out int _);
                if (*flag != 0) big = true;
                int len = Wire.ArrayItems(arr, out byte* first);
                if (depth <= len)
                {
                    long id = Wire.I64(first + (depth - 1) * 12 + 4);
                    if (id > floor) floor = id;
                }
            }
            _pFloor[p] = floor;
            _pBig[p] = big;
            if (floor > 0)
            {
                if (floors == _merge.Length) Array.Resize(ref _merge, floors * 2);
                _merge[floors++] = floor;
            }
        }

        // The scan runs down to its lowest floor, so the weakest quarter of the floors go the slow way instead of making
        // everyone else's scan read far more rows.
        Array.Sort(_merge, 0, floors);
        long cutoff = floors > 0 ? _merge[floors / 4] : 0;
        for (int p = 0; p < pages.Count; p++)
        {
            byte* r = (byte*)FolloweesOf(pages.A[p], out int n);
            long floor = _pFloor[p];
            _pSlow[p] = -1;
            if (n == 0) continue;
            if (!_pBig[p]) _slowCounts.Add(pages.A[p]);
            if (floor < cutoff || floor == 0)
            {
                _pSlow[p] = _slowPages.Add(null, 0, pages.A[p], pages.B[p]);
                continue;
            }
            if (floor < lowest) lowest = floor;
            for (int i = 0; i < n; i++, r = Wire.NextMsg(r))
            {
                byte* c = Wire.Cols(r);
                Wire.Skip(ref c);
                long id = Wire.NextI64(ref c);
                int f = _followees.Find(id);
                if (_fHub[f] != 0 || _fRefs[f] < 0) continue;
                _fRefs[f] = -1;
                _authorIds[others++] = id;
            }
        }

        _sent.Clear();
        if (others > 0)
        {
            Pg p = _pg;
            _sent.Add((_followees, Sql.FeedOthers));
            p.Bind(Sql.ReadName[Sql.FeedOthers], 2);
            p.PLongs(_authorIds, others, others, distinct: false);
            p.PLong(lowest);
            p.Exec();
        }
        Send(_slowPages, Sql.PageFeed, Shape.LongLimits);
        Send(_slowCounts, Sql.CountFeed, Shape.Longs);
        if (_sent.Count > 0) Launch(Round.FeedRest);
        else
        {
            BuildFeedPages();
            AfterPages();
        }
    }

    void BuildFeedPages()
    {
        KeySet pages = _pages[4];
        _feedIds.Len = 0;
        for (int p = 0; p < pages.Count; p++)
        {
            int limit = (int)(pages.B[p] >> 32), offset = (int)pages.B[p];
            byte* r = (byte*)FolloweesOf(pages.A[p], out int n);
            _pOff[p] = _feedIds.Len / 8;
            _pLen[p] = 0;
            _pTotal[p] = 0;
            if (n == 0) continue;
            _pTotal[p] = 1000;
            if (!_pBig[p])
            {
                byte* c = Wire.Cols(_slowCounts.RowOf(pages.A[p]));
                Wire.Skip(ref c);
                _pTotal[p] = Wire.NextI64(ref c);
            }
            if (_pSlow[p] >= 0)
            {
                byte* c = Wire.Cols((byte*)_slowPages.Row[_pSlow[p]]);
                Wire.Skip(ref c);
                byte* arr = Wire.Next(ref c, out int _);
                int len = Wire.ArrayItems(arr, out byte* item);
                _feedIds.Ensure(len * 8);
                for (int i = 0; i < len; i++, item += 12)
                {
                    *(long*)(_feedIds.P + _feedIds.Len) = Wire.I64(item + 4);
                    _feedIds.Len += 8;
                }
                _pLen[p] = len;
                continue;
            }

            long floor = _pFloor[p];
            int have = 0;
            for (int i = 0; i < n; i++, r = Wire.NextMsg(r))
            {
                byte* c = Wire.Cols(r);
                Wire.Skip(ref c);
                int f = _followees.Find(Wire.NextI64(ref c));
                byte* row = (byte*)_followees.Row[f];
                if (row == null) continue;
                if (_fHub[f] != 0)
                {
                    byte* h = Wire.Cols(row);
                    Wire.Skip(ref h);
                    byte* arr = Wire.Next(ref h, out int _);
                    int len = Wire.ArrayItems(arr, out byte* item);
                    for (int k = 0; k < len; k++, item += 12)
                    {
                        long id = Wire.I64(item + 4);
                        if (id < floor) break;
                        if (have == _merge.Length) Array.Resize(ref _merge, have * 2);
                        _merge[have++] = id;
                    }
                }
                else
                {
                    int rows = _followees.RowN[f];
                    for (int k = 0; k < rows; k++, row = Wire.NextMsg(row))
                    {
                        byte* a = Wire.Cols(row);
                        Wire.Skip(ref a);
                        long id = Wire.NextI64(ref a);
                        if (id < floor) break;
                        if (have == _merge.Length) Array.Resize(ref _merge, have * 2);
                        _merge[have++] = id;
                    }
                }
            }
            Array.Sort(_merge, 0, have);
            int take = Math.Min(limit, Math.Max(0, have - offset));
            _feedIds.Ensure(take * 8);
            for (int i = 0; i < take; i++)
            {
                *(long*)(_feedIds.P + _feedIds.Len) = _merge[have - 1 - offset - i];
                _feedIds.Len += 8;
            }
            _pLen[p] = take;
        }
    }

    /// How many ids the job's page has, and the i-th: lists keep their array where Postgres put it, feeds were merged here.
    int PageCount(Job j)
    {
        if (j.Op == Op.Feed) return _pLen[j.I1];
        return Wire.ArrayItems(j.PageIds, out j.PageFirst);
    }

    long PageId(Job j, int i) => j.Op == Op.Feed
        ? ((long*)_feedIds.P)[_pOff[j.I1] + i]
        : Wire.I64(j.PageFirst + i * 12 + 4);

    void AfterPages()
    {
        for (int i = 0; i < _count; i++) ResolveA(_jobs[i]);

        _sent.Clear();
        Send(_rows, Sql.ArticleRows, Shape.Longs);
        Send(_extra, Sql.ArticleTags, Shape.Longs);
        Send(_extra, Sql.FavoriteCounts, Shape.Longs);
        Send(_favorited, Sql.Favorited, Shape.Pairs);
        if (_needTags || _extra.Count > 0)
        {
            _needTags = true;
            _tagCount = 0;
            _tagRank.Clear();
            _sent.Add((null, Sql.Tags));
            _pg.Bind(Sql.ReadName[Sql.Tags], 0);
            _pg.Exec();
        }
        if (_sent.Count > 0) Launch(Round.B);
        else AfterB();
    }

    /// The slug asked for is the row's.
    static bool SlugMatches(Job j, byte* slug, int slugLen) =>
        slugLen == j.KeyLen && new ReadOnlySpan<byte>(slug, slugLen).SequenceEqual(new ReadOnlySpan<byte>(j.Key, j.KeyLen));

    /// Records what the job needs from the next rounds, or why it fails.
    void ResolveA(Job j)
    {
        switch (j.Op)
        {
            case Op.GetUser:
                if (_usersById.Row[j.I1] == 0) j.Err = Err.UserGone;
                return;
            case Op.Login:
            case Op.Tags:
                return;
            case Op.Profile:
            {
                byte* row = (byte*)_profiles.Row[j.I1];
                if (row == null) { j.Err = Err.NoUser; return; }
                if (j.Viewer != 0)
                {
                    byte* c = Wire.Cols(row);
                    Wire.Skip(ref c);
                    _following.Add(j.Viewer, Wire.NextI64(ref c));
                }
                return;
            }
            case Op.Article:
            case Op.ArticleById:
            {
                byte* row = (byte*)_full.Row[j.I1];
                if (row != null && j.Op == Op.Article)
                {
                    byte* c = Wire.Cols(row);
                    Wire.Skip(ref c);
                    Wire.Skip(ref c);
                    byte* slug = Wire.Next(ref c, out int sl);
                    if (!SlugMatches(j, slug, sl)) row = null;
                }
                if (row == null) { j.Err = Err.NoArticle; return; }
                _extra.Add(j.ArticleId);
                if (j.Viewer != 0) _favorited.Add(j.Viewer, j.ArticleId);
                return;
            }
            case Op.Comments:
            {
                byte* row = (byte*)_slugOnly.Row[j.I1];
                if (row != null)
                {
                    byte* c = Wire.Cols(row);
                    Wire.Skip(ref c);
                    byte* slug = Wire.Next(ref c, out int sl);
                    if (!SlugMatches(j, slug, sl)) row = null;
                }
                if (row == null) { j.Err = Err.NoArticle; return; }
                byte* r = (byte*)_comments.Row[j.I2];
                int n = _comments.RowN[j.I2];
                for (int i = 0; i < n; i++, r = Wire.NextMsg(r))
                {
                    byte* c = Wire.Cols(r);
                    Wire.Skip(ref c); Wire.Skip(ref c); Wire.Skip(ref c); Wire.Skip(ref c); Wire.Skip(ref c);
                    long author = Wire.NextI64(ref c);
                    _authors.Add(author);
                    if (j.Viewer != 0) _following.Add(j.Viewer, author);
                }
                return;
            }
            default:
            {
                if (j.Op != Op.Feed)
                {
                    byte* c = Wire.Cols((byte*)_pages[Kind(j.Op)].Row[j.I1]);
                    Wire.Skip(ref c);
                    j.PageIds = Wire.Next(ref c, out int _);
                }
                int n = PageCount(j);
                for (int i = 0; i < n; i++)
                {
                    long id = PageId(j, i);
                    if (_full.RowOf(id) == null) _rows.Add(id);
                    _extra.Add(id);
                    if (j.Viewer != 0) _favorited.Add(j.Viewer, id);
                }
                return;
            }
        }
    }

    /// The article's row, from whichever read had it. Its columns start ord, author_id, slug, title, description,
    /// created_at, updated_at; the full read's go on with body.
    byte* ArticleRow(long id)
    {
        byte* r = _full.RowOf(id);
        return r != null ? r : _rows.RowOf(id);
    }

    static long AuthorOf(byte* articleRow)
    {
        byte* c = Wire.Cols(articleRow);
        Wire.Skip(ref c);
        return Wire.NextI64(ref c);
    }

    void AfterB()
    {
        for (int i = 0; i < _count; i++)
        {
            Job j = _jobs[i];
            if (j.Err == Err.None) ResolveB(j);
        }

        _sent.Clear();
        Send(_authors, Sql.Authors, Shape.Longs);
        Send(_following, Sql.Following, Shape.Pairs);
        if (_sent.Count > 0) Launch(Round.C);
        else Finish();
    }

    void ResolveB(Job j)
    {
        switch (j.Op)
        {
            case Op.GetUser:
            case Op.Login:
            case Op.Tags:
            case Op.Profile:
            case Op.Comments:
                return;
            case Op.Article:
            case Op.ArticleById:
                WantAuthor(j, _full.RowOf(j.ArticleId));
                return;
            default:
            {
                int n = PageCount(j);
                for (int i = 0; i < n; i++)
                {
                    byte* row = ArticleRow(PageId(j, i));
                    if (row != null) WantAuthor(j, row);
                }
                return;
            }
        }
    }

    void WantAuthor(Job j, byte* row)
    {
        long author = AuthorOf(row);
        _authors.Add(author);
        if (j.Viewer != 0 && j.Op != Op.Feed) _following.Add(j.Viewer, author);     // a feed holds only followed authors
    }

    void Finish()
    {
        if (_tagsAsked) BuildTagsJson();
        Answers();
    }

    void Answers()
    {
        _pg.Batch = null;
        _pg.UseOwnBuffers();
        for (int i = 0; i < _count; i++) Gather(_jobs[i]);
        Publish();
    }

    void BuildTagsJson()
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
    }

    // ---------- on a network thread: the rows are read-only here ----------

    bool Follows(long viewer, long author) => viewer != 0 && _following.RowOf(viewer, author) != null;
    bool Favorited(long viewer, long article) => viewer != 0 && _favorited.RowOf(viewer, article) != null;

    public override void Answer(Job j, Loop io)
    {
        if (j.Err != Err.None) { io.FailWith(j); return; }
        if (j.Op == Op.Login)
        {
            io.LoginRow(j, (byte*)_usersByEmail.Row[j.I1]);
            return;
        }
        if (!io.Alive(j)) { io.JobDone(j); return; }
        switch (j.Op)
        {
            case Op.GetUser:
            {
                byte* c = Wire.Cols((byte*)_usersById.Row[j.I1]);
                Wire.Skip(ref c);
                io.AnswerUserPresented(j, c, 200);
                return;
            }
            case Op.Tags:
                io.Begin(j.Conn, 200);
                io.W.Put(_tagsJson.P, _tagsJson.Len);
                io.End(j.Conn);
                io.JobDone(j);
                return;
            case Op.Profile:
            {
                byte* c = Wire.Cols((byte*)_profiles.Row[j.I1]);
                Wire.Skip(ref c);
                long id = Wire.NextI64(ref c);
                io.AnswerProfile(j, c, Follows(j.Viewer, id));
                return;
            }
            case Op.Article:
            case Op.ArticleById:
            {
                if (_authors.RowOf(AuthorOf(_full.RowOf(j.ArticleId))) == null)
                {
                    j.Err = Err.NoArticle;
                    io.FailWith(j);
                    return;
                }
                io.Begin(j.Conn, j.Op == Op.ArticleById ? j.Status : 200);
                Buf w = io.W;
                w.Put("{\"article\":"u8);
                ArticleObj(io, j.ArticleId, j, true);
                w.Put((byte)'}');
                io.End(j.Conn);
                io.JobDone(j);
                return;
            }
            case Op.Comments:
                AnswerComments(j, io);
                return;
            default:
                AnswerList(j, io);
                return;
        }
    }

    void ArticleObj(Loop io, long id, Job j, bool withBody)
    {
        Buf w = io.W;
        long viewer = j.Viewer;
        byte* c = Wire.Cols(ArticleRow(id));
        Wire.Skip(ref c);
        long author = Wire.NextI64(ref c);
        byte* slug = Wire.Next(ref c, out int sl);
        byte* title = Wire.Next(ref c, out int tl);
        byte* desc = Wire.Next(ref c, out int dl);
        long created = Wire.NextI64(ref c);
        long updated = Wire.NextI64(ref c);

        w.Put("{\"slug\":"u8);
        w.JsonStr(slug, sl);
        w.Put(",\"title\":"u8);
        w.JsonStr(title, tl);
        w.Put(",\"description\":"u8);
        w.JsonStr(desc, dl);
        if (withBody)
        {
            byte* body = Wire.Next(ref c, out int bn);
            w.Put(",\"body\":"u8);
            w.JsonStr(body, bn);
        }
        int xi = _extra.Find(id);
        long favs = 0;
        if (_extra.Row2[xi] != 0)
        {
            byte* x = Wire.Cols((byte*)_extra.Row2[xi]);
            Wire.Skip(ref x);
            favs = Wire.NextI64(ref x);
        }
        w.Put(",\"tagList\":["u8);
        TagList(w, (byte*)_extra.Row[xi], _extra.RowN[xi]);
        w.Put("],\"createdAt\":"u8);
        w.Timestamp(created);
        w.Put(",\"updatedAt\":"u8);
        w.Timestamp(updated);
        w.Put(Favorited(viewer, id) ? ",\"favorited\":true,\"favoritesCount\":"u8 : ",\"favorited\":false,\"favoritesCount\":"u8);
        w.Dec(favs);
        w.Put(",\"author\":"u8);
        byte* a = Wire.Cols(_authors.RowOf(author));
        Wire.Skip(ref a);
        byte* uname = Wire.Next(ref a, out int ul);
        byte* bio = Wire.Next(ref a, out int bl);
        byte* image = Wire.Next(ref a, out int il);
        io.ProfileBody(uname, ul, bio, bl, image, il, j.Op == Op.Feed || Follows(viewer, author));
        w.Put((byte)'}');
    }

    /// Tag ids to names, in name order: the tags table was read sorted, so a tag's position there is its order.
    void TagList(Buf w, byte* row, int n)
    {
        if (n == 0) return;
        Span<int> sort = n <= 64 ? stackalloc int[64] : new int[n];
        int have = 0;
        for (int i = 0; i < n; i++, row = Wire.NextMsg(row))
        {
            byte* c = Wire.Cols(row);
            Wire.Skip(ref c);
            int rank = _tagRank.Get(Wire.I32(c + 4));
            if (rank >= 0) sort[have++] = rank;
        }
        if (have > 1) sort.Slice(0, have).Sort();
        for (int i = 0; i < have; i++)
        {
            if (i > 0) w.Put((byte)',');
            w.JsonStr((byte*)_tagName[sort[i]], _tagLen[sort[i]]);
        }
    }

    void AnswerComments(Job j, Loop io)
    {
        byte* row = (byte*)_comments.Row[j.I2];
        int n = _comments.RowN[j.I2];
        io.Begin(j.Conn, 200);
        Buf w = io.W;
        w.Put("{\"comments\":["u8);
        bool first = true;
        for (int i = 0; i < n; i++, row = Wire.NextMsg(row))
        {
            byte* c = Wire.Cols(row);
            Wire.Skip(ref c);
            long id = Wire.NextI64(ref c);
            byte* body = Wire.Next(ref c, out int bn);
            long created = Wire.NextI64(ref c);
            long updated = Wire.NextI64(ref c);
            long author = Wire.NextI64(ref c);
            byte* who = _authors.RowOf(author);
            if (who == null) continue;      // deleted between the rounds
            if (!first) w.Put((byte)',');
            first = false;
            byte* a = Wire.Cols(who);
            Wire.Skip(ref a);
            byte* un = Wire.Next(ref a, out int ul);
            byte* bio = Wire.Next(ref a, out int bl);
            byte* image = Wire.Next(ref a, out int il);
            io.CommentObj(id, body, bn, created, updated, un, ul, bio, bl, image, il, Follows(j.Viewer, author));
        }
        w.Put("]}"u8);
        io.End(j.Conn);
        io.JobDone(j);
    }

    void AnswerList(Job j, Loop io)
    {
        io.Begin(j.Conn, 200);
        Buf w = io.W;
        w.Put("{\"articles\":["u8);
        int n = PageCount(j);
        bool first = true;
        for (int i = 0; i < n; i++)
        {
            long id = PageId(j, i);
            byte* art = ArticleRow(id);                 // deleted between the rounds: no row, or no author
            if (art == null || _authors.RowOf(AuthorOf(art)) == null) continue;
            if (!first) w.Put((byte)',');
            first = false;
            ArticleObj(io, id, j, false);
        }
        w.Put("],\"articlesCount\":"u8);
        long total = 0;
        if (j.Op == Op.Feed)
        {
            total = _pTotal[j.I1];
        }
        else
        {
            byte* row = (byte*)_counts[Kind(j.Op)].Row[j.I2];
            if (row != null)
            {
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                total = Wire.NextI64(ref c);
            }
        }
        w.Dec(total);
        w.Put((byte)'}');
        io.End(j.Conn);
        io.JobDone(j);
    }

    // ---------- the end ----------

    /// Back on the Postgres thread, before reuse.
    public void Reset()
    {
        Array.Clear(_jobs, 0, _count);
        _count = 0;
        _usersById.Clear(); _usersByEmail.Clear(); _profiles.Clear();
        _full.Clear(); _slugOnly.Clear(); _comments.Clear();
        for (int i = 0; i < 5; i++) _pages[i].Clear();
        for (int i = 0; i < 4; i++) _counts[i].Clear();
        _rows.Clear(); _extra.Clear(); _favorited.Clear(); _authors.Clear(); _following.Clear();
        if (_followees.Count > 0) Array.Clear(_fRefs, 0, _followees.Count);
        _feedViewers.Clear(); _followees.Clear(); _slowPages.Clear(); _slowCounts.Clear();
        _tagRank.Clear();
        _tagCount = 0;
        for (int i = 0; i < _in.Length; i++) _in[i].Shrink(16 << 20);
    }

    void FailAll(Err e)
    {
        for (int i = 0; i < _count; i++) _jobs[i].Err = e;
        Answers();
    }

    public void Fail() => FailAll(Err.ConnLost);
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
