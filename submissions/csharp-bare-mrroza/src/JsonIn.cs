using System;
using System.Buffers;
using System.Buffers.Text;
using System.Text.Unicode;

namespace Bare;

/// A strict JSON reader over bytes. Only the fields a route wants are decoded; everything else is checked and skipped.
unsafe ref struct JsonIn
{
    static readonly SearchValues<byte> Special = SearchValues.Create(
        "\"\\\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000a\u000b\u000c\u000d\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f"u8);

    byte* _p;
    readonly byte* _end;

    JsonIn(byte* p, int n)
    {
        _p = p;
        _end = p + n;
    }

    void Ws()
    {
        while (_p < _end && (*_p == ' ' || *_p == '\n' || *_p == '\r' || *_p == '\t')) _p++;
    }

    bool Eat(byte c)
    {
        Ws();
        if (_p < _end && *_p == c) { _p++; return true; }
        return false;
    }

    bool Peek(byte c)
    {
        Ws();
        return _p < _end && *_p == c;
    }

    bool AtEnd()
    {
        Ws();
        return _p == _end;
    }

    bool Lit(ReadOnlySpan<byte> s)
    {
        if (_end - _p < s.Length || !new ReadOnlySpan<byte>(_p, s.Length).SequenceEqual(s)) return false;
        _p += s.Length;
        return true;
    }

    bool Skip(int depth = 0)
    {
        if (depth > 64) return false;
        Ws();
        if (_p >= _end) return false;
        switch (*_p)
        {
            case (byte)'"': return SkipStr();
            case (byte)'{':
                _p++;
                if (Eat((byte)'}')) return true;
                while (true)
                {
                    Ws();
                    if (!SkipStr() || !Eat((byte)':') || !Skip(depth + 1)) return false;
                    if (Eat((byte)',')) continue;
                    return Eat((byte)'}');
                }
            case (byte)'[':
                _p++;
                if (Eat((byte)']')) return true;
                while (true)
                {
                    if (!Skip(depth + 1)) return false;
                    if (Eat((byte)',')) continue;
                    return Eat((byte)']');
                }
            case (byte)'t': return Lit("true"u8);
            case (byte)'f': return Lit("false"u8);
            case (byte)'n': return Lit("null"u8);
            default: return Num();
        }
    }

    bool Num()
    {
        byte* p = _p;
        if (p < _end && *p == '-') p++;
        if (p >= _end) return false;
        if (*p == '0') p++;
        else if (*p >= '1' && *p <= '9') { while (p < _end && *p >= '0' && *p <= '9') p++; }
        else return false;
        if (p < _end && *p == '.')
        {
            p++;
            byte* d = p;
            while (p < _end && *p >= '0' && *p <= '9') p++;
            if (p == d) return false;
        }
        if (p < _end && (*p == 'e' || *p == 'E'))
        {
            p++;
            if (p < _end && (*p == '+' || *p == '-')) p++;
            byte* d = p;
            while (p < _end && *p >= '0' && *p <= '9') p++;
            if (p == d) return false;
        }
        _p = p;
        return true;
    }

    bool SkipStr()
    {
        if (_p >= _end || *_p != '"') return false;
        return RawStr(out _, out _, out _);
    }

    /// The raw bytes between the quotes, checked for bad escapes and raw control characters.
    bool RawStr(out byte* start, out int len, out bool escaped)
    {
        start = _p + 1;
        len = 0;
        escaped = false;
        byte* p = _p + 1;
        while (true)
        {
            int i = new ReadOnlySpan<byte>(p, (int)(_end - p)).IndexOfAny(Special);
            if (i < 0) return false;
            p += i;
            if (*p == '"')
            {
                len = (int)(p - start);
                _p = p + 1;
                return true;
            }
            if (*p != '\\') return false;
            escaped = true;
            p++;
            if (p >= _end) return false;
            switch (*p)
            {
                case (byte)'"': case (byte)'\\': case (byte)'/': case (byte)'b':
                case (byte)'f': case (byte)'n': case (byte)'r': case (byte)'t':
                    p++;
                    break;
                case (byte)'u':
                    if (_end - p < 5 || Hex4(p + 1) < 0) return false;
                    p += 5;
                    break;
                default:
                    return false;
            }
        }
    }

    static int Hex4(byte* p)
    {
        int v = 0;
        for (int i = 0; i < 4; i++)
        {
            byte c = p[i];
            int d = c >= '0' && c <= '9' ? c - '0'
                : c >= 'a' && c <= 'f' ? c - 'a' + 10
                : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
            if (d < 0) return -1;
            v = v << 4 | d;
        }
        return v;
    }

    /// Unescapes raw into o (never longer than raw). Returns -1 for text Postgres can't store: invalid UTF-8 or NUL.
    static int Decode(byte* raw, int n, bool escaped, byte* o)
    {
        int w = 0;
        if (!escaped)
        {
            new ReadOnlySpan<byte>(raw, n).CopyTo(new Span<byte>(o, n));
            w = n;
        }
        else
        {
            for (int i = 0; i < n;)
            {
                byte c = raw[i++];
                if (c != '\\') { o[w++] = c; continue; }
                c = raw[i++];
                switch (c)
                {
                    case (byte)'b': o[w++] = 8; break;
                    case (byte)'f': o[w++] = 12; break;
                    case (byte)'n': o[w++] = 10; break;
                    case (byte)'r': o[w++] = 13; break;
                    case (byte)'t': o[w++] = 9; break;
                    case (byte)'u':
                    {
                        int cp = Hex4(raw + i);
                        i += 4;
                        if (cp == 0) return -1;
                        if (cp >= 0xD800 && cp < 0xDC00 && i + 6 <= n && raw[i] == '\\' && raw[i + 1] == 'u')
                        {
                            int lo = Hex4(raw + i + 2);
                            if (lo >= 0xDC00 && lo < 0xE000)
                            {
                                cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00);
                                i += 6;
                            }
                        }
                        if (cp >= 0xD800 && cp < 0xE000) cp = 0xFFFD;   // a lone surrogate
                        if (cp < 0x80) o[w++] = (byte)cp;
                        else if (cp < 0x800)
                        {
                            o[w++] = (byte)(0xC0 | cp >> 6);
                            o[w++] = (byte)(0x80 | cp & 63);
                        }
                        else if (cp < 0x10000)
                        {
                            o[w++] = (byte)(0xE0 | cp >> 12);
                            o[w++] = (byte)(0x80 | cp >> 6 & 63);
                            o[w++] = (byte)(0x80 | cp & 63);
                        }
                        else
                        {
                            o[w++] = (byte)(0xF0 | cp >> 18);
                            o[w++] = (byte)(0x80 | cp >> 12 & 63);
                            o[w++] = (byte)(0x80 | cp >> 6 & 63);
                            o[w++] = (byte)(0x80 | cp & 63);
                        }
                        break;
                    }
                    default: o[w++] = c; break;
                }
            }
        }
        return Utf8.IsValid(new ReadOnlySpan<byte>(o, w)) ? w : -1;
    }

    /// Reads an object key into tmp (32 bytes). Longer or undecodable keys read as length -1: known to nobody.
    bool Key(byte* tmp, out int len)
    {
        len = -1;
        Ws();
        if (_p >= _end || *_p != '"') return false;
        if (!RawStr(out byte* raw, out int n, out bool esc)) return false;
        if (n <= 32) len = Decode(raw, n, esc, tmp);
        return Eat((byte)':');
    }

    // ---------- request bodies ----------

    /// `{"<wrapper>": {...}}` into the job's slots. False if the body or its wrapper is malformed, or a field has the
    /// wrong type. Fields missing or null keep Len = -1.
    public static bool ParseBody(Job j, byte* body, int n, ReadOnlySpan<byte> wrapper, FieldSet set)
    {
        var r = new JsonIn(body, n);
        byte* key = stackalloc byte[32];
        if (!r.Eat((byte)'{')) return false;
        bool seen = false;
        if (!r.Eat((byte)'}'))
        {
            while (true)
            {
                if (!r.Key(key, out int kl)) return false;
                if (kl >= 0 && new ReadOnlySpan<byte>(key, kl).SequenceEqual(wrapper))
                {
                    j.ClearSlots();
                    if (!r.Fields(j, set)) return false;
                    seen = true;
                }
                else if (!r.Skip()) return false;
                if (r.Eat((byte)',')) continue;
                if (!r.Eat((byte)'}')) return false;
                break;
            }
        }
        return seen && r.AtEnd();
    }

    bool Fields(Job j, FieldSet set)
    {
        byte* key = stackalloc byte[32];
        if (!Eat((byte)'{')) return false;
        if (Eat((byte)'}')) return true;
        while (true)
        {
            if (!Key(key, out int kl)) return false;
            int f = kl < 0 ? -1 : Field(set, new ReadOnlySpan<byte>(key, kl));
            if (f < 0)
            {
                if (!Skip()) return false;
            }
            else if (f == TagField)
            {
                if (!Tags(j)) return false;
            }
            else
            {
                Ws();
                if (Peek((byte)'n'))
                {
                    if (!Lit("null"u8)) return false;
                    j.SlotLen[f] = -1;
                }
                else
                {
                    if (!Peek((byte)'"') || !RawStr(out byte* raw, out int n, out bool esc)) return false;
                    int off = j.MemLen;
                    int w = Decode(raw, n, esc, j.Mem + off);
                    if (w < 0) return false;
                    j.MemLen = off + w;
                    j.SlotOff[f] = off;
                    j.SlotLen[f] = w;
                }
            }
            if (Eat((byte)',')) continue;
            return Eat((byte)'}');
        }
    }

    /// tagList: stored in the job's memory in Postgres's array element format, (length, bytes)*.
    bool Tags(Job j)
    {
        Ws();
        j.TagCount = 0;
        j.TagsOff = j.MemLen;
        j.TagsLen = 0;
        if (Peek((byte)'n')) return Lit("null"u8);
        if (!Eat((byte)'[')) return false;
        if (Eat((byte)']')) return true;
        while (true)
        {
            Ws();
            if (!Peek((byte)'"') || !RawStr(out byte* raw, out int n, out bool esc)) return false;
            int at = j.MemLen;
            int w = Decode(raw, n, esc, j.Mem + at + 4);
            if (w <= 0) return false;
            j.Mem[at] = (byte)(w >> 24); j.Mem[at + 1] = (byte)(w >> 16); j.Mem[at + 2] = (byte)(w >> 8); j.Mem[at + 3] = (byte)w;
            j.MemLen = at + 4 + w;
            j.TagCount++;
            if (Eat((byte)',')) continue;
            j.TagsLen = j.MemLen - j.TagsOff;
            return Eat((byte)']');
        }
    }

    const int TagField = 100;

    static int Field(FieldSet set, ReadOnlySpan<byte> k)
    {
        switch (set)
        {
            case FieldSet.User:
                if (k.SequenceEqual("email"u8)) return Job.Email;
                if (k.SequenceEqual("username"u8)) return Job.Username;
                if (k.SequenceEqual("password"u8)) return Job.Password;
                if (k.SequenceEqual("bio"u8)) return Job.Bio;
                if (k.SequenceEqual("image"u8)) return Job.Image;
                return -1;
            case FieldSet.Article:
                if (k.SequenceEqual("title"u8)) return Job.Title;
                if (k.SequenceEqual("description"u8)) return Job.Description;
                if (k.SequenceEqual("body"u8)) return Job.Body;
                return -1;
            case FieldSet.ArticleTags:
                if (k.SequenceEqual("title"u8)) return Job.Title;
                if (k.SequenceEqual("description"u8)) return Job.Description;
                if (k.SequenceEqual("body"u8)) return Job.Body;
                if (k.SequenceEqual("tagList"u8)) return TagField;
                return -1;
            default:
                if (k.SequenceEqual("body"u8)) return Job.Body;
                return -1;
        }
    }

    // ---------- JWT ----------

    /// Does the object hold the string value for this key?
    public static bool ClaimStringIs(byte* p, int n, ReadOnlySpan<byte> name, ReadOnlySpan<byte> value)
    {
        var r = new JsonIn(p, n);
        byte* key = stackalloc byte[32];
        bool found = false;
        if (!r.Eat((byte)'{')) return false;
        if (r.Eat((byte)'}')) return false;
        while (true)
        {
            if (!r.Key(key, out int kl)) return false;
            if (kl >= 0 && new ReadOnlySpan<byte>(key, kl).SequenceEqual(name))
            {
                r.Ws();
                if (!r.Peek((byte)'"') || !r.RawStr(out byte* raw, out int vl, out bool esc) || esc) return false;
                found = new ReadOnlySpan<byte>(raw, vl).SequenceEqual(value);
            }
            else if (!r.Skip()) return false;
            if (r.Eat((byte)',')) continue;
            return r.Eat((byte)'}') && r.AtEnd() && found;
        }
    }

    /// sub must be a decimal string above 0, exp a number.
    public static bool ReadClaims(byte* p, int n, out long sub, out double exp)
    {
        sub = 0;
        exp = 0;
        bool hasSub = false, hasExp = false;
        var r = new JsonIn(p, n);
        byte* key = stackalloc byte[32];
        if (!r.Eat((byte)'{') || r.Eat((byte)'}')) return false;
        while (true)
        {
            if (!r.Key(key, out int kl)) return false;
            var k = kl >= 0 ? new ReadOnlySpan<byte>(key, kl) : default;
            if (k.SequenceEqual("sub"u8))
            {
                r.Ws();
                if (!r.Peek((byte)'"') || !r.RawStr(out byte* raw, out int vl, out bool esc) || esc) return false;
                if (!ParseDecimal(raw, vl, out sub) || sub <= 0) return false;
                hasSub = true;
            }
            else if (k.SequenceEqual("exp"u8))
            {
                r.Ws();
                byte* start = r._p;
                if (!r.Num()) return false;
                if (!Utf8Parser.TryParse(new ReadOnlySpan<byte>(start, (int)(r._p - start)), out exp, out int _)) return false;
                hasExp = true;
            }
            else if (!r.Skip()) return false;
            if (r.Eat((byte)',')) continue;
            return r.Eat((byte)'}') && r.AtEnd() && hasSub && hasExp;
        }
    }

    /// Digits only, no sign or spaces. False on overflow.
    public static bool ParseDecimal(byte* p, int n, out long v)
    {
        v = 0;
        if (n <= 0) return false;
        for (int i = 0; i < n; i++)
        {
            int d = p[i] - '0';
            if (d < 0 || d > 9) return false;
            if (v > (long.MaxValue - d) / 10) return false;
            v = v * 10 + d;
        }
        return true;
    }
}

enum FieldSet { User, Article, ArticleTags, Comment }
