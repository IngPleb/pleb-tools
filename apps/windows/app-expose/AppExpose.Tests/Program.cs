using PlebTools.AppExpose;

var cases = new[]
{
    (Count: 1, Width: 1920d, Height: 1080d, Expected: (Rows: 1, Columns: 1)),
    (Count: 2, Width: 1920d, Height: 1080d, Expected: (Rows: 1, Columns: 2)),
    (Count: 4, Width: 1920d, Height: 1080d, Expected: (Rows: 2, Columns: 2)),
    (Count: 6, Width: 1920d, Height: 1080d, Expected: (Rows: 2, Columns: 3)),
    (Count: 3, Width: 900d, Height: 1600d, Expected: (Rows: 2, Columns: 2)),
};

foreach (var testCase in cases)
{
    var actual = WindowGridLayout.Calculate(testCase.Count, testCase.Width, testCase.Height);
    if (actual != testCase.Expected)
    {
        Console.Error.WriteLine(
            $"FAIL: {testCase.Count} windows at {testCase.Width}x{testCase.Height}: expected {testCase.Expected}, got {actual}");
        return 1;
    }
}

if (WindowGridLayout.Calculate(0, 1920, 1080) != (0, 0))
{
    Console.Error.WriteLine("FAIL: an empty overview should return a 0x0 grid");
    return 1;
}

Console.WriteLine($"PASS: {cases.Length + 1} App Exposé layout checks");

var taskViewCases = new[]
{
    (Aspects: new[] { 16d / 9d }, ExpectedRows: 1),
    (Aspects: new[] { 16d / 9d, 4d / 3d, 1d }, ExpectedRows: 1),
    (Aspects: Enumerable.Repeat(16d / 9d, 7).ToArray(), ExpectedRows: 2),
    (Aspects: Enumerable.Repeat(16d / 9d, 14).ToArray(), ExpectedRows: 3),
};

foreach (var testCase in taskViewCases)
{
    IReadOnlyList<TaskViewRect> layout = TaskViewLayout.Calculate(testCase.Aspects, 1920, 1032);
    if (layout.Count != testCase.Aspects.Length || layout.Any(rect => rect.Width <= 0 || rect.Height <= 29))
    {
        Console.Error.WriteLine("FAIL: Task View layout returned missing or invalid cards");
        return 1;
    }

    int rows = layout.Select(rect => Math.Round(rect.Y, 2)).Distinct().Count();
    if (rows != testCase.ExpectedRows)
    {
        Console.Error.WriteLine($"FAIL: expected {testCase.ExpectedRows} Task View rows, got {rows}");
        return 1;
    }

    if (layout.Any(rect => rect.X < 0 || rect.X + rect.Width > 1920))
    {
        Console.Error.WriteLine("FAIL: Task View card escaped the horizontal viewport");
        return 1;
    }
}

IReadOnlyList<TaskViewRect> compactLayout = TaskViewLayout.Calculate(
    Enumerable.Repeat(16d / 9d, 14).ToArray(),
    1024,
    600);
if (compactLayout.Any(rect => rect.Width <= 0 || rect.Height <= 29))
{
    Console.Error.WriteLine("FAIL: compact Task View layout returned an invalid card");
    return 1;
}

TaskViewRect singleWindow = TaskViewLayout.Calculate([16d / 9d], 1920, 1032)[0];
if (singleWindow.Width < 1100 || singleWindow.Height < 650)
{
    Console.Error.WriteLine("FAIL: a single window did not expand into the available reading area");
    return 1;
}

IReadOnlyList<TaskViewRect> threeWindows = TaskViewLayout.Calculate([1.3, 1, 0.7], 1920, 1032);
double occupiedWidth = threeWindows.Max(rect => rect.X + rect.Width) - threeWindows.Min(rect => rect.X);
if (occupiedWidth < 1650 || threeWindows.Min(rect => rect.Height) < 550)
{
    Console.Error.WriteLine("FAIL: a three-window group left too much usable monitor space empty");
    return 1;
}

Console.WriteLine($"PASS: {taskViewCases.Length + 3} Task View packing checks");
return 0;
