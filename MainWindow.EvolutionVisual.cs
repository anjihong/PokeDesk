using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DeskPokemon;

public partial class MainWindow
{
    private CancellationTokenSource? _evolutionVisualCancellation;
    private SpriteAtlas? _evolutionTargetAtlas;
    private double _evolutionTargetRatio;
    private int _evolutionTargetFrame;
    private bool _evolutionVisualActive;
    private Visibility _evolutionMissingVisibility;

    private static async Task<BitmapSource?> LoadEvolutionSparkleAsync()
    {
        try { return SpriteAtlas.LoadSheet(await SpriteAtlas.CachedAsync("effects/evo_sparkle.png")); }
        catch { return null; /* 파티클은 흰 원으로 대체한다. */ }
    }

    private async Task PlayEvolutionVisualAsync(SpriteAtlas? source, SpriteAtlas target,
        BitmapSource? sparkle, Func<bool> isCurrent)
    {
        var cancellation = new CancellationTokenSource();
        _evolutionVisualCancellation = cancellation;
        var token = cancellation.Token;
        try
        {
            if (!isCurrent()) return;
            _evolutionTargetAtlas = target;
            _evolutionTargetFrame = 0;
            _evolutionVisualActive = true;
            _evolutionMissingVisibility = SpriteMissing.Visibility;
            SpriteMissing.Visibility = Visibility.Collapsed;

            // Stage의 레이아웃 크기는 그대로 두고 효과만 실제 표시 영역으로 확장한다.
            var width = 170 / Zoom.ScaleX;
            var height = 132 / Zoom.ScaleY;
            var left = (Stage.Width - width) / 2;
            var top = Stage.Height - height;
            EvolutionParticles.Width = width;
            EvolutionParticles.Height = height;
            Canvas.SetLeft(EvolutionParticles, left);
            Canvas.SetTop(EvolutionParticles, top);

            EvolutionSourceForm.Width = source?.Body.Width ?? Stage.Width;
            EvolutionSourceForm.Height = source?.Body.Height ?? Stage.Height;
            Canvas.SetLeft(EvolutionSourceForm, 0);
            Canvas.SetTop(EvolutionSourceForm, 0);
            var targetZoom = Math.Min(BodyTargetHeight / target.Body.Height, BodyMaxWidth / target.Body.Width);
            targetZoom = Math.Max(1, Math.Round(targetZoom * 4) / 4);
            _evolutionTargetRatio = targetZoom / Zoom.ScaleX;
            EvolutionTargetForm.Width = target.Body.Width * _evolutionTargetRatio;
            EvolutionTargetForm.Height = target.Body.Height * _evolutionTargetRatio;
            Canvas.SetLeft(EvolutionTargetForm, (Stage.Width - EvolutionTargetForm.Width) / 2);
            Canvas.SetTop(EvolutionTargetForm, Stage.Height - EvolutionTargetForm.Height);
            UpdateEvolutionFrames(source);

            EvolutionParticles.Visibility = Visibility.Visible;
            EvolutionSourceForm.Visibility = source == null ? Visibility.Collapsed : Visibility.Visible;
            EvolutionSourceForm.Opacity = 0;
            EvolutionTargetForm.Visibility = Visibility.Collapsed;
            EvolutionTargetPreview.Visibility = Visibility.Collapsed;
            EvolutionSourceScale.ScaleX = EvolutionSourceScale.ScaleY = 1;
            EvolutionTargetScale.ScaleX = EvolutionTargetScale.ScaleY = .25;
            if (source != null) Animate(EvolutionSourceForm, UIElement.OpacityProperty, 0, 1, 450);
            AddEvolutionParticles(sparkle, false);
            await Task.Delay(450, token);
            if (!isCurrent()) return;
            Sprite.Visibility = Visibility.Hidden;
            await Task.Delay(350, token);
            if (!isCurrent()) return;
            EvolutionTargetForm.Visibility = Visibility.Visible;

            // PokéRogue의 1~15단계(0.5 간격)를 유지하고 각 단계 시간을 40%로 줄인다.
            for (var cycle = 1.0; cycle <= 15; cycle += .5)
            {
                var duration = 200 / cycle;
                var reverse = cycle < 15;
                AnimateScale(EvolutionSourceScale, 1, .25, duration, reverse);
                AnimateScale(EvolutionTargetScale, .25, 1, duration, reverse);
                await Task.Delay(TimeSpan.FromMilliseconds(duration * (reverse ? 2 : 1)), token);
                if (!isCurrent()) return;
            }

            EvolutionSourceForm.Visibility = Visibility.Collapsed;
            EvolutionTargetPreview.Visibility = Visibility.Visible;
            AddEvolutionParticles(sparkle, true);
            Animate(EvolutionTargetForm, UIElement.OpacityProperty, 1, 0, 500);
            await Task.Delay(750, token);
        }
        finally
        {
            if (ReferenceEquals(_evolutionVisualCancellation, cancellation))
            {
                _evolutionVisualCancellation = null;
                ResetEvolutionVisual();
            }
            cancellation.Dispose();
        }
    }

    private void UpdateEvolutionFrames(SpriteAtlas? source)
    {
        if (!_evolutionVisualActive || _evolutionTargetAtlas == null) return;
        if (source != null)
            SetEvolutionMask(EvolutionSourceMask, source, source.Frames[_frame % source.Frames.Length], 1);
        var target = _evolutionTargetAtlas;
        var frame = target.Frames[_evolutionTargetFrame++ % target.Frames.Length];
        SetEvolutionMask(EvolutionTargetMask, target, frame, _evolutionTargetRatio);
        EvolutionTargetPreview.Source = frame.Bitmap;
        EvolutionTargetPreview.Width = frame.Width * _evolutionTargetRatio;
        EvolutionTargetPreview.Height = frame.Height * _evolutionTargetRatio;
        Canvas.SetLeft(EvolutionTargetPreview, Canvas.GetLeft(EvolutionTargetForm) +
            (frame.OffsetX - target.Body.X) * _evolutionTargetRatio);
        Canvas.SetTop(EvolutionTargetPreview, Canvas.GetTop(EvolutionTargetForm) +
            (frame.OffsetY - target.Body.Y) * _evolutionTargetRatio);
    }

    private static void SetEvolutionMask(Rectangle mask, SpriteAtlas atlas, SpriteFrame frame, double ratio)
    {
        mask.Width = frame.Width * ratio;
        mask.Height = frame.Height * ratio;
        mask.OpacityMask = new ImageBrush(frame.Bitmap) { Stretch = Stretch.Fill };
        Canvas.SetLeft(mask, (frame.OffsetX - atlas.Body.X) * ratio);
        Canvas.SetTop(mask, (frame.OffsetY - atlas.Body.Y) * ratio);
    }

    private static void Animate(UIElement element, DependencyProperty property, double from, double to,
        double milliseconds, bool reverse = false)
    {
        element.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            AutoReverse = reverse,
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    private static void AnimateScale(ScaleTransform scale, double from, double to, double milliseconds, bool reverse)
    {
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            scale.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                AutoReverse = reverse,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.HoldEnd
            });
    }

    private void AddEvolutionParticles(BitmapSource? sparkle, bool inward)
    {
        var count = inward ? 20 : 12;
        var width = EvolutionParticles.Width;
        var height = EvolutionParticles.Height;
        for (var i = 0; i < count; i++)
        {
            FrameworkElement particle = sparkle == null
                ? new Ellipse { Fill = Brushes.White }
                : new Image { Source = sparkle, Stretch = Stretch.Fill };
            particle.Width = particle.Height = i % 3 == 0 ? 3 : 2;
            EvolutionParticles.Children.Add(particle);
            var angle = 2 * Math.PI * i / count;
            var centerX = width / 2 - particle.Width / 2;
            var centerY = height * .58 - particle.Height / 2;
            var fromX = inward ? centerX + Math.Cos(angle) * width * .48 : centerX + Math.Cos(angle) * width * .24;
            var fromY = inward ? centerY + Math.Sin(angle) * height * .55 : height * .95;
            var toX = inward ? centerX : fromX + Math.Sin(angle * 2) * width * .12;
            var toY = inward ? centerY : -particle.Height;
            Canvas.SetLeft(particle, fromX);
            Canvas.SetTop(particle, fromY);
            var duration = inward ? 620 : 550 + i * 25;
            Animate(particle, Canvas.LeftProperty, fromX, toX, duration);
            Animate(particle, Canvas.TopProperty, fromY, toY, duration);
            Animate(particle, UIElement.OpacityProperty, 1, 0, duration);
        }
    }

    private void CancelEvolutionVisual()
    {
        _evolutionVisualCancellation?.Cancel();
        ResetEvolutionVisual();
    }

    private void ResetEvolutionVisual()
    {
        if (!_evolutionVisualActive) return;
        _evolutionVisualActive = false;
        EvolutionSourceForm.BeginAnimation(UIElement.OpacityProperty, null);
        EvolutionTargetForm.BeginAnimation(UIElement.OpacityProperty, null);
        foreach (var scale in new[] { EvolutionSourceScale, EvolutionTargetScale })
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
        EvolutionSourceForm.Visibility = Visibility.Collapsed;
        EvolutionTargetForm.Visibility = Visibility.Collapsed;
        EvolutionTargetPreview.Visibility = Visibility.Collapsed;
        EvolutionParticles.Visibility = Visibility.Collapsed;
        EvolutionParticles.Children.Clear();
        Sprite.Visibility = Visibility.Visible;
        SpriteMissing.Visibility = _evolutionMissingVisibility;
        _evolutionTargetAtlas = null;
    }

}
