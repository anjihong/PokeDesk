using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;

namespace DeskPokemon;

/// <summary>첫 실행 스타팅 선택. 풀·불꽃·물 후보 중 하나를 누르면 선택 이벤트를 발생시킴.</summary>
public partial class StarterWindow : Window
{
    private static readonly (string Name, string Color)[] Types =
    [
        ("풀", "#78C850"), ("불꽃", "#F08030"), ("물", "#6890F0"),
    ];

    public int SelectedDex { get; private set; }
    public event Action<int>? Selected;

    public StarterWindow() : this(Settings.RollStarterChoices(), true) { }

    internal StarterWindow(int[] choices, bool loadIcons)
    {
        InitializeComponent();
        for (var i = 0; i < choices.Length; i++)
            Choices.Children.Add(MakeChoice(choices[i], Types[i].Name, Types[i].Color, loadIcons));
    }

    /// <summary>후보 카드. 아이콘은 비동기로 채우고, 실패하면 이름·타입만 표시.</summary>
    private Button MakeChoice(int dex, string type, string color, bool loadIcons)
    {
        var icon = new Image { Width = 40, Height = 30, Stretch = Stretch.None };
        if (loadIcons) _ = LoadIconAsync(icon, dex);

        var panel = new StackPanel { Width = 84 };
        panel.Children.Add(new LayoutTransformControl { LayoutTransform = new ScaleTransform(2, 2), Child = icon });
        panel.Children.Add(new TextBlock
        {
            Text = PokemonNames.Of(dex), Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = type, Foreground = Brush.Parse(color), FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var button = new Button { Content = panel, Theme = (ControlTheme)Resources["Choice"]! };
        ToolTip.SetTip(button, $"#{dex} {PokemonNames.Of(dex)}");
        button.Click += (_, _) =>
        {
            SelectedDex = dex;
            Selected?.Invoke(dex);
        };
        return button;
    }

    private static async Task LoadIconAsync(Image icon, int dex)
    {
        try
        {
            var icons = await PokemonIcons.LoadGenAsync(PokemonIcons.GenOf(dex));
            if (icons.TryGetValue(dex, out var bmp)) icon.Source = bmp;
        }
        catch
        {
            // 네트워크 실패 등: 이름만으로 선택 가능
        }
    }

    private void OnDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
