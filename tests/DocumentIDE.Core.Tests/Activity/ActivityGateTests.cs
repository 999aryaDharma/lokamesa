using System;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Activity;
using Xunit;

namespace DocumentIDE.Core.Tests.Activity;

public class ActivityGateTests
{
    [Fact]
    public void Idle_gate_is_not_busy()
    {
        var gate = new ActivityGate();
        Assert.False(gate.IsBusy);
        Assert.Equal(0, gate.ActiveCount);
        Assert.Empty(gate.ActiveReasons);
    }

    [Fact]
    public void Enter_reports_reason_and_dispose_clears_it()
    {
        var gate = new ActivityGate();
        using (gate.Enter("batch-apply"))
        {
            Assert.True(gate.IsBusy);
            Assert.Contains("batch-apply", gate.ActiveReasons);
        }
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public void Nested_leases_keep_the_gate_busy_until_the_last_dispose()
    {
        var gate = new ActivityGate();
        var outer = gate.Enter("batch-apply");
        var inner = gate.Enter("word-open");
        inner.Dispose();
        Assert.True(gate.IsBusy);
        Assert.DoesNotContain("word-open", gate.ActiveReasons);
        outer.Dispose();
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public void Double_dispose_is_idempotent()
    {
        var gate = new ActivityGate();
        var lease = gate.Enter("undo");
        lease.Dispose();
        lease.Dispose();
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public async Task Concurrent_entries_are_counted_exactly()
    {
        var gate = new ActivityGate();
        var leases = Enumerable.Range(0, 32).Select(_ => gate.Enter("git")).ToArray();
        Assert.Equal(32, gate.ActiveCount);
        await Task.WhenAll(leases.Select(l => Task.Run(l.Dispose)));
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public void Observer_is_notified_on_entry()
    {
        var observer = new RecordingObserver();
        var gate = new ActivityGate(observer);
        using (gate.Enter("reconcile"))
        {
            Assert.Equal(new[] { "reconcile" }, observer.Reasons);
        }
    }

    private sealed class RecordingObserver : IActivityObserver
    {
        public List<string> Reasons { get; } = new();
        public void Entered(string reason) => Reasons.Add(reason);
    }
}
