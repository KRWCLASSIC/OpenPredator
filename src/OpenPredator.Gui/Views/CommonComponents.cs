using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OpenPredator.Gui;

internal static class CommonComponents
{
    public static Border CreateCard(UIElement content)
    {
        return new Border()
            .Padding(14)
            .WithTheme((theme, border) =>
            {
                border.Background(theme.Palette.ControlBackground);
                border.BorderBrush(theme.Palette.ControlBorder);
                border.BorderThickness(theme.Metrics.ControlBorderThickness);
                border.CornerRadius(theme.Metrics.ControlCornerRadius);
            })
            .Child(content);
    }

    public static UIElement CreateDiagRow(string label, string value)
    {
        return new Grid()
            .Columns("160,*")
            .Rows("Auto")
            .Margin(0, 2, 0, 2)
            .Children(
                new TextBlock().Text(label).FontSize(11),
                new TextBlock().Column(1).Text(value).Bold().FontSize(11)
            );
    }
}
