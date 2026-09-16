using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.Domain;
using NetTopologySuite.Geometries;
using System.ComponentModel;
using System.Diagnostics;
using AvaloniaPoint = Avalonia.Point;

namespace MapRegionizer.App.Views;

public sealed class ManualMapEditorCanvas : Control
{
    private double _zoom = 1;
    private Vector _pan;
    private AvaloniaPoint _lastPointerPosition;
    private bool _isPanning;
    private INotifyPropertyChanged? _observedViewModel;
    private readonly Dictionary<int, CachedRegionGeometry> _regionGeometryCache = [];
    private readonly Dictionary<(int Id, bool Selected), SolidColorBrush> _regionBrushCache = [];
    private readonly Dictionary<(bool Selected, double Scale), Pen> _regionPenCache = [];
    private Avalonia.Media.Geometry? _markerGeometry;
    private IReadOnlyList<MapPoint>? _markerGeometrySource;
    private double _markerGeometryScale;
    private long _lastPreviewTicks;

    private static long PreviewIntervalTicks => Math.Max(1, Stopwatch.Frequency / 60);

    public ManualMapEditorCanvas()
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
        if (DataContext is not ManualMapEditorViewModel viewModel || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        context.FillRectangle(new SolidColorBrush(Color.Parse("#0F172A")), Bounds);
        var (scale, offsetX, offsetY) = GetTransform(viewModel);
        if (viewModel.BackgroundImage is not null && viewModel.IsBackgroundVisible)
        {
            var destination = new Rect(
                offsetX + viewModel.BackgroundOffsetX,
                offsetY + viewModel.BackgroundOffsetY,
                viewModel.Bounds.Width * scale * viewModel.BackgroundScale,
                viewModel.Bounds.Height * scale * viewModel.BackgroundScale);
            var center = destination.Center;
            using (context.PushTransform(Matrix.CreateTranslation(center.X, center.Y)))
            using (context.PushTransform(Matrix.CreateRotation(Matrix.ToRadians(viewModel.BackgroundRotation))))
            using (context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y)))
            using (context.PushOpacity(viewModel.BackgroundOpacity))
                context.DrawImage(viewModel.BackgroundImage, new Rect(viewModel.BackgroundImage.Size), destination);
        }

        using (context.PushTransform(new Matrix(scale, 0, 0, scale, offsetX, offsetY)))
        {
            foreach (var region in viewModel.DisplayRegions)
            {
                var selected = viewModel.SelectedRegionId == region.Id;
                context.DrawGeometry(
                    GetRegionBrush(region.Id, selected),
                    GetRegionPen(selected, scale),
                    GetRegionGeometry(region));
            }

            var markerGeometry = GetMarkerGeometry(viewModel, scale);
            if (markerGeometry is not null)
                context.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1 / scale), markerGeometry);
        }

        var current = viewModel.CurrentPolygon;
        if (current.Count > 0)
        {
            var currentPen = new Pen(Brushes.DeepSkyBlue, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            for (var index = 1; index < current.Count; index++)
                context.DrawLine(currentPen, ToPoint(current[index - 1], scale, offsetX, offsetY), ToPoint(current[index], scale, offsetX, offsetY));
        }

        var snap = viewModel.SnapCandidatePosition;
        if (snap.HasValue)
        {
            var point = ToPoint(snap.Value, scale, offsetX, offsetY);
            context.DrawEllipse(null, new Pen(Brushes.Lime, 2), new Rect(point.X - 7, point.Y - 7, 14, 14));
        }

        MapPoint? firstVertexPosition = null;
        if (viewModel.CurrentVertexIds.Count > 0
            && viewModel.Draft.Vertices.FirstOrDefault(vertex => vertex.Id == viewModel.CurrentVertexIds[0]) is { } firstVertex)
            firstVertexPosition = firstVertex.Position;

        if (firstVertexPosition.HasValue)
        {
            var point = ToPoint(firstVertexPosition.Value, scale, offsetX, offsetY);
            context.DrawEllipse(Brushes.Gold, new Pen(Brushes.Black, 1), new Rect(point.X - 4, point.Y - 4, 8, 8));
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (DataContext is not ManualMapEditorViewModel viewModel)
            return;
        Focus();
        if (viewModel.SelectedTool == ManualMapEditorTool.Navigate)
        {
            _isPanning = true;
            _lastPointerPosition = eventArgs.GetPosition(this);
            eventArgs.Pointer.Capture(this);
            return;
        }

        var (scale, offsetX, offsetY) = GetTransform(viewModel);
        viewModel.HandlePointer(ToMapPoint(eventArgs.GetPosition(this), scale, offsetX, offsetY), 10 / scale);
        InvalidateVisual();
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        var position = eventArgs.GetPosition(this);
        if (_isPanning)
        {
            _pan += position - _lastPointerPosition;
            _lastPointerPosition = position;
            InvalidateVisual();
            return;
        }

        if (DataContext is not ManualMapEditorViewModel viewModel
            || viewModel.SelectedTool != ManualMapEditorTool.CreateRegion)
            return;

        var now = Stopwatch.GetTimestamp();
        if (now - _lastPreviewTicks < PreviewIntervalTicks)
            return;
        _lastPreviewTicks = now;
        var (scale, offsetX, offsetY) = GetTransform(viewModel);
        viewModel.UpdatePointerPreview(ToMapPoint(position, scale, offsetX, offsetY), 10 / scale);
        InvalidateVisual();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        _isPanning = false;
        eventArgs.Pointer.Capture(null);
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
    {
        if (DataContext is not ManualMapEditorViewModel viewModel)
            return;
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
        if (DataContext is not ManualMapEditorViewModel viewModel)
            return;
        if (eventArgs.Key == Key.Enter)
        {
            viewModel.CompleteCurrentRegion();
            InvalidateVisual();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            viewModel.CancelCurrentRegion();
            InvalidateVisual();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Back)
        {
            viewModel.RemoveLastCurrentVertex();
            InvalidateVisual();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Z && (eventArgs.KeyModifiers & KeyModifiers.Control) != 0)
        {
            viewModel.UndoCommand.Execute().Subscribe();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Y && (eventArgs.KeyModifiers & KeyModifiers.Control) != 0)
        {
            viewModel.RedoCommand.Execute().Subscribe();
            eventArgs.Handled = true;
        }
    }

    private (double Scale, double OffsetX, double OffsetY) GetTransform(ManualMapEditorViewModel viewModel)
    {
        var scale = Math.Min(Bounds.Width / viewModel.Bounds.Width, Bounds.Height / viewModel.Bounds.Height) * _zoom;
        return (scale, (Bounds.Width - viewModel.Bounds.Width * scale) / 2 + _pan.X, (Bounds.Height - viewModel.Bounds.Height * scale) / 2 + _pan.Y);
    }

    private void ObserveViewModel()
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = DataContext as INotifyPropertyChanged;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ClearGeometryCaches();
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is null
            || eventArgs.PropertyName == nameof(ManualMapEditorViewModel.DisplayRegions)
            || eventArgs.PropertyName == nameof(ManualMapEditorViewModel.VertexMarkers))
        {
            if (eventArgs.PropertyName == nameof(ManualMapEditorViewModel.VertexMarkers))
                ClearMarkerGeometryCache();
            else
                ClearGeometryCaches();
        }
        InvalidateVisual();
    }

    private static MapPoint ToMapPoint(AvaloniaPoint point, double scale, double offsetX, double offsetY) =>
        new((point.X - offsetX) / scale, (point.Y - offsetY) / scale);

    private static AvaloniaPoint ToPoint(MapPoint point, double scale, double offsetX, double offsetY) =>
        new(offsetX + point.X * scale, offsetY + point.Y * scale);

    private Avalonia.Media.Geometry GetRegionGeometry(ManualMapDisplayRegion region)
    {
        if (_regionGeometryCache.TryGetValue(region.Id, out var cached)
            && ReferenceEquals(cached.Shape, region.Shape))
            return cached.Geometry;

        var geometry = ToGeometry(region.Shape);
        _regionGeometryCache[region.Id] = new CachedRegionGeometry(region.Shape, geometry);
        return geometry;
    }

    private Avalonia.Media.Geometry? GetMarkerGeometry(ManualMapEditorViewModel viewModel, double scale)
    {
        if (viewModel.VertexMarkers.Count == 0)
            return null;
        if (_markerGeometry is not null
            && ReferenceEquals(_markerGeometrySource, viewModel.VertexMarkers)
            && _markerGeometryScale.Equals(scale))
            return _markerGeometry;

        var halfSize = 4 / scale;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            foreach (var marker in viewModel.VertexMarkers)
            {
                context.BeginFigure(new AvaloniaPoint(marker.X - halfSize, marker.Y - halfSize), true);
                context.LineTo(new AvaloniaPoint(marker.X + halfSize, marker.Y - halfSize));
                context.LineTo(new AvaloniaPoint(marker.X + halfSize, marker.Y + halfSize));
                context.LineTo(new AvaloniaPoint(marker.X - halfSize, marker.Y + halfSize));
                context.EndFigure(true);
            }
        }

        _markerGeometry = geometry;
        _markerGeometrySource = viewModel.VertexMarkers;
        _markerGeometryScale = scale;
        return geometry;
    }

    private static Avalonia.Media.Geometry ToGeometry(Polygon polygon)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        DrawRing(context, polygon.ExteriorRing);
        for (var index = 0; index < polygon.NumInteriorRings; index++)
            DrawRing(context, polygon.GetInteriorRingN(index));
        return geometry;
    }

    private static void DrawRing(StreamGeometryContext context, LineString ring)
    {
        var first = ring.GetCoordinateN(0);
        context.BeginFigure(new AvaloniaPoint(first.X, first.Y), true);
        for (var index = 1; index < ring.NumPoints; index++)
        {
            var coordinate = ring.GetCoordinateN(index);
            context.LineTo(new AvaloniaPoint(coordinate.X, coordinate.Y));
        }
        context.EndFigure(true);
    }

    private SolidColorBrush GetRegionBrush(int id, bool selected)
    {
        if (_regionBrushCache.TryGetValue((id, selected), out var brush))
            return brush;
        brush = new SolidColorBrush(GetColor(id), selected ? .68 : .42);
        _regionBrushCache[(id, selected)] = brush;
        return brush;
    }

    private Pen GetRegionPen(bool selected, double scale)
    {
        if (_regionPenCache.TryGetValue((selected, scale), out var pen))
            return pen;
        pen = new Pen(selected ? Brushes.Gold : Brushes.White, (selected ? 3 : 1) / scale);
        _regionPenCache[(selected, scale)] = pen;
        return pen;
    }

    private void ClearGeometryCaches()
    {
        _regionGeometryCache.Clear();
        _regionBrushCache.Clear();
        _regionPenCache.Clear();
        ClearMarkerGeometryCache();
    }

    private void ClearMarkerGeometryCache()
    {
        _markerGeometry = null;
        _markerGeometrySource = null;
        _markerGeometryScale = 0;
    }

    private readonly record struct CachedRegionGeometry(Polygon Shape, Avalonia.Media.Geometry Geometry);

    private static Color GetColor(int id)
    {
        unchecked
        {
            var hash = id * 1103515245 + 12345;
            return Color.FromRgb((byte)(70 + (hash & 127)), (byte)(70 + ((hash >> 8) & 127)), (byte)(70 + ((hash >> 16) & 127)));
        }
    }
}
