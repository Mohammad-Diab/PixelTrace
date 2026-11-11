using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PixelTrace.Desktop.Controls;

public readonly record struct ViewState(double Zoom, Point Center, bool Fitted);

// Insiyab's PictureViewer is modal; this canvas supplies inline image inspection.
// All surrounding controls, surfaces, icons and typography use Insiyab.
public sealed class ImageViewport : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<ImageViewport, Bitmap?>(nameof(Source));
    private double _zoom = 1;
    private Point _center = new(.5, .5);
    private bool _fitted = true;
    private Point? _drag;
    private Point _dragCenter;
    private PixelSize _sourceSize;
    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public event Action<ViewState>? ViewChanged;
    public event Action<int, int>? PixelHovered;
    public ViewState State => new(_zoom, _center, _fitted);
    public string ZoomText => Source is null ? "—" : $"{_zoom * Scaling * 100:0}%";
    private double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    static ImageViewport() => AffectsRender<ImageViewport>(SourceProperty);
    public ImageViewport()
    {
        ClipToBounds = true; Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            var next = change.GetNewValue<Bitmap?>();
            var size = next?.PixelSize ?? default;
            bool changedSize = size != _sourceSize;
            _sourceSize = size;
            if (changedSize) Fit();
        }
        if (change.Property == BoundsProperty && _fitted) Fit();
    }

    private double FitZoom => Source is { } s ? Math.Min(1 / Scaling, Math.Min(Math.Max(1, Bounds.Width - 24) / s.PixelSize.Width, Math.Max(1, Bounds.Height - 24) / s.PixelSize.Height)) : 1;
    public void Fit() { _zoom = FitZoom; _center = new(.5, .5); _fitted = true; Publish(); }
    public void ActualSize() { _zoom = 1 / Scaling; _fitted = false; Clamp(); Publish(); }
    public void Step(int direction) => ZoomTo(_zoom * Math.Pow(1.25, direction), new(Bounds.Width / 2, Bounds.Height / 2));

    private Rect Destination
    {
        get
        {
            if (Source is not { } s) return default;
            double width = s.PixelSize.Width * _zoom, height = s.PixelSize.Height * _zoom;
            return new Rect(Bounds.Width / 2 - _center.X * width, Bounds.Height / 2 - _center.Y * height, width, height);
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        // Transparent hit surface keeps pan and wheel available around a fitted image.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (Source is { } s) context.DrawImage(s, new Rect(s.Size), Destination);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source is null) return;
        ZoomTo(_zoom * Math.Pow(1.25, e.Delta.Y), e.GetPosition(this)); e.Handled = true;
    }

    private void ZoomTo(double zoom, Point anchor)
    {
        if (Source is not { } s) return;
        var dest = Destination;
        double px = (anchor.X - dest.X) / _zoom, py = (anchor.Y - dest.Y) / _zoom;
        _zoom = Math.Clamp(zoom, Math.Min(FitZoom / 4, 1 / Scaling), 16 / Scaling);
        _center = new((px - (anchor.X - Bounds.Width / 2) / _zoom) / s.PixelSize.Width,
            (py - (anchor.Y - Bounds.Height / 2) / _zoom) / s.PixelSize.Height);
        _fitted = false; Clamp(); Publish();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); _drag = e.GetPosition(this); _dragCenter = _center; e.Pointer.Capture(this);
        Cursor = new Cursor(StandardCursorType.SizeAll); e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Source is not { } s) return;
        var at = e.GetPosition(this);
        if (_drag is { } from)
        {
            _center = new(_dragCenter.X - (at.X - from.X) / (_zoom * s.PixelSize.Width), _dragCenter.Y - (at.Y - from.Y) / (_zoom * s.PixelSize.Height));
            _fitted = false; Clamp(); Publish();
        }
        var dest = Destination;
        int x = (int)Math.Floor((at.X - dest.X) / _zoom), y = (int)Math.Floor((at.Y - dest.Y) / _zoom);
        if (x >= 0 && y >= 0 && x < s.PixelSize.Width && y < s.PixelSize.Height) PixelHovered?.Invoke(x, y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e); _drag = null; e.Pointer.Capture(null); Cursor = new Cursor(StandardCursorType.Cross);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e); _drag = null; Cursor = new Cursor(StandardCursorType.Cross);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.D0: case Key.NumPad0: Fit(); break;
            case Key.D1: case Key.NumPad1: ActualSize(); break;
            case Key.Add: case Key.OemPlus: Step(1); break;
            case Key.Subtract: case Key.OemMinus: Step(-1); break;
            case Key.Left: Pan(48, 0); break;
            case Key.Right: Pan(-48, 0); break;
            case Key.Up: Pan(0, 48); break;
            case Key.Down: Pan(0, -48); break;
            default: return;
        }
        e.Handled = true;
    }
    private void Pan(double x, double y)
    {
        if (Source is not { } s) return;
        _center = new(_center.X - x / (_zoom * s.PixelSize.Width), _center.Y - y / (_zoom * s.PixelSize.Height));
        _fitted = false; Clamp(); Publish();
    }
    private void Clamp()
    {
        if (Source is not { } s) return;
        double hx = Bounds.Width / (2 * _zoom * s.PixelSize.Width), hy = Bounds.Height / (2 * _zoom * s.PixelSize.Height);
        _center = new(hx >= .5 ? .5 : Math.Clamp(_center.X, hx, 1 - hx), hy >= .5 ? .5 : Math.Clamp(_center.Y, hy, 1 - hy));
    }
    private void Publish() { InvalidateVisual(); ViewChanged?.Invoke(State); }
}
