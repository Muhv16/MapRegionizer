using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace MapRegionizer.App.Views;

public sealed class LoadingSpinner : Control
{
    private const int SegmentCount = 12;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private bool _isAttached;
    private int _frame;

    public LoadingSpinner()
    {
        IsHitTestVisible = false;
        _timer.Tick += OnTimerTick;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        UpdateTimerState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
            UpdateTimerState();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var diameter = Math.Min(Bounds.Width, Bounds.Height);
        if (diameter <= 0)
            return;

        var center = Bounds.Center;
        var orbitRadius = diameter * .36;
        var dotRadius = Math.Max(1, diameter * .075);
        for (var index = 0; index < SegmentCount; index++)
        {
            var angle = (index * Math.PI * 2 / SegmentCount) - Math.PI / 2;
            var centerPoint = new Point(
                center.X + Math.Cos(angle) * orbitRadius,
                center.Y + Math.Sin(angle) * orbitRadius);
            var age = (index - _frame + SegmentCount) % SegmentCount;
            var alpha = (byte)(50 + (205 * (SegmentCount - age) / SegmentCount));
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 37, 99, 235));
            context.DrawEllipse(brush, null, centerPoint, dotRadius, dotRadius);
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _frame = (_frame + 1) % SegmentCount;
        InvalidateVisual();
    }

    private void UpdateTimerState()
    {
        if (_isAttached && IsVisible)
            _timer.Start();
        else
            _timer.Stop();
    }
}
