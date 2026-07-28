using Godot;

namespace TDBank.TDBankCode.UI;

internal static class BankUiTheme
{
    public static readonly Color Green = Color.FromHtml("#007A33");
    public static readonly Color GreenDark = Color.FromHtml("#064C2C");
    public static readonly Color GreenDeep = Color.FromHtml("#052E20");
    public static readonly Color GreenSoft = Color.FromHtml("#DFF3E8");
    public static readonly Color Cream = Color.FromHtml("#F7F1E3");
    public static readonly Color Ink = Color.FromHtml("#17251F");
    public static readonly Color Muted = Color.FromHtml("#63756C");
    public static readonly Color Gold = Color.FromHtml("#F2C14E");
    public static readonly Color Red = Color.FromHtml("#D95050");
    public static readonly Color White = Colors.White;

    public static StyleBoxFlat Panel(
        Color color,
        int radius = 16,
        Color? border = null,
        int borderWidth = 0,
        int contentMargin = 0)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius,
            BorderColor = border ?? Colors.Transparent,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            ContentMarginLeft = contentMargin,
            ContentMarginTop = contentMargin,
            ContentMarginRight = contentMargin,
            ContentMarginBottom = contentMargin,
        };
        return style;
    }

    public static void ApplyPrimaryButton(Button button)
    {
        button.AddThemeStyleboxOverride("normal", Panel(Green, 10, GreenDark, 2, 10));
        button.AddThemeStyleboxOverride("hover", Panel(Color.FromHtml("#079447"), 10, GreenSoft, 2, 10));
        button.AddThemeStyleboxOverride("pressed", Panel(GreenDark, 10, GreenSoft, 2, 10));
        button.AddThemeStyleboxOverride("disabled", Panel(Color.FromHtml("#66746D"), 10, Color.FromHtml("#7E8B85"), 1, 10));
        button.AddThemeColorOverride("font_color", White);
        button.AddThemeColorOverride("font_hover_color", White);
        button.AddThemeColorOverride("font_pressed_color", White);
        button.AddThemeColorOverride("font_disabled_color", Color.FromHtml("#C8D0CC"));
        button.AddThemeFontSizeOverride("font_size", 21);
        button.CustomMinimumSize = new Vector2(0, 52);
    }

    public static void ApplySecondaryButton(Button button)
    {
        button.AddThemeStyleboxOverride("normal", Panel(Cream, 10, Green, 2, 9));
        button.AddThemeStyleboxOverride("hover", Panel(GreenSoft, 10, Green, 2, 9));
        button.AddThemeStyleboxOverride("pressed", Panel(Color.FromHtml("#C8E8D7"), 10, GreenDark, 2, 9));
        button.AddThemeStyleboxOverride("disabled", Panel(Color.FromHtml("#D8DDD9"), 10, Color.FromHtml("#AAB4AF"), 1, 9));
        button.AddThemeColorOverride("font_color", Ink);
        button.AddThemeColorOverride("font_hover_color", GreenDark);
        button.AddThemeColorOverride("font_pressed_color", GreenDeep);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.AddThemeFontSizeOverride("font_size", 19);
        button.CustomMinimumSize = new Vector2(0, 48);
    }

    public static void ApplyTabButton(Button button, bool selected)
    {
        var normal = selected ? Green : Colors.Transparent;
        var border = selected ? GreenSoft : Color.FromHtml("#35624E");
        button.AddThemeStyleboxOverride("normal", Panel(normal, 10, border, selected ? 2 : 1, 10));
        button.AddThemeStyleboxOverride("hover", Panel(Color.FromHtml("#176342"), 10, GreenSoft, 1, 10));
        button.AddThemeStyleboxOverride("pressed", Panel(Green, 10, GreenSoft, 2, 10));
        button.AddThemeColorOverride("font_color", White);
        button.AddThemeColorOverride("font_hover_color", White);
        button.AddThemeColorOverride("font_pressed_color", White);
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Alignment = HorizontalAlignment.Left;
        button.CustomMinimumSize = new Vector2(230, 58);
    }

    public static Label Label(string text, int fontSize = 20, Color? color = null)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color ?? Ink);
        return label;
    }

    public static Label Heading(string text, int fontSize = 34)
    {
        var label = Label(text, fontSize, Ink);
        label.AddThemeColorOverride("font_shadow_color", new Color(1, 1, 1, 0.55f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }
}

