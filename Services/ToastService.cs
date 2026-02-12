namespace BunbunBroll.Services;

/// <summary>
/// Toast notification service for displaying transient messages
/// </summary>
public class ToastService
{
    public event Action<ToastMessage>? OnToast;

    /// <summary>
    /// Show a toast notification
    /// </summary>
    public void Show(string message, ToastType type = ToastType.Info)
    {
        OnToast?.Invoke(new ToastMessage(message, type));
    }

    public void ShowSuccess(string message) => Show(message, ToastType.Success);
    public void ShowError(string message) => Show(message, ToastType.Error);
    public void ShowWarning(string message) => Show(message, ToastType.Warning);
    public void ShowInfo(string message) => Show(message, ToastType.Info);
}

/// <summary>
/// Toast message data
/// </summary>
public record ToastMessage(string Message, ToastType Type);

/// <summary>
/// Toast notification types
/// </summary>
public enum ToastType
{
    Info,
    Success,
    Error,
    Warning
}
