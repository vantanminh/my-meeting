using System.Collections.ObjectModel;
using MeetingAssistant.Models;
using MeetingAssistant.Services;

namespace MeetingAssistant.ViewModels;

public sealed class FilterChipViewModel : ViewModelBase
{
    private bool _isSelected;

    public FilterChipViewModel(string name, bool isSelected = false)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class EditableLineViewModel : ViewModelBase
{
    private string _text;

    public EditableLineViewModel(string text) => _text = text;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }
}

public sealed class ActionEditorViewModel : ViewModelBase
{
    private string _text;
    private string _owner;
    private string _due;
    private bool _isComplete;

    public ActionEditorViewModel(ActionItem item)
    {
        Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
        MeetingId = item.MeetingId;
        MeetingTitle = item.MeetingTitle ?? string.Empty;
        _text = item.Text;
        _owner = item.Owner ?? string.Empty;
        _due = item.Due ?? string.Empty;
        _isComplete = item.IsComplete;
    }

    public string Id { get; }
    public string? MeetingId { get; }
    public string MeetingTitle { get; }

    public string Text { get => _text; set => SetProperty(ref _text, value); }
    public string Owner { get => _owner; set => SetProperty(ref _owner, value); }
    public string Due { get => _due; set => SetProperty(ref _due, value); }
    public bool IsComplete { get => _isComplete; set => SetProperty(ref _isComplete, value); }

    public ActionItem ToItem() => new()
    {
        Id = Id,
        Text = Text,
        Owner = Owner,
        Due = Due,
        IsComplete = IsComplete,
        MeetingId = MeetingId,
        MeetingTitle = MeetingTitle
    };
}

public sealed class DeadlineEditorViewModel : ViewModelBase
{
    public DeadlineEditorViewModel(DeadlineItem item)
    {
        Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id;
        Label = item.Label;
        Date = item.Date;
        Owner = item.Owner;
    }

    public string Id { get; }
    public string Label { get; set; }
    public string Date { get; set; }
    public string Owner { get; set; }

    public DeadlineItem ToItem() => new() { Id = Id, Label = Label, Date = Date, Owner = Owner };
}

public sealed class InboxActionViewModel : ViewModelBase
{
    public InboxActionViewModel(Meeting meeting, ActionItem item)
    {
        Meeting = meeting;
        Item = new ActionEditorViewModel(new ActionItem
        {
            Id = item.Id,
            Text = item.Text,
            Owner = item.Owner,
            Due = item.Due,
            IsComplete = item.IsComplete,
            MeetingId = meeting.Id,
            MeetingTitle = meeting.Title
        });
    }

    public Meeting Meeting { get; }
    public ActionEditorViewModel Item { get; }
}

public sealed class ProcessingStepViewModel : ViewModelBase
{
    public ProcessingStepViewModel(int index, string label)
    {
        Index = index;
        Label = label;
    }

    public int Index { get; }
    public string Label { get; }

    private bool _isComplete;
    private bool _isCurrent;

    public bool IsComplete
    {
        get => _isComplete;
        set
        {
            if (!SetProperty(ref _isComplete, value)) return;
            OnPropertyChanged(nameof(State));
        }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (!SetProperty(ref _isCurrent, value)) return;
            OnPropertyChanged(nameof(State));
        }
    }

    public string State => IsComplete ? "Done" : IsCurrent ? "Current" : "Pending";
}

/// <summary>
/// Brings an <see cref="ObservableCollection{T}"/> in line with a new ordered list using
/// moves, inserts and removals instead of Clear + Add, so the list does not rebuild every
/// row. Rows whose display signature changed are replaced in place so they redraw.
/// </summary>
public static class CollectionSync
{
    public static void Apply<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> desired,
        Func<T, string> signature,
        Dictionary<T, string> signatures)
        where T : class
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            var current = signature(item);
            if (index < target.Count && ReferenceEquals(target[index], item))
            {
                RefreshIfChanged(target, index, item, current, signatures);
                continue;
            }

            var existing = -1;
            for (var probe = index + 1; probe < target.Count; probe++)
            {
                if (!ReferenceEquals(target[probe], item)) continue;
                existing = probe;
                break;
            }

            if (existing >= 0)
            {
                target.Move(existing, index);
                RefreshIfChanged(target, index, item, current, signatures);
            }
            else
            {
                target.Insert(index, item);
                signatures[item] = current;
            }
        }

        while (target.Count > desired.Count)
        {
            signatures.Remove(target[^1]);
            target.RemoveAt(target.Count - 1);
        }
    }

    private static void RefreshIfChanged<T>(ObservableCollection<T> target, int index, T item, string current, Dictionary<T, string> signatures)
        where T : class
    {
        if (signatures.TryGetValue(item, out var previous) && previous == current) return;
        signatures[item] = current;
        // Replacing an item with itself raises a Replace change, which redraws only that row.
        target[index] = item;
    }
}

public sealed class MeetingQaTurn
{
    public MeetingQaTurn(string question, MeetingAnswer answer)
    {
        Question = question;
        Answer = answer.Answer;
        Citations = answer.Citations;
    }

    public string Question { get; }
    public string Answer { get; }
    public IReadOnlyList<QaCitation> Citations { get; }
    public bool HasCitations => Citations.Count > 0;
}
