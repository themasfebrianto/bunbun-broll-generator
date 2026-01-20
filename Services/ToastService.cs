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
