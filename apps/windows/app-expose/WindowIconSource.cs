using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PlebTools.AppExpose;

internal static class WindowIconSource
{
    public static BitmapSource? Create(nint window)
    {
        const uint getIcon = 0x007F;
        const nint smallIcon = 2;
        const int classSmallIcon = -34;

        nint icon = NativeMethods.SendMessage(window, getIcon, smallIcon, nint.Zero);
        if (icon == nint.Zero)
        {
            icon = NativeMethods.GetClassLongPtr(window, classSmallIcon);
        }

        if (icon == nint.Zero)
        {
            return null;
        }

        BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
            icon,
            System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(16, 16));
        source.Freeze();
        return source;
    }
}
