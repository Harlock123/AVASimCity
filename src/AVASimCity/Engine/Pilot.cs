namespace AVASimCity.Engine;

/// <summary>Something the mayor does: a tool used at a tile.</summary>
public readonly record struct Deed(Tool Tool, int X, int Y);

/// <summary>
/// The computer mayor, who builds a city by itself in the demonstration (and lays out the scenarios' cities). It
/// plans a grid of blocks seven tiles apart, each block four zones round a crossroads of roads with a power line
/// across every road, and fills the slots nearest the middle first: industry on one side of town, shops in the
/// middle, homes all round, each by the demand for it. It puts down a power plant before anything else and another
/// before the power runs short; a police station or a fire station where the city is going without; a stadium,
/// a seaport and an airport when the city asks for them. Whatever a disaster knocks down, it builds again.
/// </summary>
public sealed class Pilot
{
    /// <summary>How long between deeds, in seconds of the city's time.</summary>
    public const double Pace = 0.2;

    private (int X, int Y)? _origin;
    private (double X, double Y) _industry = (1, 0);
    private List<(int Bx, int By, int Q)> _slots = [];
    private readonly HashSet<(int, int, int)> _bad = [];
    private double _wait;
    private int _lastMonth = -1;

    /// <summary>Buildings it won't put up (a scenario's city without police, say), and the power plant it prefers.</summary>
    public HashSet<ZoneKind> Forbidden { get; } = [];
    public ZoneKind? PreferredPlant { get; set; }

    /// <summary>The last thing it did, for the camera to follow in the demonstration.</summary>
    public Deed? Last { get; private set; }

    public void Reset()
    {
        _origin = null;
        _slots = [];
        _bad.Clear();
        _wait = 0;
        _lastMonth = -1;
        Last = null;
    }

    /// <summary>Called every step: a deed when it's time for one, used straight away. Free: the money isn't spent.</summary>
    public Outcome? Step(World w, bool free = false)
    {
        _wait -= Field.Step;
        if (_wait > 0) return null;
        _wait = Pace;
        return Act(w, free);
    }

    /// <summary>Decides and does one thing; null if there's nothing to do (or no money to do it).</summary>
    public Outcome? Act(World w, bool free = false)
    {
        if (w.Month != _lastMonth)
        {
            _lastMonth = w.Month;
            Manage(w);
            if (w.Month % 24 == 0) _bad.Clear();
        }
        var deed = Decide(w, free);
        if (deed is not { } d) return null;
        var outcome = Sim.Apply(w, d.Tool, d.X, d.Y, free);
        if (outcome == Outcome.Done) Last = d;
        else if (outcome == Outcome.Blocked && _working is { } s) _bad.Add(s);
        return outcome;
    }

    /// <summary>The budget: a middling tax, more when money is short, and every service paid in full.</summary>
    private static void Manage(World w)
    {
        w.TaxRate = w.Funds < 1000 ? 9 : w.Funds > 40000 ? 6 : 7;
        for (int i = 0; i < 3; i++) w.Funding[i] = 100;
        w.AutoBudget = true;
    }

    private (int, int, int)? _working;
    private bool _anyRoad;
    private readonly bool[,] _main = new bool[Field.W, Field.H];

    /// <summary>The roads joined up to the ones nearest the middle of the plan: the city's own network, not an island of road off on its own.</summary>
    private void MainRoads(World w)
    {
        Array.Clear(_main);
        var (ox, oy) = _origin!.Value;
        (int X, int Y)? start = null;
        int best = int.MaxValue;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
        {
            if ((w.Net[x, y] & Net.Road) == 0) continue;
            int d = Math.Abs(x - ox) + Math.Abs(y - oy);
            if (d < best) { best = d; start = (x, y); }
        }
        _anyRoad = start != null;
        if (start is not { } s) return;
        var q = new Queue<(int X, int Y)>();
        q.Enqueue(s);
        _main[s.X, s.Y] = true;
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int u = x + Sim.Dx[d], v = y + Sim.Dy[d];
                if (!Field.In(u, v) || _main[u, v] || (w.Net[u, v] & (Net.Road | Net.Rail)) == 0) continue;
                _main[u, v] = true;
                q.Enqueue((u, v));
            }
        }
    }

    public Deed? Decide(World w, bool free = false)
    {
        _working = null;
        if (_origin == null) Plan(w);
        MainRoads(w);
        long money = free ? long.MaxValue / 4 : w.Funds;
        // Rubble on the zones' land is cleared as they're rebuilt; the power comes first.
        bool plant = w.Zones.Any(z => Sim.IsPlant(z.Kind));
        if (!plant || w.PowerCapacity > 0 && w.PowerUsed + 90 > w.PowerCapacity && w.PowerDirty == false)
        {
            var kind = PreferredPlant ?? (plant && money > 9000 && w.Residents > 20000 ? ZoneKind.Nuclear : ZoneKind.Coal);
            if (money < Sim.Cost(Sim.ToolFor(kind)) + 200) return null;
            if (Special(w, kind) is { } d) return d;
        }
        if (Repairs(w) is { } fix) return fix;
        if (!w.PowerDirty && w.PowerUsed + 30 < w.PowerCapacity && w.Month % 3 == 0)
            foreach (var z in w.Zones)
                if (!z.Powered && (z.Kind == ZoneKind.Seaport || z.Grows) && WireUp(w, z) is { } line) return line;
        int growing = w.Zones.Count(z => z.Grows && z.Level > 0);
        if (w.Residents > 1000 && !Forbidden.Contains(ZoneKind.Police) && Needs(w, ZoneKind.Police, growing, 22) && money > 800 && Station(w, ZoneKind.Police) is { } police) return police;
        if (w.Residents > 1600 && !Forbidden.Contains(ZoneKind.FireStation) && Needs(w, ZoneKind.FireStation, growing, 24) && money > 800 && Station(w, ZoneKind.FireStation) is { } fire) return fire;
        if (w.Residents > Sim.StadiumNeeded * 0.85 && !w.Zones.Any(z => z.Kind == ZoneKind.Stadium) && money > 3500 && Special(w, ZoneKind.Stadium) is { } st) return st;
        if (w.IndJobs > Sim.SeaportNeeded * 0.8 && !w.Zones.Any(z => z.Kind == ZoneKind.Seaport) && money > 5500 && Seaport(w) is { } sp) return sp;
        if (w.ComJobs > Sim.AirportNeeded * 0.8 && !w.Zones.Any(z => z.Kind == ZoneKind.Airport) && money > 10500 && Special(w, ZoneKind.Airport) is { } ap) return ap;
        if (money < 260) return null;
        return Grow(w, free);
    }

    // ───────────────────────────── the plan ─────────────────────────────

    /// <summary>Where the grid goes: on the most open land near the middle of the map. Industry goes the way there's most room.</summary>
    private void Plan(World w)
    {
        (int, int) best = (Field.W / 2, Field.H / 2);
        double most = double.MinValue;
        for (int x = 14; x < Field.W - 14; x += 3)
        for (int y = 14; y < Field.H - 14; y += 3)
        {
            int land = 0;
            for (int u = x - 12; u <= x + 12; u += 2)
            for (int v = y - 12; v <= y + 12; v += 2)
                if (Field.In(u, v) && w.Ground[u, v] != Ground.Water) land++;
            double score = land - 0.25 * (Math.Abs(x - Field.W / 2) + Math.Abs(y - Field.H / 2));
            if (score > most) { most = score; best = (x, y); }
        }
        _origin = best;
        // Industry to the side with the most land.
        (double, double)[] ways = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        double room = -1;
        foreach (var (dx, dy) in ways)
        {
            int land = 0;
            for (int r = 8; r < 40; r += 2)
            for (int s = -10; s <= 10; s += 2)
            {
                int u = best.Item1 + (int)(dx * r - dy * s), v = best.Item2 + (int)(dy * r + dx * s);
                if (Field.In(u, v) && w.Ground[u, v] != Ground.Water) land++;
            }
            if (land > room) { room = land; _industry = (dx, dy); }
        }
        var (ox, oy) = best;
        _slots = [];
        for (int bx = -ox / 7; bx <= (Field.W - ox) / 7; bx++)
        for (int by = -oy / 7; by <= (Field.H - oy) / 7; by++)
        for (int q = 0; q < 4; q++)
        {
            var (x, y) = SlotCorner(bx, by, q);
            if (x >= 2 && y >= 2 && x + 3 <= Field.W - 2 && y + 3 <= Field.H - 2) _slots.Add((bx, by, q));
        }
        _slots.Sort((a, b) => Dist(a).CompareTo(Dist(b)));
    }

    private double Dist((int Bx, int By, int Q) s)
    {
        var (x, y) = SlotCorner(s.Bx, s.By, s.Q);
        var (ox, oy) = _origin!.Value;
        return Math.Sqrt((x + 1.5 - ox) * (x + 1.5 - ox) + (y + 1.5 - oy) * (y + 1.5 - oy) * 1.2) + (x + y) * 0.001;
    }

    private (int X, int Y) BlockInside(int bx, int by) => (_origin!.Value.X + 7 * bx + 1, _origin!.Value.Y + 7 * by + 1);

    private (int X, int Y) SlotCorner(int bx, int by, int q)
    {
        var (ix, iy) = BlockInside(bx, by);
        return (ix + 3 * (q % 2), iy + 3 * (q / 2));
    }

    /// <summary>The road tiles along a slot's two outer sides, corner included, and where the power line crosses each.</summary>
    private IEnumerable<(int X, int Y, bool Wire)> SlotRoads(int bx, int by, int q)
    {
        var (ox, oy) = _origin!.Value;
        int qx = q % 2, qy = q / 2;
        var (zx, zy) = SlotCorner(bx, by, q);
        int ry = oy + 7 * by + (qy == 0 ? 0 : 7), rx = ox + 7 * bx + (qx == 0 ? 0 : 7);
        for (int i = 0; i < 4; i++)
        {
            int x = ox + 7 * bx + 3 * qx + (qx == 0 ? i : i + 1);
            yield return (x, ry, x == zx + 1);
        }
        for (int i = 0; i < 4; i++)
        {
            int y = oy + 7 * by + 3 * qy + (qy == 0 ? i : i + 1);
            yield return (rx, y, y == zy + 1);
        }
    }

    /// <summary>The next road or power line a slot still needs, if any.</summary>
    private static Deed? Missing(World w, IEnumerable<(int X, int Y, bool Wire)> roads, bool bridges)
    {
        foreach (var (x, y, wire) in roads)
        {
            if (!Field.In(x, y)) continue;
            var g = w.Ground[x, y];
            if (g is Ground.Built or Ground.Fire or Ground.Flood or Ground.Radiation) continue;
            if (g == Ground.Water)
            {
                if (bridges && w.Net[x, y] == Net.None) return new Deed(Tool.Road, x, y);
                continue;
            }
            if (!w.Has(x, y, Net.Road)) return new Deed(Tool.Road, x, y);
            if (wire && !w.Has(x, y, Net.Wire)) return new Deed(Tool.Wire, x, y);
        }
        return null;
    }

    /// <summary>A slot being made into a park, not yet finished.</summary>
    private static bool Park(World w, int x0, int y0)
    {
        bool any = false, all = true;
        for (int x = x0; x < x0 + 3; x++)
        for (int y = y0; y < y0 + 3; y++)
        {
            if (w.Ground[x, y] == Ground.Park) any = true;
            else if (!Sim.Clearable(w, x, y)) return false;
            else all = false;
        }
        return any && !all;
    }

    private static bool Free(World w, int x0, int y0, int s)
    {
        for (int x = x0; x < x0 + s; x++)
        for (int y = y0; y < y0 + s; y++)
            if (!Sim.Clearable(w, x, y)) return false;
        return true;
    }

    /// <summary>A zone (or building) of the planner's own sitting in the slot.</summary>
    private static Zone? Occupant(World w, int x, int y) => w.ZoneAt(x + 1, y + 1);

    private bool Connected(World w, int bx, int by, int q)
    {
        if (!_anyRoad) return true;
        foreach (var (x, y, _) in SlotRoads(bx, by, q))
            for (int d = -1; d < 4; d++)
            {
                int u = d < 0 ? x : x + Sim.Dx[d], v = d < 0 ? y : y + Sim.Dy[d];
                if (Field.In(u, v) && _main[u, v]) return true;
            }
        return false;
    }

    // ───────────────────────────── building ─────────────────────────────

    /// <summary>The roads and power lines round the zones already there, put back after a disaster.</summary>
    private Deed? Repairs(World w)
    {
        foreach (var s in _slots)
        {
            var (x, y) = SlotCorner(s.Bx, s.By, s.Q);
            var z = Occupant(w, x, y);
            if (z == null || _bad.Contains(s)) continue;
            if (z.X == x && z.Y == y && z.Size == 3 || z.Size > 3)
            {
                _working = s;
                if (Missing(w, SlotRoads(s.Bx, s.By, s.Q), w.Funds > 2000) is { } d) return d;
            }
        }
        _working = null;
        return null;
    }

    /// <summary>The kind of zone for a slot: by the demand, with industry kept to its side of town and shops to the middle.</summary>
    private ZoneKind? KindFor(World w, int x, int y)
    {
        var (ox, oy) = _origin!.Value;
        double rx = x + 1.5 - ox, ry = y + 1.5 - oy, dist = Math.Sqrt(rx * rx + ry * ry), along = rx * _industry.X + ry * _industry.Y;
        bool industrial = along > 9 && Math.Abs(rx * _industry.Y - ry * _industry.X) < along * 0.7 + 6;
        int Empty(ZoneKind k) => w.Zones.Count(z => z.Kind == k && z.Level == 0 && z.Powered && z.Reached);
        var options = new List<(ZoneKind K, double Score)>();
        if (industrial)
        {
            options.Add((ZoneKind.Industrial, w.DemandI + 0.3));
            options.Add((ZoneKind.Commercial, w.DemandC - 0.3));
            options.Add((ZoneKind.Residential, w.DemandR - 0.4));
        }
        else
        {
            options.Add((ZoneKind.Residential, w.DemandR + (dist > 10 ? 0.15 : 0)));
            options.Add((ZoneKind.Commercial, w.DemandC + (dist < 14 ? 0.25 : 0.05)));
            if (w.Zones.All(z => z.Kind != ZoneKind.Industrial)) options.Add((ZoneKind.Industrial, w.DemandI - 0.2));
        }
        var (kind, score) = options.MaxBy(o => o.Score);
        if (score < -0.15 || Empty(kind) >= 3 + w.Zones.Count / 30) return null;
        return kind;
    }

    /// <summary>The nearest slot to fill: its roads and power lines, then the zone.</summary>
    private Deed? Grow(World w, bool free)
    {
        int tried = 0;
        foreach (var s in _slots)
        {
            if (_bad.Contains(s)) continue;
            var (x, y) = SlotCorner(s.Bx, s.By, s.Q);
            if (Occupant(w, x, y) != null || !Free(w, x, y, 3) && !Park(w, x, y)) continue;
            if (++tried > 240) break;
            if (!Connected(w, s.Bx, s.By, s.Q)) continue;
            if (KindFor(w, x, y) is not { } kind) continue;
            _working = s;
            if (Missing(w, SlotRoads(s.Bx, s.By, s.Q), w.Funds > 3000) is { } d) return d;
            // Now and then a park in place of homes, once the city can afford it.
            if (kind == ZoneKind.Residential && (s.Bx * 7 + s.By * 13 + s.Q * 5) % 11 == 3 && (free || w.Funds > 4000))
            {
                for (int u = x; u < x + 3; u++)
                for (int v = y; v < y + 3; v++)
                    if (w.Ground[u, v] != Ground.Park) return new Deed(Tool.Park, u, v);
            }
            return new Deed(Sim.ToolFor(kind), x + 1, y + 1);
        }
        _working = null;
        return null;
    }

    private static bool Needs(World w, ZoneKind station, int growing, int per)
    {
        int have = w.Zones.Count(z => z.Kind == station);
        return have * per < growing;
    }

    /// <summary>A police or fire station in the free slot nearest the zones its service reaches least.</summary>
    private Deed? Station(World w, ZoneKind kind)
    {
        var cover = kind == ZoneKind.Police ? w.PoliceCover : w.FireCover;
        Zone? worst = null;
        double least = double.MaxValue;
        foreach (var z in w.Zones)
        {
            if (!z.Grows || z.Level == 0) continue;
            double c = cover[(int)z.Centre.X, (int)z.Centre.Y] - z.Level * 4;
            if (c < least) { least = c; worst = z; }
        }
        if (worst == null || least > 120) return null;
        (int, int, int)? pick = null;
        double best = double.MaxValue;
        foreach (var s in _slots)
        {
            if (_bad.Contains(s)) continue;
            var (x, y) = SlotCorner(s.Bx, s.By, s.Q);
            if (Occupant(w, x, y) != null || !Free(w, x, y, 3) || !Connected(w, s.Bx, s.By, s.Q)) continue;
            double d = Math.Abs(x + 1.5 - worst.Centre.X) + Math.Abs(y + 1.5 - worst.Centre.Y);
            if (d < best) { best = d; pick = s; }
        }
        if (pick is not { } p || best > 20) return null;
        _working = p;
        if (Missing(w, SlotRoads(p.Item1, p.Item2, p.Item3), true) is { } road) return road;
        var (px, py) = SlotCorner(p.Item1, p.Item2, p.Item3);
        return new Deed(Sim.ToolFor(kind), px + 1, py + 1);
    }

    /// <summary>A big building in a whole free block, its roads all round and its power lines out to them.</summary>
    private Deed? Special(World w, ZoneKind kind)
    {
        foreach (var s in _slots)
        {
            if (s.Q != 0 || _bad.Contains(s)) continue;
            var (ix, iy) = BlockInside(s.Bx, s.By);
            if (!Free(w, ix, iy, 6)) continue;
            var (ox, oy) = _origin!.Value;
            double along = (ix + 3 - ox) * _industry.X + (iy + 3 - oy) * _industry.Y;
            bool dirty = kind is ZoneKind.Coal or ZoneKind.Nuclear;
            if (dirty && along < 6 && w.Zones.Count > 0) continue;
            if (!dirty && along > 9) continue;
            if (!Connected(w, s.Bx, s.By, 0) && !Connected(w, s.Bx, s.By, 3)) continue;
            _working = s;
            for (int q = 0; q < 4; q++)
                if (Missing(w, SlotRoads(s.Bx, s.By, q), true) is { } d) return d;
            int size = Sim.SizeOf(kind);
            return new Deed(Sim.ToolFor(kind), size == 6 ? ix + 2 : ix + 1, size == 6 ? iy + 2 : iy + 1);
        }
        // Nowhere free: if a building just went in, run its power lines to the roads round it.
        return Links(w);
    }

    /// <summary>The power lines from a four-by-four building in its block out to the crossings in the roads.</summary>
    private Deed? Links(World w)
    {
        foreach (var z in w.Zones)
        {
            if (z.Size != 4) continue;
            foreach (var (x, y) in new[] { (z.X + 1, z.Y + 4), (z.X + 1, z.Y + 5), (z.X + 4, z.Y + 1), (z.X + 5, z.Y + 1) })
                if (Field.In(x, y) && !w.Has(x, y, Net.Wire) && w.Ground[x, y] is Ground.Land or Ground.Trees or Ground.Rubble or Ground.Park && (w.Net[x, y] & Net.Rail) == 0)
                    return new Deed(Tool.Wire, x, y);
        }
        return null;
    }

    /// <summary>A seaport on the shore near town, with a power line to the nearest powered tile.</summary>
    private Deed? Seaport(World w)
    {
        var (ox, oy) = _origin!.Value;
        (int, int)? best = null;
        double bd = double.MaxValue;
        for (int x = 2; x < Field.W - 2; x++)
        for (int y = 2; y < Field.H - 2; y++)
        {
            double d = Math.Abs(x - ox) + Math.Abs(y - oy);
            if (d > 45 || d >= bd || !Sim.CanBuild(w, ZoneKind.Seaport, x, y)) continue;
            bd = d;
            best = (x, y);
        }
        if (best is not { } b) return null;
        return new Deed(Tool.Seaport, b.Item1, b.Item2);
    }

    /// <summary>The next tile of power line from an unpowered building to the powered network, by the shortest way over land.</summary>
    public static Deed? WireUp(World w, Zone z)
    {
        var q = new Queue<(int X, int Y)>();
        var from = new Dictionary<(int, int), (int, int)>();
        foreach (var e in Sim.Edge(z))
            if (Field.In(e.X, e.Y)) { q.Enqueue(e); from[e] = (-1, -1); }
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            if (w.Power[x, y] && (w.Ground[x, y] == Ground.Built || w.Has(x, y, Net.Wire)))
            {
                // Walk back to the building and lay the first missing tile.
                for (var at = from[(x, y)]; at.Item1 >= 0; at = from[at])
                    if (!w.Has(at.Item1, at.Item2, Net.Wire)) return new Deed(Tool.Wire, at.Item1, at.Item2);
                return null;
            }
            if (from.Count > 3000) break;
            for (int d = 0; d < 4; d++)
            {
                int u = x + Sim.Dx[d], v = y + Sim.Dy[d];
                if (!Field.In(u, v) || from.ContainsKey((u, v)) || z.Covers(u, v)) continue;
                var g = w.Ground[u, v];
                bool ok = g == Ground.Built && w.Power[u, v] || g is Ground.Land or Ground.Trees or Ground.Rubble && w.Net[u, v] != (Net.Road | Net.Rail);
                if (!ok) continue;
                from[(u, v)] = (x, y);
                q.Enqueue((u, v));
            }
        }
        return null;
    }
}
