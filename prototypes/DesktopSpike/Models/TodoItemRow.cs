using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DesktopSpike.Models;

public sealed class TodoItemRow : INotifyPropertyChanged
{
    private string _text;
    private string _editText;
    private bool _isCompleted;
    private bool _isEditing;
    private DateTimeOffset? _completedAt;

    public TodoItemRow(string text, DateTimeOffset createdAt)
        : this(Guid.NewGuid(), text, false, createdAt, createdAt, null)
    {
    }

    public TodoItemRow(Guid id, string text, bool isCompleted, DateTimeOffset createdAt, DateTimeOffset updatedAt, DateTimeOffset? completedAt)
    {
        Id = id;
        _text = text;
        _editText = text;
        _isCompleted = isCompleted;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        _completedAt = isCompleted ? completedAt ?? updatedAt : null;
    }

    public Guid Id { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    public string EditText
    {
        get => _editText;
        set => SetField(ref _editText, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetField(ref _isCompleted, value);
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetField(ref _isEditing, value);
    }

    public DateTimeOffset? CompletedAt
    {
        get => _completedAt;
        private set
        {
            if (SetField(ref _completedAt, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedDateLabel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CompletedTimeLabel)));
            }
        }
    }

    public string CreatedDateLabel => $"创建于 {CreatedAt.ToLocalTime():M月d日}";
    public string CompletedDateLabel => CompletedAt?.ToLocalTime().ToString("yyyy年M月d日") ?? "完成时间未知";
    public string CompletedTimeLabel => CompletedAt?.ToLocalTime().ToString("HH:mm") ?? "--:--";

    public void MarkUpdated(DateTimeOffset updatedAt)
    {
        UpdatedAt = updatedAt;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdatedAt)));
    }

    public void MarkCompletion(bool isCompleted, DateTimeOffset? completedAt, DateTimeOffset updatedAt)
    {
        IsCompleted = isCompleted;
        CompletedAt = isCompleted ? completedAt ?? updatedAt : null;
        MarkUpdated(updatedAt);
    }

    public TodoRecord ToRecord() => new(Id, Text, IsCompleted, CreatedAt, UpdatedAt, CompletedAt);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
