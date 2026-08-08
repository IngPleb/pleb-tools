namespace PlebTools.AppExpose;

/// <summary>Task View-style row packing that preserves each window's visible aspect ratio.</summary>
public static class TaskViewLayout
{
    private const double HeaderHeight = 29;
    private const double CardGap = 16;

    /// <summary>Returns the number of keyboard-navigation columns for a packed window count.</summary>
    /// <param name="count">Number of windows in the application group.</param>
    /// <returns>The column count used by left, right, up, and down navigation.</returns>
    public static int ColumnCount(int count)
    {
        if (count <= 4)
        {
            return Math.Max(1, count);
        }

        int rows = count <= 10 ? 2 : 3;
        return (int)Math.Ceiling((double)count / rows);
    }

    /// <summary>Calculates centered Task View card bounds for one application group.</summary>
    /// <param name="aspectRatios">Visible width-to-height ratio of every source window.</param>
    /// <param name="width">Available overlay width in device-independent pixels.</param>
    /// <param name="height">Available overlay height in device-independent pixels.</param>
    /// <returns>One card rectangle for each input ratio, in the same order.</returns>
    public static IReadOnlyList<TaskViewRect> Calculate(
        IReadOnlyList<double> aspectRatios,
        double width,
        double height)
    {
        if (aspectRatios.Count == 0)
        {
            return [];
        }

        int rows = aspectRatios.Count <= 4 ? 1 : aspectRatios.Count <= 10 ? 2 : 3;
        int baseCount = aspectRatios.Count / rows;
        int remainder = aspectRatios.Count % rows;
        double usableWidth = width * (aspectRatios.Count == 1 ? 0.70 : 0.92);
        double areaTop = Math.Max(24, height * 0.035);
        double areaBottom = height - Math.Max(24, height * 0.035);
        double availableHeight = Math.Max(160, areaBottom - areaTop);
        double heightUtilization = aspectRatios.Count switch
        {
            1 => 0.78,
            <= 4 => 0.74,
            _ => 1,
        };
        double maximumPreviewHeight = Math.Max(
            86,
            (availableHeight * heightUtilization - CardGap * (rows - 1)) / rows - HeaderHeight);

        var rowLayouts = new List<RowLayout>();
        int offset = 0;
        for (int row = 0; row < rows; row++)
        {
            int count = baseCount + (row < remainder ? 1 : 0);
            IReadOnlyList<double> rowAspects = aspectRatios.Skip(offset).Take(count).ToList();
            double gaps = CardGap * Math.Max(0, count - 1);
            double scaleToWidth = (usableWidth - gaps) / rowAspects.Sum();
            double previewHeight = Math.Clamp(Math.Min(maximumPreviewHeight, scaleToWidth), 86, maximumPreviewHeight);
            IReadOnlyList<double> cardWidths = rowAspects.Select(aspect => aspect * previewHeight).ToList();
            rowLayouts.Add(new RowLayout(offset, previewHeight + HeaderHeight, cardWidths));
            offset += count;
        }

        double blockHeight = rowLayouts.Sum(row => row.Height) + CardGap * Math.Max(0, rows - 1);
        double y = areaTop + Math.Max(0, (availableHeight - blockHeight) / 2);
        var result = new TaskViewRect[aspectRatios.Count];
        foreach (RowLayout row in rowLayouts)
        {
            double rowWidth = row.Widths.Sum() + CardGap * Math.Max(0, row.Widths.Count - 1);
            double x = (width - rowWidth) / 2;
            for (int item = 0; item < row.Widths.Count; item++)
            {
                result[row.Offset + item] = new TaskViewRect(x, y, row.Widths[item], row.Height);
                x += row.Widths[item] + CardGap;
            }

            y += row.Height + CardGap;
        }

        return result;
    }

    private sealed record RowLayout(int Offset, double Height, IReadOnlyList<double> Widths);
}

public readonly record struct TaskViewRect(double X, double Y, double Width, double Height)
{
    /// <summary>Keeps enough of a source-position card visible to animate into the overlay.</summary>
    /// <param name="availableWidth">Overlay width in device-independent pixels.</param>
    /// <param name="availableHeight">Overlay height in device-independent pixels.</param>
    /// <returns>A rectangle constrained to the overlay's animation region.</returns>
    public TaskViewRect Clamp(double availableWidth, double availableHeight)
    {
        double width = Math.Min(Width, availableWidth);
        double height = Math.Min(Height, availableHeight);
        return new TaskViewRect(
            Math.Clamp(X, -width * 0.85, availableWidth - width * 0.15),
            Math.Clamp(Y, -height * 0.85, availableHeight - height * 0.15),
            width,
            height);
    }
}
