using System.Windows;
using System.Windows.Controls;

namespace PixelForge.Desktop;

// Equal-width cards that reflow without shrinking labels or numeric controls.
public sealed class ResponsivePanel : Panel
{
    public static readonly DependencyProperty MinimumItemWidthProperty = DependencyProperty.Register(
        nameof(MinimumItemWidth), typeof(double), typeof(ResponsivePanel),
        new FrameworkPropertyMetadata(168d, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => double.IsFinite((double)value) && (double)value > 0);
    public double MinimumItemWidth { get => (double)GetValue(MinimumItemWidthProperty); set => SetValue(MinimumItemWidthProperty, value); }
    private const double Gap = 12;
    private UIElement[] Cards => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToArray();
    private int Columns(double width, int count) => Math.Clamp((int)Math.Floor((width + Gap) / (MinimumItemWidth + Gap)), 1, Math.Max(1, count));

    protected override Size MeasureOverride(Size availableSize)
    {
        var cards = Cards;
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : MinimumItemWidth;
        int columns = Columns(width, cards.Length);
        double cellWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns), height = 0;
        for (int row = 0; row < cards.Length; row += columns)
        {
            double rowHeight = 0;
            for (int n = row; n < Math.Min(row + columns, cards.Length); n++)
            {
                cards[n].Measure(new Size(cellWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, cards[n].DesiredSize.Height);
            }
            height += rowHeight + (row == 0 ? 0 : Gap);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var cards = Cards;
        int columns = Columns(finalSize.Width, cards.Length);
        double cellWidth = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns), y = 0;
        for (int row = 0; row < cards.Length; row += columns)
        {
            double height = cards.Skip(row).Take(columns).Max(c => c.DesiredSize.Height);
            for (int n = row; n < Math.Min(row + columns, cards.Length); n++)
                cards[n].Arrange(new Rect((n - row) * (cellWidth + Gap), y, cellWidth, height));
            y += height + Gap;
        }
        return finalSize;
    }
}
