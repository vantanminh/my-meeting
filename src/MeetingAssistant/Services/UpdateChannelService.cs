using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetingAssistant.Services;

public enum UpdateCheckStatus
{
    NotConfigured,
    UpToDate,
    UpdateAvailable,
    Failed
}

public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease = null) : IComparable<SemanticVersion>
{
    public static SemanticVersion FromAssemblyVersion(Version? version)
        => new(version?.Major ?? 1, version?.Minor ?? 0, Math.Max(0, version?.Build ?? 0));

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        var buildSeparator = normalized.IndexOf('+');
        if (buildSeparator >= 0)
            normalized = normalized[..buildSeparator];

        string? preRelease = null;
        var preReleaseSeparator = normalized.IndexOf('-');
        if (preReleaseSeparator >= 0)
        {
            preRelease = normalized[(preReleaseSeparator + 1)..];
            normalized = normalized[..preReleaseSeparator];
            if (string.IsNullOrWhiteSpace(preRelease) || !IsValidPreRelease(preRelease)) return false;
        }

        var components = normalized.Split('.', StringSplitOptions.None);
        if (components.Length != 3) return false;
        if (!TryParseComponent(components[0], out var major)
            || !TryParseComponent(components[1], out var minor)
            || !TryParseComponent(components[2], out var patch))
            return false;

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0) return comparison;
        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0) return comparison;
        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0) return comparison;

        if (string.IsNullOrWhiteSpace(PreRelease) && string.IsNullOrWhiteSpace(other.PreRelease)) return 0;
        if (string.IsNullOrWhiteSpace(PreRelease)) return 1;
        if (string.IsNullOrWhiteSpace(other.PreRelease)) return -1;

        var leftIdentifiers = PreRelease.Split('.');
        var rightIdentifiers = other.PreRelease.Split('.');
        for (var index = 0; index < Math.Min(leftIdentifiers.Length, rightIdentifiers.Length); index++)
        {
            var left = leftIdentifiers[index];
            var right = rightIdentifiers[index];
            var leftIsNumber = int.TryParse(left, out var leftNumber);
            var rightIsNumber = int.TryParse(right, out var rightNumber);

            if (leftIsNumber && rightIsNumber)
            {
                var numberComparison = leftNumber.CompareTo(rightNumber);
                if (numberComparison != 0) return numberComparison;
            }
            else if (leftIsNumber != rightIsNumber)
            {
                return leftIsNumber ? -1 : 1;
            }
            else
            {
                var textComparison = string.Compare(left, right, StringComparison.Ordinal);
                if (textComparison != 0) return textComparison;
            }
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    public override string ToString()
        => string.IsNullOrWhiteSpace(PreRelease)
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    private static bool TryParseComponent(string value, out int component)
        => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out component)
            && component >= 0;

    private static bool IsValidPreRelease(string value)
        => value.Split('.').All(identifier => identifier.Length > 0
            && identifier.All(character => char.IsLetterOrDigit(character) || character == '-'));
}

public sealed class UpdateChannelConfiguration
{
    public const string ConfigFileName = "update.config.json";
    public const string OwnerEnvironmentVariable = "MEETING_ASSISTANT_GITHUB_OWNER";
    public const string RepositoryEnvironmentVariable = "MEETING_ASSISTANT_GITHUB_REPOSITORY";
    public const string AssetNameEnvironmentVariable = "MEETING_ASSISTANT_UPDATE_ASSET";
    public const string EnabledEnvironmentVariable = "MEETING_ASSISTANT_UPDATE_ENABLED";
    public const string DefaultOwner = "vantanminh";
    public const string DefaultRepository = "my-meeting";
    public const string DefaultAssetName = "MeetingAssistant-Setup.exe";

    public UpdateChannelConfiguration(string? baseDirectory = null)
    {
        var configPath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, ConfigFileName);
        var fileConfiguration = LoadFileConfiguration(configPath);
        var environmentOwner = Environment.GetEnvironmentVariable(OwnerEnvironmentVariable);
        var environmentRepository = Environment.GetEnvironmentVariable(RepositoryEnvironmentVariable);
        var environmentAssetName = Environment.GetEnvironmentVariable(AssetNameEnvironmentVariable);
        var environmentEnabled = Environment.GetEnvironmentVariable(EnabledEnvironmentVariable);

        Owner = FirstValue(environmentOwner, fileConfiguration?.Owner, DefaultOwner);
        Repository = FirstValue(environmentRepository, fileConfiguration?.Repository, DefaultRepository);
        AssetName = FirstValue(environmentAssetName, fileConfiguration?.AssetName, DefaultAssetName) ?? DefaultAssetName;
        Enabled = ParseBoolean(environmentEnabled) ?? fileConfiguration?.Enabled ?? true;
        ConfigurationPath = File.Exists(configPath) ? configPath : null;
        Source = !string.IsNullOrWhiteSpace(environmentOwner) || !string.IsNullOrWhiteSpace(environmentRepository)
            ? "Environment"
            : ConfigurationPath is not null ? "Package config" : "Built-in repository";
    }

    public string? Owner { get; }
    public string? Repository { get; }
    public string AssetName { get; }
    public bool Enabled { get; }
    public string? ConfigurationPath { get; }
    public string Source { get; }
    public bool IsConfigured => Enabled
        && IsValidRepositoryPart(Owner)
        && IsValidRepositoryPart(Repository)
        && IsValidAssetName(AssetName);

    public Uri? LatestReleaseUri
    {
        get
        {
            if (!IsConfigured) return null;
            return new Uri($"https://api.github.com/repos/{Uri.EscapeDataString(Owner!)}/{Uri.EscapeDataString(Repository!)}/releases/latest");
        }
    }

    public Uri? RepositoryUri
    {
        get
        {
            if (!IsConfigured) return null;
            return new Uri($"https://github.com/{Uri.EscapeDataString(Owner!)}/{Uri.EscapeDataString(Repository!)}");
        }
    }

    private static UpdateFileConfiguration? LoadFileConfiguration(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<UpdateFileConfiguration>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? ParseBoolean(string? value)
        => bool.TryParse(value, out var result) ? result : null;

    private static string? FirstValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool IsValidRepositoryPart(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsValidAssetName(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)
            && !value.Contains(Path.DirectorySeparatorChar)
            && !value.Contains(Path.AltDirectorySeparatorChar)
            && string.Equals(Path.GetExtension(value), ".exe", StringComparison.OrdinalIgnoreCase);

    private sealed class UpdateFileConfiguration
    {
        public bool? Enabled { get; set; }
        public string? Owner { get; set; }
        public string? Repository { get; set; }
        public string? AssetName { get; set; }
    }
}

public sealed record AppUpdateInfo(
    SemanticVersion Version,
    string TagName,
    string? ReleaseName,
    Uri ReleasePageUri,
    Uri DownloadUri,
    string AssetName,
    DateTimeOffset? PublishedAt);

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, AppUpdateInfo? Update = null);

public sealed record UpdateDownloadResult(bool Success, string Message, string? InstallerPath = null);

public interface IUpdateChannelService : IDisposable
{
    SemanticVersion CurrentVersion { get; }
    UpdateChannelConfiguration Configuration { get; }
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
    Task<UpdateDownloadResult> DownloadAsync(AppUpdateInfo update, CancellationToken cancellationToken = default);
    Task<UpdateDownloadResult> DownloadAndLaunchAsync(AppUpdateInfo update, CancellationToken cancellationToken = default);
}

public sealed class UpdateChannelService : IUpdateChannelService
{
    private const long MaximumDownloadBytes = 256L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _checkTimeout;
    private readonly TimeSpan _downloadTimeout;

    public UpdateChannelService(
        UpdateChannelConfiguration configuration,
        HttpClient? httpClient = null,
        SemanticVersion? currentVersion = null,
        TimeSpan? checkTimeout = null,
        TimeSpan? downloadTimeout = null)
    {
        Configuration = configuration;
        CurrentVersion = currentVersion ?? SemanticVersion.FromAssemblyVersion(typeof(UpdateChannelService).Assembly.GetName().Version);
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttpClient = httpClient is null;
        _checkTimeout = checkTimeout ?? TimeSpan.FromSeconds(8);
        _downloadTimeout = downloadTimeout ?? TimeSpan.FromMinutes(15);
        if (_checkTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(checkTimeout));
        if (_downloadTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(downloadTimeout));

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MeetingAssistant-Update/1.0");
        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public SemanticVersion CurrentVersion { get; }
    public UpdateChannelConfiguration Configuration { get; }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!Configuration.IsConfigured)
            return new(UpdateCheckStatus.NotConfigured, "Updates are not configured for this build.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_checkTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, Configuration.LatestReleaseUri);

        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(UpdateCheckStatus.Failed, "No public release has been published yet.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new(UpdateCheckStatus.Failed, "GitHub update checks are rate limited. Try again later.");
            if (!response.IsSuccessStatusCode)
                return new(UpdateCheckStatus.Failed, "GitHub could not provide update information.");

            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                await response.Content.ReadAsStreamAsync(timeout.Token), JsonOptions, timeout.Token);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
                return new(UpdateCheckStatus.Failed, "GitHub returned an invalid release.");
            if (release.Draft || release.Prerelease)
                return new(UpdateCheckStatus.Failed, "The latest GitHub release is not ready for installation.");
            if (!SemanticVersion.TryParse(release.TagName, out var releaseVersion))
                return new(UpdateCheckStatus.Failed, "The latest GitHub release has an invalid version.");

            if (releaseVersion.CompareTo(CurrentVersion) <= 0)
                return new(UpdateCheckStatus.UpToDate, "You are up to date.");

            var asset = release.Assets?.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, Configuration.AssetName, StringComparison.OrdinalIgnoreCase));
            if (asset is null || !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var downloadUri) || !IsAllowedGitHubUri(downloadUri))
                return new(UpdateCheckStatus.Failed, $"The release does not contain {Configuration.AssetName}.");

            var releasePageUri = Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var parsedReleasePage)
                && IsAllowedGitHubUri(parsedReleasePage)
                ? parsedReleasePage
                : new Uri($"https://github.com/{Uri.EscapeDataString(Configuration.Owner!)}/{Uri.EscapeDataString(Configuration.Repository!)}/releases/tag/{Uri.EscapeDataString(release.TagName)}");

            return new(
                UpdateCheckStatus.UpdateAvailable,
                $"Update available: v{releaseVersion}",
                new AppUpdateInfo(
                    releaseVersion,
                    release.TagName,
                    release.Name,
                    releasePageUri,
                    downloadUri,
                    Configuration.AssetName,
                    release.PublishedAt));
        }
        catch (HttpRequestException)
        {
            return new(UpdateCheckStatus.Failed, "Could not reach GitHub. Check the network and try again.");
        }
        catch (JsonException)
        {
            return new(UpdateCheckStatus.Failed, "GitHub returned an invalid update response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(UpdateCheckStatus.Failed, "GitHub took too long to respond.");
        }
    }

    public async Task<UpdateDownloadResult> DownloadAndLaunchAsync(AppUpdateInfo update, CancellationToken cancellationToken = default)
    {
        var download = await DownloadAsync(update, cancellationToken);
        if (!download.Success || string.IsNullOrWhiteSpace(download.InstallerPath))
            return download;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = download.InstallerPath,
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("/CLOSEAPPLICATIONS");
            startInfo.ArgumentList.Add("/RESTARTAPPLICATIONS");
            Process.Start(startInfo);
            return new(true, "Update downloaded. Restarting Meeting Assistant.", download.InstallerPath);
        }
        catch (InvalidOperationException)
        {
            return new(false, "The update was downloaded, but the installer could not be started.", download.InstallerPath);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new(false, "The update was downloaded, but Windows could not start the installer.", download.InstallerPath);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    public async Task<UpdateDownloadResult> DownloadAsync(AppUpdateInfo update, CancellationToken cancellationToken = default)
    {
        if (!Configuration.IsConfigured
            || !string.Equals(update.AssetName, Configuration.AssetName, StringComparison.OrdinalIgnoreCase)
            || update.Version.CompareTo(CurrentVersion) <= 0
            || !IsAllowedGitHubUri(update.DownloadUri))
            return new(false, "The update download address is not trusted.");

        var updatesDirectory = Path.Combine(Path.GetTempPath(), "MeetingAssistant", "updates");
        var extension = Path.GetExtension(Configuration.AssetName);
        var fileName = $"{Path.GetFileNameWithoutExtension(Configuration.AssetName)}-{update.Version}{extension}";
        var installerPath = Path.Combine(updatesDirectory, fileName);
        var temporaryPath = installerPath + ".download";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_downloadTimeout);
        try
        {
            Directory.CreateDirectory(updatesDirectory);
            using var response = await _httpClient.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return new(false, "GitHub could not download the update.");
            if (response.Content.Headers.ContentLength > MaximumDownloadBytes)
                return new(false, "The update package is larger than the safe download limit.");

            await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await response.Content.CopyToAsync(output, timeout.Token);
                await output.FlushAsync(timeout.Token);
            }

            var length = new FileInfo(temporaryPath).Length;
            if (length == 0)
                return new(false, "GitHub returned an empty update package.");

            File.Move(temporaryPath, installerPath, overwrite: true);
            return new(true, "Update package downloaded.", installerPath);
        }
        catch (HttpRequestException)
        {
            return new(false, "Could not download the update from GitHub.");
        }
        catch (IOException)
        {
            return new(false, "Windows could not save the update package.");
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows denied access to the update folder.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "The update download took too long. Try again.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
            }
        }
    }

    private static bool IsAllowedGitHubUri(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttps
            && (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
        public string? Name { get; set; }
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
        public bool Draft { get; set; }
        public bool Prerelease { get; set; }
        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
