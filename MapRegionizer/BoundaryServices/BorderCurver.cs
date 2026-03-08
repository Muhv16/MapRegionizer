using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;
using System.Globalization;

namespace MapRegionizer.BoundaryServices;

internal class BorderCurver
{
    private readonly GeometryFactory _factory;
    private readonly MapOptions _options;
    private readonly Random random;
    private const int KeyDecimalPlaces = 6; // точность при формировании ключей (можно настроить)

    public BorderCurver(GeometryFactory factory, MapOptions options)
    {
        _factory = factory;
        _options = options;
        random = new Random();
    }

    /// <summary>
    /// Возвращает словарь замен для атомарных сегментов в виде: key(segment A->B) => curved LineString (A->B)
    /// Ключи формируются канонично с фиксированным округлением.
    /// </summary>
    public Dictionary<string, LineString> Distortion(List<LineString> borders, Polygon continentPolygon)
    {
        // Индекс исходных атомарных сегментов: key -> list of (parent border, index)
        var segmentIndex = new Dictionary<string, List<(LineString parent, int index)>>(StringComparer.Ordinal);
        foreach (var border in borders)
        {
            var coords = border.Coordinates;
            for (int i = 0; i < coords.Length - 1; i++)
            {
                var k = MakeKey(coords[i], coords[i + 1]);
                if (!segmentIndex.TryGetValue(k, out var list))
                {
                    list = new List<(LineString, int)>();
                    segmentIndex[k] = list;
                }
                list.Add((border, i));
                // также индексируем обратный ключ, чтобы мы знали, что такой сегмент существует с другой ориентацией
                var kRev = MakeKey(coords[i + 1], coords[i]);
                if (!segmentIndex.ContainsKey(kRev))
                    segmentIndex[kRev] = new List<(LineString, int)>();
            }
        }

        // Результирующая таблица для атомарных сегментов
        var segmentReplacements = new Dictionary<string, LineString>(StringComparer.Ordinal);

        // Обрабатываем каждую уникальную border (исходную полилинию) и создаём replacement подотрезки для её атомарных сегментов
        foreach (var border in borders)
        {
            var newLine = CurveLine(border, continentPolygon);
            // если замена не удалась, используем оригинал
            if (newLine == null || newLine.IsEmpty)
                newLine = border;

            var parentCoords = border.Coordinates;
            var indexed = new LengthIndexedLine(newLine);

            for (int i = 0; i < parentCoords.Length - 1; i++)
            {
                var a = parentCoords[i];
                var b = parentCoords[i + 1];

                // Проецируем исходные точки на искривленную линию
                double posA = indexed.Project(a);
                double posB = indexed.Project(b);

                // Если проекции получились одинаковыми (плохо сошлись) — попробуем слегка подобрать ближайшие
                if (Math.Abs(posA - posB) < 1e-9)
                {
                    // попытаемся взять соседние позиции ±eps
                    posA = Math.Max(0, posA - 1e-6);
                    posB = Math.Min(indexed.EndIndex, posA + 1e-6);
                    if (Math.Abs(posA - posB) < 1e-12)
                    {
                        // как fallback — используем прямой отрезок между a и b
                        var fallback = _factory.CreateLineString(new[] { a.Copy(), b.Copy() });
                        var fk = MakeKey(a, b);
                        if (!segmentReplacements.ContainsKey(fk))
                            segmentReplacements[fk] = fallback;
                        var fkRev = MakeKey(b, a);
                        if (!segmentReplacements.ContainsKey(fkRev))
                            segmentReplacements[fkRev] = (LineString)fallback.Reverse();
                        continue;
                    }
                }

                double start = Math.Min(posA, posB);
                double end = Math.Max(posA, posB);

                Geometry extracted;
                try
                {
                    extracted = indexed.ExtractLine(start, end);
                }
                catch
                {
                    // экстренная обработка — fallback на прямой отрезок
                    extracted = _factory.CreateLineString(new[] { a.Copy(), b.Copy() });
                }

                LineString extractedLine;
                if (extracted is LineString ls)
                {
                    extractedLine = ls;
                }
                else if (extracted is MultiLineString mls && mls.NumGeometries > 0)
                {
                    // Обычно результат будет одним LineString; берём первый непротиворечивый
                    extractedLine = (LineString)mls.GetGeometryN(0);
                }
                else
                {
                    // fallback
                    extractedLine = _factory.CreateLineString(new[] { a.Copy(), b.Copy() });
                }

                // Убедимся, что извлечённая линия покрывает оба конца — если нет, расширим/корректируем
                if (extractedLine.NumPoints < 2)
                {
                    extractedLine = _factory.CreateLineString(new[] { a.Copy(), b.Copy() });
                }

                // Сохраняем замену для ключа A->B и для обратного ключа B->A (реверсированную геометрию)
                var keyF = MakeKey(a, b);
                if (!segmentReplacements.ContainsKey(keyF))
                    segmentReplacements[keyF] = extractedLine;

                var keyR = MakeKey(b, a);
                if (!segmentReplacements.ContainsKey(keyR))
                    segmentReplacements[keyR] = (LineString)extractedLine.Reverse();
            }
        }

        return segmentReplacements;
    }

    private string MakeKey(Coordinate a, Coordinate b)
    {
        // Округляем координаты до фиксированного числа знаков, чтобы избежать мелкой плавающей погрешности
        string fmt = "F" + KeyDecimalPlaces.ToString(CultureInfo.InvariantCulture);
        var s1 = a.X.ToString(fmt, CultureInfo.InvariantCulture) + ":" + a.Y.ToString(fmt, CultureInfo.InvariantCulture);
        var s2 = b.X.ToString(fmt, CultureInfo.InvariantCulture) + ":" + b.Y.ToString(fmt, CultureInfo.InvariantCulture);
        return s1 + "|" + s2;
    }

    private LineString CurveLine(LineString line, Polygon continentPolygon)
    {
        if (line.Length < _options.MinLineLenghtToCurve)
            return line;
        int points = (int)Math.Round(line.Length * _options.DistortionDetail, MidpointRounding.ToEven);
        if (points <= 0)
            return line;
        var newPoints = GetEquidistantPoints(line, points);
        if (newPoints.Count == 0)
            return line;

        var coords = line.Coordinates.ToList();
        var yOffsets = new double[newPoints.Count];
        var xOffsets = new double[newPoints.Count];

        if (Math.Abs(line.EndPoint.X - line.StartPoint.X) > _options.MinLineLenghtToCurve / 3)
        {
            yOffsets = GenerateCurveOffsets(newPoints.Count);
        }
        if (Math.Abs(line.EndPoint.Y - line.StartPoint.Y) > _options.MinLineLenghtToCurve / 3)
        {
            xOffsets = GenerateCurveOffsets(newPoints.Count);
        }

        var offsetDir = random.Next(0, 2) == 0 ? -1 : 1;
        for (int i = 0; i < newPoints.Count; i++)
        {
            newPoints[i].X += xOffsets[i] * offsetDir;
            newPoints[i].Y += yOffsets[i] * offsetDir;
            coords.Insert(i + 1, newPoints[i]);
        }
        var newLine = _factory.CreateLineString(coords.ToArray());

        if (!continentPolygon.Covers(newLine))
        {
            var clippedLine = newLine.Intersection(continentPolygon);
            if (clippedLine is MultiLineString mls)
            {
                var firstLine = mls.GetGeometryN(0);
                if (firstLine is LineString safeFirstLine)
                    newLine = safeFirstLine;
                else newLine = line;
            }
            else if (clippedLine is LineString safeLine)
                newLine = safeLine;
            else newLine = line;
        }

        return newLine;
    }

    private double[] GenerateCurveOffsets(int pointsCount)
    {
        double noiseStrength = 0.2;

        var result = new double[pointsCount];
        if (pointsCount < 0)
            return result;
        if (pointsCount <= 3)
        {
            for (int i = 0; i < pointsCount; i++)
            {
                result[i] = random.Next(-1, 2);
            }
        }
        else
        {
            int centerIndex = result.Length / 2;
            bool evenLength = result.Length % 2 == 0;

            double stretchFactor = 1.0 / (centerIndex * centerIndex);
            if (evenLength) stretchFactor = 1.0 / ((centerIndex - 0.5) * (centerIndex - 0.5));

            for (int i = 0; i < result.Length; i++)
            {
                double distance = i - centerIndex;
                if (evenLength) distance = i - centerIndex + 0.5;
                double value = _options.MaxOffst * (1 - stretchFactor * distance * distance);

                if (pointsCount > 9 && Math.Abs(distance) > 1)
                {
                    double noise = (random.NextDouble() - 0.5) * noiseStrength;
                    value += noise;
                }
                result[i] = Math.Max(value, 0.1);
            }

            if (evenLength)
            {
                result[centerIndex - 1] = Math.Max(result[centerIndex - 1], result[centerIndex]);
            }
        }
        return result;
    }

    private static List<Coordinate> GetEquidistantPoints(LineString line, int numberOfIntermediatePoints)
    {
        if (numberOfIntermediatePoints <= 0)
            throw new ArgumentException("Количество точек должно быть больше 0");

        var points = new List<Coordinate>();
        double totalLength = line.Length;
        double segmentLength = totalLength / (numberOfIntermediatePoints + 1);

        var locIndex = new LengthLocationMap(line);

        for (int i = 1; i <= numberOfIntermediatePoints; i++)
        {
            double distance = segmentLength * i;
            var location = locIndex.GetLocation(distance);
            points.Add(location.GetCoordinate(line));
        }

        return points;
    }
}