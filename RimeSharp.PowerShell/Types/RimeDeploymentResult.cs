namespace RimeSharp.PowerShell;

/// <summary>
/// Identifies the native deployment operation selected by Deploy-Rime.
/// </summary>
public enum RimeDeploymentOperation
{
    Workspace,
    ConfigFile,
}

/// <summary>
/// Describes a completed or failed RIME deployment attempt.
/// </summary>
public sealed class RimeDeploymentResult
{
    public RimeDeploymentOperation Operation { get; }
    public bool Succeeded { get; }
    public bool? NativeOperationSucceeded { get; }
    public bool CleanupSucceeded { get; }
    public string AppName { get; }
    public string SharedDataDir { get; }
    public string UserDataDir { get; }
    public string? DistributionName { get; }
    public string? DistributionCodeName { get; }
    public string? DistributionVersion { get; }
    public int MinLogLevel { get; }
    public string? LogDir { get; }
    public string? PrebuiltDataDir { get; }
    public string? StagingDir { get; }
    public string? ConfigFile { get; }
    public string? VersionKey { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public TimeSpan Duration { get; }
    public RimeNotification[] Notifications { get; }

    internal RimeDeploymentResult(
        RimeDeploymentOperation operation,
        bool? nativeOperationSucceeded,
        bool cleanupSucceeded,
        RimeTraitsDataView traits,
        string? configFile,
        string? versionKey,
        DateTimeOffset startedAtUtc,
        TimeSpan duration,
        RimeNotification[] notifications)
    {
        Operation = operation;
        NativeOperationSucceeded = nativeOperationSucceeded;
        CleanupSucceeded = cleanupSucceeded;
        Succeeded = nativeOperationSucceeded == true && cleanupSucceeded;
        AppName = traits.AppName;
        SharedDataDir = traits.SharedDataDir;
        UserDataDir = traits.UserDataDir;
        DistributionName = traits.DistributionName;
        DistributionCodeName = traits.DistributionCodeName;
        DistributionVersion = traits.DistributionVersion;
        MinLogLevel = traits.MinLogLevel;
        LogDir = traits.LogDir;
        PrebuiltDataDir = traits.PrebuiltDataDir;
        StagingDir = traits.StagingDir;
        ConfigFile = configFile;
        VersionKey = versionKey;
        StartedAtUtc = startedAtUtc;
        Duration = duration;
        Notifications = notifications;
    }
}

internal sealed class RimeTraitsDataView
{
    internal string AppName { get; }
    internal string SharedDataDir { get; }
    internal string UserDataDir { get; }
    internal string? DistributionName { get; }
    internal string? DistributionCodeName { get; }
    internal string? DistributionVersion { get; }
    internal int MinLogLevel { get; }
    internal string? LogDir { get; }
    internal string? PrebuiltDataDir { get; }
    internal string? StagingDir { get; }

    internal RimeTraitsDataView(
        string appName,
        string sharedDataDir,
        string userDataDir,
        string? distributionName,
        string? distributionCodeName,
        string? distributionVersion,
        int minLogLevel,
        string? logDir,
        string? prebuiltDataDir,
        string? stagingDir)
    {
        AppName = appName;
        SharedDataDir = sharedDataDir;
        UserDataDir = userDataDir;
        DistributionName = distributionName;
        DistributionCodeName = distributionCodeName;
        DistributionVersion = distributionVersion;
        MinLogLevel = minLogLevel;
        LogDir = logDir;
        PrebuiltDataDir = prebuiltDataDir;
        StagingDir = stagingDir;
    }
}
