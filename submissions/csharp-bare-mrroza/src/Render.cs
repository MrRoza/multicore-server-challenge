using System;

namespace Bare;

/// Responses: rows go straight from Postgres's bytes into the output buffer, with no objects between.
sealed unsafe partial class Loop
{
    public Buf W => _w;
    public long NowSec => _nowSec;

    public void Begin(Conn c, int status)
    {
        Buf w = _w;
        w.Len = 0;
        w.Put(status switch
        {
            200 => "HTTP/1.1 200 OK\r\n"u8,
            201 => "HTTP/1.1 201 Created\r\n"u8,
            400 => "HTTP/1.1 400 Bad Request\r\n"u8,
            401 => "HTTP/1.1 401 Unauthorized\r\n"u8,
            403 => "HTTP/1.1 403 Forbidden\r\n"u8,
            404 => "HTTP/1.1 404 Not Found\r\n"u8,
            413 => "HTTP/1.1 413 Content Too Large\r\n"u8,
            422 => "HTTP/1.1 422 Unprocessable Content\r\n"u8,
            431 => "HTTP/1.1 431 Request Header Fields Too Large\r\n"u8,
            501 => "HTTP/1.1 501 Not Implemented\r\n"u8,
            503 => "HTTP/1.1 503 Service Unavailable\r\n"u8,
            _ => "HTTP/1.1 500 Internal Server Error\r\n"u8,
        });
        if (c.CloseAfter) w.Put("Connection: close\r\n"u8);
        // The length is patched in at the end, right-aligned: spaces before a value are legal header whitespace.
        w.Put("Content-Type: application/json\r\nContent-Length:          \r\n\r\n"u8);
        _bodyStart = w.Len;
    }

    public void End(Conn c)
    {
        Buf w = _w;
        int n = w.Len - _bodyStart;
        byte* d = w.P + _bodyStart - 5;   // the last digit, before \r\n\r\n
        do
        {
            *d-- = (byte)('0' + n % 10);
            n /= 10;
        } while (n > 0);
        SendResponse(c);
    }

    void NoContent(Conn c)
    {
        Buf w = _w;
        w.Len = 0;
        w.Put("HTTP/1.1 204 No Content\r\n"u8);
        if (c.CloseAfter) w.Put("Connection: close\r\n"u8);
        w.Put("\r\n"u8);
        SendResponse(c);
    }

    /// An error with no job behind it.
    void Error(Conn c, int status, ReadOnlySpan<byte> msg)
    {
        Begin(c, status);
        _w.Put("{\"errors\":{\"body\":[\""u8);
        _w.Put(msg);
        _w.Put("\"]}}"u8);
        End(c);
    }

    /// An error ends the job. The message is a fixed string with nothing to escape.
    public void FailJob(Job j, int status, ReadOnlySpan<byte> msg)
    {
        if (Alive(j)) Error(j.Conn, status, msg);
        JobDone(j);
    }

    public void AnswerNoContent(Job j)
    {
        if (Alive(j)) NoContent(j.Conn);
        JobDone(j);
    }

    // ---------- users ----------

    void UserBody(byte* email, int el, byte* token, int tl, byte* name, int nl, byte* bio, int bl, byte* image, int il)
    {
        Buf w = _w;
        w.Put("{\"user\":{\"email\":"u8);
        w.JsonStr(email, el);
        w.Put(",\"token\":\""u8);
        w.Put(token, tl);       // made of base64url and dots if we issued or checked it
        w.Put("\",\"username\":"u8);
        w.JsonStr(name, nl);
        w.Put(",\"bio\":"u8);
        w.JsonStrOrNull(bio, bl);
        w.Put(",\"image\":"u8);
        w.JsonStrOrNull(image, il);
        w.Put("}}"u8);
    }

    /// `username, email, bio, image` from the cursor, with the job's presented token.
    public void AnswerUserPresented(Job j, byte* cols, int status)
    {
        if (Alive(j))
        {
            byte* name = Wire.Next(ref cols, out int nl);
            byte* email = Wire.Next(ref cols, out int el);
            byte* bio = Wire.Next(ref cols, out int bl);
            byte* image = Wire.Next(ref cols, out int il);
            Begin(j.Conn, status);
            UserBody(email, el, j.Token, j.TokenLen, name, nl, bio, bl, image, il);
            End(j.Conn);
        }
        JobDone(j);
    }

    /// `username, email, bio, image` from the cursor, with a fresh token.
    public void AnswerUserMinted(Job j, long id, byte* cols, int status)
    {
        if (Alive(j))
        {
            byte* tok = stackalloc byte[256];
            int tl = Jwt.Mint(id, _nowSec, tok);
            byte* name = Wire.Next(ref cols, out int nl);
            byte* email = Wire.Next(ref cols, out int el);
            byte* bio = Wire.Next(ref cols, out int bl);
            byte* image = Wire.Next(ref cols, out int il);
            Begin(j.Conn, status);
            UserBody(email, el, tok, tl, name, nl, bio, bl, image, il);
            End(j.Conn);
        }
        JobDone(j);
    }

    /// Phase A found the login's row, or didn't. The row moves into the job, because the batch's buffers don't outlive
    /// the batch and bcrypt takes longer than that.
    public void LoginRow(Job j, byte* row)
    {
        if (row == null)
        {
            FailJob(j, 401, "wrong email or password"u8);
            return;
        }
        if (!Alive(j))
        {
            JobDone(j);
            return;
        }
        int len = 1 + Wire.I32(row + 1);
        j.Reserve(len);
        j.RowOff = j.MemLen;
        Buffer.MemoryCopy(row, j.Mem + j.RowOff, len, len);
        j.MemLen += len;
        byte* c = Wire.Cols(j.Mem + j.RowOff);
        for (int i = 0; i < 6; i++) Wire.Skip(ref c);
        byte* hash = Wire.Next(ref c, out int hl);
        if (hl != 60)
        {
            FailJob(j, 401, "wrong email or password"u8);
            return;
        }
        if (!Bcrypt.Verify(j, j.Slot(Job.Password), j.SlotLen[Job.Password], hash))
            FailJob(j, 503, "too many password checks waiting"u8);
    }

    /// The row was copied when the login left for bcrypt (ord, id, username, email, bio, image, password_hash).
    void FinishLogin(Job j)
    {
        if (!j.PwOk)
        {
            FailJob(j, 401, "wrong email or password"u8);
            return;
        }
        byte* cols = Wire.Cols(j.Mem + j.RowOff);
        Wire.Skip(ref cols);
        long id = Wire.NextI64(ref cols);
        AnswerUserMinted(j, id, cols, 200);
    }

    // ---------- profiles ----------

    public void ProfileBody(byte* name, int nl, byte* bio, int bl, byte* image, int il, bool following)
    {
        Buf w = _w;
        w.Put("{\"username\":"u8);
        w.JsonStr(name, nl);
        w.Put(",\"bio\":"u8);
        w.JsonStrOrNull(bio, bl);
        w.Put(",\"image\":"u8);
        w.JsonStrOrNull(image, il);
        w.Put(following ? ",\"following\":true}"u8 : ",\"following\":false}"u8);
    }

    /// `username, bio, image` from the cursor.
    public void AnswerProfile(Job j, byte* cols, bool following)
    {
        if (Alive(j))
        {
            byte* name = Wire.Next(ref cols, out int nl);
            byte* bio = Wire.Next(ref cols, out int bl);
            byte* image = Wire.Next(ref cols, out int il);
            Begin(j.Conn, 200);
            _w.Put("{\"profile\":"u8);
            ProfileBody(name, nl, bio, bl, image, il, following);
            _w.Put((byte)'}');
            End(j.Conn);
        }
        JobDone(j);
    }

    // ---------- comments ----------

    /// `id, body, created_at, updated_at` come first in the cursor; the author columns are read by the caller's shape.
    public void CommentObj(long id, byte* body, int bodyLen, long created, long updated,
        byte* name, int nl, byte* bio, int bl, byte* image, int il, bool following)
    {
        Buf w = _w;
        w.Put("{\"id\":"u8);
        w.Dec(id);
        w.Put(",\"createdAt\":"u8);
        w.Timestamp(created);
        w.Put(",\"updatedAt\":"u8);
        w.Timestamp(updated);
        w.Put(",\"body\":"u8);
        w.JsonStr(body, bodyLen);
        w.Put(",\"author\":"u8);
        ProfileBody(name, nl, bio, bl, image, il, following);
        w.Put((byte)'}');
    }
}
