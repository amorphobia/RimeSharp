using System.Runtime.InteropServices;

namespace RimeSharp
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint CustomSettingsInit(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string configId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string generatorId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint SwitcherSettingsInit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void CustomSettingsDestroy(nint ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool CustomSettingsAccess(nint ptr);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool SchemaListAccess(nint ptr, out RimeSchemaList list);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SchemaListDestroy(ref RimeSchemaList list);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate bool SelectSchemas(nint ptr,
        [In] string[] schemaIdList, int count);

    public sealed class RimeLevers
    {
        private static readonly Lazy<RimeLevers> s_instance = new(() => new RimeLevers());

        private readonly RimeLeversAPI _levers;

        private RimeLevers()
        {
            var rime = Rime.Instance();
            var module = rime.FindModule("levers");
            var apiPtr = module.GetAPI();
            _levers = Marshal.PtrToStructure<RimeLeversAPI>(apiPtr);
        }

        public static RimeLevers Instance() => s_instance.Value;

        internal IntPtr CustomSettingsInit(string configId, string generatorId) =>
            _levers.CustomSettingsInit(configId, generatorId);

        internal void CustomSettingsDestroy(IntPtr ptr) => _levers.CustomSettingsDestroy(ptr);

        internal bool LoadSettings(IntPtr ptr) => _levers.LoadSettings(ptr);

        internal bool SaveSettings(IntPtr ptr) => _levers.SaveSettings(ptr);

        internal IntPtr SwitcherSettingsInit() => _levers.SwitcherSettingsInit();

        internal bool GetAvailableSchemaList(IntPtr ptr, out RimeSchemaList list)
            => _levers.GetAvailableSchemaList(ptr, out list);
        
        internal bool GetSelectedSchemaList(IntPtr ptr, out RimeSchemaList list)
            => _levers.GetSelectedSchemaList(ptr, out list);
        
        internal void SchemaListDestroy(ref RimeSchemaList list)
            => _levers.SchemaListDestroy(ref list);

        internal bool SelectSchemas(IntPtr ptr, in string[] schemaIdList, int count)
            => _levers.SelectSchemas(ptr, schemaIdList, count);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RimeLeversAPI
    {
        private readonly int _dataSize;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public CustomSettingsInit CustomSettingsInit;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public CustomSettingsDestroy CustomSettingsDestroy;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public CustomSettingsAccess LoadSettings;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public CustomSettingsAccess SaveSettings;
        public IntPtr CustomizeBool;
        public IntPtr CustomizeInt;
        public IntPtr CustomizeDouble;
        public IntPtr CustomizeString;
        public IntPtr IsFirstRun;
        public IntPtr SettingsIsModified;
        public IntPtr SettingsGetConfig;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public SwitcherSettingsInit SwitcherSettingsInit;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public SchemaListAccess GetAvailableSchemaList;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public SchemaListAccess GetSelectedSchemaList;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public SchemaListDestroy SchemaListDestroy;
        public IntPtr GetSchemaId;
        public IntPtr GetSchemaName;
        public IntPtr GetSchemaVersion;
        public IntPtr GetSchemaAuthor;
        public IntPtr GetSchemaDescription;
        public IntPtr GetSchemaFilePath;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public SelectSchemas SelectSchemas;
        public IntPtr GetHotKeys;
        public IntPtr SetHotKeys;
        public IntPtr UserDictIteratorInit;
        public IntPtr UserDictIteratorDestroy;
        public IntPtr NextUserDict;
        public IntPtr BackupUserDict;
        public IntPtr RestoreUserDict;
        public IntPtr ExportUserDict;
        public IntPtr ImportUserDict;
        public IntPtr CustomizeItem;
    }
}
