using System.Diagnostics;
using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Run one non-coordinating native RIME deployment operation.
/// </summary>
[Cmdlet(
    "Deploy",
    "Rime",
    DefaultParameterSetName = WorkspaceParameterSet,
    SupportsShouldProcess = true,
    ConfirmImpact = ConfirmImpact.Medium)]
[OutputType(typeof(RimeDeploymentResult))]
public sealed class DeployRimeCmdlet : RimeTraitsCmdlet
{
    private const string WorkspaceParameterSet = "Workspace";
    private const string ConfigFileParameterSet = "ConfigFile";

    [Parameter(Mandatory = true, ParameterSetName = WorkspaceParameterSet)]
    public SwitchParameter Workspace { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = ConfigFileParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? ConfigFile { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = ConfigFileParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? VersionKey { get; set; }

    protected override void ProcessRecord()
    {
        var traits = BuildTraits();
        var operation = ParameterSetName == ConfigFileParameterSet
            ? RimeDeploymentOperation.ConfigFile
            : RimeDeploymentOperation.Workspace;
        var configFile = operation == RimeDeploymentOperation.ConfigFile
            ? NormalizeConfigFile(ConfigFile!)
            : null;
        var versionKey = operation == RimeDeploymentOperation.ConfigFile
            ? NormalizeConfigPath(VersionKey!, nameof(VersionKey))
            : null;
        var target = operation == RimeDeploymentOperation.Workspace
            ? $"workspace for {traits.UserDataDir}"
            : $"configuration file {configFile}";

        if (!ShouldProcess(target, "Deploy RIME")) return;

        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        Rime? rime = null;
        RimeNotificationBridgeLifetime? bridge = null;
        bool? nativeOperationSucceeded = null;
        var cleanupSucceeded = true;
        var setupEntered = false;
        Exception? primaryFailure = null;

        using var gate = AcquireNativeGate();
        RimeProcessRuntime.BeginDeployment(this);
        try
        {
            ThrowIfStopping();

            rime = Rime.Instance();
            var nativeTraits = traits.CreateNativeTraits();
            setupEntered = true;
            rime.Setup(ref nativeTraits);
            ThrowIfStopping();

            bridge = RimeNotificationBridgeLifetime.CreateDeployment();
            rime.SetNotificationHandler(bridge.Handler, bridge.ContextObject);
            ThrowIfStopping();

            rime.DeployerInitialize(ref nativeTraits);
            ThrowIfStopping();

            nativeOperationSucceeded = operation == RimeDeploymentOperation.Workspace
                ? rime.Deploy()
                : rime.DeployConfigFile(configFile!, versionKey!);
            ThrowIfStopping();

            if (nativeOperationSucceeded != true)
            {
                primaryFailure = new InvalidOperationException(
                    $"The native {operation} deployment operation failed.");
            }
        }
        catch (Exception ex)
        {
            primaryFailure = ex;
        }
        finally
        {
            if (setupEntered && rime is not null)
            {
                try
                {
                    rime.Finalize1();
                }
                catch (Exception cleanupFailure)
                {
                    cleanupSucceeded = false;
                    if (primaryFailure is null)
                    {
                        primaryFailure = cleanupFailure;
                    }
                    else
                    {
                        primaryFailure.Data["RimeDeploymentCleanupFailure"] = cleanupFailure;
                    }
                }
            }

            if (cleanupSucceeded)
            {
                bridge?.Release();
                RimeProcessRuntime.ResetInactive(invalidateSessions: true);
            }
            else
            {
                RimeProcessRuntime.RetainFaultedLifecycle(rime, bridge);
            }

            stopwatch.Stop();
        }

        if (StopToken.IsCancellationRequested)
        {
            var cancellation = new OperationCanceledException(
                "Deploy-Rime was stopped.",
                StopToken);
            if (primaryFailure is not null)
            {
                cancellation.Data["RimeDeploymentOperationFailure"] = primaryFailure;
            }

            primaryFailure = cancellation;
        }

        var notifications = bridge?.GetDeploymentNotifications()
            ?? Array.Empty<RimeNotification>();
        var result = new RimeDeploymentResult(
            operation,
            nativeOperationSucceeded,
            cleanupSucceeded,
            traits.CreateView(),
            configFile,
            versionKey,
            startedAtUtc,
            stopwatch.Elapsed,
            notifications);

        if (primaryFailure is not null || !result.Succeeded)
        {
            primaryFailure ??= new InvalidOperationException(
                $"The {operation} deployment did not succeed.");
            RimeProcessRuntime.ThrowError(
                this,
                primaryFailure,
                "RimeDeploymentFailed",
                primaryFailure is OperationCanceledException
                    ? ErrorCategory.OperationStopped
                    : ErrorCategory.ResourceUnavailable,
                result);
            return;
        }

        WriteObject(result, enumerateCollection: false);
    }

    internal static string NormalizeConfigFile(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "ConfigFile cannot be empty or whitespace.",
                nameof(ConfigFile));
        }

        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(value)
            || normalized.IndexOf(':') >= 0)
        {
            throw new ArgumentException(
                "ConfigFile must be relative to the RIME data directories.",
                nameof(ConfigFile));
        }

        var segments = normalized.Split(
            new[] { '/' },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment == "." || segment == ".."))
        {
            throw new ArgumentException(
                "ConfigFile cannot contain '.' or '..' path segments.",
                nameof(ConfigFile));
        }

        return string.Join("/", segments);
    }

    internal static string NormalizeConfigPath(string value, string parameterName)
        => RimeConfigPath.Normalize(
            value,
            allowEmpty: false,
            parameterName: parameterName);
}
