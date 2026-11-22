using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using PhoneBox.Agent;
using Xunit;

public sealed class ProcessMonitorTests
{
    private static ProcessMonitor NewMonitor() =>
        new(NullLogger<ProcessMonitor>.Instance, new SessionMonitor(NullLogger<SessionMonitor>.Instance));

    private static void SampleTwice(ProcessMonitor mon)
    {
        var sample = typeof(ProcessMonitor).GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic)!;
        sample.Invoke(mon, null);
        sample.Invoke(mon, null);
    }

    [Fact]
    public void KernelTaskAndWindowServerAreProtectedFromKill()
    {
        Assert.Contains("kernel_task", UnixProc.Protected);
        Assert.Contains("WindowServer", UnixProc.Protected);

        var mon = NewMonitor();
        var byName = mon.Kill("kernel_task");
        Assert.Equal(0, byName.Killed);
        Assert.Contains("protected", byName.Error, StringComparison.OrdinalIgnoreCase);

        byName = mon.Kill("WindowServer");
        Assert.Equal(0, byName.Killed);
        Assert.Contains("protected", byName.Error, StringComparison.OrdinalIgnoreCase);

        // Request spelling must not bypass Ordinal protected entries (e.g. "Finder").
        byName = mon.Kill("finder");
        Assert.Equal(0, byName.Killed);
        Assert.Contains("protected", byName.Error, StringComparison.OrdinalIgnoreCase);

        var byPid = mon.KillPid(0);
        Assert.Equal(0, byPid.Killed);
        Assert.Contains("protected", byPid.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MacOsKernelTaskAppearsInSnapshotWhenEnumerated()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var mon = NewMonitor();
        SampleTwice(mon);
        var groups = mon.Snapshot();
        var kernel = Assert.Single(groups, g => g.Name.Equals("kernel_task", StringComparison.Ordinal));
        Assert.True(kernel.Locked);
        Assert.True(kernel.MemMb > 0); // wired memory via vm_stat

        // GetProcessesByName("kernel_task") misses PID 0 when ProcessName is empty; Detail must still resolve it.
        var detail = mon.Detail("kernel_task");
        var instance = Assert.Single(detail.Instances, i => i.Pid == 0);
        Assert.True(instance.Locked);
        Assert.True(instance.MemMb > 0);
        Assert.True(detail.Locked);
    }

    [Fact]
    public void MacOsWindowServerHasPositiveMemoryWhenRunning()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var mon = NewMonitor();
        SampleTwice(mon);
        var windowServer = mon.Snapshot()
            .FirstOrDefault(g => g.Name.Equals("WindowServer", StringComparison.Ordinal));
        if (windowServer is null) return; // headless / no GUI session

        Assert.True(windowServer.MemMb > 0);
    }

    [Fact]
    public void MacOsKernelWiredBytes_ReadsVmStat()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Assert.True(UnixProc.KernelWiredBytes() > 0);
    }
}
