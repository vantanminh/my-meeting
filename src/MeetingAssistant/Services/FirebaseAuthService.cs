using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

/// <summary>
/// Firebase Identity Toolkit REST adapter. Configuration is opt-in through environment
/// variables, so local/offline development never needs a secret checked into the repo.
/// </summary>
public sealed class FirebaseAuthService : IAuthService
{
    private readonly FirebaseConfiguration _configuration;
    private readonly LocalAuthService _localFallback;
    private readonly FirebaseSessionStore _sessionStore;
    private readonly HttpClient _httpClient = new();

    public FirebaseAuthService(FirebaseConfiguration configuration, LocalAuthService localFallback)
    {
        _configuration = configuration;
        _localFallback = localFallback;
        _sessionStore = new FirebaseSessionStore();
    }

    public UserSession? CurrentSession { get; private set; }
    public bool IsConfigured => _configuration.IsConfigured;

    public async Task<UserSession?> RestoreAsync()
    {
        if (_configuration.IsConfigured)
        {
            var storedSession = await _sessionStore.LoadAsync();
            if (storedSession is not null)
            {
                var refreshed = await RefreshSessionAsync(storedSession);
                if (refreshed is not null)
                {
                    CurrentSession = refreshed;
                    await _localFallback.CacheSessionAsync(refreshed);
                    return CurrentSession;
                }

                await _sessionStore.ClearAsync();
                await _localFallback.SignOutAsync();
                return null;
            }

            var localSession = await _localFallback.RestoreAsync();
            CurrentSession = localSession?.IsOffline == true ? localSession : null;
            return CurrentSession;
        }

        CurrentSession = await _localFallback.RestoreAsync();
        return CurrentSession;
    }

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        var result = IsConfigured
            ? await AuthenticateAsync("accounts:signInWithPassword", email, password, null)
            : await _localFallback.SignInAsync(email, password);
        CurrentSession = result.Session;
        return result;
    }

    public async Task<AuthResult> SignUpAsync(string displayName, string email, string password)
    {
        var result = IsConfigured
            ? await AuthenticateAsync("accounts:signUp", email, password, displayName)
            : await _localFallback.SignUpAsync(displayName, email, password);
        CurrentSession = result.Session;
        return result;
    }

    public async Task<AuthResult> RequestPasswordResetAsync(string email)
    {
        if (!IsConfigured) return await _localFallback.RequestPasswordResetAsync(email);
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
            return new(false, Error: "Enter your email address first.");

        try
        {
            var endpoint = $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={Uri.EscapeDataString(_configuration.ApiKey!)}";
            using var response = await _httpClient.PostAsJsonAsync(endpoint, new
            {
                requestType = "PASSWORD_RESET",
                email
            });
            var body = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode
                ? new AuthResult(true)
                : new AuthResult(false, Error: FirebaseError(body));
        }
        catch (HttpRequestException)
        {
            return new(false, Error: "Firebase is not reachable. Try again later.");
        }
    }

    public async Task<AuthResult> SignInOfflineAsync()
    {
        await _sessionStore.ClearAsync();
        var result = await _localFallback.SignInOfflineAsync();
        CurrentSession = result.Session;
        return result;
    }

    public async Task SignOutAsync()
    {
        CurrentSession = null;
        await _sessionStore.ClearAsync();
        await _localFallback.SignOutAsync();
    }

    private async Task<AuthResult> AuthenticateAsync(string operation, string email, string password, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
            return new(false, Error: "Enter a valid email address.");
        if (password.Length < 6)
            return new(false, Error: "Password must be at least 6 characters.");

        try
        {
            var endpoint = $"https://identitytoolkit.googleapis.com/v1/{operation}?key={Uri.EscapeDataString(_configuration.ApiKey!)}";
            using var response = await _httpClient.PostAsJsonAsync(endpoint, new
            {
                email,
                password,
                returnSecureToken = true
            });
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return new(false, Error: FirebaseError(body));

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var session = new UserSession
            {
                UserId = root.GetProperty("localId").GetString() ?? $"firebase-{Guid.NewGuid():N}",
                Email = root.TryGetProperty("email", out var emailElement) ? emailElement.GetString() ?? email : email,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? DisplayNameFromEmail(email) : displayName.Trim(),
                IsOffline = false,
                AccessToken = root.TryGetProperty("idToken", out var tokenElement) ? tokenElement.GetString() : null,
                RefreshToken = root.TryGetProperty("refreshToken", out var refreshTokenElement) ? refreshTokenElement.GetString() : null
            };
            CurrentSession = session;
            await _localFallback.CacheSessionAsync(session);
            await _sessionStore.SaveAsync(session);
            return new(true, session);
        }
        catch (HttpRequestException)
        {
            return new(false, Error: "Firebase is not reachable. Try again or use the local workspace.");
        }
        catch (JsonException)
        {
            return new(false, Error: "Firebase returned an unexpected response. Try again.");
        }
    }

    private async Task<UserSession?> RefreshSessionAsync(UserSession storedSession)
    {
        if (string.IsNullOrWhiteSpace(storedSession.RefreshToken)) return null;

        try
        {
            var endpoint = $"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(_configuration.ApiKey!)}";
            using var response = await _httpClient.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = storedSession.RefreshToken
            }));
            if (!response.IsSuccessStatusCode) return null;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            storedSession.UserId = root.TryGetProperty("user_id", out var userId) ? userId.GetString() ?? storedSession.UserId : storedSession.UserId;
            storedSession.AccessToken = root.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null;
            storedSession.RefreshToken = root.TryGetProperty("refresh_token", out var refreshToken) ? refreshToken.GetString() ?? storedSession.RefreshToken : storedSession.RefreshToken;
            await _sessionStore.SaveAsync(storedSession);
            return storedSession.AccessToken is null ? null : storedSession;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static string FirebaseError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var message = document.RootElement.GetProperty("error").GetProperty("message").GetString();
            return message switch
            {
                "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" => "That email or password is not correct.",
                "EMAIL_EXISTS" => "An account already exists for that email.",
                "WEAK_PASSWORD" => "Choose a stronger password.",
                _ => "Firebase could not authenticate this workspace."
            };
        }
        catch (JsonException)
        {
            return "Firebase could not authenticate this workspace.";
        }
    }

    private static string DisplayNameFromEmail(string email)
    {
        var localPart = email.Split('@')[0];
        return string.Join(' ', localPart
            .Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part.Length > 0)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }
}
