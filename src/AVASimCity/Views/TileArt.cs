using AVASimCity.Engine;

namespace AVASimCity.Views;

/// <summary>
/// The map's pictures, all drawn here pixel by pixel: grass, woods, water with its beaches, roads, railways and
/// power lines that join up with their neighbours, bridges, rubble and the rest; and every zone at every level
/// of growth, rich or poor, in four looks, from an empty lot to houses, apartments and towers, shops to
/// skyscrapers, sheds to refineries; and the stations, plants, stadium, seaport and airport. Each tile has a key
/// that says everything about how it looks, so a tile is drawn again only when its key changes.
/// </summary>
public static class TileArt
{
    public const int T = 16;
    private static readonly Dictionary<ulong, uint[]> Tiles = [];
    private static readonly Dictionary<(ZoneKind, int, int, int), Pix> Lots = [];
    private static readonly Lock Guard = new();

    // The palette.
    private const uint Grass = 0xFF6A9A48, GrassDark = 0xFF5A8A3C, Sand = 0xFFDCCB92, WetSand = 0xFFC2AE78;
    private const uint Sea = 0xFF2F62A6, Shallow = 0xFF4A86C4, Foam = 0xFFB8D8F0;
    private const uint Leaf = 0xFF2F6E2C, LeafMid = 0xFF3D8436, LeafLight = 0xFF5AA448;
    private const uint Asphalt = 0xFF55585F, Kerb = 0xFF9A9CA2, Dash = 0xFFE8D060;
    private const uint Ballast = 0xFF8A7C6C, Sleeper = 0xFF5A4634, Steel = 0xFFC8CCD4;
    private const uint Pole = 0xFF6B4A2A, Cable = 0xFF262628;
    private const uint Dirt = 0xFF9E8A68, Pavement = 0xFFB4B0A8, Concrete = 0xFFA49C8C;

    private const int N = 1, E = 2, S = 4, Wd = 8, NE = 16, SE = 32, SW = 64, NW = 128;

    // ───────────────────────────── keys ─────────────────────────────

    private static int Mask(World w, int x, int y, Func<int, int, bool> f, bool diagonals)
    {
        int m = 0;
        if (f(x, y - 1)) m |= N;
        if (f(x + 1, y)) m |= E;
        if (f(x, y + 1)) m |= S;
        if (f(x - 1, y)) m |= Wd;
        if (diagonals)
        {
            if (f(x + 1, y - 1)) m |= NE;
            if (f(x + 1, y + 1)) m |= SE;
            if (f(x - 1, y + 1)) m |= SW;
            if (f(x - 1, y - 1)) m |= NW;
        }
        return m;
    }

    private static int Variant(int x, int y, int n) => (int)(Pix.Rand(x, y, 77) * n);

    /// <summary>Everything about how a tile looks, in one number.</summary>
    public static ulong Key(World w, int x, int y)
    {
        var g = w.Ground[x, y];
        var net = w.Net[x, y];
        ulong v = (uint)Variant(x, y, 8);
        bool Wet(int u, int t) => Field.In(u, t) && w.Ground[u, t] == Ground.Water;
        if (g == Ground.Built)
        {
            var z = w.ZoneAt(x, y)!;
            ulong k = (ulong)z.Kind | (ulong)(uint)z.Level << 4 | (ulong)(uint)z.Wealth << 8 | (ulong)(uint)(z.Variant & 3) << 10 |
                      (ulong)(uint)(x - z.X) << 12 | (ulong)(uint)(y - z.Y) << 16;
            return 10UL << 56 | k;
        }
        if (net != Net.None)
        {
            bool Has(int u, int t, Net n) => Field.In(u, t) && (w.Net[u, t] & n) != 0;
            int road = Mask(w, x, y, (u, t) => Has(u, t, Net.Road), false);
            int rail = Mask(w, x, y, (u, t) => Has(u, t, Net.Rail), false);
            int wire = Mask(w, x, y, (u, t) => Has(u, t, Net.Wire) || Field.In(u, t) && w.Ground[u, t] == Ground.Built, false);
            int water = g == Ground.Water ? 0 : Mask(w, x, y, Wet, true);
            return 9UL << 56 | (g == Ground.Water ? 1UL : 0UL) | (ulong)net << 1 | (ulong)(uint)road << 4 | (ulong)(uint)rail << 8 |
                   (ulong)(uint)wire << 12 | v << 16 | (ulong)(uint)water << 20 |
                   (ulong)(uint)(g == Ground.Water ? Mask(w, x, y, (u, t) => Field.In(u, t) && !Wet(u, t), true) : 0) << 28;
        }
        return g switch
        {
            Ground.Land => 1UL << 56 | v | (ulong)(uint)Mask(w, x, y, Wet, true) << 8,
            Ground.Water => 2UL << 56 | v | (ulong)(uint)Mask(w, x, y, (u, t) => Field.In(u, t) && !Wet(u, t), true) << 8,
            Ground.Trees => 3UL << 56 | v | (ulong)(uint)Mask(w, x, y, (u, t) => Field.In(u, t) && w.Ground[u, t] == Ground.Trees, false) << 4 |
                            (ulong)(uint)Mask(w, x, y, Wet, true) << 8,
            Ground.Rubble => 4UL << 56 | v,
            Ground.Park => 5UL << 56 | v | (ulong)(uint)Mask(w, x, y, (u, t) => Field.In(u, t) && w.Ground[u, t] == Ground.Park, false) << 4,
            Ground.Fire => 6UL << 56 | v,
            Ground.Flood => 7UL << 56 | v,
            _ => 8UL << 56 | (v & 1),
        };
    }

    /// <summary>The 16×16 picture for a key.</summary>
    public static uint[] Tile(ulong key)
    {
        lock (Guard)
        {
            if (Tiles.TryGetValue(key, out var t)) return t;
            t = Draw(key);
            Tiles[key] = t;
            return t;
        }
    }

    private static uint[] Draw(ulong key)
    {
        int cat = (int)(key >> 56);
        int v = (int)(key & 7);
        var p = new Pix(T, T);
        switch (cat)
        {
            case 1:
                GrassTile(p, v);
                Beach(p, (int)(key >> 8) & 0xFF);
                break;
            case 2: WaterTile(p, v, (int)(key >> 8) & 0xFF); break;
            case 3:
                GrassTile(p, v);
                Beach(p, (int)(key >> 8) & 0xFF);
                Trees(p, v, (int)(key >> 4) & 15);
                break;
            case 4: Rubble(p, v); break;
            case 5: Park(p, v, (int)(key >> 4) & 15); break;
            case 6: Burnt(p, v); break;
            case 7: FloodTile(p, v); break;
            case 8: Radiation(p); break;
            case 9: NetTile(p, key); break;
            case 10:
            {
                var kind = (ZoneKind)(key & 15);
                int level = (int)(key >> 4) & 15, wealth = (int)(key >> 8) & 3, variant = (int)(key >> 10) & 3;
                int dx = (int)(key >> 12) & 15, dy = (int)(key >> 16) & 15;
                var lot = Lot(kind, level, wealth, variant);
                for (int j = 0; j < T; j++)
                for (int i = 0; i < T; i++) p.P[j * T + i] = lot.P[(dy * T + j) * lot.W + dx * T + i];
                break;
            }
        }
        return p.P;
    }

    // ───────────────────────────── the land ─────────────────────────────

    private static void GrassTile(Pix p, int v)
    {
        p.Fill(0, 0, T, T, Grass);
        for (int j = 0; j < T; j++)
        for (int i = 0; i < T; i++)
        {
            double r = Pix.Rand(i + v * 16, j, 11);
            if (r < 0.12) p.Set(i, j, GrassDark);
            else if (r > 0.93) p.Set(i, j, 0xFF7CAE56);
        }
        // A tuft or two, and now and then a flower.
        for (int k = 0; k < 2; k++)
        {
            int x = (int)(Pix.Rand(v, k, 3) * 14) + 1, y = (int)(Pix.Rand(v, k, 4) * 14) + 1;
            p.Set(x, y, 0xFF4E7C32);
            p.Set(x + 1, y - 1, 0xFF4E7C32);
        }
        if (v == 3) p.Set(6, 9, 0xFFF0E070);
        if (v == 6) p.Set(11, 4, 0xFFF0F0F0);
    }

    /// <summary>Sand along the sides (and corners) that meet the water.</summary>
    private static void Beach(Pix p, int m)
    {
        if (m == 0) return;
        for (int j = 0; j < T; j++)
        for (int i = 0; i < T; i++)
        {
            int d = 99;
            if ((m & N) != 0) d = Math.Min(d, j);
            if ((m & S) != 0) d = Math.Min(d, T - 1 - j);
            if ((m & Wd) != 0) d = Math.Min(d, i);
            if ((m & E) != 0) d = Math.Min(d, T - 1 - i);
            if ((m & NE) != 0 && (m & (N | E)) == 0) d = Math.Min(d, Math.Max(j, T - 1 - i));
            if ((m & SE) != 0 && (m & (S | E)) == 0) d = Math.Min(d, Math.Max(T - 1 - j, T - 1 - i));
            if ((m & SW) != 0 && (m & (S | Wd)) == 0) d = Math.Min(d, Math.Max(T - 1 - j, i));
            if ((m & NW) != 0 && (m & (N | Wd)) == 0) d = Math.Min(d, Math.Max(j, i));
            double edge = 3 + Pix.Rand(i, j, 21) * 1.2;
            if (d < 1) p.Set(i, j, WetSand);
            else if (d < edge) p.Set(i, j, Pix.Rand(i, j, 22) < 0.15 ? Pix.Scale(Sand, 0.92) : Sand);
            else if (d < edge + 1) p.Set(i, j, Pix.Mix(p.Get(i, j), Sand, 0.4));
        }
    }

    private static void WaterTile(Pix p, int v, int land)
    {
        p.Fill(0, 0, T, T, Sea);
        for (int j = 0; j < T; j++)
        for (int i = 0; i < T; i++)
            if (Pix.Rand(i + v * 7, j, 31) < 0.08) p.Set(i, j, Pix.Scale(Sea, 0.92));
        // Ripples.
        for (int k = 0; k < 2; k++)
        {
            int x = (int)(Pix.Rand(v, k, 32) * 11) + 1, y = (int)(Pix.Rand(v, k, 33) * 14) + 1;
            p.HLine(x, x + 2, y, 0xFF5A8ED0);
        }
        if (land == 0) return;
        // Shallows by the shore, with a line of foam.
        for (int j = 0; j < T; j++)
        for (int i = 0; i < T; i++)
        {
            int d = 99;
            if ((land & N) != 0) d = Math.Min(d, j);
            if ((land & S) != 0) d = Math.Min(d, T - 1 - j);
            if ((land & Wd) != 0) d = Math.Min(d, i);
            if ((land & E) != 0) d = Math.Min(d, T - 1 - i);
            if ((land & NE) != 0) d = Math.Min(d, Math.Max(j, T - 1 - i));
            if ((land & SE) != 0) d = Math.Min(d, Math.Max(T - 1 - j, T - 1 - i));
            if ((land & SW) != 0) d = Math.Min(d, Math.Max(T - 1 - j, i));
            if ((land & NW) != 0) d = Math.Min(d, Math.Max(j, i));
            if (d == 0) p.Set(i, j, Foam);
            else if (d < 4) p.Set(i, j, Pix.Mix(Shallow, Sea, d / 4.0));
        }
    }

    private static void Tree(Pix p, double x, double y, double r, int seed)
    {
        uint leaf = seed % 3 == 0 ? 0xFF2A6430 : Leaf;
        p.Disc(x + 1.2, y + 1.4, r, 0x70000000);
        p.Disc(x, y, r, leaf);
        p.Disc(x - 0.4, y - 0.4, r * 0.75, LeafMid);
        p.Disc(x - r * 0.35, y - r * 0.4, r * 0.38, LeafLight);
    }

    /// <summary>Woods: trees in a jittered grid, reaching the sides that run on into more woods.</summary>
    private static void Trees(Pix p, int v, int joined)
    {
        var spots = new List<(double X, double Y, double R)>();
        for (int a = 0; a < 3; a++)
        for (int b = 0; b < 3; b++)
        {
            double x = 2.5 + a * 5.5 + (Pix.Rand(v, a * 3 + b, 41) - 0.5) * 2.4, y = 2.5 + b * 5.5 + (Pix.Rand(v, a * 3 + b, 42) - 0.5) * 2.4;
            bool edge = a == 0 && (joined & Wd) == 0 || a == 2 && (joined & E) == 0 || b == 0 && (joined & N) == 0 || b == 2 && (joined & S) == 0;
            if (edge && Pix.Rand(v, a * 3 + b, 43) < 0.55) continue;
            spots.Add((x, y, 2.6 + Pix.Rand(v, a * 3 + b, 44) * 1.3));
        }
        foreach (var (x, y, r) in spots.OrderBy(s => s.Y)) Tree(p, x, y, r, (int)(x * 7 + y));
    }

    private static void Rubble(Pix p, int v)
    {
        p.Fill(0, 0, T, T, 0xFF8E7E68);
        p.Grain(0, 0, T, T, 0.15, v + 50);
        for (int k = 0; k < 9; k++)
        {
            int x = (int)(Pix.Rand(v, k, 51) * 14), y = (int)(Pix.Rand(v, k, 52) * 14);
            uint c = k % 3 == 0 ? 0xFF6A6460 : k % 3 == 1 ? 0xFFA89A88 : 0xFF7A5A48;
            p.Fill(x, y, 2, 2, c);
            p.Set(x + 1, y + 1, Pix.Scale(c, 0.7));
        }
    }

    private static void Park(Pix p, int v, int joined)
    {
        p.Fill(0, 0, T, T, 0xFF78B250);
        p.Grain(0, 0, T, T, 0.06, v + 60);
        // Mown stripes.
        for (int j = 0; j < T; j += 4) p.Shade(0, j, T, 2, 1.05);
        // Paths to the neighbouring park tiles.
        uint path = 0xFFD8C8A0;
        p.Fill(7, 7, 2, 2, path);
        if ((joined & N) != 0) p.Fill(7, 0, 2, 8, path);
        if ((joined & S) != 0) p.Fill(7, 8, 2, 8, path);
        if ((joined & Wd) != 0) p.Fill(0, 7, 8, 2, path);
        if ((joined & E) != 0) p.Fill(8, 7, 8, 2, path);
        switch (v % 4)
        {
            case 0:
                p.Disc(8, 8, 4, 0xFFC8C0B0);
                p.Disc(8, 8, 3, 0xFF4A90D0);
                p.Disc(7.5, 7.5, 1, 0xFFE8F4FF);
                break;
            case 1:
                Tree(p, 4, 4, 3, 1);
                Tree(p, 12, 11, 3, 2);
                break;
            case 2:
                p.Fill(2, 2, 4, 3, 0xFFD84868);
                p.Fill(10, 11, 4, 3, 0xFFE8C040);
                Tree(p, 12, 4, 2.6, 3);
                break;
            default:
                p.Disc(4, 12, 2.5, 0xFF4A90D0);
                Tree(p, 12, 4, 3, 4);
                p.Fill(10, 12, 4, 1, 0xFF7A5A3A);
                break;
        }
    }

    private static void Burnt(Pix p, int v)
    {
        p.Fill(0, 0, T, T, 0xFF3C2C24);
        p.Grain(0, 0, T, T, 0.3, v + 70);
        for (int k = 0; k < 5; k++) p.Set((int)(Pix.Rand(v, k, 71) * 16), (int)(Pix.Rand(v, k, 72) * 16), 0xFFFF8030);
    }

    private static void FloodTile(Pix p, int v)
    {
        p.Fill(0, 0, T, T, 0xFF4E6E8E);
        p.Grain(0, 0, T, T, 0.12, v + 80);
        for (int k = 0; k < 3; k++)
        {
            int x = (int)(Pix.Rand(v, k, 81) * 11) + 1, y = (int)(Pix.Rand(v, k, 82) * 14) + 1;
            p.HLine(x, x + 3, y, 0xFF7A6A50);
        }
    }

    private static void Radiation(Pix p)
    {
        p.Fill(0, 0, T, T, 0xFF54523E);
        p.Grain(0, 0, T, T, 0.2, 90);
        p.Disc(8, 8, 6, 0xFFF0D020);
        for (int k = 0; k < 3; k++)
        {
            double a = -Math.PI / 2 + k * Math.Tau / 3;
            for (double r = 2; r < 5.6; r += 0.5)
            for (double da = -0.5; da <= 0.5; da += 0.1)
                p.Set((int)(8 + Math.Cos(a + da) * r), (int)(8 + Math.Sin(a + da) * r), 0xFF202020);
        }
        p.Disc(8, 8, 1.3, 0xFF202020);
    }

    // ───────────────────────────── roads, rails and power lines ─────────────────────────────

    private static void NetTile(Pix p, ulong key)
    {
        bool onWater = (key & 1) != 0;
        var net = (Net)((key >> 1) & 7);
        int road = (int)(key >> 4) & 15, rail = (int)(key >> 8) & 15, wire = (int)(key >> 12) & 15, v = (int)(key >> 16) & 7;
        int water = (int)(key >> 20) & 0xFF, land = (int)(key >> 28) & 0xFF;
        if (onWater) WaterTile(p, v, land);
        else
        {
            GrassTile(p, v);
            Beach(p, water);
        }
        bool hasRoad = (net & Net.Road) != 0, hasRail = (net & Net.Rail) != 0, hasWire = (net & Net.Wire) != 0;
        if (hasRoad) Road(p, road == 0 ? (hasRail && rail != 0 ? (rail & (N | S)) != 0 ? E | Wd : N | S : E | Wd) : road, onWater);
        if (hasRail) Rail(p, rail == 0 ? (hasRoad ? (road & (E | Wd)) != 0 ? N | S : E | Wd : E | Wd) : rail, onWater, hasRoad);
        if (hasWire) Wire(p, wire == 0 ? (hasRoad ? (road & (E | Wd)) != 0 ? N | S : E | Wd : E | Wd) : wire, hasRoad || hasRail);
    }

    /// <summary>A band 10 pixels wide down the middle to each side it joins, kerbs along its edges, dashes down a straight.</summary>
    private static void Road(Pix p, int m, bool bridge)
    {
        const int a = 3, b = 12;
        uint top = bridge ? 0xFF6E6C6A : Asphalt;
        if (bridge)
        {
            // The deck's shadow on the water and its railings.
            if ((m & (E | Wd)) != 0) p.Fill(0, b + 1, T, 2, 0x50000000);
            if ((m & (N | S)) != 0) p.Fill(b + 1, 0, 2, T, 0x50000000);
        }
        p.Fill(a, a, b - a + 1, b - a + 1, top);
        if ((m & N) != 0) p.Fill(a, 0, b - a + 1, a, top);
        if ((m & S) != 0) p.Fill(a, b + 1, b - a + 1, T - b - 1, top);
        if ((m & Wd) != 0) p.Fill(0, a, a, b - a + 1, top);
        if ((m & E) != 0) p.Fill(b + 1, a, T - b - 1, b - a + 1, top);
        p.Grain(0, 0, T, T, 0.04, 99);
        uint kerb = bridge ? 0xFFD8D0C0 : Kerb;
        // Kerbs: along each arm's sides, and round the middle where no arm goes.
        if ((m & N) != 0) { p.VLine(a, 0, a, kerb); p.VLine(b, 0, a, kerb); }
        if ((m & S) != 0) { p.VLine(a, b, T - 1, kerb); p.VLine(b, b, T - 1, kerb); }
        if ((m & Wd) != 0) { p.HLine(0, a, a, kerb); p.HLine(0, a, b, kerb); }
        if ((m & E) != 0) { p.HLine(b, T - 1, a, kerb); p.HLine(b, T - 1, b, kerb); }
        if ((m & N) == 0) p.HLine(a, b, a, kerb);
        if ((m & S) == 0) p.HLine(a, b, b, kerb);
        if ((m & Wd) == 0) p.VLine(a, a, b, kerb);
        if ((m & E) == 0) p.VLine(b, a, b, kerb);
        if (m == (E | Wd)) for (int i = 1; i < T; i += 6) p.HLine(i, i + 2, 7, Dash);
        if (m == (N | S)) for (int j = 1; j < T; j += 6) p.VLine(7, j, j + 2, Dash);
        if (bridge)
        {
            if ((m & (E | Wd)) != 0) for (int i = 0; i < T; i += 4) { p.Set(i, a - 1, 0xFF3A3A3A); p.Set(i, b + 1, 0xFF3A3A3A); }
            if ((m & (N | S)) != 0) for (int j = 0; j < T; j += 4) { p.Set(a - 1, j, 0xFF3A3A3A); p.Set(b + 1, j, 0xFF3A3A3A); }
        }
    }

    private static void Rail(Pix p, int m, bool bridge, bool overRoad)
    {
        const int a = 4, b = 11;
        void Arm(int dir)
        {
            bool across = dir is E or Wd;
            int from = dir is N or Wd ? 0 : 8, to = dir is N or Wd ? 8 : T;
            for (int t = from; t < to; t++)
            {
                if (!overRoad)
                {
                    for (int s = a; s <= b; s++)
                        if (across) p.Set(t, s, bridge ? 0xFF6A5A48 : Ballast);
                        else p.Set(s, t, bridge ? 0xFF6A5A48 : Ballast);
                    if (t % 3 == 1)
                        for (int s = a; s <= b; s++)
                            if (across) p.Set(t, s, Sleeper);
                            else p.Set(s, t, Sleeper);
                }
                if (across) { p.Set(t, 5, Steel); p.Set(t, 10, Steel); }
                else { p.Set(5, t, Steel); p.Set(10, t, Steel); }
            }
        }
        foreach (int dir in new[] { N, E, S, Wd })
            if ((m & dir) != 0) Arm(dir);
        if (overRoad)
        {
            // A level crossing: striped boards where the road meets the rails.
            for (int i = 3; i <= 12; i++)
                if ((m & (E | Wd)) != 0) { p.Set(i, 3, i % 2 == 0 ? 0xFFF0F0F0 : 0xFFD03030); p.Set(i, 12, i % 2 == 0 ? 0xFFF0F0F0 : 0xFFD03030); }
                else { p.Set(3, i, i % 2 == 0 ? 0xFFF0F0F0 : 0xFFD03030); p.Set(12, i, i % 2 == 0 ? 0xFFF0F0F0 : 0xFFD03030); }
        }
    }

    private static void Wire(Pix p, int m, bool beside)
    {
        int px = beside ? 1 : 7, py = beside ? 1 : 7;
        foreach (int dir in new[] { N, E, S, Wd })
        {
            if ((m & dir) == 0) continue;
            for (int o = -1; o <= 1; o += 2)
            {
                if (dir is E or Wd) { int y = 7 + o * 2; if (dir == E) p.HLine(7, T - 1, y, Cable); else p.HLine(0, 7, y, Cable); }
                else { int x = 7 + o * 2; if (dir == S) p.VLine(x, 7, T - 1, Cable); else p.VLine(x, 0, 7, Cable); }
            }
        }
        // The pole, with its cross-arm and its shadow.
        p.Fill(px + 1, py + 1, 2, 2, 0x60000000);
        p.Fill(px, py, 2, 2, Pole);
        if ((m & (E | Wd)) != 0) p.VLine(px, py - 2, py + 3, Pole);
        else p.HLine(px - 2, px + 3, py, Pole);
        p.Set(px, py, 0xFF9A7A50);
    }

    // ───────────────────────────── zones and buildings ─────────────────────────────

    /// <summary>A whole zone's picture (three, four or six tiles square), drawn once for each look and kept.</summary>
    public static Pix Lot(ZoneKind kind, int level, int wealth, int variant)
    {
        lock (Guard)
        {
            var k = (kind, level, wealth, variant);
            if (Lots.TryGetValue(k, out var lot)) return lot;
            int s = Sim.SizeOf(kind) * T;
            lot = new Pix(s, s);
            int seed = (int)kind * 1000 + level * 100 + wealth * 10 + variant;
            switch (kind)
            {
                case ZoneKind.Residential: Homes(lot, level, wealth, variant, seed); break;
                case ZoneKind.Commercial: Shops(lot, level, wealth, variant, seed); break;
                case ZoneKind.Industrial: Factories(lot, level, variant, seed); break;
                case ZoneKind.Police: Police(lot); break;
                case ZoneKind.FireStation: FireStation(lot); break;
                case ZoneKind.Coal: Coal(lot); break;
                case ZoneKind.Nuclear: Nuclear(lot); break;
                case ZoneKind.Stadium: Stadium(lot); break;
                case ZoneKind.Seaport: Seaport(lot); break;
                default: Airport(lot); break;
            }
            Lots[k] = lot;
            return lot;
        }
    }

    private static readonly string[] Glyphs =
    [
        "11110100011000111110101001001010001", // R
        "01110100011000010000100001000101110", // C
        "11111001000010000100001000010011111", // I
    ];

    /// <summary>An empty zone: cleared ground, its edge marked in its colour, and its letter in the middle.</summary>
    private static void EmptyLot(Pix p, ZoneKind kind, int seed)
    {
        uint c = kind switch { ZoneKind.Residential => 0xFF40C060, ZoneKind.Commercial => 0xFF4A90F0, _ => 0xFFF0C030 };
        p.Fill(0, 0, p.W, p.H, 0xFFA8986E);
        p.Grain(0, 0, p.W, p.H, 0.08, seed);
        for (int i = 1; i < p.W - 1; i++)
            if (i % 4 < 2)
            {
                p.Set(i, 1, c); p.Set(i, p.H - 2, c); p.Set(1, i, c); p.Set(p.W - 2, i, c);
            }
        string g = Glyphs[(int)kind];
        for (int j = 0; j < 7; j++)
        for (int i = 0; i < 5; i++)
            if (g[j * 5 + i] == '1')
            {
                p.Fill(17 + i * 3, 14 + j * 3, 3, 3, 0x50000000);
                p.Fill(16 + i * 3, 13 + j * 3, 3, 3, c);
            }
    }

    private static uint Pick(uint[] colours, int seed, int k) => colours[(int)(Pix.Rand(seed, k, 101) * colours.Length)];

    private static readonly uint[][] RoofsByWealth =
    [
        [0xFF8A7060, 0xFF7A7A80, 0xFF8C6450, 0xFF6E6A5E],
        [0xFFB04A3A, 0xFFA0583A, 0xFF6A6A8A, 0xFF8A4A3A, 0xFF5A6A7A],
        [0xFF3A5A9A, 0xFF2A6A6A, 0xFF9A3A3A, 0xFF5A4A7A, 0xFF3A6A4A],
    ];

    private static readonly uint[] WallsByWealth = [0xFFC4B49C, 0xFFE6DAC6, 0xFFF6F2EA];

    private static void Lawn(Pix p, int seed, int wealth)
    {
        p.Fill(0, 0, p.W, p.H, wealth == 0 ? 0xFF7C9A52 : 0xFF74A64E);
        p.Grain(0, 0, p.W, p.H, 0.07, seed);
    }

    private static void Homes(Pix p, int level, int wealth, int variant, int seed)
    {
        if (level == 0) { EmptyLot(p, ZoneKind.Residential, seed); return; }
        Lawn(p, seed, wealth);
        var roofs = RoofsByWealth[wealth];
        uint wall = WallsByWealth[wealth];
        switch (level)
        {
            case 1:
            case 2:
            {
                // Houses in a ring of eight round a green (or a pool, if they can afford one); fewer at first.
                int[] cells = level == 1 ? variant switch { 0 => [0, 2, 7], 1 => [1, 5, 6], 2 => [3, 2, 8], _ => [0, 4, 8] } : [0, 1, 2, 3, 5, 6, 7, 8];
                p.Fill(0, 22, p.W, 4, 0xFFC8C0A8);
                for (int c = 0; c < 9; c++)
                {
                    int cx = c % 3 * 16, cy = c / 3 * 16;
                    if (cells.Contains(c))
                    {
                        if (c is 0 or 1 or 2) p.Fill(cx + 7, cy + 12, 2, 10, 0xFFB8B0A0);
                        p.House(cx + 2, cy + 6, 11, 6, 3, Pick(roofs, seed, c), wall, seed + c);
                    }
                    else if (c == 4 && wealth == 2)
                    {
                        p.Fill(cx + 3, cy + 4, 10, 7, 0xFFE8E8E8);
                        p.Fill(cx + 4, cy + 5, 8, 5, 0xFF48A8E0);
                    }
                    else Tree(p, cx + 8 + (Pix.Rand(c, seed, 1) - 0.5) * 6, cy + 7 + (Pix.Rand(c, seed, 2) - 0.5) * 6, 3.4, c);
                }
                if (level == 2) Tree(p, 40, 44, 3, 9);
                break;
            }
            case 3:
                // Low-rise apartments, laid out four ways.
                switch (variant)
                {
                    case 0:
                        p.Fill(2, 34, 44, 12, 0xFF8A8C90);
                        for (int i = 4; i < 44; i += 5) p.VLine(i, 35, 39, 0xFFE0E0E0);
                        Cars(p, 3, 41, 42, seed);
                        p.Box(3, 14, 19, 12, 9, Pick(roofs, seed, 1), wall);
                        p.Box(26, 16, 19, 11, 8, Pick(roofs, seed, 2), Pix.Scale(wall, 0.95));
                        Tree(p, 24, 5, 3, 1);
                        Tree(p, 6, 3, 2.6, 2);
                        break;
                    case 1:
                        p.Box(3, 12, 30, 10, 8, Pick(roofs, seed, 1), wall);
                        p.Box(33, 12, 12, 26, 8, Pick(roofs, seed, 1), Pix.Scale(wall, 0.92));
                        p.Fill(4, 30, 24, 14, 0xFF8A8C90);
                        Cars(p, 5, 34, 22, seed);
                        Tree(p, 8, 4, 2.6, 3);
                        break;
                    case 2:
                        // Terraces: three rows of houses joined up.
                        for (int r = 0; r < 3; r++)
                        {
                            uint roof = Pick(roofs, seed, 10 + r);
                            for (int k = 0; k < 4; k++) p.House(3 + k * 10, 10 + r * 14, 10, 6, 4, Pix.Scale(roof, 1 + (k % 2) * 0.08), wall, seed + r * 4 + k);
                        }
                        break;
                    default:
                        // Round a courtyard.
                        p.Fill(14, 16, 20, 14, 0xFF84B85A);
                        p.Disc(24, 23, 3, 0xFF48A8E0);
                        p.Box(3, 12, 42, 6, 8, Pick(roofs, seed, 4), wall);
                        p.Box(3, 18, 9, 18, 8, Pick(roofs, seed, 4), Pix.Scale(wall, 0.95));
                        p.Box(36, 18, 9, 18, 8, Pick(roofs, seed, 4), Pix.Scale(wall, 0.9));
                        p.Box(3, 36, 42, 6, 8, Pick(roofs, seed, 4), wall);
                        break;
                }
                break;
            case 4:
                switch (variant)
                {
                    case 0:
                        p.Fill(2, 40, 44, 7, 0xFF8A8C90);
                        Cars(p, 3, 42, 42, seed);
                        p.Box(3, 20, 20, 13, 15, Pick(roofs, seed, 3), wall);
                        p.Box(26, 22, 19, 12, 13, Pick(roofs, seed, 4), Pix.Scale(wall, 0.94));
                        p.Fill(8, 7, 3, 2, 0xFF5A5A5A);
                        Tree(p, 44, 4, 3, 3);
                        break;
                    case 1:
                        // A long slab on stilts over the car park.
                        p.Fill(2, 38, 44, 9, 0xFF8A8C90);
                        Cars(p, 3, 41, 42, seed);
                        p.Box(3, 24, 42, 12, 16, Pick(roofs, seed, 5), wall);
                        for (int i = 6; i < 44; i += 9) p.Fill(i, 10, 3, 2, 0xFF5A5A5A);
                        break;
                    case 2:
                        for (int k = 0; k < 3; k++) p.Box(2 + k * 15, 22 + k % 2 * 4, 13, 12, 14 + k % 2 * 3, Pick(roofs, seed, 6 + k), Pix.Scale(wall, 1 - k * 0.03));
                        Tree(p, 8, 44, 2.6, 4);
                        Tree(p, 24, 45, 2.6, 5);
                        Tree(p, 40, 44, 2.6, 6);
                        break;
                    default:
                        p.Box(3, 18, 18, 18, 14, Pick(roofs, seed, 7), wall);
                        p.Box(26, 28, 19, 10, 18, Pick(roofs, seed, 8), Pix.Scale(wall, 0.92));
                        p.Fill(24, 42, 22, 5, 0xFF8A8C90);
                        Cars(p, 24, 43, 22, seed);
                        Tree(p, 34, 4, 3, 7);
                        break;
                }
                break;
            default:
            {
                // Tower blocks, by the money: concrete for the poor, glass for the rich.
                uint tw = wealth == 2 ? 0xFFDCE4EE : wall;
                var glass = wealth == 2 ? Windows.Glass : Windows.Rows;
                switch (variant)
                {
                    case 0:
                        p.Fill(2, 38, 44, 9, 0xFF9A9CA0);
                        Cars(p, 4, 43, 40, seed);
                        p.Box(4, 26, 17, 12, 22, Pick(roofs, seed, 5), tw, glass);
                        p.Box(25, 30, 19, 13, 27, Pix.Scale(Pick(roofs, seed, 6), 0.9), Pix.Scale(tw, 0.95), glass);
                        p.Fill(30, 6, 4, 3, 0xFF6A6A6A);
                        p.Fill(10, 7, 3, 2, 0xFF6A6A6A);
                        break;
                    case 1:
                        // One great tower on a green plaza.
                        p.Fill(4, 38, 40, 8, 0xFFC8C4BA);
                        for (int k = 0; k < 4; k++) Tree(p, 7 + k * 11, 43, 2.4, k);
                        p.Box(11, 32, 26, 14, 28, Pick(roofs, seed, 7), tw, glass);
                        p.Fill(18, 7, 12, 3, 0xFF6A6A6A);
                        break;
                    case 2:
                        // Three slim towers.
                        p.Fill(2, 40, 44, 7, 0xFF9A9CA0);
                        Cars(p, 4, 42, 40, seed);
                        for (int k = 0; k < 3; k++) p.Box(2 + k * 15, 30 + k % 2 * 6, 12, 10, 24 + k % 2 * 4, Pick(roofs, seed, 8 + k), Pix.Scale(tw, 1 - k * 0.04), glass);
                        break;
                    default:
                        p.Box(3, 30, 16, 14, 26, Pick(roofs, seed, 11), tw, glass);
                        p.Box(22, 24, 23, 12, 12, Pix.Scale(Pick(roofs, seed, 12), 0.95), Pix.Scale(tw, 0.93), glass);
                        p.Fill(22, 40, 23, 6, 0xFF9A9CA0);
                        Cars(p, 23, 41, 22, seed);
                        Tree(p, 40, 5, 3, 9);
                        break;
                }
                break;
            }
        }
    }

    private static void Cars(Pix p, int x, int y, int w, int seed)
    {
        uint[] colours = [0xFFD03030, 0xFF3060C0, 0xFFE8E8E8, 0xFF303030, 0xFFE0B030, 0xFF40A060];
        for (int i = x + 1; i < x + w - 3; i += 5)
            if (Pix.Rand(i, y, seed) < 0.55)
            {
                uint c = colours[(int)(Pix.Rand(i, seed, 7) * colours.Length)];
                p.Fill(i, y, 3, 4, c);
                p.Set(i + 1, y + 1, 0xFF9AB4CC);
            }
    }

    private static readonly uint[] Glass = [0xFF6A8AB0, 0xFF5A7E96, 0xFF86A2BC, 0xFF6E8A8E, 0xFF7A88A8];
    private static readonly uint[] Awnings = [0xFFD84040, 0xFF40A0D0, 0xFFE8B030, 0xFF40B060, 0xFFB050C0];

    private static void Shops(Pix p, int level, int wealth, int variant, int seed)
    {
        if (level == 0) { EmptyLot(p, ZoneKind.Commercial, seed); return; }
        p.Fill(0, 0, p.W, p.H, Pavement);
        p.Grain(0, 0, p.W, p.H, 0.05, seed);
        uint roof = 0xFF8C9098;
        switch (level)
        {
            case 1:
            {
                for (int i = 4; i < 44; i += 5) p.VLine(i, 36, 41, 0xFFF0F0F0);
                Cars(p, 3, 37, 42, seed);
                int[] widths = variant % 2 == 0 ? [14, 12, 12] : [20, 18];
                int x = 2;
                foreach (int bw in widths)
                {
                    uint wall = Pick([0xFFE8D8C0, 0xFFD8C8E0, 0xFFF0E8D0, 0xFFC8D8C8], seed, x);
                    p.Box(x, 16, bw, 12, 6, Pix.Scale(roof, 1 + Pix.Rand(x, seed, 3) * 0.2), wall, Windows.Few);
                    uint aw = Pick(Awnings, seed, x + 1);
                    for (int i = x + 1; i < x + bw - 1; i++) p.Set(i, 26, (i - x) % 2 == 0 ? aw : 0xFFF0F0F0);
                    x += bw + 2;
                }
                Tree(p, 6, 4, 2.6, 1);
                Tree(p, 42, 4, 2.6, 2);
                break;
            }
            case 2:
                Cars(p, 3, 41, 42, seed);
                p.Box(2, 20, 26, 15, 9, roof, Pick([0xFFE0D0B8, 0xFFD0D8E0], seed, 1));
                for (int i = 3; i < 27; i++) p.Set(i, 33, (i % 2 == 0) ? Pick(Awnings, seed, 2) : 0xFFF0F0F0);
                p.Box(31, 22, 14, 12, 13, Pix.Scale(roof, 0.9), Pick(Glass, seed, 3), Windows.Glass);
                p.Fill(8, 13, 4, 3, 0xFF6A6E74);
                break;
            case 3:
                switch (variant)
                {
                    case 0:
                    case 2:
                        Cars(p, 3, 42, 42, seed);
                        p.Box(3, 26, 19, 13, 19, roof, Pick(Glass, seed, 4), Windows.Glass);
                        p.Box(26, 28, 19, 12, 15, Pix.Scale(roof, 1.1), Pick(Glass, seed, 5), Windows.Glass);
                        p.Fill(8, 10, 5, 3, 0xFF6A6E74);
                        p.Fill(30, 15, 4, 3, 0xFF6A6E74);
                        break;
                    default:
                        // An office block round an atrium, with a fountain out front.
                        p.Box(3, 22, 42, 16, 16, roof, Pick(Glass, seed, 6), Windows.Glass);
                        p.Fill(16, 8, 16, 8, Pix.Scale(Pick(Glass, seed, 6), 1.3));
                        p.Disc(24, 43, 3, 0xFFC8C0B0);
                        p.Disc(24, 43, 2, 0xFF48A8E0);
                        Tree(p, 8, 44, 2.4, 1);
                        Tree(p, 40, 44, 2.4, 2);
                        break;
                }
                break;
            default:
            {
                // A skyscraper on a plaza, with a helipad, a crown, a twin, or steps.
                p.Fill(4, 40, 40, 6, 0xFFC8C4BA);
                for (int k = 0; k < 4; k++) Tree(p, 8 + k * 11, 44, 2.2, k);
                uint g = Pick(Glass, seed, 6);
                switch (variant)
                {
                    case 0:
                        p.Box(12, 31, 24, 14, 30, 0xFF7A7E86, g, Windows.Glass);
                        p.Disc(24, 8, 5, 0xFF5A5E66);
                        p.HLine(22, 26, 8, 0xFFF0F0F0);
                        p.VLine(22, 6, 10, 0xFFF0F0F0);
                        p.VLine(26, 6, 10, 0xFFF0F0F0);
                        p.Box(2, 30, 9, 8, 10, roof, Pix.Scale(g, 0.9), Windows.Glass);
                        break;
                    case 1:
                        p.Box(12, 31, 24, 14, 30, 0xFF7A7E86, g, Windows.Glass);
                        p.Fill(16, 3, 16, 6, Pix.Scale(g, 1.2));
                        p.Fill(20, 1, 8, 3, Pix.Scale(g, 1.35));
                        p.Box(37, 32, 9, 8, 12, roof, Pix.Scale(g, 0.9), Windows.Glass);
                        break;
                    case 2:
                        p.Box(4, 30, 17, 13, 28, 0xFF6E727A, g, Windows.Glass);
                        p.Box(27, 30, 17, 13, 28, 0xFF6E727A, Pix.Scale(g, 0.95), Windows.Glass);
                        p.Fill(21, 16, 6, 3, 0xFF8A8E96);
                        break;
                    default:
                        p.Box(6, 36, 36, 10, 8, roof, Pix.Scale(g, 0.9), Windows.Glass);
                        p.Box(11, 32, 26, 10, 18, 0xFF7A7E86, g, Windows.Glass);
                        p.Box(16, 28, 16, 8, 26, 0xFF6A6E76, Pix.Scale(g, 1.08), Windows.Glass);
                        p.VLine(24, 0, 2, 0xFFD0D0D0);
                        break;
                }
                break;
            }
        }
    }

    /// <summary>A saw-toothed factory roof: north lights in stripes.</summary>
    private static void Sawtooth(Pix p, int x, int y, int w, int d, int h, uint wall)
    {
        p.Box(x, y, w, d, h, 0xFF8A9098, wall, Windows.Few);
        for (int j = y - h + 1; j < y - h + d - 1; j++)
        for (int i = x + 1; i < x + w - 1; i++)
            p.Set(i, j, (j - y + h) % 4 < 2 ? 0xFFB0B8C4 : 0xFF6A7078);
    }

    private static void Factories(Pix p, int level, int variant, int seed)
    {
        if (level == 0) { EmptyLot(p, ZoneKind.Industrial, seed); return; }
        p.Fill(0, 0, p.W, p.H, Concrete);
        p.Grain(0, 0, p.W, p.H, 0.08, seed);
        for (int k = 0; k < 4; k++) p.Disc(6 + Pix.Rand(k, seed, 1) * 36, 6 + Pix.Rand(k, seed, 2) * 36, 2, 0x30000000);
        switch (level)
        {
            case 1:
                p.Box(3, 16, 22, 13, 5, 0xFF9AA0A8, 0xFFB0A890, Windows.Few);
                for (int i = 4; i < 24; i += 2) p.VLine(i, 12, 23, 0xFF848A92);
                p.Box(28, 22, 17, 11, 4, 0xFF8A6A50, 0xFFA89880, Windows.Few);
                // A lorry.
                p.Fill(8, 38, 10, 4, 0xFFE8E8E8);
                p.Fill(18, 38, 4, 4, 0xFFD03030);
                p.Fill(30, 40, 8, 3, 0xFF8A7050);
                break;
            case 2:
                Sawtooth(p, 3, 18, 28, 16, 7, 0xFFB0A088);
                p.Chimney(37, 30, 24, 0xFF9A7060);
                p.Tank(40, 40, 4, 0xFFC8C8C0);
                break;
            case 3:
                Sawtooth(p, 2, 22, 30, 18, 9, 0xFFA89880);
                p.Chimney(36, 26, 24, 0xFF8A6A5A);
                p.Chimney(42, 30, 28, 0xFF8A6A5A);
                p.Tank(9, 42, 4, 0xFFD0D0C8);
                p.Tank(20, 42, 4, 0xFFD0D0C8);
                p.Fill(30, 40, 16, 6, 0xFF6A6E70);
                break;
            default:
                // A refinery: tanks, a cracking tower, chimneys and pipes.
                p.HLine(2, 45, 30, 0xFF5A5A5A);
                p.HLine(2, 45, 31, 0xFF8A8A8A);
                p.VLine(24, 4, 46, 0xFF5A5A5A);
                p.Tank(10, 12, 7, 0xFFDADAD2);
                p.Tank(10, 40, 6, 0xFFDADAD2);
                p.Tank(36, 40, 7, variant % 2 == 0 ? 0xFFD8C090 : 0xFFDADAD2);
                p.Box(30, 20, 10, 8, 18, 0xFF7A7E86, 0xFF9A9EA6, Windows.Few);
                p.Chimney(43, 24, 22, 0xFF8A6A5A);
                p.Chimney(18, 26, 18, 0xFF8A6A5A);
                break;
        }
    }

    private static void Police(Pix p)
    {
        p.Fill(0, 0, p.W, p.H, Pavement);
        p.Grain(0, 0, p.W, p.H, 0.05, 301);
        p.Box(4, 16, 40, 14, 9, 0xFF3A5A9A, 0xFFE8E8F0);
        // A star on the roof, the cars out front.
        p.Disc(24, 13, 4, 0xFFF0D040);
        p.Disc(24, 13, 1.6, 0xFF3A5A9A);
        for (int k = 0; k < 3; k++)
        {
            int x = 8 + k * 12;
            p.Fill(x, 38, 8, 5, 0xFFF0F0F0);
            p.Fill(x + 3, 38, 2, 5, 0xFF202830);
            p.Set(x + 4, 39, 0xFF3070F0);
            p.Set(x + 4, 41, 0xFFE03030);
        }
        p.VLine(44, 2, 12, 0xFF8A8A8A);
        p.Fill(45, 2, 3, 2, 0xFF3A5A9A);
    }

    private static void FireStation(Pix p)
    {
        p.Fill(0, 0, p.W, p.H, Pavement);
        p.Grain(0, 0, p.W, p.H, 0.05, 302);
        p.Box(4, 14, 40, 16, 10, 0xFF8A2A22, 0xFFB85A40, Windows.None);
        for (int k = 0; k < 3; k++)
        {
            int x = 8 + k * 12;
            p.Fill(x, 22, 9, 8, 0xFFF0F0F0);
            p.Fill(x + 1, 23, 7, 7, 0xFF3A3030);
        }
        // An engine on the apron, ladder and all.
        p.Fill(9, 36, 16, 6, 0xFFD02020);
        p.Fill(10, 37, 13, 1, 0xFFE0E0E0);
        p.Fill(22, 36, 3, 6, 0xFFB01818);
        p.Set(23, 37, 0xFF9AB4CC);
        p.Fill(30, 4, 14, 6, 0xFF8A8A8A);
    }

    private static void Coal(Pix p)
    {
        p.Fill(0, 0, p.W, p.H, 0xFF8C8478);
        p.Grain(0, 0, p.W, p.H, 0.1, 401);
        // The coal heap, and the belt up to the boilers.
        for (int k = 0; k < 40; k++)
            p.Disc(6 + Pix.Rand(k, 1, 402) * 24, 48 + Pix.Rand(k, 2, 402) * 10, 3 + Pix.Rand(k, 3, 402) * 2, k % 3 == 0 ? 0xFF3A3A3E : 0xFF1E1E22);
        p.Line(30, 52, 40, 42, 0xFF5A5A5A);
        p.Line(31, 52, 41, 42, 0xFF5A5A5A);
        p.Box(3, 24, 32, 18, 12, 0xFF9AA0A8, 0xFFB0A894);
        p.Box(36, 26, 14, 18, 20, 0xFF7A7E86, 0xFF9A948A);
        p.Chimney(52, 46, 42, 0xFFA08070);
        p.Chimney(58, 50, 40, 0xFFA08070);
        p.Tank(10, 8, 4, 0xFFD0D0C8);
    }

    private static void CoolingTower(Pix p, double cx, double cy, double r)
    {
        p.Disc(cx + 4, cy + 4, r, 0x60000000);
        p.Disc(cx, cy, r, 0xFFB8B8B2);
        p.Disc(cx - r * 0.15, cy - r * 0.15, r * 0.88, 0xFFD6D6D0);
        p.Disc(cx, cy, r * 0.62, 0xFF5A5A5A);
        p.Disc(cx + r * 0.1, cy + r * 0.1, r * 0.5, 0xFF3E3E40);
    }

    private static void Nuclear(Pix p)
    {
        p.Fill(0, 0, p.W, p.H, 0xFF9C9888);
        p.Grain(0, 0, p.W, p.H, 0.06, 501);
        p.Fill(0, 0, p.W, 2, 0xFFE0C030);
        CoolingTower(p, 16, 17, 13);
        CoolingTower(p, 16, 46, 12);
        p.Box(34, 52, 27, 10, 8, 0xFF8A8E96, 0xFFC8C4B8);
        // The containment dome.
        p.Disc(48.5, 25, 11, 0x60000000);
        p.Disc(47, 23, 11, 0xFFC8C8C8);
        p.Disc(45, 21, 7, 0xFFE4E4E4);
        p.Disc(43, 19, 3, 0xFFF8F8F8);
    }

    private static void Stadium(Pix p)
    {
        p.Fill(0, 0, p.W, p.H, Pavement);
        p.Grain(0, 0, p.W, p.H, 0.05, 601);
        p.Disc(33.5, 33.5, 30, 28, 0x60000000);
        p.Disc(32, 32, 30, 28, 0xFF8E8E98);
        for (int r = 0; r < 4; r++) p.Disc(32, 32, 28 - r * 2, 26 - r * 2, r % 2 == 0 ? 0xFFC04848 : 0xFF4868B8);
        p.Disc(32, 32, 20, 17, 0xFF58A040);
        for (int y = 14; y < 50; y++)
        for (int x = 11; x < 53; x++)
        {
            double u = (x + 0.5 - 32) / 20, v = (y + 0.5 - 32) / 17;
            if (u * u + v * v <= 1 && x / 4 % 2 == 0) p.Set(x, y, 0xFF4E9638);
        }
        p.Frame(17, 21, 30, 22, 0xFFF0F0F0);
        p.VLine(32, 21, 42, 0xFFF0F0F0);
        p.Disc(32, 32, 4, 0x30FFFFFF);
    }

    private static void Seaport(Pix p)
    {
        p.Fill(0, 0, p.W, p.H, 0xFFA8A49C);
        p.Grain(0, 0, p.W, p.H, 0.06, 701);
        p.Box(3, 14, 26, 10, 7, 0xFF7A8A9A, 0xFFB8B0A0);
        p.Box(34, 12, 26, 10, 6, 0xFF8A6A50, 0xFFB8B0A0);
        // Containers, stacked.
        uint[] colours = [0xFFC84030, 0xFF3060B0, 0xFF40A060, 0xFFE09030, 0xFF8A8A8A, 0xFFB03070];
        for (int row = 0; row < 3; row++)
        for (int k = 0; k < 6; k++)
        {
            int x = 4 + k * 9, y = 30 + row * 6;
            uint c = colours[(int)(Pix.Rand(row, k, 702) * colours.Length)];
            p.Fill(x + 1, y + 1, 8, 4, 0x50000000);
            p.Fill(x, y, 8, 4, c);
            p.HLine(x, x + 7, y, Pix.Scale(c, 1.3));
        }
        // Two cranes.
        foreach (int cx in new[] { 18, 46 })
        {
            p.Frame(cx - 4, 46, 9, 12, 0xFFE08A20);
            p.Line(cx - 4, 46, cx + 4, 57, 0xFFC07010);
            p.Fill(cx - 1, 26, 3, 34, 0x30000000);
            p.VLine(cx, 24, 60, 0xFFF0A030);
            p.VLine(cx + 1, 24, 60, 0xFFC07010);
        }
        for (int i = 2; i < 62; i += 8) p.Fill(i, 61, 2, 2, 0xFF3A3A3A);
    }

    private static void Plane(Pix p, int x, int y, bool across)
    {
        if (across)
        {
            p.Fill(x - 6, y, 13, 2, 0xFFF4F4F4);
            p.Fill(x - 1, y - 5, 3, 12, 0xFFE8E8E8);
            p.Fill(x - 6, y - 2, 2, 6, 0xFFE8E8E8);
            p.Set(x + 6, y, 0xFF3060B0);
        }
        else
        {
            p.Fill(x, y - 6, 2, 13, 0xFFF4F4F4);
            p.Fill(x - 5, y - 1, 12, 3, 0xFFE8E8E8);
            p.Fill(x - 2, y + 5, 6, 2, 0xFFE8E8E8);
            p.Set(x, y - 6, 0xFF3060B0);
        }
    }

    private static void Airport(Pix p)
    {
        p.Fill(0, 0, p.W, p.H, 0xFF7AAA56);
        p.Grain(0, 0, p.W, p.H, 0.06, 801);
        // The runway, with its markings and lights.
        p.Fill(2, 30, 92, 14, 0xFF4E5056);
        p.Grain(2, 30, 92, 14, 0.05, 802);
        for (int i = 12; i < 84; i += 8) p.HLine(i, i + 4, 37, 0xFFF0F0F0);
        for (int j = 32; j < 42; j += 2) { p.HLine(4, 8, j, 0xFFF0F0F0); p.HLine(87, 91, j, 0xFFF0F0F0); }
        for (int i = 2; i < 94; i += 6) { p.Set(i, 29, 0xFFF0E060); p.Set(i, 44, 0xFFF0E060); }
        // Taxiway and apron.
        p.Fill(6, 52, 84, 5, 0xFF6A6C72);
        p.Fill(10, 44, 4, 9, 0xFF6A6C72);
        p.Fill(80, 44, 4, 9, 0xFF6A6C72);
        p.Fill(4, 60, 60, 16, 0xFF8E9096);
        p.Box(6, 80, 46, 12, 8, 0xFFC8CCD4, 0xFF7A9AB8, Windows.Glass);
        // The tower and the hangars.
        p.Box(56, 84, 6, 6, 22, 0xFF3A4A5A, 0xFFD8D8D8, Windows.None);
        p.Fill(55, 59, 8, 4, 0xFF4A6A8A);
        for (int k = 0; k < 2; k++)
        {
            p.Box(66 + k * 14, 78, 12, 14, 9, 0xFFB8BCC2, 0xFF9A9EA6, Windows.None);
            for (int j = 70; j < 82; j += 2) p.HLine(67 + k * 14, 76 + k * 14, j, 0xFF9EA2AA);
        }
        Plane(p, 16, 67, false);
        Plane(p, 34, 67, false);
        Plane(p, 50, 37, true);
        // The windsock and a few lights in the grass.
        p.VLine(86, 10, 18, 0xFF8A8A8A);
        p.Fill(87, 10, 4, 2, 0xFFF07020);
        for (int i = 10; i < 80; i += 12) p.Set(i, 22, 0xFFF0E060);
        Tree(p, 8, 8, 3.4, 1);
        Tree(p, 18, 12, 3, 2);
    }

    // ───────────────────────────── the mini-map ─────────────────────────────

    /// <summary>A tile's colour on the mini-map.</summary>
    public static uint MiniColour(World w, int x, int y)
    {
        var net = w.Net[x, y];
        if (w.Ground[x, y] == Ground.Built)
        {
            var z = w.ZoneAt(x, y)!;
            double lift = z.Grows ? 0.55 + z.Level * 0.12 : 1;
            uint c = z.Kind switch
            {
                ZoneKind.Residential => 0xFF48C868, ZoneKind.Commercial => 0xFF4A8EF0, ZoneKind.Industrial => 0xFFF0C030,
                ZoneKind.Police => 0xFF7090F0, ZoneKind.FireStation => 0xFFF05040, ZoneKind.Coal or ZoneKind.Nuclear => 0xFFC070E0,
                _ => 0xFFE8E8E8,
            };
            return z.Level == 0 && z.Grows ? Pix.Mix(c, 0xFFA8986E, 0.6) : Pix.Scale(c, lift);
        }
        if ((net & Net.Road) != 0) return 0xFF3A3A40;
        if ((net & Net.Rail) != 0) return 0xFF8A6A4A;
        if ((net & Net.Wire) != 0 && w.Ground[x, y] != Ground.Water) return 0xFF9A9A6A;
        return w.Ground[x, y] switch
        {
            Ground.Water => net != Net.None ? 0xFF5A5A60 : 0xFF2F62A6,
            Ground.Trees => 0xFF336E2E,
            Ground.Park => 0xFF84C060,
            Ground.Rubble => 0xFF8E7E68,
            Ground.Fire => 0xFFFF6020,
            Ground.Flood => 0xFF557090,
            Ground.Radiation => 0xFFD8C830,
            _ => 0xFF6A9A48,
        };
    }
}
