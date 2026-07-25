using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

internal static class SessionValidation
{
    internal static void EnsureSessionValid(
        PSCmdlet cmdlet,
        Rime rime,
        RimeSession session)
    {
        if (rime.FindSession(session.Id)) return;

        var ex = new ArgumentException(
            $"Session {session.Id} is invalid or already destroyed.", nameof(session));
        cmdlet.ThrowTerminatingError(new ErrorRecord(ex, "RimeSessionInvalid",
            ErrorCategory.InvalidArgument, session));
    }
}
