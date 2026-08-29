using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

/// <summary>
/// Local sync adapter. Firebase can implement this same contract; the UI always
/// reports whether a meeting is safely cached and whether cloud sync is paused.
/// </summary>
public sealed class LocalCloudSyncService : ICloudSyncService
{
    public bool IsPaused { get; set; }
    public string StatusLabel => IsPaused ? "Sync paused" : "Local workspace";

    public async Task<SyncResult> SyncAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(220, cancellationToken);

        return IsPaused
            ? new SyncResult(true, "Saved locally", "Cloud sync is paused in Settings")
            : new SyncResult(true, "Local cache ready", "Cloud connector is ready for Firebase credentials");
    }
}
