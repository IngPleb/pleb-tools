using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PlebTools.AppExpose;

public partial class OverviewWindow : Window
{
    private const int SwRestore = 9;
    private const uint DwmThumbnailRectDestination = 0x00000001;
    private const uint DwmThumbnailOpacity = 0x00000004;
    private const uint DwmThumbnailVisible = 0x00000008;
    private const int DwmWindowAttributeImmersiveDarkMode = 20;
    private const int DwmWindowAttributeSystemBackdropType = 38;
    private const double CardHeaderHeight = 29;
    private static readonly Duration OpenDuration = TimeSpan.FromMilliseconds(300);
    private static readonly Duration CloseDuration = TimeSpan.FromMilliseconds(240);

    private readonly IReadOnlyList<AppWindow> _windows;
    private readonly NativeMethods.RECT _monitorWorkArea;
    private readonly List<CardState> _cards = [];
    private readonly List<nint> _thumbnails = [];
    private int _columns;
    private int _selectedIndex;
    private bool _closing;
    private bool _closeCommitted;
    private nint _activationTarget;

    public OverviewWindow(IReadOnlyList<AppWindow> windows, nint focusedWindow)
    {
        _windows = windows;
        _monitorWorkArea = NativeMethods.GetMonitorWorkArea(focusedWindow);
        _selectedIndex = Math.Max(0, windows.ToList().FindIndex(window => window.Handle == focusedWindow));

        BitmapSource desktop = DesktopCapture.Capture(_monitorWorkArea);

        InitializeComponent();
        DesktopBackdrop.Source = desktop;

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HwndTopmost,
            _monitorWorkArea.Left,
            _monitorWorkArea.Top,
            _monitorWorkArea.Width,
            _monitorWorkArea.Height,
            NativeMethods.SwpShowWindow);

        int lightMode = 0;
        NativeMethods.DwmSetWindowAttribute(handle, DwmWindowAttributeImmersiveDarkMode, ref lightMode, sizeof(int));
        int transientBackdrop = 3;
        NativeMethods.DwmSetWindowAttribute(handle, DwmWindowAttributeSystemBackdropType, ref transientBackdrop, sizeof(int));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildCards();
        RegisterThumbnails();
        UpdateSelection();
        CompositionTarget.Rendering += OnCompositionRendering;
        Focus();
        BeginOpenAnimation();
    }

    private void BuildCards()
    {
        IReadOnlyList<double> aspects = _windows
            .Select(window => NativeMethods.GetWindowVisualBounds(window.Handle))
            .Select(bounds => bounds.Height > 0 ? Math.Clamp((double)bounds.Width / bounds.Height, 0.55, 2.4) : 16d / 9d)
            .ToList();
        IReadOnlyList<TaskViewRect> targets = TaskViewLayout.Calculate(aspects, ActualWidth, ActualHeight);
        _columns = TaskViewLayout.ColumnCount(_windows.Count);

        for (int index = 0; index < _windows.Count; index++)
        {
            int capturedIndex = index;
            AppWindow appWindow = _windows[index];
            var previewHost = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 24, 24)),
                ClipToBounds = true,
            };

            var icon = new System.Windows.Controls.Image
            {
                Source = WindowIconSource.Create(appWindow.Handle),
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 7, 0),
                Stretch = Stretch.Uniform,
            };
            var title = new TextBlock
            {
                Text = appWindow.Title,
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var titleBar = new Grid { Height = CardHeaderHeight, Margin = new Thickness(8, 0, 8, 0) };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleBar.Children.Add(icon);
            Grid.SetColumn(title, 1);
            titleBar.Children.Add(title);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CardHeaderHeight) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(titleBar);
            Grid.SetRow(previewHost, 1);
            content.Children.Add(previewHost);

            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(244, 246, 247, 249)),
                BorderThickness = new Thickness(3),
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Child = content,
                Cursor = System.Windows.Input.Cursors.Hand,
                SnapsToDevicePixels = true,
            };
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 5,
                Opacity = 0.28,
                Color = System.Windows.Media.Color.FromRgb(30, 46, 63),
            };
            card.MouseLeftButtonUp += (_, _) => Activate(capturedIndex);
            card.MouseEnter += (_, _) =>
            {
                _selectedIndex = capturedIndex;
                UpdateSelection();
            };

            TaskViewRect source = SourceRectToDips(NativeMethods.GetWindowVisualBounds(appWindow.Handle));
            TaskViewRect target = targets[index];
            SetCardRect(card, source);
            WindowCanvas.Children.Add(card);
            _cards.Add(new CardState(card, previewHost, source, target));
        }
    }

    private TaskViewRect SourceRectToDips(NativeMethods.RECT source)
    {
        PresentationSource? presentationSource = PresentationSource.FromVisual(this);
        Matrix fromPixels = presentationSource?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        System.Windows.Point topLeft = fromPixels.Transform(new System.Windows.Point(
            source.Left - _monitorWorkArea.Left,
            source.Top - _monitorWorkArea.Top));
        System.Windows.Point bottomRight = fromPixels.Transform(new System.Windows.Point(
            source.Right - _monitorWorkArea.Left,
            source.Bottom - _monitorWorkArea.Top));

        double width = Math.Max(120, bottomRight.X - topLeft.X);
        double height = Math.Max(90, bottomRight.Y - topLeft.Y);
        return new TaskViewRect(topLeft.X, topLeft.Y, width, height).Clamp(ActualWidth, ActualHeight);
    }

    private static void SetCardRect(FrameworkElement card, TaskViewRect rect)
    {
        Canvas.SetLeft(card, rect.X);
        Canvas.SetTop(card, rect.Y);
        card.Width = rect.Width;
        card.Height = rect.Height;
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

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        UpdateThumbnailLayout();
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
            FrameworkElement host = _cards[index].PreviewHost;
            if (thumbnail == nint.Zero || host.ActualWidth <= 0 || host.ActualHeight <= 0)
            {
                continue;
            }

            System.Windows.Point topLeft = toPixels.Transform(host.TranslatePoint(new System.Windows.Point(0, 0), this));
            System.Windows.Point bottomRight = toPixels.Transform(host.TranslatePoint(new System.Windows.Point(host.ActualWidth, host.ActualHeight), this));
            if (NativeMethods.DwmQueryThumbnailSourceSize(thumbnail, out NativeMethods.SIZE sourceSize) == 0 &&
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

    private void BeginOpenAnimation()
    {
        bool animate = SystemParameters.ClientAreaAnimation;
        Duration duration = animate ? OpenDuration : new Duration(TimeSpan.FromMilliseconds(1));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BackdropLayer.Opacity = 0;
        BackdropLayer.BeginAnimation(OpacityProperty, Animation(0, 1, duration, ease));

        foreach (CardState state in _cards)
        {
            AnimateCard(state, state.Source, state.Target, duration, ease);
        }
    }

    private static DoubleAnimation Animation(
        double from,
        double to,
        Duration duration,
        IEasingFunction easing,
        int delayMilliseconds = 0)
    {
        return new DoubleAnimation(from, to, duration)
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMilliseconds),
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        };
    }

    private static void AnimateCard(
        CardState state,
        TaskViewRect from,
        TaskViewRect to,
        Duration duration,
        IEasingFunction easing)
    {
        state.Card.BeginAnimation(Canvas.LeftProperty, Animation(from.X, to.X, duration, easing));
        state.Card.BeginAnimation(Canvas.TopProperty, Animation(from.Y, to.Y, duration, easing));
        state.Card.BeginAnimation(WidthProperty, Animation(from.Width, to.Width, duration, easing));
        state.Card.BeginAnimation(HeightProperty, Animation(from.Height, to.Height, duration, easing));
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        int nextIndex = _selectedIndex;
        switch (e.Key)
        {
            case Key.Escape:
                BeginClose();
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
        var selected = new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 194, 255));
        for (int index = 0; index < _cards.Count; index++)
        {
            _cards[index].Card.BorderBrush = index == _selectedIndex ? selected : System.Windows.Media.Brushes.Transparent;
        }
    }

    private void Activate(int index)
    {
        _selectedIndex = index;
        UpdateSelection();
        _activationTarget = _windows[index].Handle;
        BeginClose();
    }

    private void BeginClose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        IsHitTestVisible = false;
        bool animate = SystemParameters.ClientAreaAnimation;
        Duration duration = animate ? CloseDuration : new Duration(TimeSpan.FromMilliseconds(1));
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        foreach (CardState state in _cards)
        {
            TaskViewRect current = new(
                Canvas.GetLeft(state.Card),
                Canvas.GetTop(state.Card),
                state.Card.ActualWidth,
                state.Card.ActualHeight);
            AnimateCard(state, current, state.Source, duration, ease);
        }

        DoubleAnimation backgroundFade = Animation(BackdropLayer.Opacity, 0, duration, ease);
        backgroundFade.Completed += (_, _) => CommitClose();
        BackdropLayer.BeginAnimation(OpacityProperty, backgroundFade);
    }

    private void CommitClose()
    {
        _closeCommitted = true;
        Close();
        if (_activationTarget != nint.Zero)
        {
            NativeMethods.ShowWindowAsync(_activationTarget, SwRestore);
            NativeMethods.SetForegroundWindow(_activationTarget);
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        BeginClose();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closeCommitted)
        {
            e.Cancel = true;
            BeginClose();
            return;
        }

        base.OnClosing(e);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnCompositionRendering;
        foreach (nint thumbnail in _thumbnails)
        {
            if (thumbnail != nint.Zero)
            {
                NativeMethods.DwmUnregisterThumbnail(thumbnail);
            }
        }

        _thumbnails.Clear();
    }

    private sealed record CardState(
        Border Card,
        FrameworkElement PreviewHost,
        TaskViewRect Source,
        TaskViewRect Target);
}
