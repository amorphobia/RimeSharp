using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

public abstract class RimeTraitsCmdlet : RimeCmdlet
{
    [Parameter]
    [AllowEmptyString]
    public string AppName { get; set; } = "RimeSharp.PowerShell";

    [Parameter]
    [AllowEmptyString]
    public string SharedDataDir { get; set; } = "shared";

    [Parameter]
    [AllowEmptyString]
    public string UserDataDir { get; set; } = "user";

    [Parameter]
    [AllowEmptyString]
    public string? DistributionName { get; set; }

    [Parameter]
    [AllowEmptyString]
    public string? DistributionCodeName { get; set; }

    [Parameter]
    [AllowEmptyString]
    public string? DistributionVersion { get; set; }

    [Parameter]
    [ValidateRange(0, 3)]
    public int MinLogLevel { get; set; }

    [Parameter]
    [AllowEmptyString]
    public string? LogDir { get; set; }

    [Parameter]
    [AllowEmptyString]
    public string? PrebuiltDataDir { get; set; }

    [Parameter]
    [AllowEmptyString]
    public string? StagingDir { get; set; }

    internal RimeTraitsData BuildTraits()
        => RimeTraitsData.Create(
            this,
            AppName,
            SharedDataDir,
            UserDataDir,
            DistributionName,
            DistributionCodeName,
            DistributionVersion,
            MinLogLevel,
            LogDir,
            PrebuiltDataDir,
            StagingDir);
}

internal sealed class RimeTraitsData
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

    private RimeTraitsData(
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

    internal static RimeTraitsData Create(
        PSCmdlet cmdlet,
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
        ValidateRequiredText(appName, nameof(appName));
        ValidateOptionalText(distributionName, nameof(distributionName));
        ValidateOptionalText(distributionCodeName, nameof(distributionCodeName));
        ValidateOptionalText(distributionVersion, nameof(distributionVersion));

        return new RimeTraitsData(
            appName,
            ResolveRequiredFileSystemPath(cmdlet, sharedDataDir, nameof(sharedDataDir)),
            ResolveRequiredFileSystemPath(cmdlet, userDataDir, nameof(userDataDir)),
            distributionName,
            distributionCodeName,
            distributionVersion,
            minLogLevel,
            ResolveFileSystemPath(cmdlet, logDir, nameof(logDir), true),
            ResolveFileSystemPath(cmdlet, prebuiltDataDir, nameof(prebuiltDataDir), false),
            ResolveFileSystemPath(cmdlet, stagingDir, nameof(stagingDir), false));
    }

    private static string ResolveRequiredFileSystemPath(
        PSCmdlet cmdlet,
        string? path,
        string parameterName)
    {
        if (path is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        return ResolveFileSystemPath(
            cmdlet,
            path,
            parameterName,
            preserveEmpty: false)!;
    }

    internal RimeTraits CreateNativeTraits()
    {
        var traits = new RimeTraits
        {
            AppName = AppName,
            SharedDataDir = SharedDataDir,
            UserDataDir = UserDataDir,
            MinLogLevel = MinLogLevel,
        };

        if (DistributionName is not null) traits.DistributionName = DistributionName;
        if (DistributionCodeName is not null) traits.DistributionCodeName = DistributionCodeName;
        if (DistributionVersion is not null) traits.DistributionVersion = DistributionVersion;
        if (LogDir is not null) traits.LogDir = LogDir;
        if (PrebuiltDataDir is not null) traits.PrebuiltDataDir = PrebuiltDataDir;
        if (StagingDir is not null) traits.StagingDir = StagingDir;
        return traits;
    }

    internal RimeTraitsDataView CreateView()
        => new(
            AppName,
            SharedDataDir,
            UserDataDir,
            DistributionName,
            DistributionCodeName,
            DistributionVersion,
            MinLogLevel,
            LogDir,
            PrebuiltDataDir,
            StagingDir);

    private static void ValidateRequiredText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The value cannot be empty or whitespace.",
                parameterName);
        }
    }

    private static void ValidateOptionalText(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A supplied value cannot be empty or whitespace.",
                parameterName);
        }
    }

    private static string? ResolveFileSystemPath(
        PSCmdlet cmdlet,
        string? path,
        string parameterName,
        bool preserveEmpty)
    {
        if (path is null) return null;
        if (path.Length == 0 && preserveEmpty) return path;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "The value must be a non-empty FileSystem path.",
                parameterName);
        }

        ProviderInfo provider;
        PSDriveInfo drive;
        var resolved = cmdlet.SessionState.Path.GetUnresolvedProviderPathFromPSPath(
            path,
            out provider,
            out drive);
        if (!string.Equals(provider.Name, "FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The value must be a FileSystem path.",
                parameterName);
        }

        return resolved;
    }
}
