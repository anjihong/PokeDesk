using Avalonia.Media;

namespace DeskPokemon;

public partial class MainWindow
{
    private ScaleTransform Squash => (ScaleTransform)Stage.RenderTransform!;
    private ScaleTransform LevelPulse => (ScaleTransform)LevelText.RenderTransform!;
    private ScaleTransform EggScale => (ScaleTransform)((TransformGroup)EggStage.RenderTransform!).Children[0];
    private RotateTransform EggRotate => (RotateTransform)((TransformGroup)EggStage.RenderTransform!).Children[1];
    private ScaleTransform ResultScale => (ScaleTransform)ResultStage.RenderTransform!;
    private ScaleTransform ResultZoomScale => (ScaleTransform)ResultZoom.LayoutTransform!;
    private TranslateTransform BubbleOffset => (TranslateTransform)Bubble.RenderTransform!;
    private TranslateTransform EggOffset => (TranslateTransform)EggGroup.RenderTransform!;
    private readonly Dictionary<string, Timeline> _animations = new();

    // Original WPF keyframes shared by every desktop backend.
    private void BuildAnimations()
    {
        _animations.Add("Bounce", new Timeline(false, [
            new(v => Squash.ScaleY = v, 1, [new(0, 1, Ease.Linear), new(0.06, 0.8, Ease.Linear), new(0.14, 1.12, Ease.Linear), new(0.22, 1, Ease.Linear)]),
            new(v => Squash.ScaleX = v, 1, [new(0, 1, Ease.Linear), new(0.06, 1.15, Ease.Linear), new(0.14, 0.95, Ease.Linear), new(0.22, 1, Ease.Linear)])
        ]));
        _animations.Add("LevelUp", new Timeline(false, [
            new(v => LevelPulse.ScaleX = v, 1, [new(0, 1, Ease.Linear), new(0.1, 1.5, Ease.Linear), new(0.35, 1, Ease.Linear)]),
            new(v => LevelPulse.ScaleY = v, 1, [new(0, 1, Ease.Linear), new(0.1, 1.5, Ease.Linear), new(0.35, 1, Ease.Linear)])
        ]));
        _animations.Add("EggWait", new Timeline(true, [
            new(v => EggScale.ScaleY = v, 1, [new(0, 1, Ease.Linear), new(0.35, 0.94, Ease.EggWindUp), new(0.5, 1.04, Ease.EggPop), new(1.1, 1, Ease.EggSpring), new(1.9, 0.97, Ease.EggSwing), new(2.7, 1, Ease.EggSwing), new(3.5, 0.97, Ease.EggSwing), new(4.3, 1, Ease.EggSwing)]),
            new(v => EggScale.ScaleX = v, 1, [new(0, 1, Ease.Linear), new(0.35, 1.04, Ease.EggWindUp), new(0.5, 0.97, Ease.EggPop), new(1.1, 1, Ease.EggSpring), new(1.9, 1.025, Ease.EggSwing), new(2.7, 1, Ease.EggSwing), new(3.5, 1.025, Ease.EggSwing), new(4.3, 1, Ease.EggSwing)])
        ]));
        _animations.Add("EggIdle", new Timeline(true, [
            new(v => EggRotate.Angle = v, 0, [new(0, 0, Ease.Linear), new(0.12, -12, Ease.EggPop), new(0.32, 12, Ease.EggSwing), new(0.5, -8, Ease.EggSwing), new(0.66, 5, Ease.EggSwing), new(0.95, 0, Ease.EggSpring)]),
            new(v => EggScale.ScaleY = v, 1, [new(0, 1, Ease.Linear), new(0.95, 1, Ease.Linear), new(1.25, 0.91, Ease.EggWindUp), new(1.4, 1.06, Ease.EggPop), new(2, 1, Ease.EggSpring), new(2.7, 0.97, Ease.EggSwing), new(3.4, 1, Ease.EggSwing), new(4.1, 0.97, Ease.EggSwing), new(4.8, 1, Ease.EggSwing)]),
            new(v => EggScale.ScaleX = v, 1, [new(0, 1, Ease.Linear), new(0.95, 1, Ease.Linear), new(1.25, 1.06, Ease.EggWindUp), new(1.4, 0.96, Ease.EggPop), new(2, 1, Ease.EggSpring), new(2.7, 1.025, Ease.EggSwing), new(3.4, 1, Ease.EggSwing), new(4.1, 1.025, Ease.EggSwing), new(4.8, 1, Ease.EggSwing)])
        ]));
        _animations.Add("EggShake", new Timeline(false, [
            new(v => EggRotate.Angle = v, 0, [new(0, 0, Ease.Linear), new(0.08, -12, Ease.Linear), new(0.2, 12, Ease.Linear), new(0.32, -12, Ease.Linear), new(0.44, 12, Ease.Linear), new(0.52, 0, Ease.Linear)])
        ]));
        _animations.Add("FlashOut", new Timeline(false, [
            new(v => Flash.Opacity = v, 0, [new(0, 1, Ease.Linear), new(0.35, 0, Ease.Linear)])
        ]));
        _animations.Add("ResultPop", new Timeline(false, [
            new(v => ResultScale.ScaleX = v, 1, [new(0, 0, Ease.Linear), new(0.2, 1.25, Ease.Linear), new(0.35, 1, Ease.Linear)]),
            new(v => ResultScale.ScaleY = v, 1, [new(0, 0, Ease.Linear), new(0.2, 1.25, Ease.Linear), new(0.35, 1, Ease.Linear)])
        ]));
    }
}
