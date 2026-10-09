using NullWave.Helpers.Diagnostics;

namespace NullWave.Tests.Diagnostics;

/// <summary>The timeline is static, so these tests share one collection and reset it around each test.</summary>
[Collection("StartupTimeline")]
public class StartupTimelineTests : IDisposable
{
    private long _now;

    public StartupTimelineTests() => StartupTimeline.ResetForTests(() => _now);
    public void Dispose() => StartupTimeline.ResetForTests();

    [Fact]
    public void Marks_record_the_clock_value_in_the_order_they_were_made()
    {
        _now = 95;   StartupTimeline.Mark("Main entered");
        _now = 610;  StartupTimeline.Mark("Framework initialized");
        _now = 1180; StartupTimeline.Mark("MainWindow opened");

        var marks = StartupTimeline.Snapshot();

        Assert.Equal(new[] { "Main entered", "Framework initialized", "MainWindow opened" },
            marks.Select(m => m.Name));
        Assert.Equal(new long[] { 95, 610, 1180 }, marks.Select(m => m.Ms));
    }

    [Fact]
    public void Summary_lists_every_mark_on_one_line()
    {
        _now = 95;  StartupTimeline.Mark("Main entered");
        _now = 610; StartupTimeline.Mark("Framework initialized");

        Assert.Equal("Main entered 95 ms | Framework initialized 610 ms", StartupTimeline.Summary());
    }

    [Fact]
    public void Summary_is_empty_when_nothing_was_marked()
        => Assert.Equal(string.Empty, StartupTimeline.Summary());

    [Fact]
    public void The_same_name_can_be_marked_twice()
    {
        _now = 10; StartupTimeline.Mark("tick");
        _now = 20; StartupTimeline.Mark("tick");

        Assert.Equal(2, StartupTimeline.Snapshot().Count);
    }

    [Fact]
    public void A_snapshot_is_a_copy_that_later_marks_do_not_change()
    {
        _now = 1; StartupTimeline.Mark("a");
        var snapshot = StartupTimeline.Snapshot();

        _now = 2; StartupTimeline.Mark("b");

        Assert.Single(snapshot);
        Assert.Equal(2, StartupTimeline.Snapshot().Count);
    }

    [Fact]
    public void LogSummary_does_not_throw_when_the_logger_is_not_configured()
    {
        _now = 5; StartupTimeline.Mark("x");

        var error = Record.Exception(StartupTimeline.LogSummary);

        Assert.Null(error);
    }

    [Fact]
    public void Marks_from_many_threads_are_all_kept()
    {
        Parallel.For(0, 200, i => StartupTimeline.Mark("m" + i));

        Assert.Equal(200, StartupTimeline.Snapshot().Count);
    }
}