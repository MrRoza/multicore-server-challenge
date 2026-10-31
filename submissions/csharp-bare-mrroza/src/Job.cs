using System.Runtime.InteropServices;

namespace Bare;

/// Why a request fails, decided on the Postgres thread and answered on its network thread.
enum Err : byte
{
    None, UserGone, NoUser, NoArticle, NoComment, NotYourComment, NotYourArticle, Taken, Database, ConnLost,
}

enum Op : byte
{
    GetUser, Login, Profile, Article, ArticleById, Comments,
    ListGlobal, ListTag, ListAuthor, ListFavorited, Feed, Tags,
    Register, UpdateUser, Follow, Unfollow, Favorite, Unfavorite,
    CreateComment, DeleteComment, CreateArticle, UpdateArticle, DeleteArticle,
}

/// One request while it waits on Postgres or bcrypt. Pooled per loop. The strings it needs are in its own memory,
/// because the receive buffer belongs to the next request by then.
sealed unsafe class Job
{
    // Slots of the parsed JSON body.
    public const int Email = 0, Username = 1, Password = 2, Bio = 3, Image = 4;
    public const int Title = 0, Description = 1, Body = 2;

    public Op Op;
    public Loop Loop;
    public Conn Conn;
    public uint ConnGen;
    public Job Next;
    public long Enqueued;

    public long Viewer;
    public int Limit, Offset;
    public int KeyOff, KeyLen;
    public int TokenOff, TokenLen;
    public int HashOff;
    public int RowOff;
    public long CommentId;
    public long ArticleId;
    public int Status;
    public int TagsOff, TagsLen, TagCount;
    public int Attempts;
    public bool PwOk;

    public int I1, I2;          // positions in the batch's key sets
    public byte* PageIds;       // list page: the id array in round A's buffer
    public byte* PageFirst;     // its first element
    public byte* Row;           // write: the row its answer comes from
    public Err Err;

    public byte* Mem;
    public int MemLen, MemCap;

    public readonly int[] SlotOff = new int[5];
    public readonly int[] SlotLen = new int[5];

    public Job()
    {
        MemCap = 1024;
        Mem = (byte*)NativeMemory.Alloc((nuint)MemCap);
    }

    public void Reset()
    {
        MemLen = 0;
        Next = null;
        Viewer = 0;
        KeyLen = 0;
        TokenLen = 0;
        TagCount = 0;
        TagsLen = 0;
        Attempts = 0;
        PwOk = false;
        Status = 200;
        PageIds = null;
        Row = null;
        Err = Err.None;
        ClearSlots();
    }

    public void ClearSlots()
    {
        for (int i = 0; i < 5; i++) SlotLen[i] = -1;
    }

    /// Room for n more bytes. Moves Mem, so raw pointers into it don't survive this call.
    public void Reserve(int n)
    {
        if (MemCap - MemLen >= n) return;
        int cap = System.Math.Max(MemCap * 2, MemLen + n);
        Mem = (byte*)NativeMemory.Realloc(Mem, (nuint)cap);
        MemCap = cap;
    }

    public void ShrinkAfterUse()
    {
        if (MemCap <= 1 << 16) return;
        NativeMemory.Free(Mem);
        MemCap = 1024;
        Mem = (byte*)NativeMemory.Alloc((nuint)MemCap);
    }

    public byte* Key => Mem + KeyOff;
    public byte* Token => Mem + TokenOff;
    public byte* Slot(int i) => Mem + SlotOff[i];
}
