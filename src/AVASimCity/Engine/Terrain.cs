namespace AVASimCity.Engine;

/// <summary>The kinds of land a new map can be: what water it has.</summary>
public readonly record struct Lay(bool River, int CoastSides, int Lakes, double Woods);

/// <summary>
/// New land for a city: bare earth with woods in clumps, and water: a coast along one or two sides, a river winding
/// across, a lake or two. The same seed always makes the same land.
/// </summary>
public static class Terrain
{
    public static Lay LayFor(int seed)
    {
        var d = new Dice(seed * 977 + 3);
        return new Lay(d.Chance(0.75), d.Chance(0.6) ? 1 + d.Next(2) : 0, d.Next(3), 0.3 + d.Next() * 0.25);
    }

    public static void Make(World w, int seed) => Make(w, seed, LayFor(seed));

    public static void Make(World w, int seed, Lay lay)
    {
        var d = new Dice(seed * 7919 + 11);
        const int W = Field.W, H = Field.H;
        var water = new bool[W, H];
        // The coast: the sea along a side, its edge wandering.
        var sides = new List<int> { 0, 1, 2, 3 };
        for (int k = 0; k < lay.CoastSides && sides.Count > 0; k++)
        {
            int side = sides[d.Next(sides.Count)];
            sides.Remove(side);
            sides.Remove((side + 2) % 4);
            var edge = Noise1(d, side % 2 == 0 ? H : W, 14);
            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                int along = side % 2 == 0 ? y : x;
                int depth = side switch { 0 => x, 1 => y, 2 => W - 1 - x, _ => H - 1 - y };
                if (depth < 6 + edge[along] * 12) water[x, y] = true;
            }
        }
        // The river: across the map from one side to the other, meandering, wider in places.
        if (lay.River)
        {
            bool across = d.Chance(0.5);
            int len = across ? W : H, span = across ? H : W;
            double pos = span * (0.3 + d.Next() * 0.4), drift = 0;
            var width = Noise1(d, len, 20);
            for (int t = 0; t < len; t++)
            {
                drift = Math.Clamp(drift + (d.Next() - 0.5) * 0.35, -0.9, 0.9);
                pos = Math.Clamp(pos + drift, span * 0.15, span * 0.85);
                double half = 1.2 + width[t] * 2.2;
                for (int o = (int)(pos - half); o <= (int)(pos + half); o++)
                {
                    int x = across ? t : o, y = across ? o : t;
                    if (Field.In(x, y)) water[x, y] = true;
                }
            }
        }
        // Lakes.
        for (int k = 0; k < lay.Lakes; k++)
        {
            double cx = 15 + d.Next() * (W - 30), cy = 15 + d.Next() * (H - 30), r = 3 + d.Next() * 5;
            for (int x = (int)(cx - r * 1.6); x <= cx + r * 1.6; x++)
            for (int y = (int)(cy - r * 1.6); y <= cy + r * 1.6; y++)
            {
                if (!Field.In(x, y)) continue;
                double q = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy) * 1.4) / r;
                if (q + Hash(x, y, seed) * 0.35 < 1) water[x, y] = true;
            }
        }
        // Smooth away lone tiles of water and land.
        for (int pass = 0; pass < 2; pass++)
        {
            var next = (bool[,])water.Clone();
            for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                int n = 0;
                for (int u = x - 1; u <= x + 1; u++)
                for (int v = y - 1; v <= y + 1; v++)
                    if ((u, v) != (x, y) && (Field.In(u, v) ? water[u, v] : water[x, y])) n++;
                if (n >= 6) next[x, y] = true;
                else if (n <= 2) next[x, y] = false;
            }
            water = next;
        }
        // Woods: clumps where a rolling noise runs high, thicker by the water.
        var woods = Noise2(d, 10);
        var fine = Noise2(d, 4);
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            w.Net[x, y] = Net.None;
            w.Timer[x, y] = 0;
            if (water[x, y]) { w.Ground[x, y] = Ground.Water; continue; }
            double t = woods[x, y] * 0.75 + fine[x, y] * 0.25;
            w.Ground[x, y] = t > 1 - lay.Woods ? Ground.Trees : Ground.Land;
        }
    }

    private static double Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65535.0;
    }

    /// <summary>A smooth wander between 0 and 1 along a line, with a knot every <paramref name="every"/>.</summary>
    private static double[] Noise1(Dice d, int n, int every)
    {
        var knots = new double[n / every + 2];
        for (int i = 0; i < knots.Length; i++) knots[i] = d.Next();
        var a = new double[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / every, f = t - Math.Floor(t);
            f = f * f * (3 - 2 * f);
            a[i] = knots[(int)t] * (1 - f) + knots[(int)t + 1] * f;
        }
        return a;
    }

    /// <summary>A smooth rolling field between 0 and 1 over the map.</summary>
    private static double[,] Noise2(Dice d, int every)
    {
        int gw = Field.W / every + 2, gh = Field.H / every + 2;
        var g = new double[gw, gh];
        for (int i = 0; i < gw; i++)
        for (int j = 0; j < gh; j++) g[i, j] = d.Next();
        var a = new double[Field.W, Field.H];
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
        {
            double tx = (double)x / every, ty = (double)y / every;
            int i = (int)tx, j = (int)ty;
            double fx = tx - i, fy = ty - j;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            a[x, y] = (g[i, j] * (1 - fx) + g[i + 1, j] * fx) * (1 - fy) + (g[i, j + 1] * (1 - fx) + g[i + 1, j + 1] * fx) * fy;
        }
        return a;
    }
}
