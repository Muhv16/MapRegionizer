using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MapRegionizer.App.ViewModels;
using NetTopologySuite.Geometries;
using System.ComponentModel;

namespace MapRegionizer.App.Views;

public sealed class RegionEditorCanvas : Control
{
    private double _zoom = 1;
    private Vector _pan;
    private Avalonia.Point _lastPointerPosition;
    private bool _isPanning;
    private bool _isDraggingVertex;
    private INotifyPropertyChanged? _observedViewModel;

    public RegionEditorCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;
        DataContextChanged += (_, _) => ObserveViewModel();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (DataContext is not RegionEditorViewModel viewModel || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        context.FillRectangle(new SolidColorBrush(Color.Parse("#0F172A")), Bounds);
        var (scale, offsetX, offsetY) = GetTransform(viewModel);
        if (viewModel.BackgroundImage is not null && viewModel.IsBackgroundVisible)
        {
            var destination = new Rect(offsetX + viewModel.BackgroundOffsetX, offsetY + viewModel.BackgroundOffsetY,
                viewModel.Bounds.Width * scale * viewModel.BackgroundScale, viewModel.Bounds.Height * scale * viewModel.BackgroundScale);
            var center = destination.Center;
            using (context.PushTransform(Matrix.CreateTranslation(center.X, center.Y)))
            using (context.PushTransform(Matrix.CreateRotation(Matrix.ToRadians(viewModel.BackgroundRotation))))
            using (context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y)))
            using (context.PushOpacity(viewModel.BackgroundOpacity))
                context.DrawImage(viewModel.BackgroundImage, new Rect(viewModel.BackgroundImage.Size), destination);
        }

        foreach (var region in viewModel.DisplayRegions)
        {
            var geometry = ToGeometry(region.Shape, scale, offsetX, offsetY);
            var color = GetColor(region.Id.Value);
            var selected = viewModel.SelectedRegionId == region.Id;
            context.DrawGeometry(new SolidColorBrush(color, selected ? .65 : .42), new Pen(selected ? Brushes.Gold : Brushes.White, selected ? 3 : 1), geometry);
        }

        if (viewModel.SplitPreviewLine is { } split)
        {
            var pen = new Pen(Brushes.DeepSkyBlue, 2);
            context.DrawLine(pen, ToPoint(new Coordinate(split.Start.X, split.Start.Y), scale, offsetX, offsetY),
                ToPoint(new Coordinate(split.End.X, split.End.Y), scale, offsetX, offsetY));
        }

        if (viewModel.ShowVertexMarkers)
        {
            var markerBrush = viewModel.IsVertexDragValid is false ? Brushes.OrangeRed : Brushes.Red;
            foreach (var vertex in viewModel.VertexMarkers)
            {
                var point = ToPoint(new Coordinate(vertex.X, vertex.Y), scale, offsetX, offsetY);
                context.DrawEllipse(markerBrush, new Pen(Brushes.White, 1), new Rect(point.X - 5, point.Y - 5, 10, 10));
            }
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (DataContext is not RegionEditorViewModel viewModel) return;
        Focus();
        if (viewModel.SelectedTool == RegionEditorTool.Navigate)
        {
            _isPanning = true;
            _lastPointerPosition = eventArgs.GetPosition(this);
            eventArgs.Pointer.Capture(this);
            return;
        }
        var (scale, offsetX, offsetY) = GetTransform(viewModel);
        var position = eventArgs.GetPosition(this);
        var mapPoint = ToMapPoint(position, scale, offsetX, offsetY);
        if (viewModel.SelectedTool == RegionEditorTool.MoveVertex && viewModel.BeginVertexDrag(mapPoint, 12 / scale))
        {
            _isDraggingVertex = true;
            eventArgs.Pointer.Capture(this);
            InvalidateVisual();
            return;
        }
        viewModel.HandlePointer(mapPoint);
        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (_isDraggingVertex)
        {
            if (DataContext is RegionEditorViewModel viewModel)
            {
                var (scale, offsetX, offsetY) = GetTransform(viewModel);
                viewModel.UpdateVertexDrag(ToMapPoint(eventArgs.GetPosition(this), scale, offsetX, offsetY));
                InvalidateVisual();
            }
            return;
        }
        if (DataContext is RegionEditorViewModel splitViewModel)
        {
            var (scale, offsetX, offsetY) = GetTransform(splitViewModel);
            splitViewModel.UpdatePointerPreview(ToMapPoint(eventArgs.GetPosition(this), scale, offsetX, offsetY));
            InvalidateVisual();
        }
        if (!_isPanning) return;
        var position = eventArgs.GetPosition(this);
        _pan += position - _lastPointerPosition;
        _lastPointerPosition = position;
        InvalidateVisual();
    }

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (_isDraggingVertex)
        {
            _isDraggingVertex = false;
            eventArgs.Pointer.Capture(null);
            if (DataContext is RegionEditorViewModel viewModel)
                await viewModel.EndVertexDragAsync();
            InvalidateVisual();
            return;
        }
        _isPanning = false;
        eventArgs.Pointer.Capture(null);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (DataContext is not RegionEditorViewModel viewModel) return;
        var position = eventArgs.GetPosition(this);
        var (oldScale, oldOffsetX, oldOffsetY) = GetTransform(viewModel);
        var mapX = (position.X - oldOffsetX) / oldScale;
        var mapY = (position.Y - oldOffsetY) / oldScale;
        _zoom = Math.Clamp(_zoom * (eventArgs.Delta.Y > 0 ? 1.18 : 1 / 1.18), .2, 16);
        var baseScale = Math.Min(Bounds.Width / viewModel.Bounds.Width, Bounds.Height / viewModel.Bounds.Height) * _zoom;
        var baseOffsetX = (Bounds.Width - viewModel.Bounds.Width * baseScale) / 2;
        var baseOffsetY = (Bounds.Height - viewModel.Bounds.Height * baseScale) / 2;
        _pan = new Vector(position.X - baseOffsetX - mapX * baseScale, position.Y - baseOffsetY - mapY * baseScale);
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        const double step = 48;
        _pan += eventArgs.Key switch
        {
            Key.Left => new Vector(step, 0),
            Key.Right => new Vector(-step, 0),
            Key.Up => new Vector(0, step),
            Key.Down => new Vector(0, -step),
            _ => default
        };
        if (eventArgs.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            InvalidateVisual();
            eventArgs.Handled = true;
        }
    }

    private (double Scale, double OffsetX, double OffsetY) GetTransform(RegionEditorViewModel viewModel)
    {
        var scale = Math.Min(Bounds.Width / viewModel.Bounds.Width, Bounds.Height / viewModel.Bounds.Height) * _zoom;
        return (scale, (Bounds.Width - viewModel.Bounds.Width * scale) / 2 + _pan.X, (Bounds.Height - viewModel.Bounds.Height * scale) / 2 + _pan.Y);
    }

    private static MapRegionizer.Core.Domain.MapPoint ToMapPoint(Avalonia.Point point, double scale, double offsetX, double offsetY) =>
        new((point.X - offsetX) / scale, (point.Y - offsetY) / scale);

    private void ObserveViewModel()
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = DataContext as INotifyPropertyChanged;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs) => InvalidateVisual();

    private static Avalonia.Media.Geometry ToGeometry(Polygon polygon, double scale, double offsetX, double offsetY)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        DrawRing(context, polygon.ExteriorRing, scale, offsetX, offsetY);
        for (var index = 0; index < polygon.NumInteriorRings; index++)
            DrawRing(context, polygon.GetInteriorRingN(index), scale, offsetX, offsetY);
        return geometry;
    }

    private static void DrawRing(StreamGeometryContext context, LineString ring, double scale, double offsetX, double offsetY)
    {
        var first = ring.GetCoordinateN(0);
        context.BeginFigure(ToPoint(first, scale, offsetX, offsetY), true);
        for (var index = 1; index < ring.NumPoints; index++)
            context.LineTo(ToPoint(ring.GetCoordinateN(index), scale, offsetX, offsetY));
        context.EndFigure(true);
    }

    private static Avalonia.Point ToPoint(Coordinate coordinate, double scale, double offsetX, double offsetY) => new(offsetX + coordinate.X * scale, offsetY + coordinate.Y * scale);
    private static Color GetColor(int id)
    {
        unchecked { var hash = id * 1103515245 + 12345; return Color.FromRgb((byte)(70 + (hash & 127)), (byte)(70 + ((hash >> 8) & 127)), (byte)(70 + ((hash >> 16) & 127))); }
    }
}
