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
return 0;
