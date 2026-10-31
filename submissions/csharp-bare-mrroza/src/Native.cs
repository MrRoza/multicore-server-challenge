using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]
[module: SkipLocalsInit]

namespace Bare;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
struct EpollEvent { public uint Events; public ulong Data; }

[StructLayout(LayoutKind.Sequential)]
struct TimeSpec { public long Sec; public long Nsec; }

/// libc, called directly. Calls that never block on a non-blocking socket skip the GC transition.
static unsafe class Sys
{
    const string C = "libc";

    public const int EAGAIN = 11, EINTR = 4, EMFILE = 24, ENFILE = 23;
    public const uint EPOLLIN = 1, EPOLLOUT = 4, EPOLLERR = 8, EPOLLHUP = 16, EPOLLET = 1u << 31;
    public const int EPOLL_CTL_ADD = 1, EPOLL_CTL_DEL = 2;
    public const int AF_INET = 2, SOCK_STREAM = 1, SOCK_NONBLOCK = 0x800, SOCK_CLOEXEC = 0x80000;
    public const int SOL_SOCKET = 1, SO_REUSEADDR = 2, SO_REUSEPORT = 15, IPPROTO_TCP = 6, TCP_NODELAY = 1;
    public const int MSG_NOSIGNAL = 0x4000;

    [DllImport(C), SuppressGCTransition] public static extern nint recv(int fd, byte* buf, nint n, int flags);
    [DllImport(C), SuppressGCTransition] public static extern nint send(int fd, byte* buf, nint n, int flags);
    [DllImport(C), SuppressGCTransition] public static extern int* __errno_location();
    [DllImport(C), SuppressGCTransition] public static extern int clock_gettime(int clk, TimeSpec* ts);

    [DllImport(C)] public static extern int close(int fd);
    [DllImport(C)] public static extern int epoll_create1(int flags);
    [DllImport(C)] public static extern int epoll_ctl(int ep, int op, int fd, EpollEvent* ev);
    [DllImport(C)] public static extern int epoll_wait(int ep, EpollEvent* evs, int max, int timeout);
    [DllImport(C)] public static extern int socket(int domain, int type, int proto);
    [DllImport(C)] public static extern int bind(int fd, byte* addr, int len);
    [DllImport(C)] public static extern int listen(int fd, int backlog);
    [DllImport(C)] public static extern int connect(int fd, byte* addr, int len);
    [DllImport(C)] public static extern int setsockopt(int fd, int level, int name, int* val, int len);
    [DllImport(C)] public static extern int accept4(int fd, byte* addr, int* len, int flags);
    [DllImport(C)] public static extern int eventfd(uint init, int flags);
    [DllImport(C)] public static extern nint read(int fd, byte* buf, nint n);
    [DllImport(C)] public static extern nint write(int fd, byte* buf, nint n);
    [DllImport(C)] public static extern nint getrandom(byte* buf, nint n, uint flags);
    [DllImport(C)] public static extern int open(byte* path, int flags);

    public static int Errno => *__errno_location();

    public static long MonoMs()
    {
        TimeSpec t;
        clock_gettime(1, &t);
        return t.Sec * 1000 + t.Nsec / 1000000;
    }

    public static long UnixSec()
    {
        TimeSpec t;
        clock_gettime(0, &t);
        return t.Sec;
    }

    public static void Random(byte* p, int n)
    {
        while (n > 0)
        {
            nint r = getrandom(p, n, 0);
            if (r <= 0) throw new InvalidOperationException("getrandom failed");
            p += r;
            n -= (int)r;
        }
    }

    public static void Ipv4(byte* a, uint hostOrderIp, int port)
    {
        new Span<byte>(a, 16).Clear();
        a[0] = AF_INET;
        a[2] = (byte)(port >> 8);
        a[3] = (byte)port;
        a[4] = (byte)(hostOrderIp >> 24);
        a[5] = (byte)(hostOrderIp >> 16);
        a[6] = (byte)(hostOrderIp >> 8);
        a[7] = (byte)hostOrderIp;
    }

    public static uint ParseIp(string host)
    {
        if (host == "localhost") return 0x7F000001;
        var parts = host.Split('.');
        if (parts.Length != 4) throw new ArgumentException("host must be an IPv4 address or localhost: " + host);
        uint ip = 0;
        foreach (var s in parts) ip = ip << 8 | byte.Parse(s);
        return ip;
    }
}
