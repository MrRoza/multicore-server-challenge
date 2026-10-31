using System;
using System.Runtime.CompilerServices;
using System.Text.Unicode;

namespace Bare;

/// HTTP/1.1 parsing and routing. A request is sliced in place in the receive buffer; a job copies only what it keeps.
sealed unsafe partial class Loop
{
    enum M : byte { Other, Get, Post, Put, Delete }

    /// Handles one request at p. Returns the bytes it used, or 0 if it isn't complete yet.
    int OneRequest(Conn c, byte* p, int n)
    {
        var buf = new ReadOnlySpan<byte>(p, n);
        int headEnd = buf.IndexOf("\r\n\r\n"u8);
        if (headEnd < 0)
        {
            if (n > MaxHeader) Fatal(c, 431, "request header too large"u8);
            return 0;
        }

        int lineEnd = buf.IndexOf((byte)'\r');
        var line = buf.Slice(0, lineEnd);
        int sp1 = line.IndexOf((byte)' ');
        int sp2 = sp1 < 0 ? -1 : line.LastIndexOf((byte)' ');
        if (sp1 <= 0 || sp2 <= sp1 + 0) { Fatal(c, 400, "bad request line"u8); return 0; }
        var method = line.Slice(0, sp1);
        var target = line.Slice(sp1 + 1, sp2 - sp1 - 1);
        var version = line.Slice(sp2 + 1);
        if (!version.StartsWith("HTTP/1."u8)) { Fatal(c, 400, "bad version"u8); return 0; }
        bool close = version.SequenceEqual("HTTP/1.0"u8);

        byte* auth = null;
        int authLen = -1;
        long contentLength = 0;

        // Headers: only three matter, so most lines are rejected on their first byte and length.
        int pos = lineEnd + 2;
        while (pos < headEnd + 2)
        {
            int e = buf.Slice(pos).IndexOf((byte)'\r');
            int len = e;
            byte first = (byte)(p[pos] | 0x20);
            if (first == 'a' && len > 14 && Name(p + pos, "authorization:"u8))
            {
                Trim(p + pos + 14, len - 14, out auth, out authLen);
            }
            else if (first == 'c')
            {
                if (len > 15 && Name(p + pos, "content-length:"u8))
                {
                    Trim(p + pos + 15, len - 15, out byte* v, out int vl);
                    if (!JsonIn.ParseDecimal(v, vl, out contentLength)) { Fatal(c, 400, "bad content length"u8); return 0; }
                }
                else if (len > 11 && Name(p + pos, "connection:"u8))
                {
                    Trim(p + pos + 11, len - 11, out byte* v, out int vl);
                    if (vl == 5 && Name(v, "close"u8)) close = true;
                    else if (vl == 10 && Name(v, "keep-alive"u8)) close = false;
                }
            }
            else if (first == 't' && len > 18 && Name(p + pos, "transfer-encoding:"u8))
            {
                Fatal(c, 501, "chunked requests are not supported"u8);
                return 0;
            }
            pos += len + 2;
        }

        if (contentLength > MaxBody) { Fatal(c, 413, "body too large"u8); return 0; }
        int total = headEnd + 4 + (int)contentLength;
        if (n < total) return 0;
        byte* body = p + headEnd + 4;

        M m = method.SequenceEqual("GET"u8) ? M.Get
            : method.SequenceEqual("POST"u8) ? M.Post
            : method.SequenceEqual("PUT"u8) ? M.Put
            : method.SequenceEqual("DELETE"u8) ? M.Delete : M.Other;
        if (m == M.Other) close = true;  // HEAD and friends: the error body wouldn't be framed for them
        c.CloseAfter = close;

        fixed (byte* t = target)
            Route(c, m, t, target.Length, auth, authLen, body, (int)contentLength);
        return total;
    }

    /// Case-insensitive prefix; the literal is lowercase.
    static bool Name(byte* p, ReadOnlySpan<byte> lower)
    {
        for (int i = 0; i < lower.Length; i++)
        {
            byte c = p[i];
            if (c >= 'A' && c <= 'Z') c |= 0x20;
            if (c != lower[i]) return false;
        }
        return true;
    }

    static void Trim(byte* p, int n, out byte* v, out int vl)
    {
        while (n > 0 && (*p == ' ' || *p == '\t')) { p++; n--; }
        while (n > 0 && (p[n - 1] == ' ' || p[n - 1] == '\t')) n--;
        v = p;
        vl = n;
    }

    void Fatal(Conn c, int status, ReadOnlySpan<byte> msg)
    {
        c.CloseAfter = true;
        Error(c, status, msg);
    }

    // ---------- routing ----------

    void Route(Conn c, M m, byte* t, int tn, byte* auth, int authLen, byte* body, int bodyLen)
    {
        var target = new ReadOnlySpan<byte>(t, tn);
        int q = target.IndexOf((byte)'?');
        var path = q < 0 ? target : target.Slice(0, q);
        byte* query = q < 0 ? null : t + q + 1;
        int queryLen = q < 0 ? 0 : tn - q - 1;

        if (!path.StartsWith("/api/"u8)) { NotFound(c); return; }
        var rest = path.Slice(5);
        int s1 = rest.IndexOf((byte)'/');
        var head = s1 < 0 ? rest : rest.Slice(0, s1);
        var tail = s1 < 0 ? default : rest.Slice(s1 + 1);
        bool hasTail = s1 >= 0;

        if (head.SequenceEqual("articles"u8)) { RouteArticles(c, m, tail, hasTail, query, queryLen, auth, authLen, body, bodyLen); return; }
        if (head.SequenceEqual("tags"u8) && !hasTail && m == M.Get)
        {
            NewJob(c, Op.Tags);
            EnqueueRead(c.Cur);
            return;
        }
        if (head.SequenceEqual("profiles"u8) && hasTail) { RouteProfiles(c, m, tail, auth, authLen); return; }
        if (head.SequenceEqual("user"u8) && !hasTail)
        {
            if (m == M.Get) { GetUser(c, auth, authLen); return; }
            if (m == M.Put) { PutUser(c, auth, authLen, body, bodyLen); return; }
        }
        if (head.SequenceEqual("users"u8) && m == M.Post)
        {
            if (!hasTail) { Register(c, body, bodyLen); return; }
            if (tail.SequenceEqual("login"u8)) { Login(c, body, bodyLen); return; }
        }
        NotFound(c);
    }

    void NotFound(Conn c) => Error(c, 404, "not found"u8);

    /// 0 anonymous, above 0 a user; -1 means a 401 went out. `required` makes a missing header a 401 as well.
    long Viewer(Conn c, byte* auth, int authLen, bool required, out int tokenAt)
    {
        tokenAt = 0;
        if (authLen < 0)
        {
            if (!required) return 0;
            Error(c, 401, "missing token"u8);
            return -1;
        }
        const int Prefix = 6; // "Token "
        if (authLen <= Prefix || !new ReadOnlySpan<byte>(auth, Prefix).SequenceEqual("Token "u8) ||
            !Jwt.Verify(auth + Prefix, authLen - Prefix, _nowSec, out long sub))
        {
            Error(c, 401, "invalid token"u8);
            return -1;
        }
        tokenAt = Prefix;
        return sub;
    }

    /// Percent-decodes a path or query value into the job's memory. False if the result isn't text Postgres can hold,
    /// which also means no row can match it.
    bool CopyKey(Job j, ReadOnlySpan<byte> raw, bool plusIsSpace)
    {
        j.Reserve(raw.Length);
        byte* o = j.Mem + j.MemLen;
        int w = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            byte b = raw[i];
            if (b == '%' && i + 2 < raw.Length && Hex(raw[i + 1]) >= 0 && Hex(raw[i + 2]) >= 0)
            {
                b = (byte)(Hex(raw[i + 1]) << 4 | Hex(raw[i + 2]));
                i += 2;
            }
            else if (b == '+' && plusIsSpace) b = (byte)' ';
            if (b == 0) return false;
            o[w++] = b;
        }
        j.KeyOff = j.MemLen;
        j.KeyLen = w;
        j.MemLen += w;
        return Utf8.IsValid(new ReadOnlySpan<byte>(o, w));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int Hex(byte c) => c >= '0' && c <= '9' ? c - '0'
        : c >= 'a' && c <= 'f' ? c - 'a' + 10
        : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

    // ---------- users ----------

    void Register(Conn c, byte* body, int n)
    {
        Job j = NewJob(c, Op.Register);
        j.Reserve(n * 2 + 128);
        if (!JsonIn.ParseBody(j, body, n, "user"u8, FieldSet.User) ||
            j.SlotLen[Job.Username] <= 0 || j.SlotLen[Job.Email] <= 0 ||
            j.SlotLen[Job.Password] <= 0 || j.SlotLen[Job.Password] > 72)
        {
            FailJob(j, 422, "username, email and password must be non-empty strings"u8);
            return;
        }
        j.HashOff = j.MemLen;
        j.MemLen += 60;
        if (!Bcrypt.Hash(j, j.Slot(Job.Password), j.SlotLen[Job.Password], j.Mem + j.HashOff))
            FailJob(j, 503, "too many password checks waiting"u8);
    }

    void Login(Conn c, byte* body, int n)
    {
        Job j = NewJob(c, Op.Login);
        j.Reserve(n * 2 + 128);
        if (!JsonIn.ParseBody(j, body, n, "user"u8, FieldSet.User) ||
            j.SlotLen[Job.Email] <= 0 || j.SlotLen[Job.Password] <= 0)
        {
            FailJob(j, 422, "email and password must be non-empty strings"u8);
            return;
        }
        j.KeyOff = j.SlotOff[Job.Email];
        j.KeyLen = j.SlotLen[Job.Email];
        EnqueueRead(j);
    }

    void GetUser(Conn c, byte* auth, int authLen)
    {
        long v = Viewer(c, auth, authLen, true, out int at);
        if (v < 0) return;
        Job j = NewJob(c, Op.GetUser);
        j.Viewer = v;
        KeepToken(j, auth + at, authLen - at);
        EnqueueRead(j);
    }

    static void KeepToken(Job j, byte* t, int n)
    {
        j.Reserve(n);
        Buffer.MemoryCopy(t, j.Mem + j.MemLen, n, n);
        j.TokenOff = j.MemLen;
        j.TokenLen = n;
        j.MemLen += n;
    }

    void PutUser(Conn c, byte* auth, int authLen, byte* body, int n)
    {
        long v = Viewer(c, auth, authLen, true, out int at);
        if (v < 0) return;
        Job j = NewJob(c, Op.UpdateUser);
        j.Viewer = v;
        KeepToken(j, auth + at, authLen - at);
        j.Reserve(n * 2 + 128);
        if (!JsonIn.ParseBody(j, body, n, "user"u8, FieldSet.User) ||
            j.SlotLen[Job.Email] == 0 || j.SlotLen[Job.Username] == 0 ||
            j.SlotLen[Job.Password] == 0 || j.SlotLen[Job.Password] > 72)
        {
            FailJob(j, 422, "invalid user fields"u8);
            return;
        }
        if (j.SlotLen[Job.Password] > 0)
        {
            j.HashOff = j.MemLen;
            j.MemLen += 60;
            if (!Bcrypt.Hash(j, j.Slot(Job.Password), j.SlotLen[Job.Password], j.Mem + j.HashOff))
                FailJob(j, 503, "too many password checks waiting"u8);
            return;
        }
        j.HashOff = -1;
        EnqueueWrite(j);
    }

    /// Back from bcrypt: a hash goes on to its write, a verified login gets its answer.
    void AfterBcrypt(Job j)
    {
        if (j.Op == Op.Login)
        {
            FinishLogin(j);
            return;
        }
        EnqueueWrite(j);
    }

    // ---------- profiles ----------

    void RouteProfiles(Conn c, M m, ReadOnlySpan<byte> tail, byte* auth, int authLen)
    {
        int s = tail.IndexOf((byte)'/');
        var name = s < 0 ? tail : tail.Slice(0, s);
        if (name.IsEmpty) { NotFound(c); return; }
        if (s < 0 && m == M.Get)
        {
            long v = Viewer(c, auth, authLen, false, out _);
            if (v < 0) return;
            Job j = NewJob(c, Op.Profile);
            j.Viewer = v;
            if (!CopyKey(j, name, false)) { FailJob(j, 404, "no such user"u8); return; }
            EnqueueRead(j);
            return;
        }
        if (s >= 0 && tail.Slice(s + 1).SequenceEqual("follow"u8) && (m == M.Post || m == M.Delete))
        {
            long v = Viewer(c, auth, authLen, true, out _);
            if (v < 0) return;
            Job j = NewJob(c, m == M.Post ? Op.Follow : Op.Unfollow);
            j.Viewer = v;
            if (!CopyKey(j, name, false)) { FailJob(j, 404, "no such user"u8); return; }
            EnqueueWrite(j);
            return;
        }
        NotFound(c);
    }

    // ---------- articles ----------

    void RouteArticles(Conn c, M m, ReadOnlySpan<byte> tail, bool hasTail, byte* query, int queryLen,
        byte* auth, int authLen, byte* body, int bodyLen)
    {
        if (!hasTail)
        {
            if (m == M.Get) ListArticles(c, query, queryLen, auth, authLen, false);
            else if (m == M.Post) CreateArticle(c, auth, authLen, body, bodyLen);
            else NotFound(c);
            return;
        }
        int s = tail.IndexOf((byte)'/');
        var slug = s < 0 ? tail : tail.Slice(0, s);
        if (slug.IsEmpty) { NotFound(c); return; }
        if (s < 0)
        {
            if (m == M.Get)
            {
                if (slug.SequenceEqual("feed"u8)) { ListArticles(c, query, queryLen, auth, authLen, true); return; }
                long v = Viewer(c, auth, authLen, false, out _);
                if (v < 0) return;
                SlugJob(c, Op.Article, v, slug, read: true);
            }
            else if (m == M.Put) UpdateArticle(c, slug, auth, authLen, body, bodyLen);
            else if (m == M.Delete)
            {
                long v = Viewer(c, auth, authLen, true, out _);
                if (v < 0) return;
                SlugJob(c, Op.DeleteArticle, v, slug, read: false);
            }
            else NotFound(c);
            return;
        }

        var sub = tail.Slice(s + 1);
        if (sub.SequenceEqual("favorite"u8) && (m == M.Post || m == M.Delete))
        {
            long v = Viewer(c, auth, authLen, true, out _);
            if (v < 0) return;
            SlugJob(c, m == M.Post ? Op.Favorite : Op.Unfavorite, v, slug, read: false);
            return;
        }
        if (sub.SequenceEqual("comments"u8))
        {
            if (m == M.Get)
            {
                long v = Viewer(c, auth, authLen, false, out _);
                if (v < 0) return;
                SlugJob(c, Op.Comments, v, slug, read: true);
                return;
            }
            if (m == M.Post) { CreateComment(c, slug, auth, authLen, body, bodyLen); return; }
        }
        else if (sub.StartsWith("comments/"u8) && m == M.Delete)
        {
            long v = Viewer(c, auth, authLen, true, out _);
            if (v < 0) return;
            var id = sub.Slice(9);
            long cid = 0;
            fixed (byte* pid = id)
            {
                if (id.IsEmpty || !JsonIn.ParseDecimal(pid, id.Length, out cid)) { Error(c, 404, "no such comment"u8); return; }
            }
            Job j = NewJob(c, Op.DeleteComment);
            j.Viewer = v;
            j.CommentId = cid;
            if (!CopyKey(j, slug, false)) { FailJob(j, 404, "no such article"u8); return; }
            EnqueueWrite(j);
            return;
        }
        NotFound(c);
    }

    void SlugJob(Conn c, Op op, long viewer, ReadOnlySpan<byte> slug, bool read)
    {
        Job j = NewJob(c, op);
        j.Viewer = viewer;
        if (!CopyKey(j, slug, false)) { FailJob(j, 404, "no such article"u8); return; }
        if (read) EnqueueRead(j);
        else EnqueueWrite(j);
    }

    void CreateArticle(Conn c, byte* auth, int authLen, byte* body, int n)
    {
        long v = Viewer(c, auth, authLen, true, out _);
        if (v < 0) return;
        Job j = NewJob(c, Op.CreateArticle);
        j.Viewer = v;
        j.Reserve(n * 3 + 128);
        if (!JsonIn.ParseBody(j, body, n, "article"u8, FieldSet.ArticleTags) ||
            j.SlotLen[Job.Title] <= 0 || j.SlotLen[Job.Description] <= 0 || j.SlotLen[Job.Body] <= 0)
        {
            FailJob(j, 422, "title, description and body must be non-empty strings"u8);
            return;
        }
        EnqueueWrite(j);
    }

    void UpdateArticle(Conn c, ReadOnlySpan<byte> slug, byte* auth, int authLen, byte* body, int n)
    {
        long v = Viewer(c, auth, authLen, true, out _);
        if (v < 0) return;
        Job j = NewJob(c, Op.UpdateArticle);
        j.Viewer = v;
        j.Reserve(n * 2 + slug.Length + 128);
        bool keyOk = CopyKey(j, slug, false);
        if (!JsonIn.ParseBody(j, body, n, "article"u8, FieldSet.Article) ||
            j.SlotLen[Job.Title] == 0 || j.SlotLen[Job.Description] == 0 || j.SlotLen[Job.Body] == 0)
        {
            FailJob(j, 422, "fields must be non-empty strings"u8);
            return;
        }
        if (!keyOk) { FailJob(j, 404, "no such article"u8); return; }
        EnqueueWrite(j);
    }

    void CreateComment(Conn c, ReadOnlySpan<byte> slug, byte* auth, int authLen, byte* body, int n)
    {
        long v = Viewer(c, auth, authLen, true, out _);
        if (v < 0) return;
        Job j = NewJob(c, Op.CreateComment);
        j.Viewer = v;
        j.Reserve(n * 2 + slug.Length + 128);
        bool keyOk = CopyKey(j, slug, false);
        if (!JsonIn.ParseBody(j, body, n, "comment"u8, FieldSet.Comment) || j.SlotLen[Job.Body] <= 0)
        {
            FailJob(j, 422, "body must be a non-empty string"u8);
            return;
        }
        if (!keyOk) { FailJob(j, 404, "no such article"u8); return; }
        EnqueueWrite(j);
    }

    // ---------- lists ----------

    void ListArticles(Conn c, byte* q, int qn, byte* auth, int authLen, bool feed)
    {
        long v = Viewer(c, auth, authLen, feed, out _);
        if (v < 0) return;

        long limit = 20, offset = 0;
        ReadOnlySpan<byte> tag = default, author = default, favorited = default;
        var rest = new ReadOnlySpan<byte>(q, qn);
        bool seenLimit = false, seenOffset = false;
        byte* tmp = stackalloc byte[32];
        while (!rest.IsEmpty)
        {
            int amp = rest.IndexOf((byte)'&');
            var pair = amp < 0 ? rest : rest.Slice(0, amp);
            rest = amp < 0 ? default : rest.Slice(amp + 1);
            int eq = pair.IndexOf((byte)'=');
            var name = eq < 0 ? pair : pair.Slice(0, eq);
            var val = eq < 0 ? default : pair.Slice(eq + 1);
            if (name.SequenceEqual("limit"u8))
            {
                if (seenLimit) continue;
                seenLimit = true;
                if (!QueryInt(val, tmp, out limit) || limit < 1 || limit > 100) { Error(c, 422, "limit must be an integer from 1 to 100"u8); return; }
            }
            else if (name.SequenceEqual("offset"u8))
            {
                if (seenOffset) continue;
                seenOffset = true;
                if (!QueryInt(val, tmp, out offset)) { Error(c, 422, "offset must be a non-negative integer"u8); return; }
            }
            else if (name.SequenceEqual("tag"u8)) { if (tag.IsEmpty) tag = val; }
            else if (name.SequenceEqual("author"u8)) { if (author.IsEmpty) author = val; }
            else if (name.SequenceEqual("favorited"u8)) { if (favorited.IsEmpty) favorited = val; }
        }

        Op op;
        ReadOnlySpan<byte> key = default;
        if (feed) op = Op.Feed;
        else if (!tag.IsEmpty) { op = Op.ListTag; key = tag; }
        else if (!author.IsEmpty) { op = Op.ListAuthor; key = author; }
        else if (!favorited.IsEmpty) { op = Op.ListFavorited; key = favorited; }
        else op = Op.ListGlobal;

        Job j = NewJob(c, op);
        j.Viewer = v;
        j.Limit = (int)limit;
        j.Offset = offset > int.MaxValue ? int.MaxValue : (int)offset;   // past any table: an empty page either way
        if (!key.IsEmpty && !CopyKey(j, key, true))
        {
            // Text no row can hold: the empty list an unknown tag or user gets.
            if (Alive(j))
            {
                Begin(c, 200);
                _w.Put("{\"articles\":[],\"articlesCount\":0}"u8);
                End(c);
            }
            JobDone(j);
            return;
        }
        EnqueueRead(j);
    }

    /// Digits after percent-decoding; "+5", " 5" and overflow all fail.
    static bool QueryInt(ReadOnlySpan<byte> v, byte* tmp, out long r)
    {
        r = 0;
        if (v.IsEmpty || v.Length > 32) return false;
        int w = 0;
        for (int i = 0; i < v.Length; i++)
        {
            byte b = v[i];
            if (b == '%' && i + 2 < v.Length && Hex(v[i + 1]) >= 0 && Hex(v[i + 2]) >= 0)
            {
                b = (byte)(Hex(v[i + 1]) << 4 | Hex(v[i + 2]));
                i += 2;
            }
            tmp[w++] = b;
        }
        return JsonIn.ParseDecimal(tmp, w, out r);
    }
}
