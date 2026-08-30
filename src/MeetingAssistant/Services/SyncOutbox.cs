using System.IO;
using System.Text.Json;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed class SyncOutboxItem
{
    public string MeetingId { get; set; } = string.Empty;
    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.Now;
    public int Attempts { get; set; }
}

public sealed class JsonSyncOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<SyncOutboxItem>> LoadAsync()
    {
        if (!File.Exists(AppPaths.OutboxPath)) return [];
        try
        {
            await using var stream = File.OpenRead(AppPaths.OutboxPath);
            return await JsonSerializer.DeserializeAsync<List<SyncOutboxItem>>(stream, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<SyncOutboxItem> items)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var temporaryPath = $"{AppPaths.OutboxPath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, items, JsonOptions);
        }

        File.Move(temporaryPath, AppPaths.OutboxPath, overwrite: true);
    }

    public async Task EnqueueAsync(string meetingId)
    {
        var items = await LoadAsync();
        if (items.Any(item => string.Equals(item.MeetingId, meetingId, StringComparison.OrdinalIgnoreCase)))
            return;
        items.Add(new SyncOutboxItem { MeetingId = meetingId });
        await SaveAsync(items);
    }

    public async Task RemoveAsync(string meetingId)
    {
        var items = await LoadAsync();
        items.RemoveAll(item => string.Equals(item.MeetingId, meetingId, StringComparison.OrdinalIgnoreCase));
        await SaveAsync(items);
    }
}
