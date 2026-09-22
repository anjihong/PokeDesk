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
            existing.Content = text;
            return;
        }
        var tip = new ToolTip { Content = text };
        RenderOptions.SetTextRenderingMode(tip, TextRenderingMode.Antialias);
        ToolTip.SetTip(control, tip);
    }
}
