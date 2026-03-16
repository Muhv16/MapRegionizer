using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Simplify;
using System.Collections.Concurrent;


namespace MapRegionizer;


internal class MapBuilder
{
    private readonly GeometryFactory _factory;
    private readonly MapOptions _options;

    public List<Polygon> MapPolygons { get; private set; }

    public MapBuilder(GeometryFactory factory, MapOptions options)
    {
        _factory = factory;
        _options = options;
        MapPolygons = new List<Polygon>();
    }

    public void BuildMapFromCoords(List<Coordinate> terrainCoords)
    {
        var continents = FindContinents(terrainCoords)
            .Where(c => c.Length > 3);

        var bag = new ConcurrentBag<Polygon>();

        Parallel.ForEach(continents, continent =>
        {
            var rawPoly = BuildPolygonFromPixels(continent);
            if (rawPoly == null || rawPoly.ExteriorRing.NumPoints <= 3)
                return;

            var simp = DouglasPeuckerSimplifier.Simplify(rawPoly, _options.SimplifyTolerance);

            if (simp is Polygon p && p.ExteriorRing.NumPoints > 3)
                bag.Add(p);
        });

        MapPolygons = bag.ToList();
    }

    private List<Coordinate[]> FindContinents(List<Coordinate> terrainCoords)
    {
        var unvisited = new HashSet<Coordinate>(terrainCoords);
        var result = new List<Coordinate[]>();

        var dirs = new[]
        {
        new Coordinate( 1,  0),
        new Coordinate(-1,  0),
        new Coordinate( 0,  1),
        new Coordinate( 0, -1)
    };

        while (unvisited.Count > 0)
        {
            var start = unvisited.First();
            var queue = new Queue<Coordinate>();
            var continent = new List<Coordinate>();
            queue.Enqueue(start);
            unvisited.Remove(start);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                continent.Add(cur);
                foreach (var d in dirs)
                {
                    var nb = new Coordinate(cur.X + d.X, cur.Y + d.Y);
                    if (unvisited.Contains(nb))
                    {
                        unvisited.Remove(nb);
                        queue.Enqueue(nb);
                    }
                }
            }

            result.Add(continent.ToArray());
        }

        return result;
    }

    private Polygon BuildPolygonFromPixels(Coordinate[] pixels)
    {
        // 1) Для каждого пикселя создаём unit-квадрат [x,y]–[x+1,y+1]
        var rects = BuildRectanglesFromPixels(pixels);

        // 2) Union уже сильно меньшего числа геометрий
        var unioned = CascadedPolygonUnion.Union(rects);

        // 3) Собираем все Polygon'ы из результата (поддерживаем Polygon / MultiPolygon / GeometryCollection)
        var polygons = new List<Polygon>();
        if (unioned == null) throw new InvalidOperationException("Union produced null geometry.");

        if (unioned is Polygon polySingle)
        {
            polygons.Add(polySingle);
        }
        else if (unioned is MultiPolygon mpoly)
        {
            polygons.AddRange(mpoly.Geometries.OfType<Polygon>());
        }
        else if (unioned is GeometryCollection gcol)
        {
            for (int i = 0; i < gcol.NumGeometries; i++)
            {
                if (gcol.GetGeometryN(i) is Polygon p) polygons.Add(p);
            }
        }

        if (!polygons.Any())
            throw new InvalidOperationException("No polygons produced by union.");

        // 4) Выбираем самый большой полигон как внешний
        var outer = polygons.OrderByDescending(p => p.Area).First();

        // 5) Собираем внутренние кольца:
        var holeRings = new List<LinearRing>();

        // 5a) Уже существующие внутренние кольца у внешнего полигона
        for (int i = 0; i < outer.NumInteriorRings; i++)
        {
            var interior = outer.GetInteriorRingN(i);
            holeRings.Add(_factory.CreateLinearRing(interior.Coordinates));
        }
        // 6) Строим итоговый полигон через фабрику (outerRing + holes)
        var outerRing = _factory.CreateLinearRing(outer.ExteriorRing.Coordinates);
        var result = _factory.CreatePolygon(outerRing, holeRings.ToArray());

        return result;
    }

    private readonly record struct Segment(int X0, int X1);
    private sealed class ActiveRect
    {
        public int X0, X1;
        public int Y0;          // стартовая строка (включительно)
        public int Y1Exclusive; // конец по Y (исключительно)
    }

    /// <summary>
    /// Строит прямоугольники из пикселей:
    /// 1) RLE по X в каждой строке Y
    /// 2) вертикально склеивает одинаковые сегменты (X0..X1) на соседних строках
    /// </summary>
    private List<Geometry> BuildRectanglesFromPixels(Coordinate[] pixels)
    {
        // 0) Убираем дубли и приводим к целым координатам (предполагаем, что пиксели лежат в целых)
        var uniq = new HashSet<(int x, int y)>();
        foreach (var p in pixels)
        {
            int x = (int)p.X;
            int y = (int)p.Y;
            uniq.Add((x, y));
        }

        // 1) Группируем X по строкам Y
        var rows = new Dictionary<int, List<int>>();
        foreach (var (x, y) in uniq)
        {
            if (!rows.TryGetValue(y, out var list))
            {
                list = new List<int>();
                rows[y] = list;
            }
            list.Add(x);
        }

        // 2) Идём по Y в порядке возрастания, строим сегменты и склеиваем вертикально
        var ys = rows.Keys.OrderBy(v => v).ToArray();

        var active = new Dictionary<Segment, ActiveRect>(); // сегмент -> активный прямоугольник
        var rectangles = new List<Geometry>();

        void FlushActiveNotIn(HashSet<Segment> stillActiveSegments)
        {
            // всё, что не продлилось на текущей строке — закрываем и превращаем в геометрию
            var toClose = active.Keys.Where(s => !stillActiveSegments.Contains(s)).ToList();
            foreach (var seg in toClose)
            {
                rectangles.Add(CreateRectanglePolygon(active[seg]));
                active.Remove(seg);
            }
        }

        int prevY = int.MinValue;

        foreach (var y in ys)
        {
            // Если есть "дырка" по Y (нет строк между prevY и y), то активные прямоугольники надо закрыть
            if (prevY != int.MinValue && y != prevY + 1 && active.Count > 0)
            {
                foreach (var ar in active.Values)
                    rectangles.Add(CreateRectanglePolygon(ar));
                active.Clear();
            }

            var xs = rows[y];
            xs.Sort();

            // 2a) RLE: строим сегменты [x0..x1] по непрерывным x
            var segmentsThisRow = new List<Segment>();
            int i = 0;
            while (i < xs.Count)
            {
                int x0 = xs[i];
                int x1 = x0;
                i++;
                while (i < xs.Count && xs[i] == x1 + 1)
                {
                    x1 = xs[i];
                    i++;
                }
                segmentsThisRow.Add(new Segment(x0, x1));
            }

            // 2b) Продлеваем/создаём активные прямоугольники
            var stillActive = new HashSet<Segment>();
            foreach (var seg in segmentsThisRow)
            {
                stillActive.Add(seg);

                if (active.TryGetValue(seg, out var ar))
                {
                    // сегмент тот же, строка соседняя => увеличиваем высоту
                    ar.Y1Exclusive = y + 1;
                }
                else
                {
                    // новый прямоугольник высотой 1 строка: [y..y+1)
                    active[seg] = new ActiveRect
                    {
                        X0 = seg.X0,
                        X1 = seg.X1,
                        Y0 = y,
                        Y1Exclusive = y + 1
                    };
                }
            }

            // 2c) Закрываем те, которые не продлились
            FlushActiveNotIn(stillActive);

            prevY = y;
        }

        // 3) Закрываем остатки
        foreach (var ar in active.Values)
            rectangles.Add(CreateRectanglePolygon(ar));

        return rectangles;

        Geometry CreateRectanglePolygon(ActiveRect ar)
        {
            // ar.X0..ar.X1 включительно => по X правая граница = X1 + 1
            // ar.Y0..ar.Y1Exclusive-1 => по Y верхняя граница = Y1Exclusive
            double sz = _options.PixelSize;

            double x0 = ar.X0 * sz;
            double x1 = (ar.X1 + 1) * sz;
            double y0 = ar.Y0 * sz;
            double y1 = ar.Y1Exclusive * sz;

            var ring = _factory.CreateLinearRing(new[]
            {
                    new Coordinate(x0, y0),
                    new Coordinate(x1, y0),
                    new Coordinate(x1, y1),
                    new Coordinate(x0, y1),
                    new Coordinate(x0, y0)
                });

            return _factory.CreatePolygon(ring);
        }
    }

    /// <summary>
    /// Возвращает список полигонов водной поверхности (фон + дыры/озера).
    /// </summary>
    public List<Polygon> BuildSeaPolygons(IEnumerable<Polygon> continentPolygons, int imageWidth, int imageHeight, double pixelSize, GeometryFactory factory, double simplifyTolerance = 0.0)
    {
        // 1) Создаём прямоугольник, покрывающий всю маску
        double x0 = 0;
        double y0 = 0;
        double x1 = imageWidth * pixelSize;
        double y1 = imageHeight * pixelSize;

        var rectRing = factory.CreateLinearRing(new[]
        {
            new Coordinate(x0, y0),
            new Coordinate(x1, y0),
            new Coordinate(x1, y1),
            new Coordinate(x0, y1),
            new Coordinate(x0, y0)
        });
        var fullRect = factory.CreatePolygon(rectRing);

        // 2) Объединяем континенты в одну геометрию (если есть)
        Geometry? unionedContinents = null;
        var continentList = continentPolygons?.ToList() ?? new List<Polygon>();
        if (continentList.Count == 0)
        {
            unionedContinents = factory.CreateGeometryCollection(null);
        }
        else
        {
            if (simplifyTolerance > 0)
                continentList = continentList.Select(p => (Polygon)DouglasPeuckerSimplifier.Simplify(p, simplifyTolerance)).ToList();

            unionedContinents = CascadedPolygonUnion.Union(continentList.Cast<Geometry>().ToList()) ?? factory.CreateGeometryCollection(null);

            // возможно исправление топологии (если union вернул невалидную геометрию)
            if (!unionedContinents.IsValid)
                unionedContinents = unionedContinents.Buffer(0);
        }

        // 3) Вычитаем континенты из прямоугольника — получаем водную поверхность
        var seaGeom = fullRect.Difference(unionedContinents);

        if (seaGeom == null || seaGeom.IsEmpty)
            return new List<Polygon>();

        // 4) На всякий случай починим валидность
        if (!seaGeom.IsValid)
            seaGeom = seaGeom.Buffer(0);

        // 5) Развернём результат в список Polygon
        var seaPolygons = new List<Polygon>();
        switch (seaGeom)
        {
            case Polygon p: seaPolygons.Add(p); break;
            case MultiPolygon mp:
                seaPolygons.AddRange(mp.Geometries.OfType<Polygon>());
                break;
            case GeometryCollection gc:
                for (int i = 0; i < gc.NumGeometries; i++)
                    if (gc.GetGeometryN(i) is Polygon p2)
                        seaPolygons.Add(p2);
                break;
        }

        return seaPolygons;
    }

}
