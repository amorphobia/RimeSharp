using System.Management.Automation;
using System.Threading;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Base class for cmdlets that participate in process-wide native serialization.
/// </summary>
public abstract class RimeCmdlet : PSCmdlet, IDisposable
{
    private readonly CancellationTokenSource _stopSource = new();

    internal CancellationToken StopToken => _stopSource.Token;

    internal RimeNativeGateLease AcquireNativeGate()
        => RimeProcessRuntime.AcquireNativeGate(StopToken);

    internal void ThrowIfStopping()
        => StopToken.ThrowIfCancellationRequested();

    protected override void StopProcessing()
        => _stopSource.Cancel();

    public void Dispose()
        => _stopSource.Dispose();
}

/// <summary>
/// Base class for cmdlets that require one explicit module-owned session.
/// </summary>
public abstract class RimeSessionCmdlet : RimeCmdlet
{
    [Parameter(
        Mandatory = true,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true)]
    [AllowNull]
    public RimeSession? Session { get; set; }
}

internal sealed class RimeNativeGateLease : IDisposable
{
    private SemaphoreSlim? _gate;

    internal RimeNativeGateLease(SemaphoreSlim gate)
    {
        _gate = gate;
    }

    public void Dispose()
    {
        var gate = Interlocked.Exchange(ref _gate, null);
        gate?.Release();
    }
}
