using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;

namespace DeskPokemon;

internal static class UiToolTips
{
    public static void Set(Control control, string text)
    {
        // Popups have a separate visual root and do not inherit window render options.
        if (ToolTip.GetTip(control) is ToolTip existing)
        {
            if (existing.Content is TooltipText label) label.Text = text;
            else existing.Content = new TooltipText { Text = text };
            return;
        }
        var tip = new ToolTip { Content = new TooltipText { Text = text } };
        RenderOptions.SetTextRenderingMode(tip, TextRenderingMode.Antialias);
        ToolTip.SetTip(control, tip);
    }
}

// 같은 내장 폰트라도 CoreText/DirectWrite의 힌팅은 작은 툴팁의 획 두께를 다르게 만든다.
// TextBlock은 측정에 사용하고, 같은 텍스트의 윤곽선을 직접 그린다.
internal sealed class TooltipText : Decorator
{
    private readonly TextBlock _label = new() { Opacity = 0 };
    private (string? Text, Typeface Typeface, double Size, FlowDirection Flow)? _key;
    private Geometry? _geometry;

    public TooltipText() => Child = _label;

    public string? Text
    {
        get => _label.Text;
        set
        {
            _label.Text = value;
            AutomationProperties.SetName(this, value ?? "");
            InvalidateVisual();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TooltipTextPeer(this);

    public override void Render(DrawingContext context)
    {
        var key = (Text, new Typeface(_label.FontFamily, _label.FontStyle, _label.FontWeight, _label.FontStretch),
            _label.FontSize, _label.FlowDirection);
        if (_key != key)
        {
            var formatted = new FormattedText(Text ?? "", CultureInfo.CurrentCulture, _label.FlowDirection,
                key.Item2, _label.FontSize, Brushes.White);
            _geometry = formatted.BuildGeometry(default);
            _key = key;
        }
        if (_geometry != null) context.DrawGeometry(_label.Foreground, null, _geometry);
    }

    private sealed class TooltipTextPeer(TooltipText owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => null;
    }
}
