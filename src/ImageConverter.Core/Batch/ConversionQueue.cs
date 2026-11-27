using System.Collections.Concurrent;
using ImageConverter.Core.Engine;

namespace ImageConverter.Core.Batch;

/// <summary>워커 풀 방식의 배치 변환 큐. 병렬 처리, 일시정지/재개, 취소, 대용량 파일 동시성 제한을 지원한다.</summary>
public sealed class ConversionQueue
{
    private readonly IConversionEngine _engine;
    private readonly object _gateLock = new();
    private TaskCompletionSource<bool> _gate = CompletedGate();

    public ConversionQueue(IConversionEngine engine) => _engine = engine;

    /// <summary>작업이 끝날 때마다(워커 스레드에서) 호출된다.</summary>
    public event Action<ConversionJob>? JobFinished;

    public bool IsPaused { get { lock (_gateLock) return !_gate.Task.IsCompleted; } }

    public void Pause()
    {
        lock (_gateLock)
        {
            if (_gate.Task.IsCompleted)
                _gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (_gateLock) _gate.TrySetResult(true);
    }

    public async Task<BatchSummary> RunAsync(IEnumerable<ConversionJob> jobs, BatchOptions options, CancellationToken ct)
    {
        var list = jobs.Where(j => j.Status == JobStatus.Pending).ToList();
        var pending = new ConcurrentQueue<ConversionJob>(list);
        var resolver = new OutputPathResolver(options.Output, list.Select(j => j.InputPath));
        using var heavy = new SemaphoreSlim(Math.Max(1, options.MaxParallelLargeFiles));
        int workers = Math.Max(1, Math.Min(options.MaxParallelism, Math.Max(1, list.Count)));

        var tasks = Enumerable.Range(0, workers)
            .Select(_ => Task.Run(() => WorkerAsync(pending, options, resolver, heavy, ct)))
            .ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // 취소로 처리하지 못한 항목 정리
        foreach (var j in list.Where(j => j.Status == JobStatus.Pending))
        {
            j.Status = JobStatus.Canceled;
            JobFinished?.Invoke(j);
        }

        Resume();
        return new BatchSummary(
            list.Count(j => j.Status == JobStatus.Succeeded),
            list.Count(j => j.Status == JobStatus.Failed),
            list.Count(j => j.Status == JobStatus.Skipped),
            list.Count(j => j.Status == JobStatus.Canceled));
    }

    private async Task WorkerAsync(ConcurrentQueue<ConversionJob> pending, BatchOptions o,
        OutputPathResolver resolver, SemaphoreSlim heavy, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (pending.IsEmpty) return; // 남은 일이 없으면 일시정지 중이어도 끝낸다
            try { await CurrentGate().WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            if (!pending.TryDequeue(out var job)) return;
            await RunOneAsync(job, o, resolver, heavy, ct).ConfigureAwait(false);
        }
    }

    private async Task RunOneAsync(ConversionJob job, BatchOptions o, OutputPathResolver resolver,
        SemaphoreSlim heavy, CancellationToken ct)
    {
        bool isLarge = job.InputBytes >= o.LargeFileThresholdBytes;
        bool gotHeavy = false;
        try
        {
            if (isLarge)
            {
                try { await heavy.WaitAsync(ct).ConfigureAwait(false); gotHeavy = true; }
                catch (OperationCanceledException) { job.Status = JobStatus.Canceled; return; }
            }

            string? outPath;
            try { outPath = resolver.Resolve(job.InputPath, job.SourceRoot, o.TargetFormat); }
            catch (Exception ex) { Fail(job, ex.Message); return; }

            if (outPath is null)
            {
                job.Message = "같은 이름의 파일이 있어 건너뜀";
                job.Status = JobStatus.Skipped;
                return;
            }

            job.OutputPath = outPath;
            job.Progress = 0;
            job.Status = JobStatus.Running;

            ConversionResult result;
            bool existedBefore = File.Exists(outPath);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                var req = new ConversionRequest(job.InputPath, outPath, o.TargetFormat, o.Conversion);
                result = await _engine.ConvertAsync(req, new JobProgress(job), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryDeletePartial(outPath, existedBefore);
                job.Message = null;
                job.Status = JobStatus.Canceled;
                return;
            }
            catch (Exception ex)
            {
                result = ConversionResult.Fail(ex.Message);
            }

            if (result.Success)
            {
                job.Progress = 100;
                if (result.WrittenPath != null) { outPath = result.WrittenPath; job.OutputPath = outPath; }
                job.Message = JoinMessage(result.Note, result.KeepSource && !string.IsNullOrWhiteSpace(o.MoveSourceTo)
                    ? "원본은 그대로 둠(첫 장면만 변환)"
                    : MoveSource(job, outPath, o.MoveSourceTo));
                job.Status = JobStatus.Succeeded;
            }
            else
            {
                TryDeletePartial(outPath, existedBefore);
                Fail(job, result.ErrorMessage ?? "알 수 없는 오류");
            }
        }
        finally
        {
            if (gotHeavy) heavy.Release();
            JobFinished?.Invoke(job);
        }
    }

    /// <summary>성공한 원본만, 결과 파일이 실제로 따로 있을 때만 옮긴다. 옮기지 못해도 변환 결과는 성공으로 둔다.</summary>
    private static string? MoveSource(ConversionJob job, string outPath, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        if (string.Equals(Path.GetFullPath(job.InputPath), Path.GetFullPath(outPath), StringComparison.OrdinalIgnoreCase)
            || !(File.Exists(outPath) || Directory.Exists(outPath)))
            return "원본은 그대로 둠(결과 파일을 확인하지 못함)";
        try
        {
            var moved = SourceMover.MoveToFolder(job.InputPath, folder);
            job.MovedSourcePath = moved;
            return "원본 이동: " + moved;
        }
        catch (Exception ex)
        {
            return "원본을 옮기지 못함: " + ex.Message;
        }
    }

    private static string? JoinMessage(string? a, string? b) =>
        string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : a + " · " + b;

    private static void Fail(ConversionJob job, string message)
    {
        job.Message = message;
        job.Status = JobStatus.Failed;
    }

    // 변환 전부터 있던 파일(덮어쓰기 대상)은 지우지 않는다.
    private static void TryDeletePartial(string path, bool existedBefore)
    {
        if (existedBefore) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 무시 */ }
    }

    private Task CurrentGate() { lock (_gateLock) return _gate.Task; }

    private static TaskCompletionSource<bool> CompletedGate()
    {
        var t = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        t.SetResult(true);
        return t;
    }

    private sealed class JobProgress : IProgress<double>
    {
        private readonly ConversionJob _job;
        public JobProgress(ConversionJob job) => _job = job;
        public void Report(double value) => _job.Progress = Math.Clamp(value, 0, 100);
    }
}
