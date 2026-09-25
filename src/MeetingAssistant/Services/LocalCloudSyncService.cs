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

    public Task<IReadOnlyList<Meeting>> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Meeting>>([]);
    }

    public Task<SyncResult> SyncAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsPaused
            ? new SyncResult(true, "Saved locally", "Settings and meetings stay in the local file")
            : new SyncResult(true, "Saved locally", "This workspace never leaves this computer"));
    }
}
