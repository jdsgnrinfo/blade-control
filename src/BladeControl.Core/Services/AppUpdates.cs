using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using BladeControl.Core.Diagnostics;

namespace BladeControl.Core.Services;

/// <summary>
/// A release on GitHub newer or not than this app: its version, its notes, its
/// page, and the portable zip with the file holding the zip's checksum. The zip
/// and checksum are null when the release has none, and the update then goes
/// through the release's page.
/// </summary>
internal sealed record AppRelease(Version Version, string Notes, string PageUrl, string? ZipUrl, string? ChecksumUrl, long ZipBytes)
{
    /// <summary>Whether the app can download and put this release in itself.</summary>
    public bool CanInstall => ZipUrl is not null && ChecksumUrl is not null;
}

/// <summary>
/// Reads GitHub's answer for the latest release. Only this project's releases
/// count, and only files under its own release downloads, so the app never
/// fetches from anywhere else whatever the answer says.
/// </summary>
internal static partial class ReleaseParser
{
    public const string LatestReleaseApi = "https://api.github.com/repos/jdsgnrinfo/blade-control/releases/latest";
    public const string ReleasesPage = "https://github.com/jdsgnrinfo/blade-control/releases";
    private const string DownloadsPrefix = "https://github.com/jdsgnrinfo/blade-control/releases/download/";

    // The portable zip, as the release script names it: BladeControl-v0.1.5-win-x64.zip.
    [GeneratedRegex(@"^BladeControl-v\d+\.\d+\.\d+-win-x64\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex ZipName();

    /// <summary>The release in GitHub's JSON, or null when it is not one (a draft, a pre-release, or nothing readable).</summary>
    public static AppRelease? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (Bool(root, "draft") || Bool(root, "prerelease"))
                return null;

            if (ParseVersion(Text(root, "tag_name")) is not { } version)
                return null;

            var page = Text(root, "html_url") is { } url && url.StartsWith(ReleasesPage, StringComparison.OrdinalIgnoreCase) ? url : ReleasesPage;
            string? zipUrl = null, checksumUrl = null;
            long zipBytes = 0;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Text(asset, "name") ?? string.Empty;
                    var download = Text(asset, "browser_download_url");

                    if (download is null || !download.StartsWith(DownloadsPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ZipName().IsMatch(name))
                    {
                        zipUrl = download;
                        zipBytes = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0;
                    }
                }

                // The checksum belongs to the zip found, by name: the zip's name plus ".sha256".
                if (zipUrl is not null)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (Text(asset, "browser_download_url") is { } download &&
                            download.Equals(zipUrl + ".sha256", StringComparison.OrdinalIgnoreCase))
                        {
                            checksumUrl = download;
                        }
                    }
                }
            }

            return new AppRelease(version, Text(root, "body")?.Trim() ?? string.Empty, page, zipUrl, checksumUrl, zipBytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v0.1.5" or "0.1.5" as a version of three parts; null for anything else.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var text = tag?.Trim().TrimStart('v', 'V');

        return Version.TryParse(text, out var version) && version.Major >= 0
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build))
            : null;
    }

    /// <summary>Whether <paramref name="release"/> is newer than <paramref name="current"/>, comparing major, minor and patch only.</summary>
    public static bool IsNewer(Version release, Version current) =>
        release > new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    /// <summary>
    /// The checksum in a ".sha256" file: 64 hex digits, alone or followed by the
    /// file's name, as sha256sum and the release script write it. Upper case.
    /// </summary>
    public static string? ParseChecksum(string text)
    {
        var first = text.Trim().Split((char[])[' ', '\t', '\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToUpperInvariant() : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>
/// Asks GitHub for the latest release and downloads its zip. One small request
/// per check; the zip, about 75 MB, only when the user asks to update.
/// </summary>
internal sealed class AppUpdateClient
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);

    private readonly string _userAgent;

    public AppUpdateClient(Version current) =>
        _userAgent = $"BladeControl/{current.ToString(3)}";

    /// <summary>The latest release, or null when there is none GitHub calls latest. Throws when GitHub cannot be reached.</summary>
    public async Task<AppRelease?> LatestAsync(CancellationToken cancel)
    {
        using var client = Client(CheckTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseParser.LatestReleaseApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);

        // No release yet answers 404.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return ReleaseParser.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false));
    }

    /// <summary>
    /// Downloads the release's zip into <paramref name="folder"/>, checks it
    /// against its published checksum and unpacks it there. Returns the folder
    /// holding the new BladeControl.exe. Throws when the download fails or the
    /// checksum does not match, leaving nothing unpacked.
    /// </summary>
    public async Task<string> DownloadAsync(AppRelease release, string folder, IProgress<double>? progress, CancellationToken cancel)
    {
        if (!release.CanInstall)
            throw new InvalidOperationException("The release has no portable zip with a checksum.");

        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        Directory.CreateDirectory(folder);
        var zipPath = Path.Combine(folder, "update.zip");

        // No overall timeout for the zip: a slow connection is still a working one.
        using var client = Client(Timeout.InfiniteTimeSpan);
        var expected = ReleaseParser.ParseChecksum(await client.GetStringAsync(release.ChecksumUrl, cancel).ConfigureAwait(false))
            ?? throw new InvalidDataException("The release's checksum file could not be read.");

        using (var response = await client.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.ZipBytes;

            await using var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using var target = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                done += read;

                if (total > 0)
                    progress?.Report(Math.Min(1, done / (double)total));
            }
        }

        string actual;

        await using (var zip = File.OpenRead(zipPath))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(zip, cancel).ConfigureAwait(false));

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(zipPath);
            throw new InvalidDataException($"The downloaded zip's checksum {actual} does not match the published {expected}.");
        }

        var unpacked = Path.Combine(folder, "files");
        ZipFile.ExtractToDirectory(zipPath, unpacked);
        File.Delete(zipPath);

        var app = UpdateInstaller.FindAppFolder(unpacked)
            ?? throw new InvalidDataException("The zip holds no BladeControl.exe.");

        AppLog.Info($"Update {release.Version.ToString(3)} downloaded and checked ({expected}).");
        return app;
    }

    private HttpClient Client(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_userAgent);
        return client;
    }
}
