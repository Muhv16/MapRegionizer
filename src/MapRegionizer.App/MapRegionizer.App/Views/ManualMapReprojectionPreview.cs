using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapRegionizer.Core.Domain;
using MapRegionizer.Core.ManualAuthoring;
using MapRegionizer.Core.ManualAuthoring.Georeferencing;
using MapRegionizer.Core.Spatial;

namespace MapRegionizer.App.Views;

public sealed record MapTransformPreviewMarker(int Id, MapPoint Position, MapPoint? ExpectedPosition = null);

/// <summary>Side-by-side outline preview with a geographic graticule on the target map.</summary>
public sealed class ManualMapReprojectionPreview : Control
{
    public ManualMapDraft? SourceDraft { get; set; }
    public MapSpatialReference? SourceReference { get; set; }
    public ManualMapDraft? TargetDraft { get; set; }
    public MapSpatialReference? TargetReference { get; set; }
    public Bitmap? SourceBackgroundImage { get; set; }
    public BackgroundImagePlacement? SourceBackgroundPlacement { get; set; }
    public Bitmap? TargetBackgroundImage { get; set; }
    public string EmptyMessage { get; set; } = "Результат появится после проверки параметров.";
    public string TargetLabel { get; set; } = "После";
    public IReadOnlyList<MapTransformPreviewMarker> SourceMarkers { get; set; } = [];
    public IReadOnlyList<MapTransformPreviewMarker> TargetMarkers { get; set; } = [];

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0B1220")), new Rect(Bounds.Size));
        if (SourceDraft is null || SourceReference is null || Bounds.Width < 20 || Bounds.Height < 20)
            return;

        var gap = 20.0;
        var panelWidth = (Bounds.Width - gap) / 2;
        DrawPanel(context, SourceDraft, SourceReference, new Rect(0, 0, panelWidth, Bounds.Height), "До", false, SourceBackgroundImage, SourceBackgroundPlacement, false, SourceMarkers);
        if (TargetDraft is not null && TargetReference is not null)
            DrawPanel(context, TargetDraft, TargetReference, new Rect(panelWidth + gap, 0, panelWidth, Bounds.Height), TargetLabel, true, TargetBackgroundImage, null, true, TargetMarkers);
        else
        {
            var message = new FormattedText(EmptyMessage, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 14, Brushes.White)
            { MaxTextWidth = Math.Max(1, panelWidth - 32) };
            context.DrawText(message, new Point(panelWidth + gap + 16, 16));
        }
    }

    private static void DrawPanel(
        DrawingContext context,
        ManualMapDraft draft,
        MapSpatialReference reference,
        Rect panel,
        string label,
        bool drawGraticule,
        Bitmap? backgroundImage,
        BackgroundImagePlacement? backgroundPlacement,
        bool fillTargetMap,
        IReadOnlyList<MapTransformPreviewMarker> markers)
    {
        using var clip = context.PushClip(panel);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#334155")), 1), panel.Deflate(0.5));
        context.DrawText(new FormattedText(label, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.White), new Point(panel.X + 8, panel.Y + 6));

        const double padding = 18;
        var drawable = new Rect(panel.X + padding, panel.Y + 26, Math.Max(1, panel.Width - padding * 2), Math.Max(1, panel.Height - 58));
        var mapWidth = reference.WidthInMapUnits;
        var mapHeight = reference.HeightInMapUnits;
        var scale = Math.Min(drawable.Width / mapWidth, drawable.Height / mapHeight);
        var left = drawable.X + (drawable.Width - mapWidth * scale) / 2;
        var top = drawable.Y + (drawable.Height - mapHeight * scale) / 2;

        if (backgroundImage is not null)
        {
            var destination = fillTargetMap || backgroundPlacement is null
                ? new Rect(left, top, mapWidth * scale, mapHeight * scale)
                : new Rect(
                    left + backgroundPlacement.X * scale,
                    top + backgroundPlacement.Y * scale,
                    backgroundPlacement.Width * scale,
                    backgroundPlacement.Height * scale);
            var center = destination.Center;
            using (context.PushTransform(Matrix.CreateTranslation(center.X, center.Y)))
            using (context.PushTransform(Matrix.CreateRotation(Matrix.ToRadians(backgroundPlacement?.RotationDegrees ?? 0))))
            using (context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y)))
                context.DrawImage(backgroundImage, new Rect(backgroundImage.Size), destination);
        }

        if (drawGraticule)
            DrawGraticule(context, reference, left, top, scale);

        var vertices = draft.Vertices.ToDictionary(vertex => vertex.Id, vertex => new Point(left + vertex.Position.X * scale, top + vertex.Position.Y * scale));
        var fill = new SolidColorBrush(Color.Parse(drawGraticule ? "#353F83F8" : "#3547556A"));
        var pen = new Pen(new SolidColorBrush(Color.Parse(drawGraticule ? "#FF7DD3FC" : "#FFCBD5E1")), 1.25);
        foreach (var region in draft.Regions)
        {
            var ids = region.VertexIds.ToList();
            if (ids.Count < 3)
                continue;
            if (ids[0] == ids[^1])
                ids.RemoveAt(ids.Count - 1);
            if (ids.Any(id => !vertices.ContainsKey(id)))
                continue;
            var geometry = new StreamGeometry();
            using (var builder = geometry.Open())
            {
                builder.BeginFigure(vertices[ids[0]], true);
                for (var index = 1; index < ids.Count; index++)
                    builder.LineTo(vertices[ids[index]]);
                builder.EndFigure(true);
            }
            context.DrawGeometry(fill, pen, geometry);
        }
        foreach (var marker in markers)
        {
            var position = new Point(left + marker.Position.X * scale, top + marker.Position.Y * scale);
            if (marker.ExpectedPosition is { } expected)
            {
                var expectedPoint = new Point(left + expected.X * scale, top + expected.Y * scale);
                var expectedPen = new Pen(Brushes.Gold, 1.5);
                context.DrawLine(expectedPen, position, expectedPoint);
                context.DrawLine(expectedPen, expectedPoint + new Vector(-4, 0), expectedPoint + new Vector(4, 0));
                context.DrawLine(expectedPen, expectedPoint + new Vector(0, -4), expectedPoint + new Vector(0, 4));
            }
            context.DrawEllipse(Brushes.DeepSkyBlue, new Pen(Brushes.White, 1), position, 3.5, 3.5);
            context.DrawText(new FormattedText(marker.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White),
                position + new Vector(5, -14));
        }
        if (markers.Any(marker => marker.ExpectedPosition is not null))
            context.DrawText(new FormattedText("● расчёт   + известно", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.Gold), new Point(panel.X + 8, panel.Bottom - 23));
    }

    private static void DrawGraticule(DrawingContext context, MapSpatialReference reference, double left, double top, double scale)
    {
        var mapping = MapSpatialContext.Create(reference);
        var coverage = reference.Coverage;
        var longitudeStep = coverage.Longitude.SpanDegrees >= 120 ? 30 : coverage.Longitude.SpanDegrees >= 40 ? 10 : 5;
        var latitudeStep = coverage.LatitudeSpan >= 120 ? 30 : coverage.LatitudeSpan >= 40 ? 10 : 5;
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#485B7087")), 0.8);

        for (var lonOffset = 0.0; lonOffset <= coverage.Longitude.SpanDegrees + 1e-9; lonOffset += longitudeStep)
        {
            var longitude = coverage.Longitude.StartLongitudeDegrees + lonOffset;
            DrawGeoLine(context, Enumerable.Range(0, 37).Select(i => new GeoCoordinate(
                longitude, coverage.SouthLatitude + coverage.LatitudeSpan * i / 36.0)), mapping, left, top, scale, gridPen);
        }
        var firstLatitude = Math.Ceiling(coverage.SouthLatitude / latitudeStep) * latitudeStep;
        for (var latitude = firstLatitude; latitude <= coverage.NorthLatitude; latitude += latitudeStep)
        {
            DrawGeoLine(context, Enumerable.Range(0, 73).Select(i => new GeoCoordinate(
                coverage.Longitude.StartLongitudeDegrees + coverage.Longitude.SpanDegrees * i / 72.0, latitude)), mapping, left, top, scale, gridPen);
        }
    }

    private static void DrawGeoLine(DrawingContext context, IEnumerable<GeoCoordinate> points, MapSpatialContext mapping, double left, double top, double scale, Pen pen)
    {
        Point? previous = null;
        foreach (var geographic in points)
        {
            try
            {
                var grid = mapping.GeographicToGrid(geographic);
                var current = new Point(
                    left + grid.X * mapping.SpatialReference.UnitsPerCell * scale,
                    top + grid.Y * mapping.SpatialReference.UnitsPerCell * scale);
                if (previous is { } from && Math.Sqrt(Math.Pow(current.X - from.X, 2) + Math.Pow(current.Y - from.Y, 2)) < 2000)
                    context.DrawLine(pen, from, current);
                previous = current;
            }
            catch (ArgumentException)
            {
                previous = null;
            }
        }
    }
}
