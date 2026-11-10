using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace QuickPanel.Services;

/// <summary>
/// v1.9：系统监控 —— CPU / 内存 / 网速。纯 Win32 API，零第三方依赖、零外联。
/// </summary>
public class SystemMonitor : IDisposable
{
    // ---- 内存 ----
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ---- CPU（GetSystemTimes 差分）----
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _cpuPrimed;

    // ---- 网速（NetworkInterface 计数器差分）----
    private long _prevBytes;
    private DateTime _prevNetTime = DateTime.MinValue;
    private string? _netIface;

    public double CpuPercent { get; private set; }
    public double MemoryPercent { get; private set; }
    public double NetDownKbps { get; private set; }

    public SystemMonitor()
    {
        try
        {
            _netIface = PickInterface();
        }
        catch { /* 无网卡时忽略 */ }
        Update();
    }

    public void Update()
    {
        UpdateCpu();
        UpdateMemory();
        UpdateNet();
    }

    private void UpdateCpu()
    {
        try
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;
            var i = idle.ToUInt64();
            var k = kernel.ToUInt64();
            var u = user.ToUInt64();

            if (!_cpuPrimed)
            {
                _prevIdle = i; _prevKernel = k; _prevUser = u;
                _cpuPrimed = true;
                return;
            }

            var idleDelta = i - _prevIdle;
            var totalDelta = (k - _prevKernel) + (u - _prevUser); // kernel 已含 idle
            _prevIdle = i; _prevKernel = k; _prevUser = u;

            if (totalDelta > 0)
                CpuPercent = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
        }
        catch { /* 忽略 */ }
    }

    private void UpdateMemory()
    {
        try
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref m))
                MemoryPercent = m.dwMemoryLoad;
        }
        catch { /* 忽略 */ }
    }

    private void UpdateNet()
    {
        try
        {
            if (_netIface is null) return;
            var stats = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name == _netIface);
            if (stats is null) return;

            var bytes = stats.GetIPv4Statistics().BytesReceived;
            var now = DateTime.UtcNow;
            if (_prevNetTime != DateTime.MinValue)
            {
                var sec = (now - _prevNetTime).TotalSeconds;
                if (sec > 0.05)
                {
                    var delta = bytes - _prevBytes;
                    if (delta >= 0)
                        NetDownKbps = delta / 1024.0 / sec; // KB/s
                }
            }
            _prevBytes = bytes;
            _prevNetTime = now;
        }
        catch { /* 忽略 */ }
    }

    private static string? PickInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .OrderByDescending(n =>
            {
                try { return n.GetIPv4Statistics().BytesReceived; }
                catch { return 0; }
            })
            .FirstOrDefault()?.Name;
    }

    /// <summary>格式化监控文本：CPU 12% · 内存 68% · ↓2.3 MB/s</summary>
    public string ToDisplayText()
    {
        var net = NetDownKbps >= 1024
            ? $"{NetDownKbps / 1024:0.0} MB/s"
            : $"{NetDownKbps:0} KB/s";
        return $"CPU {CpuPercent:0}% · 内存 {MemoryPercent:0}% · ↓{net}";
    }

    public void Dispose() { }
}
