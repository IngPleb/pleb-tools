using System.Runtime.InteropServices;

namespace PlebTools.AppExpose;

/// <summary>Reads the shell identity Windows uses to distinguish packaged apps and app modes.</summary>
internal static class WindowIdentity
{
    private const ushort VariantWideString = 31;
    private static readonly Guid PropertyStoreInterfaceId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey AppUserModelId = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        5);

    public static string? GetAppUserModelId(nint window)
    {
        IPropertyStore? store = null;
        PropVariant value = default;

        try
        {
            Guid interfaceId = PropertyStoreInterfaceId;
            int result = NativeMethods.SHGetPropertyStoreForWindow(window, ref interfaceId, out store);
            if (result != 0 || store is null)
            {
                return null;
            }

            PropertyKey key = AppUserModelId;
            store.GetValue(ref key, out value);
            return value.Type == VariantWideString && value.Pointer != nint.Zero
                ? Marshal.PtrToStringUni(value.Pointer)
                : null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (value.Type != 0)
            {
                NativeMethods.PropVariantClear(ref value);
            }

            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        uint GetCount();
        PropertyKey GetAt(uint propertyIndex);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
}
