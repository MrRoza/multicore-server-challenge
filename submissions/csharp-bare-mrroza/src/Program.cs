using System;

namespace Bare;

static unsafe class Program
{
    static int Env(string name, int fallback)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(v) ? fallback : int.Parse(v);
    }

    static int Main()
    {
        string db = Environment.GetEnvironmentVariable("DATABASE_URL");
        string secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrEmpty(db) || string.IsNullOrEmpty(secret))
        {
            Console.Error.WriteLine("DATABASE_URL and JWT_SECRET are required");
            return 2;
        }

        Sha256.Init();
        Blowfish.Init();
        Jwt.Init(secret);

        var s = new Settings
        {
            Loops = Env("BARE_LOOPS", 2),
            ReadConns = Env("BARE_READ_CONNS", 4),
            WriteConns = Env("BARE_WRITE_CONNS", 1),
            BatchMax = Env("BARE_BATCH_MAX", 1024),
            GatherMs = Env("BARE_GATHER_MS", 5),
            WriteGroupMax = Env("BARE_WRITE_GROUP", 128),
        };
        if (s.Loops * (s.ReadConns + s.WriteConns) > 95)
        {
            Console.Error.WriteLine("more than 95 Postgres connections");
            return 2;
        }

        // First write opens the console streams, which needs descriptors; do it before clients can use them all.
        Console.Error.Flush();

        string host = Environment.GetEnvironmentVariable("HOST");
        int port = Env("PORT", 3000);
        uint ip = string.IsNullOrEmpty(host) ? 0 : Sys.ParseIp(host);

        // Idle threads cost nothing, and the kernel shares CPU per runnable thread: with too few, logins starve behind
        // Postgres's backends on a busy box.
        Bcrypt.Start(Env("BARE_BCRYPT_THREADS", 4), Env("BARE_BCRYPT_WAIT_MS", 100));
        Server.Run(s, PgConfig.Parse(db), ip, port);
        return 0;
    }
}
