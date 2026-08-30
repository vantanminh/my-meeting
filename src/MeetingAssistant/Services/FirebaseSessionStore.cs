using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed class FirebaseSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private string PathName => AppPaths.FirebaseSessionPath;

    public async Task SaveAsync(UserSession session)
    {
        if (string.IsNullOrWhiteSpace(session.RefreshToken)) return;
        Directory.CreateDirectory(AppPaths.RootDirectory);
        var protectedToken = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(session.RefreshToken),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        var stored = new StoredSession
        {
            UserId = session.UserId,
            Email = session.Email,
            DisplayName = session.DisplayName,
            RefreshToken = Convert.ToBase64String(protectedToken)
        };

        var temporaryPath = $"{PathName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, stored, JsonOptions);
        }

        File.Move(temporaryPath, PathName, overwrite: true);
    }

    public async Task<UserSession?> LoadAsync()
    {
        if (!File.Exists(PathName)) return null;

        try
        {
            await using var stream = File.OpenRead(PathName);
            var stored = await JsonSerializer.DeserializeAsync<StoredSession>(stream, JsonOptions);
            if (stored is null || string.IsNullOrWhiteSpace(stored.RefreshToken)) return null;
            var protectedToken = Convert.FromBase64String(stored.RefreshToken);
            var refreshToken = ProtectedData.Unprotect(
                protectedToken,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return new UserSession
            {
                UserId = stored.UserId,
                Email = stored.Email,
                DisplayName = stored.DisplayName,
                IsOffline = false,
                RefreshToken = Encoding.UTF8.GetString(refreshToken)
            };
        }
        catch (CryptographicException)
        {
            await ClearAsync();
            return null;
        }
        catch (FormatException)
        {
            await ClearAsync();
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            await ClearAsync();
            return null;
        }
    }

    public Task ClearAsync()
    {
        if (File.Exists(PathName)) File.Delete(PathName);
        return Task.CompletedTask;
    }

    private sealed class StoredSession
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
}
