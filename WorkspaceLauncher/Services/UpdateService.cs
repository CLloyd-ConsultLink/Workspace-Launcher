using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkspaceLauncher.Services;

public sealed class UpdateService
{
    private const string ReleaseApiUrl =
        "https://api.github.com/repos/CLloyd-ConsultLink/Workspace-Launcher/releases/latest";
    private const string InstallerFileName = "WorkspaceLauncher-Setup-win-x64.exe";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<UpdateRelease?> CheckForUpdateAsync()
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(ReleaseApiUrl);
        response.EnsureSuccessStatusCode();

        await using Stream releaseStream = await response.Content.ReadAsStreamAsync();
        ReleaseMetadata? release = await JsonSerializer.DeserializeAsync<ReleaseMetadata>(releaseStream);
        if (release is null)
        {
            throw new InvalidDataException("GitHub returned an empty release response.");
        }

        if (!TryParseStableVersion(release.TagName, out Version? releaseVersion) ||
            releaseVersion is null)
        {
            throw new InvalidDataException($"The latest release has an invalid stable version tag: {release.TagName}");
        }

        string? installedVersionText = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!TryParseStableVersion(installedVersionText, out Version? installedVersion) ||
            installedVersion is null)
        {
            throw new InvalidOperationException($"The installed app version is invalid: {installedVersionText}");
        }

        if (releaseVersion <= installedVersion)
        {
            return null;
        }

        ReleaseAsset? installer = release.Assets?.FirstOrDefault(asset =>
            string.Equals(asset.Name, InstallerFileName, StringComparison.Ordinal));
        if (installer is null ||
            !Uri.TryCreate(installer.BrowserDownloadUrl, UriKind.Absolute, out Uri? installerUri) ||
            installerUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(installerUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The latest release does not contain a valid {InstallerFileName} asset.");
        }

        const string digestPrefix = "sha256:";
        if (installer.Digest is null ||
            !installer.Digest.StartsWith(digestPrefix, StringComparison.OrdinalIgnoreCase) ||
            !IsSha256Hex(installer.Digest[digestPrefix.Length..]))
        {
            throw new InvalidDataException($"The latest release does not publish a valid SHA-256 digest for {InstallerFileName}.");
        }

        return new UpdateRelease(release.TagName, releaseVersion, installerUri, installer.Digest[digestPrefix.Length..]);
    }

    public async Task<string> DownloadInstallerAsync(UpdateRelease release)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, release.InstallerUri);
        using HttpResponseMessage response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        string installerPath = Path.Combine(
            Path.GetTempPath(),
            $"WorkspaceLauncher-Setup-{Guid.NewGuid():N}.exe");
        bool verified = false;

        try
        {
            await using Stream responseStream = await response.Content.ReadAsStreamAsync();
            await using var installerStream = new FileStream(
                installerPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await responseStream.ReadAsync(buffer)) > 0)
            {
                hash.AppendData(buffer, 0, bytesRead);
                await installerStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            }

            await installerStream.FlushAsync();
            string actualDigest = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actualDigest, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The downloaded installer failed SHA-256 verification. Expected {release.Sha256}, received {actualDigest}.");
            }

            verified = true;
            return installerPath;
        }
        finally
        {
            if (!verified && File.Exists(installerPath))
            {
                File.Delete(installerPath);
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WorkspaceManager-UpdateCheck/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static bool TryParseStableVersion(string? value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        int metadataIndex = normalized.IndexOf('+');
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        if (normalized.Contains('-') ||
            !Version.TryParse(normalized, out Version? parsed) ||
            parsed is null ||
            parsed.Build < 0)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ReleaseMetadata
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<ReleaseAsset> Assets { get; set; } = [];
    }

    private sealed class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }
}

public sealed record UpdateRelease(
    string TagName,
    Version Version,
    Uri InstallerUri,
    string Sha256);
