namespace PlebTools.AppExpose;

/// <summary>Calculates a balanced grid while favoring the monitor's aspect ratio.</summary>
public static class WindowGridLayout
{
    public static (int Rows, int Columns) Calculate(int itemCount, double width, double height)
    {
        if (itemCount <= 0)
        {
            return (0, 0);
        }

        double aspectRatio = width > 0 && height > 0 ? width / height : 16d / 9d;
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(itemCount * aspectRatio)));
        int rows = (int)Math.Ceiling((double)itemCount / columns);

        while (columns > 1 && (columns - 1) * rows >= itemCount)
        {
            columns--;
        }

        return (rows, columns);
    }
}
