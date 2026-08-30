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
        _owner = item.Owner;
        _due = item.Due;
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
