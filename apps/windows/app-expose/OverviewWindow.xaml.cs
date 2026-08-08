using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PlebTools.AppExpose;

public partial class OverviewWindow : Window
{
    private const int SwRestore = 9;
    private const uint DwmThumbnailRectDestination = 0x00000001;
    private const uint DwmThumbnailOpacity = 0x00000004;
    private const uint DwmThumbnailVisible = 0x00000008;
    private const int DwmWindowAttributeImmersiveDarkMode = 20;
    private const int DwmWindowAttributeSystemBackdropType = 38;

    private readonly IReadOnlyList<AppWindow> _windows;
    private readonly List<Border> _cards = [];
    private readonly List<FrameworkElement> _previewHosts = [];
    private readonly List<nint> _thumbnails = [];
    private int _columns;
    private int _selectedIndex;
    private bool _isClosing;

    public OverviewWindow(IReadOnlyList<AppWindow> windows, nint focusedWindow)
    {
        _windows = windows;
        _selectedIndex = Math.Max(0, windows.ToList().FindIndex(window => window.Handle == focusedWindow));

        InitializeComponent();
        AppName.Text = windows[0].ProcessName;
        WindowCount.Text = windows.Count == 1 ? "1 open window" : $"{windows.Count} open windows";

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        SizeChanged += (_, _) => QueueThumbnailLayout();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        NativeMethods.RECT monitorRect = NativeMethods.GetMonitorWorkArea(_windows[0].Handle);
        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            monitorRect.Left,
            monitorRect.Top,
            monitorRect.Width,
            monitorRect.Height,
            NativeMethods.SwpShowWindow);

        int enabled = 1;
        NativeMethods.DwmSetWindowAttribute(handle, DwmWindowAttributeImmersiveDarkMode, ref enabled, sizeof(int));
        int transientBackdrop = 3;
        NativeMethods.DwmSetWindowAttribute(handle, DwmWindowAttributeSystemBackdropType, ref transientBackdrop, sizeof(int));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildGrid();
        RegisterThumbnails();
        UpdateSelection();
        Focus();
        QueueThumbnailLayout();
    }

    private void BuildGrid()
    {
        (_columns, int rows) = WindowGridLayout.Calculate(_windows.Count, ActualWidth, ActualHeight);
        WindowGrid.ColumnDefinitions.Clear();
        WindowGrid.RowDefinitions.Clear();

        for (int column = 0; column < _columns; column++)
        {
            WindowGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (int row = 0; row < rows; row++)
        {
            WindowGrid.RowDefinitions.Add(new RowDefinition());
        }

        for (int index = 0; index < _windows.Count; index++)
        {
            int capturedIndex = index;
            var previewHost = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 20)),
                Margin = new Thickness(1),
            };

            var title = new TextBlock
            {
                Text = _windows[index].Title,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(title);
            Grid.SetRow(previewHost, 1);
            content.Children.Add(previewHost);

            var card = new Border
            {
                Margin = new Thickness(10),
                Padding = new Thickness(12, 0, 12, 12),
                CornerRadius = new CornerRadius(10),
                Background = (System.Windows.Media.Brush)FindResource("CardSurface"),
                BorderThickness = new Thickness(2),
                Child = content,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            card.MouseLeftButtonUp += (_, _) => Activate(capturedIndex);
            card.MouseEnter += (_, _) =>
            {
                _selectedIndex = capturedIndex;
                UpdateSelection();
            };

            Grid.SetColumn(card, index % _columns);
            Grid.SetRow(card, index / _columns);
            WindowGrid.Children.Add(card);
            _cards.Add(card);
            _previewHosts.Add(previewHost);
        }
    }

    private void RegisterThumbnails()
    {
        nint destination = new WindowInteropHelper(this).Handle;
        foreach (AppWindow window in _windows)
        {
            int result = NativeMethods.DwmRegisterThumbnail(destination, window.Handle, out nint thumbnail);
            _thumbnails.Add(result == 0 ? thumbnail : nint.Zero);
        }
    }

    private void QueueThumbnailLayout()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, UpdateThumbnailLayout);
    }

    private void UpdateThumbnailLayout()
    {
        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return;
        }

        Matrix toPixels = source.CompositionTarget.TransformToDevice;
        for (int index = 0; index < _thumbnails.Count; index++)
        {
            nint thumbnail = _thumbnails[index];
            FrameworkElement host = _previewHosts[index];
            if (thumbnail == nint.Zero || host.ActualWidth <= 0 || host.ActualHeight <= 0)
            {
                continue;
            }

            System.Windows.Point topLeftDip = host.TranslatePoint(new System.Windows.Point(0, 0), this);
            System.Windows.Point bottomRightDip = host.TranslatePoint(new System.Windows.Point(host.ActualWidth, host.ActualHeight), this);
            System.Windows.Point topLeft = toPixels.Transform(topLeftDip);
            System.Windows.Point bottomRight = toPixels.Transform(bottomRightDip);

            NativeMethods.SIZE sourceSize;
            if (NativeMethods.DwmQueryThumbnailSourceSize(thumbnail, out sourceSize) == 0 &&
                sourceSize.Width > 0 && sourceSize.Height > 0)
            {
                FitInside(ref topLeft, ref bottomRight, sourceSize.Width, sourceSize.Height);
            }

            var properties = new NativeMethods.DWM_THUMBNAIL_PROPERTIES
            {
                Flags = DwmThumbnailRectDestination | DwmThumbnailOpacity | DwmThumbnailVisible,
                Destination = new NativeMethods.RECT(
                    (int)Math.Round(topLeft.X),
                    (int)Math.Round(topLeft.Y),
                    (int)Math.Round(bottomRight.X),
                    (int)Math.Round(bottomRight.Y)),
                Opacity = 255,
                Visible = true,
            };
            NativeMethods.DwmUpdateThumbnailProperties(thumbnail, ref properties);
        }
    }

    private static void FitInside(
        ref System.Windows.Point topLeft,
        ref System.Windows.Point bottomRight,
        int sourceWidth,
        int sourceHeight)
    {
        double availableWidth = bottomRight.X - topLeft.X;
        double availableHeight = bottomRight.Y - topLeft.Y;
        double scale = Math.Min(availableWidth / sourceWidth, availableHeight / sourceHeight);
        double width = sourceWidth * scale;
        double height = sourceHeight * scale;
        double horizontalInset = (availableWidth - width) / 2;
        double verticalInset = (availableHeight - height) / 2;

        topLeft = new System.Windows.Point(topLeft.X + horizontalInset, topLeft.Y + verticalInset);
        bottomRight = new System.Windows.Point(topLeft.X + width, topLeft.Y + height);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        int nextIndex = _selectedIndex;
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                return;
            case Key.Enter:
            case Key.Space:
                Activate(_selectedIndex);
                e.Handled = true;
                return;
            case Key.Left:
                nextIndex--;
                break;
            case Key.Right:
            case Key.Tab when Keyboard.Modifiers != ModifierKeys.Shift:
                nextIndex++;
                break;
            case Key.Up:
                nextIndex -= _columns;
                break;
            case Key.Down:
                nextIndex += _columns;
                break;
            case Key.Tab:
                nextIndex--;
                break;
            default:
                return;
        }

        _selectedIndex = (nextIndex % _windows.Count + _windows.Count) % _windows.Count;
        UpdateSelection();
        e.Handled = true;
    }

    private void UpdateSelection()
    {
        System.Windows.Media.Brush selected = (System.Windows.Media.Brush)FindResource("Selection");
        for (int index = 0; index < _cards.Count; index++)
        {
            _cards[index].BorderBrush = index == _selectedIndex ? selected : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void Activate(int index)
    {
        nint target = _windows[index].Handle;
        Close();
        NativeMethods.ShowWindowAsync(target, SwRestore);
        NativeMethods.SetForegroundWindow(target);
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_isClosing)
        {
            Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        foreach (nint thumbnail in _thumbnails)
        {
            if (thumbnail != nint.Zero)
            {
                NativeMethods.DwmUnregisterThumbnail(thumbnail);
            }
        }

        _thumbnails.Clear();
    }
}
