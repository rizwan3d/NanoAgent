using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NanoAgent.Desktop.Models;

public sealed partial class DesktopTextPrompt : ObservableObject
{
    private readonly Action _onCancelled;
    private readonly Action<string> _onSubmitted;
    private bool _isResolved;

    [ObservableProperty]
    private string _value;

    public DesktopTextPrompt(
        string label,
        string? description,
        string? defaultValue,
        bool allowCancellation,
        bool isSecret,
        Action<string> onSubmitted,
        Action onCancelled)
    {
        ArgumentNullException.ThrowIfNull(onSubmitted);
        ArgumentNullException.ThrowIfNull(onCancelled);

        Title = string.IsNullOrWhiteSpace(label) ? "Prompt" : label.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Value = defaultValue ?? string.Empty;
        AllowCancellation = allowCancellation;
        IsSecret = isSecret;
        _onSubmitted = onSubmitted;
        _onCancelled = onCancelled;

        SubmitCommand = new RelayCommand(Submit, () => !_isResolved);
        CancelCommand = new RelayCommand(Cancel, () => AllowCancellation && !_isResolved);
    }

    public bool AllowCancellation { get; }

    public IRelayCommand CancelCommand { get; }

    public string? Description { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool IsSecret { get; }

    public char PasswordChar => IsSecret ? '*' : '\0';

    public IRelayCommand SubmitCommand { get; }

    public string Title { get; }

    public event EventHandler? Dismissed;

    public void Dismiss()
    {
        if (_isResolved)
        {
            return;
        }

        _isResolved = true;
        NotifyCommandStatesChanged();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel()
    {
        if (_isResolved)
        {
            return;
        }

        _isResolved = true;
        NotifyCommandStatesChanged();
        _onCancelled();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyCommandStatesChanged()
    {
        SubmitCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void Submit()
    {
        if (_isResolved)
        {
            return;
        }

        _isResolved = true;
        NotifyCommandStatesChanged();
        _onSubmitted(Value);
        Dismissed?.Invoke(this, EventArgs.Empty);
    }
}
