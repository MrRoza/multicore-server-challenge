using System;

namespace Bare;

/// Group commit. All waiting writes go out as one pipeline ending in a single Sync, which Postgres runs as one
/// transaction with one WAL flush; every write is answered only after the ReadyForQuery that follows the commit.
/// If any statement fails, nothing was committed: the writes run again one at a time, so a bad one fails alone.
sealed unsafe class WriteGroup
{
    static readonly int UniqueViolation = Pg.SqlState("23505");
    static readonly int ForeignKeyViolation = Pg.SqlState("23503");
    static readonly int Deadlock = Pg.SqlState("40P01");
    static readonly int SerializationFailure = Pg.SqlState("40001");

    readonly Loop _loop;
    readonly Pg _pg;
    Job[] _jobs = new Job[128];
    int _count;
    bool _solo;
    int _soloAt;

    public WriteGroup(Loop loop, Pg pg)
    {
        _loop = loop;
        _pg = pg;
    }

    public void Add(Job j)
    {
        if (_count == _jobs.Length) Array.Resize(ref _jobs, _count * 2);
        _jobs[_count++] = j;
    }

    public void Begin()
    {
        _solo = false;
        for (int i = 0; i < _count; i++) Bind(_jobs[i]);
        Go();
    }

    void Go()
    {
        _pg.Sync();
        _pg.BeginRound(0);
        _loop.KickPg(_pg);
    }

    void Bind(Job j)
    {
        Pg p = _pg;
        byte* m = j.Mem;
        switch (j.Op)
        {
            case Op.Register:
                p.Bind(Sql.WriteName[Sql.Register], 3);
                p.PText(j.Slot(Job.Username), j.SlotLen[Job.Username]);
                p.PText(j.Slot(Job.Email), j.SlotLen[Job.Email]);
                p.PText(m + j.HashOff, 60);
                break;
            case Op.UpdateUser:
                p.Bind(Sql.WriteName[Sql.UpdateUser], 6);
                p.PLong(j.Viewer);
                p.PSlot(j.Slot(Job.Email), j.SlotLen[Job.Email]);
                p.PSlot(j.Slot(Job.Username), j.SlotLen[Job.Username]);
                if (j.SlotLen[Job.Password] > 0) p.PText(m + j.HashOff, 60);
                else p.PNull();
                p.PSlot(j.Slot(Job.Bio), j.SlotLen[Job.Bio]);
                p.PSlot(j.Slot(Job.Image), j.SlotLen[Job.Image]);
                break;
            case Op.Follow:
            case Op.Unfollow:
            case Op.Favorite:
            case Op.Unfavorite:
            {
                int stmt = j.Op == Op.Follow ? Sql.Follow : j.Op == Op.Unfollow ? Sql.Unfollow
                    : j.Op == Op.Favorite ? Sql.Favorite : Sql.Unfavorite;
                p.Bind(Sql.WriteName[stmt], 2);
                p.PLong(j.Viewer);
                p.PText(j.Key, j.KeyLen);
                break;
            }
            case Op.CreateComment:
                p.Bind(Sql.WriteName[Sql.CreateComment], 3);
                p.PLong(j.Viewer);
                p.PText(j.Key, j.KeyLen);
                p.PText(j.Slot(Job.Body), j.SlotLen[Job.Body]);
                break;
            case Op.DeleteComment:
                p.Bind(Sql.WriteName[Sql.DeleteComment], 3);
                p.PLong(j.CommentId);
                p.PText(j.Key, j.KeyLen);
                p.PLong(j.Viewer);
                break;
            case Op.CreateArticle:
                if (j.TagCount > 0)
                {
                    p.Bind(Sql.WriteName[Sql.InsertTags], 1);
                    p.PTagArray(m + j.TagsOff, j.TagsLen, j.TagCount);
                    p.Exec();
                }
                p.Bind(Sql.WriteName[Sql.CreateArticle], 5);
                p.PLong(j.Viewer);
                p.PText(j.Slot(Job.Title), j.SlotLen[Job.Title]);
                p.PText(j.Slot(Job.Description), j.SlotLen[Job.Description]);
                p.PText(j.Slot(Job.Body), j.SlotLen[Job.Body]);
                p.PTagArray(m + j.TagsOff, j.TagsLen, j.TagCount);
                break;
            case Op.UpdateArticle:
                p.Bind(Sql.WriteName[Sql.UpdateArticle], 5);
                p.PText(j.Key, j.KeyLen);
                p.PLong(j.Viewer);
                p.PSlot(j.Slot(Job.Title), j.SlotLen[Job.Title]);
                p.PSlot(j.Slot(Job.Description), j.SlotLen[Job.Description]);
                p.PSlot(j.Slot(Job.Body), j.SlotLen[Job.Body]);
                break;
            case Op.DeleteArticle:
                p.Bind(Sql.WriteName[Sql.DeleteArticle], 2);
                p.PText(j.Key, j.KeyLen);
                p.PLong(j.Viewer);
                break;
        }
        p.Exec();
    }

    public void OnReply()
    {
        int err = _pg.FirstError;
        bool failed = _pg.ErrorCount > 0;
        _pg.Finish();

        if (!_solo)
        {
            if (failed)
            {
                // Nothing was committed. Run each write alone.
                _solo = true;
                _soloAt = 0;
                Bind(_jobs[0]);
                Go();
                return;
            }
            byte* cur = _pg.In[0].P;
            for (int i = 0; i < _count; i++) cur = Result(_jobs[i], cur);
            Release();
            return;
        }

        Job j = _jobs[_soloAt];
        if (failed)
        {
            if ((err == Deadlock || err == SerializationFailure) && ++j.Attempts < 4)
            {
                Bind(j);
                Go();
                return;
            }
            if (err == UniqueViolation) _loop.FailJob(j, 422, "username, email or slug already taken"u8);
            else if (err == ForeignKeyViolation) _loop.FailJob(j, 401, "user no longer exists"u8);
            else
            {
                Console.Error.WriteLine("write failed, SQLSTATE code " + err);
                _loop.FailJob(j, 500, "database error"u8);
            }
        }
        else
        {
            Result(j, _pg.In[0].P);
        }
        _soloAt++;
        if (_soloAt < _count)
        {
            Bind(_jobs[_soloAt]);
            Go();
            return;
        }
        Release();
    }

    /// One statement's rows: the first row (or null) and where the next statement's replies start.
    static byte* NextResult(byte* p, out byte* row)
    {
        row = null;
        while (true)
        {
            byte t = *p;
            if (t == (byte)'D' && row == null) row = p;
            byte* n = Wire.NextMsg(p);
            if (t == (byte)'C') return n;
            p = n;
        }
    }

    byte* Result(Job j, byte* cur)
    {
        if (j.Op == Op.CreateArticle && j.TagCount > 0) cur = NextResult(cur, out _);
        cur = NextResult(cur, out byte* row);
        switch (j.Op)
        {
            case Op.Register:
            {
                if (row == null) { _loop.FailJob(j, 422, "username or email already taken"u8); break; }
                byte* c = Wire.Cols(row);
                long id = Wire.NextI64(ref c);
                _loop.AnswerUserMinted(j, id, c, 201);
                break;
            }
            case Op.UpdateUser:
            {
                if (row == null) { _loop.FailJob(j, 401, "user no longer exists"u8); break; }
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                _loop.AnswerUserPresented(j, c, 200);
                break;
            }
            case Op.Follow:
            case Op.Unfollow:
            {
                if (row == null) { _loop.FailJob(j, 404, "no such user"u8); break; }
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                _loop.AnswerProfile(j, c, j.Op == Op.Follow);
                break;
            }
            case Op.Favorite:
            case Op.Unfavorite:
            {
                if (row == null) { _loop.FailJob(j, 404, "no such article"u8); break; }
                byte* c = Wire.Cols(row);
                ReadAfterCommit(j, Wire.NextI64(ref c), 200);
                break;
            }
            case Op.CreateComment:
            {
                if (row == null) { _loop.FailJob(j, 404, "no such article"u8); break; }
                if (_loop.Alive(j))
                {
                    byte* c = Wire.Cols(row);
                    long id = Wire.NextI64(ref c);
                    byte* body = Wire.Next(ref c, out int bn);
                    long created = Wire.NextI64(ref c);
                    long updated = Wire.NextI64(ref c);
                    byte* un = Wire.Next(ref c, out int ul);
                    byte* bio = Wire.Next(ref c, out int bl);
                    byte* image = Wire.Next(ref c, out int il);
                    _loop.Begin(j.Conn, 201);
                    _loop.W.Put("{\"comment\":"u8);
                    _loop.CommentObj(id, body, bn, created, updated, un, ul, bio, bl, image, il, false);
                    _loop.W.Put((byte)'}');
                    _loop.End(j.Conn);
                }
                _loop.JobDone(j);
                break;
            }
            case Op.DeleteComment:
            {
                if (row == null) { _loop.FailJob(j, 404, "no such comment"u8); break; }
                byte* c = Wire.Cols(row);
                if (Wire.NextI64(ref c) != j.Viewer) { _loop.FailJob(j, 403, "not your comment"u8); break; }
                _loop.AnswerNoContent(j);
                break;
            }
            case Op.CreateArticle:
            {
                byte* c = Wire.Cols(row);
                ReadAfterCommit(j, Wire.NextI64(ref c), 201);
                break;
            }
            case Op.UpdateArticle:
            case Op.DeleteArticle:
            {
                if (row == null) { _loop.FailJob(j, 404, "no such article"u8); break; }
                byte* c = Wire.Cols(row);
                long id = Wire.NextI64(ref c);
                if (Wire.NextI64(ref c) != j.Viewer) { _loop.FailJob(j, 403, "not your article"u8); break; }
                if (j.Op == Op.DeleteArticle) _loop.AnswerNoContent(j);
                else ReadAfterCommit(j, id, 200);
                break;
            }
        }
        return cur;
    }

    /// The response shows the committed state, so it comes from an ordinary read that starts after the commit.
    void ReadAfterCommit(Job j, long articleId, int status)
    {
        if (!_loop.Alive(j)) { _loop.JobDone(j); return; }
        j.Op = Op.ArticleById;
        j.ArticleId = articleId;
        j.Status = status;
        _loop.EnqueueRead(j, urgent: true);
    }

    void Release()
    {
        Array.Clear(_jobs, 0, _count);
        _count = 0;
        _pg.In[0].Shrink(16 << 20);
    }

    /// The connection is gone. A commit may or may not have happened, so these writes must not run again.
    public void Fail()
    {
        for (int i = _solo ? _soloAt : 0; i < _count; i++) _loop.FailJob(_jobs[i], 503, "database connection lost"u8);
        Release();
    }
}
