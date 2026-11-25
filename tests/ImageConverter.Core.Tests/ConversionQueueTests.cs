using ImageConverter.Core.Batch;
using ImageConverter.Core.Engine;

namespace ImageConverter.Core.Tests;

public class ConversionQueueTests
{
    private sealed class FakeEngine : IConversionEngine
    {
        private int _current, _max;
        public int MaxConcurrent => _max;
        public int DelayMs { get; init; } = 20;
        public Func<ConversionRequest, bool>? FailWhen { get; init; }
        public IReadOnlyCollection<string> SupportedInputExtensions => new[] { ".jpg" };

        public async Task<ConversionResult> ConvertAsync(ConversionRequest r, IProgress<double>? progress, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _current);
            int snapshot;
            do { snapshot = _max; } while (now > snapshot && Interlocked.CompareExchange(ref _max, now, snapshot) != snapshot);
            try
            {
                await Task.Delay(DelayMs, ct);
                if (FailWhen?.Invoke(r) == true) return ConversionResult.Fail("boom");
                File.WriteAllBytes(r.OutputPath, new byte[] { 1 });
                progress?.Report(100);
                return ConversionResult.Ok(1);
            }
            finally { Interlocked.Decrement(ref _current); }
        }
    }

    private static List<ConversionJob> MakeJobs(TempDir d, int n) =>
        Enumerable.Range(0, n).Select(i => new ConversionJob(d.Touch($"f{i}.jpg"), 4)).ToList();

    [Fact]
    public async Task Converts_all_jobs()
    {
        using var d = new TempDir();
        var jobs = MakeJobs(d, 5);

        var s = await new ConversionQueue(new FakeEngine()).RunAsync(jobs, new BatchOptions { MaxParallelism = 2 }, default);

        Assert.Equal(5, s.Succeeded);
        Assert.All(jobs, j => Assert.True(File.Exists(j.OutputPath)));
    }

    [Fact]
    public async Task Respects_max_parallelism()
    {
        using var d = new TempDir();
        var engine = new FakeEngine { DelayMs = 50 };

        await new ConversionQueue(engine).RunAsync(MakeJobs(d, 8), new BatchOptions { MaxParallelism = 3 }, default);

        Assert.InRange(engine.MaxConcurrent, 2, 3);
    }

    [Fact]
    public async Task Large_files_are_limited_to_one_at_a_time()
    {
        using var d = new TempDir();
        var engine = new FakeEngine { DelayMs = 50 };
        var jobs = Enumerable.Range(0, 4).Select(i => new ConversionJob(d.Touch($"big{i}.jpg"), 500)).ToList();

        await new ConversionQueue(engine).RunAsync(jobs,
            new BatchOptions { MaxParallelism = 4, LargeFileThresholdBytes = 100, MaxParallelLargeFiles = 1 }, default);

        Assert.Equal(1, engine.MaxConcurrent);
    }

    [Fact]
    public async Task Failure_is_recorded_and_does_not_stop_others()
    {
        using var d = new TempDir();
        var jobs = MakeJobs(d, 3);
        var engine = new FakeEngine { FailWhen = r => r.InputPath.EndsWith("f1.jpg") };

        var s = await new ConversionQueue(engine).RunAsync(jobs, new BatchOptions(), default);

        Assert.Equal(2, s.Succeeded);
        Assert.Equal(1, s.Failed);
        Assert.Equal("boom", jobs[1].Message);
    }

    [Fact]
    public async Task Cancel_marks_remaining_jobs_canceled()
    {
        using var d = new TempDir();
        var jobs = MakeJobs(d, 20);
        using var cts = new CancellationTokenSource();
        var queue = new ConversionQueue(new FakeEngine { DelayMs = 100 });

        var run = queue.RunAsync(jobs, new BatchOptions { MaxParallelism = 1 }, cts.Token);
        await Task.Delay(30);
        cts.Cancel();
        var s = await run;

        Assert.True(s.Canceled >= 18);
        Assert.All(jobs, j => Assert.NotEqual(JobStatus.Pending, j.Status));
    }

    [Fact]
    public async Task Pause_holds_new_jobs_until_resume()
    {
        using var d = new TempDir();
        var jobs = MakeJobs(d, 6);
        var queue = new ConversionQueue(new FakeEngine { DelayMs = 30 });

        queue.Pause();
        var run = queue.RunAsync(jobs, new BatchOptions { MaxParallelism = 2 }, default);
        await Task.Delay(150);
        Assert.All(jobs, j => Assert.Equal(JobStatus.Pending, j.Status));

        queue.Resume();
        var s = await run;
        Assert.Equal(6, s.Succeeded);
    }

    [Fact]
    public async Task Pausing_after_the_last_job_started_still_completes_the_batch()
    {
        using var d = new TempDir();
        var jobs = MakeJobs(d, 1);
        var queue = new ConversionQueue(new FakeEngine { DelayMs = 100 });

        var run = queue.RunAsync(jobs, new BatchOptions { MaxParallelism = 2 }, default);
        await Task.Delay(30);
        queue.Pause();

        var done = await Task.WhenAny(run, Task.Delay(3000));
        Assert.Same(run, done);
    }

    [Fact]
    public async Task Moves_only_successful_sources_to_the_folder()
    {
        using var d = new TempDir();
        var good = new ConversionJob(d.Touch("good.jpg"), 4);
        var bad = new ConversionJob(d.Touch("bad.jpg"), 4);
        var keep = Path.Combine(d.Path, "originals");
        var engine = new FakeEngine { FailWhen = r => r.InputPath.EndsWith("bad.jpg") };

        await new ConversionQueue(engine).RunAsync(new[] { good, bad },
            new BatchOptions { TargetFormat = "png", MoveSourceTo = keep }, default);

        Assert.Equal(JobStatus.Succeeded, good.Status);
        Assert.False(File.Exists(good.InputPath));
        Assert.True(File.Exists(Path.Combine(keep, "good.jpg")));
        Assert.True(File.Exists(good.OutputPath));
        Assert.Contains("원본 이동", good.Message);
        Assert.Equal(JobStatus.Failed, bad.Status);
        Assert.True(File.Exists(bad.InputPath));
        Assert.False(File.Exists(Path.Combine(keep, "bad.jpg")));
    }

    [Fact]
    public async Task Sources_stay_without_a_move_folder()
    {
        using var d = new TempDir();
        var jobs = MakeJobs(d, 2);

        await new ConversionQueue(new FakeEngine()).RunAsync(jobs, new BatchOptions(), default);

        Assert.All(jobs, j => Assert.True(File.Exists(j.InputPath)));
    }

    [Fact]
    public void Move_renames_instead_of_overwriting()
    {
        using var d = new TempDir();
        var keep = Path.Combine(d.Path, "keep");
        var existing = d.Touch(Path.Combine("keep", "a.jpg"), bytes: 9);
        var source = d.Touch("a.jpg", bytes: 3);

        var moved = SourceMover.MoveToFolder(source, keep);

        Assert.Equal(Path.Combine(keep, "a (1).jpg"), moved);
        Assert.Equal(9, new FileInfo(existing).Length);
        Assert.Equal(3, new FileInfo(moved).Length);
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void Move_into_the_same_folder_leaves_the_file()
    {
        using var d = new TempDir();
        var source = d.Touch("a.jpg");

        Assert.Equal(source, SourceMover.MoveToFolder(source, d.Path));
        Assert.True(File.Exists(source));
    }
}
