using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PlebTools.AppExpose;

internal static class DesktopCapture
{
    public static BitmapSource Capture(NativeMethods.RECT bounds)
    {
        using var bitmap = new System.Drawing.Bitmap(
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height),
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(
                bounds.Left,
                bounds.Top,
                0,
                0,
                bitmap.Size,
                System.Drawing.CopyPixelOperation.SourceCopy);
        }

        nint handle = bitmap.GetHbitmap();
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(handle);
        }
    }
}
