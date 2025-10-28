using System.Runtime.InteropServices;

namespace RimeSharp;

public class RimeCustomSettings : SafeHandle
{
    protected static readonly RimeLevers s_api = RimeLevers.Instance();
    protected RimeCustomSettings(IntPtr ptr) : base(ptr, true) { }
    public RimeCustomSettings(string configId, string generatorId) :
        base(s_api.CustomSettingsInit(configId, generatorId), true)
    { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    public bool LoadSettings() => s_api.LoadSettings(handle);

    public bool SaveSettings() => s_api.SaveSettings(handle);

    protected override bool ReleaseHandle()
    {
        s_api.CustomSettingsDestroy(handle);
        return true;
    }
}

public class RimeSwitcherSettings() : RimeCustomSettings(s_api.SwitcherSettingsInit())
{
    private RimeSchemaListItem[] GetSchemaList(SchemaListAccess access)
    {
        if (!access(handle, out var list)) return [];
        var size = Marshal.SizeOf<RimeSchemaListItem>();
        var items = new RimeSchemaListItem[(int)list.Size];
        for (var i = 0; i < (int)list.Size; ++i)
        {
            var ptr = IntPtr.Add(list.List, i * size);
            items[i] = Marshal.PtrToStructure<RimeSchemaListItem>(ptr);
        }
        s_api.SchemaListDestroy(ref list);
        return items;
    }

    public RimeSchemaListItem[] GetAvailableSchemaList()
        => GetSchemaList(s_api.GetAvailableSchemaList);

    public RimeSchemaListItem[] GetSelectedSchemaList()
        => GetSchemaList(s_api.GetSelectedSchemaList);

    public bool SelectSchemas(string[] schemaIdList)
        => s_api.SelectSchemas(handle, schemaIdList, schemaIdList.Length);
}