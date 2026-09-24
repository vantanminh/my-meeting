using System.IO;
using System.Text.Json;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

/// <summary>
/// A local auth adapter used by the offline-first shell. The interface is deliberately
/// shaped like the Firebase adapter that can replace it when project credentials exist.
/// </summary>
public sealed class LocalAuthService : IAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _sessionPath;

    public LocalAuthService()
    {
        Directory.CreateDirectory(AppPaths.RootDirectory);
        _sessionPath = AppPaths.SessionPath;
    }

    public UserSession? CurrentSession { get; private set; }

    public async Task<UserSession?> RestoreAsync()
    {
        if (!File.Exists(_sessionPath)) return null;

        try
        {
            await using var stream = File.OpenRead(_sessionPath);
            CurrentSession = await JsonSerializer.DeserializeAsync<UserSession>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            CurrentSession = null;
        }

        return CurrentSession;
    }

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        await Task.Delay(450);

        if (!email.Contains('@', StringComparison.Ordinal) || email.Length < 5)
            return new(false, Error: "Enter a valid email address.");
        if (password.Length < 6)
            return new(false, Error: "Password must be at least 6 characters.");

        var session = CreateSession(email, isOffline: false);
        await PersistAsync(session);
        return new(true, session);
    }

    public async Task<AuthResult> SignUpAsync(string displayName, string email, string password)
    {
        await Task.Delay(550);

        if (string.IsNullOrWhiteSpace(displayName))
            return new(false, Error: "Tell us your name to create the workspace.");
        if (!email.Contains('@', StringComparison.Ordinal) || email.Length < 5)
            return new(false, Error: "Enter a valid email address.");
        if (password.Length < 6)
            return new(false, Error: "Password must be at least 6 characters.");

        var session = CreateSession(email, isOffline: false);
        session.DisplayName = displayName.Trim();
        await PersistAsync(session);
        return new(true, session);
    }

    public async Task<AuthResult> RequestPasswordResetAsync(string email)
    {
        await Task.Delay(180);
        if (!email.Contains('@', StringComparison.Ordinal) || email.Length < 5)
            return new(false, Error: "Enter your email address first.");
        return new(true);
    }

    public async Task<AuthResult> SignInOfflineAsync()
    {
        var session = new UserSession
        {
            UserId = "offline",
            Email = "you@local.workspace",
            DisplayName = "Local workspace",
            IsOffline = true
        };
        await PersistAsync(session);
        return new(true, session);
    }

    public async Task CacheSessionAsync(UserSession session)
    {
        await PersistAsync(session);
    }

    public async Task SignOutAsync()
    {
        CurrentSession = null;
        if (File.Exists(_sessionPath)) File.Delete(_sessionPath);
        await Task.CompletedTask;
    }

    private static UserSession CreateSession(string email, bool isOffline)
    {
        var localPart = email.Split('@')[0];
        var displayName = string.Join(' ', localPart
            .Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

        return new UserSession
        {
            UserId = $"local-{Guid.NewGuid():N}",
            Email = email.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Workspace member" : displayName,
            IsOffline = isOffline
        };
    }

    private async Task PersistAsync(UserSession session)
    {
        CurrentSession = session;
        await using var stream = File.Create(_sessionPath);
        await JsonSerializer.SerializeAsync(stream, session, JsonOptions);
    }
}
