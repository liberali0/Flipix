using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using ImageConverter.Core.Updates;

namespace ImageConverter.App.Services;

/// <summary>
/// 새 버전 확인과 자동 업데이트. GitHub 최신 릴리스에서 설치 파일을 받아 해시를 확인한 뒤
/// 조용한 업데이트 모드(/SILENT /UPDATE)로 실행한다. 설치 마법사는 기존 설치 위치·선택을 그대로 쓰고,
/// 끝나면 Workshop을 다시 연다.
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public static Version? CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version;

    public static string CurrentVersionText
    {
        get
        {
            var v = CurrentVersion;
            return v is null ? "" : $"v{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }
    }

    /// <summary>새 버전이 있으면 그 정보를, 없거나 확인할 수 없으면 null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, UpdateFeed.LatestReleaseApi);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null; // 릴리스가 없거나(404) 요청 제한
        var info = UpdateFeed.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return info != null && UpdateFeed.IsNewer(info.Version, CurrentVersion) ? info : null;
    }

    /// <summary>설치 파일을 임시 폴더에 받고 SHA-256을 확인한다. 맞지 않으면 지우고 예외.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        var sums = await Http.GetStringAsync(info.ChecksumsUrl, ct).ConfigureAwait(false);
        var expected = UpdateFeed.FindHash(sums, info.SetupName)
            ?? throw new InvalidOperationException("릴리스의 해시 목록에 설치 파일이 없습니다.");

        var dir = Path.Combine(Path.GetTempPath(), "Flipix-Update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, info.SetupName);
        var tmp = path + ".part";

        using (var res = await Http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength ?? 0;
            await using var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                if (total > 0) progress?.Report(100.0 * done / total);
            }
        }

        string actual;
        await using (var fs = File.OpenRead(tmp))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false)).ToLowerInvariant();
        if (actual != expected)
        {
            File.Delete(tmp);
            throw new InvalidOperationException("내려받은 설치 파일이 릴리스와 다릅니다. 잠시 뒤 다시 시도해 주세요.");
        }
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    /// <summary>받은 설치 파일을 업데이트 모드로 실행한다. 호출한 쪽은 바로 앱을 닫아야 한다.</summary>
    public static void LaunchInstaller(string setupPath)
    {
        Process.Start(new ProcessStartInfo(setupPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(setupPath)!,
        });
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub API는 User-Agent가 없으면 거절한다. 개인 정보는 넣지 않는다.
        var version = CurrentVersionText.TrimStart('v');
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Flipix-Workshop", version.Length > 0 ? version : "0"));
        return c;
    }
}
