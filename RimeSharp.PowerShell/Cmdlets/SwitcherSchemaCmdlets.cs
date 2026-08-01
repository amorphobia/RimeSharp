using System.Management.Automation;

namespace RimeSharp.PowerShell.Cmdlets;

/// <summary>
/// List schemas available to or selected in the RIME switcher.
/// </summary>
[Cmdlet(VerbsCommon.Get, "RimeSwitcherSchema")]
[OutputType(typeof(RimeSwitcherSchemaInfo))]
public sealed class GetRimeSwitcherSchemaCmdlet : RimeCmdlet
{
    private const string AvailableParameterSet = "Available";
    private const string SelectedParameterSet = "Selected";

    [Parameter(Mandatory = true, ParameterSetName = AvailableParameterSet)]
    public SwitchParameter Available { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = SelectedParameterSet)]
    public SwitchParameter Selected { get; set; }

    protected override void ProcessRecord()
    {
        RimeSwitcherSchemaInfo[] result;
        using (AcquireNativeGate())
        {
            RimeProcessRuntime.RequireActive(this);
            ThrowIfStopping();

            try
            {
                using var settings = new RimeSwitcherSettings();
                if (settings.IsInvalid || !settings.LoadSettings())
                {
                    throw new InvalidOperationException(
                        "Failed to load RIME switcher settings.");
                }

                var schemas = ParameterSetName == SelectedParameterSet
                    ? settings.GetSelectedSchemaList()
                    : settings.GetAvailableSchemaList();
                result = new RimeSwitcherSchemaInfo[schemas.Length];
                for (var i = 0; i < schemas.Length; ++i)
                {
                    result[i] = new RimeSwitcherSchemaInfo(
                        schemas[i].SchemaId,
                        schemas[i].Name);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                RimeProcessRuntime.ThrowError(
                    this,
                    ex,
                    "RimeSwitcherSettingsLoadFailed",
                    ErrorCategory.ResourceUnavailable,
                    null);
                return;
            }

            ThrowIfStopping();
        }

        foreach (var schema in result)
        {
            WriteObject(schema, enumerateCollection: false);
        }
    }
}
