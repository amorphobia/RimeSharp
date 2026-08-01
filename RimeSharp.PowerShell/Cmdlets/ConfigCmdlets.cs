using System.Collections.Generic;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// Materialize one immutable managed snapshot from an explicit config shape.
/// </summary>
[Cmdlet(
    VerbsCommon.Get,
    "RimeConfig",
    DefaultParameterSetName = ConfigParameterSet)]
[OutputType(typeof(object))]
public sealed class GetRimeConfigCmdlet : RimeCmdlet
{
    private const string ConfigParameterSet = "Config";
    private const string SchemaParameterSet = "Schema";

    [Parameter(Mandatory = true, ParameterSetName = ConfigParameterSet)]
    public string? ConfigId { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = SchemaParameterSet)]
    public string? SchemaId { get; set; }

    [Parameter]
    [AllowEmptyString]
    public string Path { get; set; } = string.Empty;

    [Parameter(Mandatory = true)]
    [AllowNull]
    public object? Shape { get; set; }

    protected override void ProcessRecord()
    {
        using var gate = AcquireNativeGate();
        var rime = RimeProcessRuntime.RequireActive(this);
        ThrowIfStopping();

        RimeNormalizedConfigShape normalizedShape;
        try
        {
            normalizedShape = RimeConfigShapeNormalizer.Normalize(Shape);
        }
        catch (Exception ex)
        {
            RimeProcessRuntime.ThrowError(
                this,
                ex,
                "RimeConfigShapeInvalid",
                ErrorCategory.InvalidArgument,
                Shape);
            return;
        }

        var normalizedPath = RimeConfigPath.Normalize(
            Path,
            allowEmpty: true,
            parameterName: nameof(Path));
        var identifier = ParameterSetName == SchemaParameterSet
            ? SchemaId
            : ConfigId;
        if (identifier is null || identifier.Length == 0)
        {
            var parameterName = ParameterSetName == SchemaParameterSet
                ? nameof(SchemaId)
                : nameof(ConfigId);
            RimeProcessRuntime.ThrowError(
                this,
                new ArgumentException(
                    "The config identifier cannot be null or empty.",
                    parameterName),
                "RimeConfigMaterializationFailed",
                ErrorCategory.InvalidArgument,
                Shape);
            return;
        }

        RimeConfig config = default;
        var configOpened = false;
        object? snapshot = null;
        RimeConfigReadFailure? readFailure = null;
        Exception? operationFailure = null;
        Exception? closeFailure = null;

        try
        {
            config = ParameterSetName == SchemaParameterSet
                ? rime.SchemaOpen(identifier)
                : rime.ConfigOpen(identifier);
            configOpened = HasNativeConfig(config);
            if (!configOpened)
            {
                throw new InvalidOperationException(
                    $"librime could not open config '{identifier}'.");
            }

            var state = new RimeConfigReadState();
            snapshot = RimeConfigMaterializer.Read(
                config,
                normalizedPath,
                normalizedShape,
                state);
            readFailure = state.Failure;
        }
        catch (Exception ex)
        {
            operationFailure = ex;
        }
        finally
        {
            if (configOpened)
            {
                try
                {
                    config.Dispose();
                }
                catch (Exception ex)
                {
                    closeFailure = ex;
                }
            }
        }

        ThrowIfStopping();
        if (operationFailure is not null
            || readFailure is not null
            || closeFailure is not null)
        {
            ThrowMaterializationError(
                identifier,
                normalizedPath,
                normalizedShape,
                readFailure,
                operationFailure,
                closeFailure);
            return;
        }

        WriteObject(snapshot, enumerateCollection: false);
    }

    private void ThrowMaterializationError(
        string identifier,
        string normalizedPath,
        RimeNormalizedConfigShape rootShape,
        RimeConfigReadFailure? readFailure,
        Exception? operationFailure,
        Exception? closeFailure)
    {
        var failurePath = readFailure?.Path ?? normalizedPath;
        var expectedKind = readFailure?.ExpectedKind ?? rootShape.DisplayName;
        Exception? inner = readFailure?.Exception ?? operationFailure;
        if (inner is not null && closeFailure is not null)
        {
            inner = new AggregateException(inner, closeFailure);
        }
        else
        {
            inner ??= closeFailure;
        }

        var sourceName = ParameterSetName == SchemaParameterSet
            ? "SchemaId"
            : "ConfigId";
        var exception = new InvalidDataException(
            $"Failed to materialize {sourceName} '{identifier}' at path " +
            $"'{failurePath}' as {expectedKind}.",
            inner);
        exception.Data[sourceName] = identifier;
        exception.Data["Path"] = failurePath;
        exception.Data["ExpectedShapeKind"] = expectedKind;

        RimeProcessRuntime.ThrowError(
            this,
            exception,
            "RimeConfigMaterializationFailed",
            ErrorCategory.InvalidData,
            Shape);
    }

    private static bool HasNativeConfig(RimeConfig config)
    {
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<RimeConfig>());
        try
        {
            Marshal.StructureToPtr(config, buffer, fDeleteOld: false);
            return Marshal.ReadIntPtr(buffer) != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

internal static class RimeConfigPath
{
    internal static string Normalize(
        string? value,
        bool allowEmpty,
        string parameterName)
    {
        if (value is null)
        {
            if (allowEmpty) return string.Empty;
            throw new ArgumentNullException(parameterName);
        }

        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "The config path cannot be empty or whitespace.",
                parameterName);
        }

        var normalized = value.Replace('\\', '/').Trim('/');
        if (!allowEmpty && normalized.Length == 0)
        {
            throw new ArgumentException(
                "The config path must contain at least one non-slash character.",
                parameterName);
        }

        return normalized;
    }

    internal static string Combine(string parent, string child)
    {
        if (parent.Length == 0) return child;
        if (child.Length == 0) return parent;
        return parent + "/" + child;
    }
}

internal static class RimeConfigMaterializer
{
    internal static object Read(
        RimeConfig config,
        string path,
        RimeNormalizedConfigShape shape,
        RimeConfigReadState state)
    {
        if (state.Failure is not null)
        {
            return Placeholder(shape);
        }

        try
        {
            return shape.Kind switch
            {
                RimeConfigShapeKind.String =>
                    ReadRequiredScalar(config.GetString(path), path, shape, state),
                RimeConfigShapeKind.Boolean =>
                    ReadRequiredScalar(config.GetBool(path), path, shape, state),
                RimeConfigShapeKind.Integer =>
                    ReadRequiredScalar(config.GetInt(path), path, shape, state),
                RimeConfigShapeKind.Double =>
                    ReadRequiredScalar(config.GetDouble(path), path, shape, state),
                RimeConfigShapeKind.FixedMap =>
                    ReadFixedMap(config, path, shape, state),
                RimeConfigShapeKind.List =>
                    ReadList(config, path, shape.ValueShape!, state),
                RimeConfigShapeKind.MapOf =>
                    ReadDynamicMap(config, path, shape.ValueShape!, state),
                _ => throw new InvalidOperationException(
                    $"Unsupported normalized config shape kind: {shape.Kind}."),
            };
        }
        catch (Exception ex)
        {
            state.Record(path, shape.DisplayName, ex);
            return Placeholder(shape);
        }
    }

    private static object ReadRequiredScalar<T>(
        T? value,
        string path,
        RimeNormalizedConfigShape shape,
        RimeConfigReadState state)
    {
        if (value is not null) return value;

        state.Record(
            path,
            shape.DisplayName,
            new InvalidDataException(
                $"The required {shape.DisplayName} scalar is missing or has the wrong type."));
        return Placeholder(shape);
    }

    private static object ReadFixedMap(
        RimeConfig config,
        string path,
        RimeNormalizedConfigShape shape,
        RimeConfigReadState state)
    {
        var result = new PSObject();
        foreach (var member in shape.Members)
        {
            var value = Read(
                config,
                RimeConfigPath.Combine(path, member.Name),
                member.Shape,
                state);
            result.Properties.Add(new PSNoteProperty(member.Name, value));
        }

        return result;
    }

    private static object[] ReadList(
        RimeConfig config,
        string path,
        RimeNormalizedConfigShape elementShape,
        RimeConfigReadState state)
    {
        var values = config.GetList<RimeConfigReadValue>(
            path,
            (currentConfig, itemPath) => new RimeConfigReadValue(
                Read(currentConfig, itemPath, elementShape, state)));
        var result = new object[values.Length];
        for (var i = 0; i < values.Length; ++i)
        {
            if (values[i] is null)
            {
                state.Record(
                    path,
                    elementShape.DisplayName,
                    new InvalidDataException(
                        "librime returned an incomplete list iteration."));
                result[i] = Placeholder(elementShape);
            }
            else
            {
                result[i] = values[i].Value;
            }
        }

        return result;
    }

    private static Dictionary<string, object> ReadDynamicMap(
        RimeConfig config,
        string path,
        RimeNormalizedConfigShape valueShape,
        RimeConfigReadState state)
    {
        var values = config.GetMap<RimeConfigReadValue>(
            path,
            (currentConfig, itemPath) => new RimeConfigReadValue(
                Read(currentConfig, itemPath, valueShape, state)));
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var entry in values)
        {
            if (entry.Value is null)
            {
                state.Record(
                    path,
                    valueShape.DisplayName,
                    new InvalidDataException(
                        "librime returned an incomplete map iteration."));
                result.Add(entry.Key, Placeholder(valueShape));
            }
            else
            {
                result.Add(entry.Key, entry.Value.Value);
            }
        }

        return result;
    }

    private static object Placeholder(RimeNormalizedConfigShape shape)
        => shape.Kind switch
        {
            RimeConfigShapeKind.String => string.Empty,
            RimeConfigShapeKind.Boolean => false,
            RimeConfigShapeKind.Integer => 0,
            RimeConfigShapeKind.Double => 0d,
            RimeConfigShapeKind.FixedMap => new PSObject(),
            RimeConfigShapeKind.List => Array.Empty<object>(),
            RimeConfigShapeKind.MapOf =>
                new Dictionary<string, object>(StringComparer.Ordinal),
            _ => new object(),
        };
}

internal sealed class RimeConfigReadValue
{
    internal object Value { get; }

    internal RimeConfigReadValue(object value)
    {
        Value = value;
    }
}

internal sealed class RimeConfigReadState
{
    internal RimeConfigReadFailure? Failure { get; private set; }

    internal void Record(string path, string expectedKind, Exception exception)
    {
        Failure ??= new RimeConfigReadFailure(path, expectedKind, exception);
    }
}

internal sealed class RimeConfigReadFailure
{
    internal string Path { get; }
    internal string ExpectedKind { get; }
    internal Exception Exception { get; }

    internal RimeConfigReadFailure(
        string path,
        string expectedKind,
        Exception exception)
    {
        Path = path;
        ExpectedKind = expectedKind;
        Exception = exception;
    }
}
