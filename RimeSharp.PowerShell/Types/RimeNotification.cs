namespace RimeSharp.PowerShell;

/// <summary>
/// Fully managed notification raised by the native RIME engine.
/// </summary>
public sealed class RimeNotification
{
    public ulong ContextObject { get; }
    public ulong SessionId { get; }
    public RimeSession? Session { get; }
    public string MessageType { get; }
    public string MessageValue { get; }
    public string? OptionName { get; }
    public bool? OptionState { get; }

    internal RimeNotification(
        UIntPtr contextObject,
        UIntPtr sessionId,
        string messageType,
        string messageValue)
    {
        ContextObject = contextObject.ToUInt64();
        SessionId = sessionId.ToUInt64();
        Session = sessionId == UIntPtr.Zero ? null : new RimeSession(sessionId);
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
