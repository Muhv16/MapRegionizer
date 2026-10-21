using MapRegionizer.Core.Domain;
using MapRegionizer.Core.Regions;
using MapRegionizer.Core.Spatial;
using System.Threading;

namespace MapRegionizer.Core.ManualAuthoring;

/// <summary>Cell offsets applied to the four edges of a manual map canvas.</summary>
public sealed record ManualCanvasResizeRequest(
    int TopCells,
    int BottomCells,
    int LeftCells,
    int RightCells);

/// <summary>The immutable result of resizing a manual draft and its geography.</summary>
public sealed record ManualCanvasResizeResult(
    ManualMapDraft Draft,
    MapSpatialReference SpatialReference,
    ManualCanvasResizeRequest Request);

public enum ManualCanvasResizeErrorCode
{
    InvalidSpatialReference,
    GridSizeMismatch,
    InvalidDimensions,
    GridSizeLimitExceeded,
    GlobalLongitudeResizeUnsupported,
    LongitudeCoverageOutOfRange,
    LatitudeCoverageOutOfRange,
    VertexOutsideNewCanvas
}

/// <summary>A rejected resize with a stable diagnostic code and user-readable message.</summary>
public sealed class ManualCanvasResizeException : InvalidOperationException
{
    public ManualCanvasResizeException(ManualCanvasResizeErrorCode code, string message)
        : base(message) => Code = code;

    public ManualCanvasResizeErrorCode Code { get; }
}

/// <summary>
/// Resizes a manual map without changing the geographic location of any
/// existing vertex. The shared vertex pool is translated once, preserving all
/// region IDs and shared-edge references.
/// </summary>
public sealed class ManualMapCanvasResizer
{
    // Keep dimensions below the sizes at which editor geometry and the
    // downstream raster stages become impractical. The product is checked in
    // 64-bit arithmetic before any new state is allocated.
    public const int MaximumGridDimension = 100_000;
    public const long MaximumGridCellCount = 100_000_000;

    public ManualCanvasResizeResult Resize(
        ManualMapDraft draft,
        MapSpatialReference spatialReference,
        ManualCanvasResizeRequest request) =>
        Resize(draft, spatialReference, request, CancellationToken.None);

    public ManualCanvasResizeResult Resize(
        ManualMapDraft draft,
        MapSpatialReference spatialReference,
        ManualCanvasResizeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(spatialReference);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            spatialReference.Validate();
        }
        catch (ArgumentException exception)
        {
            throw new ManualCanvasResizeException(
                ManualCanvasResizeErrorCode.InvalidSpatialReference,
                $"Текущая пространственная привязка некорректна: {exception.Message}");
        }

        if (spatialReference.GridWidth != draft.GridWidth || spatialReference.GridHeight != draft.GridHeight)
        {
            throw new ManualCanvasResizeException(
                ManualCanvasResizeErrorCode.GridSizeMismatch,
                "Размеры draft и пространственной привязки не совпадают.");
        }

        if (spatialReference.Coverage.IsGlobal &&
            (request.LeftCells != 0 || request.RightCells != 0))
        {
            throw new ManualCanvasResizeException(
                ManualCanvasResizeErrorCode.GlobalLongitudeResizeUnsupported,
                "У глобальной карты уже есть полный охват 360° по долготе. Изменение ширины потребовало бы изменить положение или разрешение существующих регионов.");
        }

        var newWidthLong = (long)draft.GridWidth + request.LeftCells + request.RightCells;
        var newHeightLong = (long)draft.GridHeight + request.TopCells + request.BottomCells;
        if (newWidthLong <= 0 || newHeightLong <= 0 || newWidthLong > int.MaxValue || newHeightLong > int.MaxValue)
        {
            throw new ManualCanvasResizeException(
                ManualCanvasResizeErrorCode.InvalidDimensions,
                "Новый размер холста должен быть положительным и помещаться в поддерживаемый диапазон.");
        }

        var newWidth = (int)newWidthLong;
        var newHeight = (int)newHeightLong;
        if (newWidth > MaximumGridDimension || newHeight > MaximumGridDimension || (long)newWidth * newHeight > MaximumGridCellCount)
        {
            throw new ManualCanvasResizeException(
                ManualCanvasResizeErrorCode.GridSizeLimitExceeded,
                $"Новый холст превышает ограничение {MaximumGridDimension:N0} ячеек на сторону и {MaximumGridCellCount:N0} ячеек всего.");
        }

        var oldCoverage = spatialReference.Coverage;
        var latitudePerCell = oldCoverage.LatitudeSpan / draft.GridHeight;
        var northLatitude = oldCoverage.NorthLatitude;
        var southLatitude = oldCoverage.SouthLatitude;

        if (spatialReference.GridMapping == GridMappingKind.Equirectangular)
        {
            northLatitude += request.TopCells * latitudePerCell;
            southLatitude -= request.BottomCells * latitudePerCell;
        }
        else
        {
            var northMercator = WebMercatorGridMapping.MercatorY(northLatitude);
            var southMercator = WebMercatorGridMapping.MercatorY(southLatitude);
            var mercatorPerCell = (southMercator - northMercator) / draft.GridHeight;
            northLatitude = WebMercatorGridMapping.InverseMercatorY(northMercator - request.TopCells * mercatorPerCell);
            southLatitude = WebMercatorGridMapping.InverseMercatorY(southMercator + request.BottomCells * mercatorPerCell);
        }

        if (!double.IsFinite(northLatitude) || !double.IsFinite(southLatitude) ||
            southLatitude < -90 || northLatitude > 90 || southLatitude >= northLatitude)
        {
            throw new ManualCanvasResizeException(
                ManualCanvasResizeErrorCode.LatitudeCoverageOutOfRange,
                "Изменение вышло за допустимые широтные границы для выбранной проекции. Уменьшите число добавляемых ячеек.");
        }

        if (spatialReference.GridMapping == GridMappingKind.WebMercator &&
            (southLatitude < -WebMercatorGridMapping.WebMercatorLatitudeLimit || northLatitude > WebMercatorGridMapping.WebMercatorLatitudeLimit))
        {
            throw new ManualCanvasResizeException(
                ManualCanvasResizeErrorCode.LatitudeCoverageOutOfRange,
                $"Web Mercator поддерживает широты только в пределах ±{WebMercatorGridMapping.WebMercatorLatitudeLimit:R}°. Изменение отклонено без обрезки карты.");
        }

        MapCoverage coverage;
        if (oldCoverage.IsGlobal)
        {
            coverage = MapCoverage.Global(southLatitude, northLatitude);
        }
        else
        {
            var longitudePerCell = oldCoverage.Longitude.SpanDegrees / draft.GridWidth;
            var newLongitudeSpan = oldCoverage.Longitude.SpanDegrees + (request.LeftCells + (double)request.RightCells) * longitudePerCell;
            var newLongitudeStart = oldCoverage.Longitude.StartLongitudeDegrees - request.LeftCells * longitudePerCell;
            if (!double.IsFinite(newLongitudeSpan) || newLongitudeSpan <= 0 || newLongitudeSpan > 360)
            {
                throw new ManualCanvasResizeException(
                    ManualCanvasResizeErrorCode.LongitudeCoverageOutOfRange,
                    "Новый региональный охват по долготе должен быть больше 0° и не превышать 360°.");
            }

            coverage = MapCoverage.Regional(new LongitudeInterval(newLongitudeStart, newLongitudeSpan), southLatitude, northLatitude);
        }

        var newReference = spatialReference with
        {
            GridWidth = newWidth,
            GridHeight = newHeight,
            Coverage = coverage
        };
        try
        {
            newReference.Validate();
        }
        catch (ArgumentException exception)
        {
            var code = spatialReference.GridMapping == GridMappingKind.WebMercator
                ? ManualCanvasResizeErrorCode.LatitudeCoverageOutOfRange
                : ManualCanvasResizeErrorCode.InvalidSpatialReference;
            throw new ManualCanvasResizeException(code, $"Новая географическая область недопустима: {exception.Message}");
        }

        var deltaX = request.LeftCells * spatialReference.UnitsPerCell;
        var deltaY = request.TopCells * spatialReference.UnitsPerCell;
        var maxX = newReference.WidthInMapUnits;
        var maxY = newReference.HeightInMapUnits;
        var tolerance = RegionGeometryPrecision.LengthTolerance;
        var vertices = new ManualMapVertex[draft.Vertices.Count];
        for (var index = 0; index < draft.Vertices.Count; index++)
        {
            if ((index & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var vertex = draft.Vertices[index];
            var position = new MapPoint(vertex.Position.X + deltaX, vertex.Position.Y + deltaY);
            if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
                position.X < -tolerance || position.X > maxX + tolerance ||
                position.Y < -tolerance || position.Y > maxY + tolerance)
            {
                throw new ManualCanvasResizeException(
                    ManualCanvasResizeErrorCode.VertexOutsideNewCanvas,
                    $"Изменение отклонено: вершина {vertex.Id} окажется за новыми границами холста. Сначала вручную измените или удалите затрагиваемый регион.");
            }

            vertices[index] = vertex with { Position = position };
        }

        // All vertices and straight edges inside a rectangular canvas are
        // retained. No clipping or ID repair is performed during a shrink.
        cancellationToken.ThrowIfCancellationRequested();
        var resizedDraft = new ManualMapDraft(newWidth, newHeight, vertices, draft.Regions);
        return new ManualCanvasResizeResult(resizedDraft, newReference, request);
    }
}
