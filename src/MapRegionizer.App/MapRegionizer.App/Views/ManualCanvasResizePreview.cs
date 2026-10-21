using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapRegionizer.App.ViewModels;
using MapRegionizer.Core.ManualAuthoring;
using System.ComponentModel;

namespace MapRegionizer.App.Views;

public sealed class ManualCanvasResizePreview : Control
{
    private INotifyPropertyChanged? _observedViewModel;
    private StreamGeometry? _regionGeometry;

    public ManualCanvasResizePreview()
    {
        ClipToBounds = true;
        DataContextChanged += (_, _) => ObserveViewModel();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0F172A")), Bounds);
        if (DataContext is not ManualCanvasResizeDialogViewModel viewModel || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var width = Math.Max(1, viewModel.PreviewWidthMapUnits);
        var height = Math.Max(1, viewModel.PreviewHeightMapUnits);
        var scale = Math.Min((Bounds.Width - 24) / width, (Bounds.Height - 24) / height);
        if (!double.IsFinite(scale) || scale <= 0)
            return;
        var mapWidth = width * scale;
        var mapHeight = height * scale;
        var offsetX = (Bounds.Width - mapWidth) / 2;
        var offsetY = (Bounds.Height - mapHeight) / 2;
        var newBounds = new Rect(offsetX, offsetY, mapWidth, mapHeight);
        var transform = new Matrix(scale, 0, 0, scale, offsetX, offsetY);

        using (context.PushClip(newBounds))
        using (context.PushTransform(transform))
        {
            var oldRect = new Rect(viewModel.PreviewOldX, viewModel.PreviewOldY, viewModel.PreviewOldWidth, viewModel.PreviewOldHeight);
            DrawAddedAreas(context, viewModel, oldRect, width, height);

            if (viewModel.IsBackgroundVisible && viewModel.BackgroundImage is { } bitmap)
            {
                var destination = new Rect(
                    viewModel.PreviewBackgroundX,
                    viewModel.PreviewBackgroundY,
                    viewModel.BackgroundMapWidth,
                    viewModel.BackgroundMapHeight);
                var center = destination.Center;
                using (context.PushTransform(Matrix.CreateTranslation(center.X, center.Y)))
                using (context.PushTransform(Matrix.CreateRotation(Matrix.ToRadians(viewModel.BackgroundRotation))))
                using (context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y)))
                using (context.PushOpacity(viewModel.BackgroundOpacity))
                    context.DrawImage(bitmap, new Rect(bitmap.Size), destination);
            }

            context.DrawRectangle(new SolidColorBrush(Color.Parse("#253B50"), .22), new Pen(new SolidColorBrush(Color.Parse("#F8FAFC"), .9), 1 / scale), oldRect);
            if (_regionGeometry is not null)
            {
                var regionPen = new Pen(new SolidColorBrush(Color.Parse("#FDE68A"), .95), 1.4 / scale);
                using (context.PushTransform(Matrix.CreateTranslation(viewModel.PreviewOldX, viewModel.PreviewOldY)))
                    context.DrawGeometry(null, regionPen, _regionGeometry);
            }
        }

        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#60A5FA")), 2), newBounds);
        context.DrawText(new FormattedText(
            $"Новая область {viewModel.NewSizeLabel}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12,
            new SolidColorBrush(Color.Parse("#E2E8F0"))),
            new Point(8, 8));
    }

    private static void DrawAddedAreas(
        DrawingContext context,
        ManualCanvasResizeDialogViewModel viewModel,
        Rect oldRect,
        double newWidth,
        double newHeight)
    {
        var brush = new SolidColorBrush(Color.Parse("#16A34A"), .18);
        if (viewModel.TopCells > 0)
            context.FillRectangle(brush, new Rect(0, 0, newWidth, Math.Min(oldRect.Top, newHeight)));
        if (viewModel.BottomCells > 0)
            context.FillRectangle(brush, new Rect(0, Math.Max(0, oldRect.Bottom), newWidth, Math.Max(0, newHeight - oldRect.Bottom)));
        if (viewModel.LeftCells > 0)
            context.FillRectangle(brush, new Rect(0, 0, Math.Min(oldRect.Left, newWidth), newHeight));
        if (viewModel.RightCells > 0)
            context.FillRectangle(brush, new Rect(Math.Max(0, oldRect.Right), 0, Math.Max(0, newWidth - oldRect.Right), newHeight));
    }

    private void ObserveViewModel()
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _observedViewModel = DataContext as INotifyPropertyChanged;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _regionGeometry = (DataContext as ManualCanvasResizeDialogViewModel)?.Draft is { } draft
            ? CreateRegionGeometry(draft)
            : null;
        InvalidateVisual();
    }

    private static StreamGeometry CreateRegionGeometry(ManualMapDraft draft)
    {
        var positions = draft.Vertices.ToDictionary(vertex => vertex.Id, vertex => vertex.Position);
        var geometry = new StreamGeometry();
        using var builder = geometry.Open();
        foreach (var region in draft.Regions)
        {
            var ring = region.VertexIds
                .Where(positions.ContainsKey)
                .Select(id => positions[id])
                .ToArray();
            if (ring.Length < 2)
                continue;

            builder.BeginFigure(new Point(ring[0].X, ring[0].Y), isFilled: false);
            for (var index = 1; index < ring.Length; index++)
                builder.LineTo(new Point(ring[index].X, ring[index].Y));
            builder.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManualCanvasResizeDialogViewModel.NewSizeLabel)
            or nameof(ManualCanvasResizeDialogViewModel.PreviewWidthMapUnits)
            or nameof(ManualCanvasResizeDialogViewModel.PreviewHeightMapUnits)
            or nameof(ManualCanvasResizeDialogViewModel.PreviewOldX)
            or nameof(ManualCanvasResizeDialogViewModel.PreviewOldY)
            or nameof(ManualCanvasResizeDialogViewModel.PreviewBackgroundX)
            or nameof(ManualCanvasResizeDialogViewModel.PreviewBackgroundY))
        {
            InvalidateVisual();
        }
    }
}
