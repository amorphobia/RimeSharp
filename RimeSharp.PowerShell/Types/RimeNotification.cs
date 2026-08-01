namespace RimeSharp.PowerShell;

/// <summary>
/// Fully managed notification raised by the native RIME engine.
/// </summary>
public sealed class RimeNotification
{
    public ulong SessionId { get; }
    public string MessageType { get; }
    public string MessageValue { get; }
    public string? OptionName { get; }
    public bool? OptionState { get; }

    internal RimeNotification(
        ulong sessionId,
        string messageType,
        string messageValue)
    {
        SessionId = sessionId;
        MessageType = messageType;
        MessageValue = messageValue;

        if (string.Equals(messageType, "option", StringComparison.Ordinal)
            && !string.IsNullOrEmpty(messageValue))
        {
            OptionState = messageValue[0] != '!';
            OptionName = OptionState.Value ? messageValue : messageValue.Substring(1);
        }
    }
}
