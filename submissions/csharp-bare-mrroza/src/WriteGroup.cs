using System;

namespace Bare;

/// Group commit. All waiting writes go out as one pipeline ending in a single Sync, which Postgres runs as one
/// transaction with one WAL flush; every write is answered only after the ReadyForQuery that follows the commit.
/// If any statement fails, nothing was committed: the writes run again one at a time, so a bad one fails alone.
/// The Postgres thread sorts the outcomes, and the network threads write the responses from the rows.
sealed unsafe class WriteGroup : RenderSource
{
    static readonly int UniqueViolation = Pg.SqlState("23505");
    static readonly int ForeignKeyViolation = Pg.SqlState("23503");
    static readonly int Deadlock = Pg.SqlState("40P01");
    static readonly int SerializationFailure = Pg.SqlState("40001");

    Pg _pg;
    Job[] _jobs = new Job[128];
    int _count;
    bool _solo;
    int _soloAt;
    readonly Buf[] _in = { new Buf(1 << 18) };

    public WriteGroup(Db db) : base(db) { }

    public void Add(Job j)
    {
        if (_count == _jobs.Length) Array.Resize(ref _jobs, _count * 2);
        _jobs[_count++] = j;
    }

    public void Start(Pg pg)
    {
        _pg = pg;
        pg.In = _in;
        _solo = false;
        for (int i = 0; i < _count; i++) Bind(_jobs[i]);
        Go();
    }

    void Go()
    {
        _pg.Sync();
        _pg.BeginRound(0);
        Db.KickPg(_pg);
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
                p.Bind(Sql.WriteName[j.Op == Op.Follow ? Sql.Follow : Sql.Unfollow], 2);
                p.PLong(j.Viewer);
                p.PText(j.Key, j.KeyLen);
                break;
            case Op.Favorite:
            case Op.Unfavorite:
                p.Bind(Sql.WriteName[j.Op == Op.Favorite ? Sql.Favorite : Sql.Unfavorite], 3);
                p.PLong(j.Viewer);
                p.PLong(j.ArticleId);
                p.PText(j.Key, j.KeyLen);
                break;
            case Op.CreateComment:
                p.Bind(Sql.WriteName[Sql.CreateComment], 4);
                p.PLong(j.Viewer);
                p.PLong(j.ArticleId);
                p.PText(j.Key, j.KeyLen);
                p.PText(j.Slot(Job.Body), j.SlotLen[Job.Body]);
                break;
            case Op.DeleteComment:
                p.Bind(Sql.WriteName[Sql.DeleteComment], 4);
                p.PLong(j.CommentId);
                p.PLong(j.ArticleId);
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
                p.Bind(Sql.WriteName[Sql.UpdateArticle], 6);
                p.PLong(j.ArticleId);
                p.PText(j.Key, j.KeyLen);
                p.PLong(j.Viewer);
                p.PSlot(j.Slot(Job.Title), j.SlotLen[Job.Title]);
                p.PSlot(j.Slot(Job.Description), j.SlotLen[Job.Description]);
                p.PSlot(j.Slot(Job.Body), j.SlotLen[Job.Body]);
                break;
            case Op.DeleteArticle:
                p.Bind(Sql.WriteName[Sql.DeleteArticle], 3);
                p.PLong(j.ArticleId);
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
            byte* cur = _in[0].P;
            for (int i = 0; i < _count; i++) cur = Result(_jobs[i], cur, keepRow: false);
            Done();
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
            if (err == UniqueViolation) j.Err = Err.Taken;
            else if (err == ForeignKeyViolation) j.Err = Err.UserGone;
            else
            {
                Console.Error.WriteLine("write failed, SQLSTATE code " + err);
                j.Err = Err.Database;
            }
            Gather(j);
        }
        else
        {
            Result(j, _in[0].P, keepRow: true);
        }
        _soloAt++;
        if (_soloAt < _count)
        {
            Bind(_jobs[_soloAt]);
            Go();
            return;
        }
        Done();
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

    /// Decides what happens to the job: a read of the committed article, or an answer from this row. A write running
    /// alone keeps its row in its own memory, because the next write's reply goes in the same buffer.
    byte* Result(Job j, byte* cur, bool keepRow)
    {
        if (j.Op == Op.CreateArticle && j.TagCount > 0) cur = NextResult(cur, out _);
        cur = NextResult(cur, out byte* row);
        switch (j.Op)
        {
            case Op.Register:
                if (row == null) j.Err = Err.Taken;
                break;
            case Op.UpdateUser:
                if (row == null) j.Err = Err.UserGone;
                break;
            case Op.Follow:
            case Op.Unfollow:
                if (row == null) j.Err = Err.NoUser;
                break;
            case Op.Favorite:
            case Op.Unfavorite:
            {
                if (row == null) { j.Err = Err.NoArticle; break; }
                byte* c = Wire.Cols(row);
                ReadAfterCommit(j, Wire.NextI64(ref c), 200);
                return cur;
            }
            case Op.CreateComment:
                if (row == null) j.Err = Err.NoArticle;
                break;
            case Op.DeleteComment:
            {
                if (row == null) { j.Err = Err.NoComment; break; }
                byte* c = Wire.Cols(row);
                if (Wire.NextI64(ref c) != j.Viewer) j.Err = Err.NotYourComment;
                break;
            }
            case Op.CreateArticle:
            {
                byte* c = Wire.Cols(row);
                ReadAfterCommit(j, Wire.NextI64(ref c), 201);
                return cur;
            }
            case Op.UpdateArticle:
            case Op.DeleteArticle:
            {
                if (row == null) { j.Err = Err.NoArticle; break; }
                byte* c = Wire.Cols(row);
                long id = Wire.NextI64(ref c);
                if (Wire.NextI64(ref c) != j.Viewer) { j.Err = Err.NotYourArticle; break; }
                if (j.Op == Op.UpdateArticle)
                {
                    ReadAfterCommit(j, id, 200);
                    return cur;
                }
                break;
            }
        }
        if (j.Err == Err.None && row != null && keepRow)
        {
            int len = 1 + Wire.I32(row + 1);
            j.Reserve(len);
            Buffer.MemoryCopy(row, j.Mem + j.MemLen, len, len);
            row = j.Mem + j.MemLen;
            j.MemLen += len;
        }
        j.Row = row;
        Gather(j);
        return cur;
    }

    /// The response shows the committed state, so it comes from an ordinary read that starts after the commit.
    void ReadAfterCommit(Job j, long articleId, int status)
    {
        j.Op = Op.ArticleById;
        j.ArticleId = articleId;
        j.Status = status;
        j.Enqueued = Db.Now;
        Db.EnqueueRead(j);
    }

    void Done()
    {
        _pg.Group = null;
        _pg.UseOwnBuffers();
        Publish();
    }

    // ---------- on a network thread ----------

    public override void Answer(Job j, Loop io)
    {
        if (j.Err != Err.None) { io.FailWith(j); return; }
        byte* row = j.Row;
        switch (j.Op)
        {
            case Op.Register:
            {
                byte* c = Wire.Cols(row);
                long id = Wire.NextI64(ref c);
                io.AnswerUserMinted(j, id, c, 201);
                break;
            }
            case Op.UpdateUser:
            {
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                io.AnswerUserPresented(j, c, 200);
                break;
            }
            case Op.Follow:
            case Op.Unfollow:
            {
                byte* c = Wire.Cols(row);
                Wire.Skip(ref c);
                io.AnswerProfile(j, c, j.Op == Op.Follow);
                break;
            }
            case Op.CreateComment:
            {
                if (io.Alive(j))
                {
                    byte* c = Wire.Cols(row);
                    long id = Wire.NextI64(ref c);
                    byte* body = Wire.Next(ref c, out int bn);
                    long created = Wire.NextI64(ref c);
                    long updated = Wire.NextI64(ref c);
                    byte* un = Wire.Next(ref c, out int ul);
                    byte* bio = Wire.Next(ref c, out int bl);
                    byte* image = Wire.Next(ref c, out int il);
                    io.Begin(j.Conn, 201);
                    io.W.Put("{\"comment\":"u8);
                    io.CommentObj(id, body, bn, created, updated, un, ul, bio, bl, image, il, false);
                    io.W.Put((byte)'}');
                    io.End(j.Conn);
                }
                io.JobDone(j);
                break;
            }
            default:    // DeleteComment, DeleteArticle
                io.AnswerNoContent(j);
                break;
        }
    }

    /// Back on the Postgres thread, before reuse.
    public void Reset()
    {
        Array.Clear(_jobs, 0, _count);
        _count = 0;
        _in[0].Shrink(16 << 20);
    }

    /// The connection is gone. A commit may or may not have happened, so these writes must not run again.
    public void Fail()
    {
        for (int i = _solo ? _soloAt : 0; i < _count; i++)
        {
            _jobs[i].Err = Err.ConnLost;
            Gather(_jobs[i]);
        }
        Done();
    }
}
