using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace KapeIR.Ui;

/// <summary>Shows <see cref="TextProperty"/> inside an empty TextBox as a dim hint.</summary>
public static class Watermark
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(Watermark),
        new FrameworkPropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject obj) => (string?)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string? value) => obj.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;

        box.Loaded -= OnLoaded;
        box.Loaded += OnLoaded;
        if (box.IsLoaded)
            UpdateAdorner(box);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            UpdateAdorner(box);
    }

    private static void UpdateAdorner(TextBox box)
    {
        var layer = AdornerLayer.GetAdornerLayer(box);
        if (layer is null) return;

        var existing = layer.GetAdorners(box);
        if (existing is not null)
        {
            foreach (var a in existing)
            {
                if (a is WatermarkAdorner)
                    layer.Remove(a);
            }
        }

        var text = GetText(box);
        if (string.IsNullOrEmpty(text)) return;

        var adorner = new WatermarkAdorner(box, text);
        layer.Add(adorner);
    }

    private sealed class WatermarkAdorner : Adorner
    {
        private readonly TextBox _box;
        private readonly string _hint;

        public WatermarkAdorner(TextBox adorned, string hint) : base(adorned)
        {
            _box = adorned;
            _hint = hint;
            IsHitTestVisible = false;
            _box.TextChanged += (_, _) => InvalidateVisual();
            _box.GotKeyboardFocus += (_, _) => InvalidateVisual();
            _box.LostKeyboardFocus += (_, _) => InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (!string.IsNullOrEmpty(_box.Text)) return;

            var brush = TryFindBrush(_box, "Brush.TextSecondary")
                        ?? new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E));
            brush.Freeze();

            var pad = _box.Padding;
            var ft = new FormattedText(
                _hint,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(_box.FontFamily, _box.FontStyle, _box.FontWeight, _box.FontStretch),
                _box.FontSize,
                brush,
                VisualTreeHelper.GetDpi(_box).PixelsPerDip);

            var y = pad.Top + Math.Max(0, (_box.ActualHeight - pad.Top - pad.Bottom - ft.Height) / 2);
            dc.DrawText(ft, new Point(pad.Left + 2, y));
        }

        private static SolidColorBrush? TryFindBrush(FrameworkElement fe, string key)
        {
            var res = fe.TryFindResource(key);
            return res as SolidColorBrush;
        }
    }
}
