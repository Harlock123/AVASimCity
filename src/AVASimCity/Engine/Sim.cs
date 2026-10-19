namespace AVASimCity.Engine;

/// <summary>What became of a use of a tool.</summary>
public enum Outcome { Done, Nothing, Blocked, NoMoney }

/// <summary>
/// The rules of the city, one step at a time. Every step a slice of the zones takes its turn: it looks for a road,
/// sends its people on a trip along the roads to where they want to go, and grows or declines by the demand for its
/// kind and how pleasant its land is. At the end of every month the maps are drawn afresh (traffic, pollution,
/// land value, crime, and the police and fire stations' reach), the people are counted, the demand worked out and
/// the mayor judged; at the end of every year the taxes come in and the services are paid. Fires, floods and the
/// rest of the disasters run in between.
/// </summary>
public static class Sim
{
    public const int CoalPower = 700, NuclearPower = 2000;
    /// <summary>The longest trip anyone will make along the roads, in tiles.</summary>
    public const int MaxTrip = 120;
    /// <summary>How often fires and floods move on, in steps.</summary>
    public const int FireEvery = 15;

    /// <summary>The people (or the jobs) a zone holds at each level.</summary>
    public static readonly int[] ResidentsAt = [0, 32, 90, 200, 380, 640];
    public static readonly int[] ShopJobsAt = [0, 40, 110, 230, 420];
    public static readonly int[] FactoryJobsAt = [0, 50, 130, 250, 380];

    public static int Cost(Tool t) => t switch
    {
        Tool.Bulldozer => 1, Tool.Road => 10, Tool.Rail => 20, Tool.Wire => 5, Tool.Park => 10,
        Tool.Residential or Tool.Commercial or Tool.Industrial => 100,
        Tool.Police or Tool.FireStation => 500, Tool.Stadium => 3000, Tool.Seaport => 5000, Tool.Airport => 10000,
        Tool.Coal => 3000, Tool.Nuclear => 5000, _ => 0,
    };

    /// <summary>A road, railway or power line over water: a bridge, at a higher price.</summary>
    public static int BridgeCost(Tool t) => t switch { Tool.Road => 50, Tool.Rail => 100, Tool.Wire => 25, _ => 0 };

    public static int SizeOf(ZoneKind k) => k switch
    {
        ZoneKind.Stadium or ZoneKind.Seaport or ZoneKind.Coal or ZoneKind.Nuclear => 4,
        ZoneKind.Airport => 6,
        _ => 3,
    };

    public static int MaxLevel(ZoneKind k) => k switch
    {
        ZoneKind.Residential => ResidentsAt.Length - 1,
        ZoneKind.Commercial => ShopJobsAt.Length - 1,
        ZoneKind.Industrial => FactoryJobsAt.Length - 1,
        _ => 1,
    };

    public static ZoneKind? KindOf(Tool t) => t switch
    {
        Tool.Residential => ZoneKind.Residential, Tool.Commercial => ZoneKind.Commercial, Tool.Industrial => ZoneKind.Industrial,
        Tool.Police => ZoneKind.Police, Tool.FireStation => ZoneKind.FireStation, Tool.Stadium => ZoneKind.Stadium,
        Tool.Seaport => ZoneKind.Seaport, Tool.Airport => ZoneKind.Airport, Tool.Coal => ZoneKind.Coal, Tool.Nuclear => ZoneKind.Nuclear,
        _ => null,
    };

    public static Tool ToolFor(ZoneKind k) => k switch
    {
        ZoneKind.Residential => Tool.Residential, ZoneKind.Commercial => Tool.Commercial, ZoneKind.Industrial => Tool.Industrial,
        ZoneKind.Police => Tool.Police, ZoneKind.FireStation => Tool.FireStation, ZoneKind.Stadium => Tool.Stadium,
        ZoneKind.Seaport => Tool.Seaport, ZoneKind.Airport => Tool.Airport, ZoneKind.Coal => Tool.Coal, _ => Tool.Nuclear,
    };

    /// <summary>The top-left tile of a building of this size whose middle is at (x, y).</summary>
    public static (int X, int Y) Corner(int size, int x, int y) => size == 6 ? (x - 2, y - 2) : (x - 1, y - 1);

    public static bool IsPlant(ZoneKind k) => k is ZoneKind.Coal or ZoneKind.Nuclear;

    // ───────────────────────────── a new city ─────────────────────────────

    public static long StartingFunds(Difficulty d) => d switch { Difficulty.Easy => 20000, Difficulty.Medium => 10000, _ => 5000 };

    /// <summary>A new city on fresh land: no zones, the money the difficulty gives, 1900.</summary>
    public static void Found(World w, int seed, Difficulty d, string name)
    {
        w.Name = name;
        w.Seed = seed;
        w.Difficulty = d;
        w.Funds = StartingFunds(d);
        w.Dice = new Dice(seed * 31 + 7);
        Terrain.Make(w, seed);
        w.Zones.Clear();
        w.Reindex();
        Maps(w);
    }

    // ───────────────────────────── the tools ─────────────────────────────

    /// <summary>Uses a tool at a tile (for buildings, the middle of where it goes). Free: no money changes hands.</summary>
    public static Outcome Apply(World w, Tool t, int x, int y, bool free = false)
    {
        if (!Field.In(x, y)) return Outcome.Blocked;
        return t switch
        {
            Tool.Query => Outcome.Nothing,
            Tool.Bulldozer => Bulldoze(w, x, y, free),
            Tool.Road => Lay(w, x, y, Net.Road, t, free),
            Tool.Rail => Lay(w, x, y, Net.Rail, t, free),
            Tool.Wire => Lay(w, x, y, Net.Wire, t, free),
            Tool.Park => MakePark(w, x, y, free),
            _ => Build(w, KindOf(t)!.Value, x, y, free),
        };
    }

    private static bool Pay(World w, long cost, bool free)
    {
        if (free) return true;
        if (w.Funds < cost) return false;
        w.Funds -= cost;
        return true;
    }

    /// <summary>Land that can be built on once it's cleared: bare land, trees or rubble, with nothing on it.</summary>
    public static bool Clearable(World w, int x, int y) =>
        Field.In(x, y) && w.Net[x, y] == Net.None && w.Ground[x, y] is Ground.Land or Ground.Trees or Ground.Rubble;

    public static Outcome Bulldoze(World w, int x, int y, bool free = false)
    {
        var g = w.Ground[x, y];
        if (g == Ground.Built)
        {
            var z = w.ZoneAt(x, y)!;
            if (!Pay(w, z.Size * z.Size, free)) return Outcome.NoMoney;
            RemoveZone(w, z, Ground.Rubble);
            return Outcome.Done;
        }
        if (g is Ground.Radiation or Ground.Fire or Ground.Flood) return Outcome.Blocked;
        if (w.Net[x, y] == Net.None && g is Ground.Land or Ground.Water) return Outcome.Nothing;
        if (!Pay(w, 1, free)) return Outcome.NoMoney;
        if ((w.Net[x, y] & Net.Wire) != 0) w.PowerDirty = true;
        w.Net[x, y] = Net.None;
        if (g != Ground.Water) w.Ground[x, y] = Ground.Land;
        return Outcome.Done;
    }

    private static Outcome Lay(World w, int x, int y, Net n, Tool t, bool free)
    {
        var g = w.Ground[x, y];
        var has = w.Net[x, y];
        if ((has & n) != 0) return Outcome.Nothing;
        if (g is Ground.Built or Ground.Fire or Ground.Flood or Ground.Radiation) return Outcome.Blocked;
        if (g == Ground.Water)
        {
            // A bridge carries one thing only.
            if (has != Net.None) return Outcome.Blocked;
            if (!Pay(w, BridgeCost(t), free)) return Outcome.NoMoney;
        }
        else
        {
            // Two things can cross on a tile; never all three.
            if (has != Net.None && (has | n) == (Net.Road | Net.Rail | Net.Wire)) return Outcome.Blocked;
            if (!Pay(w, Cost(t) + (g == Ground.Land ? 0 : 1), free)) return Outcome.NoMoney;
            w.Ground[x, y] = Ground.Land;
        }
        w.Net[x, y] = has | n;
        if (n == Net.Wire) w.PowerDirty = true;
        return Outcome.Done;
    }

    private static Outcome MakePark(World w, int x, int y, bool free)
    {
        if (!Clearable(w, x, y)) return w.Ground[x, y] == Ground.Park ? Outcome.Nothing : Outcome.Blocked;
        if (!Pay(w, Cost(Tool.Park), free)) return Outcome.NoMoney;
        w.Ground[x, y] = Ground.Park;
        return Outcome.Done;
    }

    /// <summary>Whether a building of this kind could go with its middle at (x, y).</summary>
    public static bool CanBuild(World w, ZoneKind k, int x, int y)
    {
        int s = SizeOf(k);
        var (x0, y0) = Corner(s, x, y);
        for (int u = x0; u < x0 + s; u++)
        for (int v = y0; v < y0 + s; v++)
            if (!Clearable(w, u, v)) return false;
        if (k == ZoneKind.Seaport)
        {
            // A port must be on the water.
            for (int i = -1; i <= s; i++)
                if (w.IsWater(x0 + i, y0 - 1) || w.IsWater(x0 + i, y0 + s) || w.IsWater(x0 - 1, y0 + i) || w.IsWater(x0 + s, y0 + i)) return true;
            return false;
        }
        return true;
    }

    private static Outcome Build(World w, ZoneKind k, int x, int y, bool free)
    {
        if (!CanBuild(w, k, x, y)) return Outcome.Blocked;
        int s = SizeOf(k);
        var (x0, y0) = Corner(s, x, y);
        int clearing = 0;
        for (int u = x0; u < x0 + s; u++)
        for (int v = y0; v < y0 + s; v++)
            if (w.Ground[u, v] != Ground.Land) clearing++;
        if (!Pay(w, Cost(ToolFor(k)) + clearing, free)) return Outcome.NoMoney;
        AddZone(w, k, x0, y0);
        return Outcome.Done;
    }

    /// <summary>Puts a zone or building down with its top-left at (x0, y0), on land already checked.</summary>
    public static Zone AddZone(World w, ZoneKind k, int x0, int y0, int level = -1)
    {
        var z = new Zone { Kind = k, X = x0, Y = y0, Variant = w.Dice.Next(4) };
        z.Level = level >= 0 ? Math.Min(level, MaxLevel(k)) : z.Grows ? 0 : 1;
        int s = z.Size, id = w.Zones.Count;
        for (int u = x0; u < x0 + s; u++)
        for (int v = y0; v < y0 + s; v++)
        {
            w.Ground[u, v] = Ground.Built;
            w.Net[u, v] = Net.None;
            w.ZoneId[u, v] = id;
        }
        w.Zones.Add(z);
        z.Pop = PopOf(z);
        w.PowerDirty = true;
        return z;
    }

    /// <summary>Takes a zone away, leaving its tiles as <paramref name="leave"/> (rubble, fire, water or radiation).</summary>
    public static void RemoveZone(World w, Zone z, Ground leave)
    {
        for (int u = z.X; u < z.X + z.Size; u++)
        for (int v = z.Y; v < z.Y + z.Size; v++)
        {
            w.Ground[u, v] = leave;
            w.Net[u, v] = Net.None;
            if (leave == Ground.Fire) w.Timer[u, v] = (byte)(10 + w.Dice.Next(14));
        }
        w.Zones.Remove(z);
        w.Reindex();
        w.PowerDirty = true;
    }

    public static double PopOf(Zone z) => z.Kind switch
    {
        ZoneKind.Residential => ResidentsAt[z.Level] * (z.Wealth == 2 ? 0.85 : 1.0),
        ZoneKind.Commercial => ShopJobsAt[z.Level],
        ZoneKind.Industrial => FactoryJobsAt[z.Level],
        _ => 0,
    };

    // ───────────────────────────── time ─────────────────────────────

    /// <summary>One step: a slice of the zones, the power when it has changed, the fires and floods, the sprites; and the month's end.</summary>
    public static void Step(World w)
    {
        if (w.PowerDirty) Power(w);
        int n = w.Zones.Count, s = w.StepInMonth;
        int from = n * s / Field.StepsPerMonth, to = n * (s + 1) / Field.StepsPerMonth;
        for (int i = from; i < to && i < w.Zones.Count; i++) UpdateZone(w, w.Zones[i]);
        if (s % FireEvery == 0) Burn(w);
        Sprites(w);
        if (w.Shake > 0) w.Shake = Math.Max(0, w.Shake - Field.Step);
        w.StepInMonth++;
        if (w.StepInMonth >= Field.StepsPerMonth)
        {
            w.StepInMonth = 0;
            EndMonth(w);
        }
    }

    /// <summary>Runs to the end of the month (a whole month, from its start).</summary>
    public static void Month(World w)
    {
        do Step(w); while (w.StepInMonth != 0);
    }

    public static void Months(World w, int n)
    {
        for (int i = 0; i < n; i++) Month(w);
    }

    private static void EndMonth(World w)
    {
        Maps(w);
        Census(w);
        Demand(w);
        Evaluate(w);
        Decay(w);
        w.Month++;
        if (w.Disasters) RandomDisaster(w);
        Scenarios.Month(w);
        Tidings(w);
        if (w.Month % 12 == 0) YearEnd(w);
    }

    // ───────────────────────────── power ─────────────────────────────

    /// <summary>
    /// Power runs from every plant through power lines and from zone to touching zone, each zone taking its share,
    /// until the plants' capacity is used up: past that, the zones are dark.
    /// </summary>
    public static void Power(World w)
    {
        w.PowerDirty = false;
        Array.Clear(w.Power);
        foreach (var z in w.Zones) z.Powered = false;
        int capacity = 0, used = 0;
        var q = w.Queue;
        int head = 0, tail = 0;
        int stamp = NextStamp(w);
        foreach (var z in w.Zones)
        {
            if (!IsPlant(z.Kind)) continue;
            capacity += z.Kind == ZoneKind.Coal ? CoalPower : NuclearPower;
            z.Powered = true;
            for (int u = z.X; u < z.X + z.Size; u++)
            for (int v = z.Y; v < z.Y + z.Size; v++)
            {
                int k = u * Field.H + v;
                w.Seen[k] = stamp;
                w.Power[u, v] = true;
                q[tail++] = k;
            }
        }
        bool short_ = false;
        while (head < tail)
        {
            int k = q[head++], x = k / Field.H, y = k % Field.H;
            for (int d = 0; d < 4; d++)
            {
                int u = x + Dx[d], v = y + Dy[d];
                if (!Field.In(u, v)) continue;
                int kk = u * Field.H + v;
                if (w.Seen[kk] == stamp) continue;
                if (w.Ground[u, v] == Ground.Built)
                {
                    var z = w.Zones[w.ZoneId[u, v]];
                    if (!z.Powered)
                    {
                        int need = z.Size * z.Size;
                        if (used + need > capacity) { short_ = true; continue; }
                        used += need;
                        z.Powered = true;
                    }
                }
                else if ((w.Net[u, v] & Net.Wire) == 0) continue;
                w.Seen[kk] = stamp;
                w.Power[u, v] = true;
                q[tail++] = kk;
            }
        }
        w.PowerCapacity = capacity;
        w.PowerUsed = used;
        if (short_) w.Tell("BROWNOUTS: BUILD ANOTHER POWER PLANT", months: 4);
    }

    internal static readonly int[] Dx = [1, 0, -1, 0], Dy = [0, 1, 0, -1];

    private static int NextStamp(World w)
    {
        if (++w.SeenStamp == int.MaxValue)
        {
            Array.Clear(w.Seen);
            w.SeenStamp = 1;
        }
        return w.SeenStamp;
    }

    // ───────────────────────────── zones ─────────────────────────────

    /// <summary>The tiles along a zone's sides (not its corners).</summary>
    public static IEnumerable<(int X, int Y)> Edge(Zone z)
    {
        int s = z.Size;
        for (int i = 0; i < s; i++)
        {
            yield return (z.X + i, z.Y - 1);
            yield return (z.X + i, z.Y + s);
            yield return (z.X - 1, z.Y + i);
            yield return (z.X + s, z.Y + i);
        }
    }

    public static bool HasRoad(World w, Zone z)
    {
        foreach (var (x, y) in Edge(z))
            if (w.Has(x, y, Net.Road | Net.Rail)) return true;
        return false;
    }

    /// <summary>How the zone's people get on: -1 no road, 0 nowhere to go along it, 1 there and back.</summary>
    public static int Trip(World w, Zone z)
    {
        bool Wanted(ZoneKind k) => z.Kind == ZoneKind.Residential ? k is ZoneKind.Commercial or ZoneKind.Industrial : k == ZoneKind.Residential;
        // Nowhere in the city they'd go: they go out of town, and that's fine.
        bool any = false;
        foreach (var o in w.Zones)
            if (o.Level > 0 && o != z && Wanted(o.Kind)) { any = true; break; }
        int stamp = NextStamp(w);
        var q = w.Queue;
        int head = 0, tail = 0;
        foreach (var (x, y) in Edge(z))
        {
            if (!w.Has(x, y, Net.Road | Net.Rail)) continue;
            int k = x * Field.H + y;
            if (w.Seen[k] == stamp) continue;
            w.Seen[k] = stamp;
            w.From[k] = -1;
            q[tail++] = k;
        }
        if (tail == 0) return -1;
        if (!any) return 1;
        // Along the roads, nearest first, to the first zone of the kind wanted; how far is in the queue's layers.
        int layerEnd = tail, depth = 0;
        int found = -1;
        while (head < tail && found < 0)
        {
            if (head == layerEnd)
            {
                if (++depth > MaxTrip) break;
                layerEnd = tail;
            }
            int k = q[head++], x = k / Field.H, y = k % Field.H;
            for (int d = 0; d < 4 && found < 0; d++)
            {
                int u = x + Dx[d], v = y + Dy[d];
                if (!Field.In(u, v)) continue;
                if (w.Ground[u, v] == Ground.Built)
                {
                    var o = w.Zones[w.ZoneId[u, v]];
                    if (o != z && o.Level > 0 && Wanted(o.Kind)) found = k;
                    continue;
                }
                if (!w.Has(u, v, Net.Road | Net.Rail)) continue;
                int kk = u * Field.H + v;
                if (w.Seen[kk] == stamp) continue;
                w.Seen[kk] = stamp;
                w.From[kk] = k;
                q[tail++] = kk;
            }
        }
        if (found < 0) return 0;
        // The way back, with the traffic laid on it: cars on the roads, trains on the rails.
        int load = 4 + z.Level * 4, worst = 0;
        for (int k = found; k >= 0; k = w.From[k])
        {
            int x = k / Field.H, y = k % Field.H;
            bool rail = (w.Net[x, y] & Net.Road) == 0;
            w.Trips[x, y] = (ushort)Math.Min(60000, w.Trips[x, y] + (rail ? load / 2 : load));
            if (!rail) worst = Math.Max(worst, (int)w.Traffic[x, y]);
        }
        z.Jammed = worst > 225;
        return 1;
    }

    /// <summary>How much a zone wants to grow, -1 to 1: the demand for its kind, and how pleasant (or busy) its land is.</summary>
    public static double Appeal(World w, Zone z)
    {
        var (cx, cy) = ((int)z.Centre.X, (int)z.Centre.Y);
        double lv = w.LandValue[cx, cy] / 255.0, pol = w.Pollution[cx, cy] / 255.0, crime = w.Crime[cx, cy] / 255.0;
        double near = 0;
        for (int u = cx - 4; u <= cx + 4; u += 2)
        for (int v = cy - 4; v <= cy + 4; v += 2)
            if (Field.In(u, v)) near += w.Density[u, v];
        near /= 25 * 255.0;
        double s = z.Kind switch
        {
            ZoneKind.Residential => 0.12 + 0.7 * lv - 0.9 * pol - 0.5 * crime,
            ZoneKind.Commercial => 0.08 + 0.4 * lv + 0.6 * near - 0.4 * crime - 0.2 * pol,
            _ => 0.18 - 0.15 * crime,
        };
        double demand = z.Kind switch { ZoneKind.Residential => w.DemandR, ZoneKind.Commercial => w.DemandC, _ => w.DemandI };
        return Math.Clamp(0.8 * demand + 0.6 * s - 0.03 * z.Level, -1, 1);
    }

    /// <summary>A zone's turn: it grows a level, falls back one, or stays, as it finds things.</summary>
    public static void UpdateZone(World w, Zone z)
    {
        if (!z.Grows) return;
        int trip = Trip(w, z);
        z.NoRoad = trip < 0;
        z.Reached = trip > 0;
        if (trip < 1) z.Jammed = false;
        double v = Appeal(w, z);
        if (!z.Powered) v = Math.Min(v, -0.5);
        if (trip < 1) v = Math.Min(v, trip < 0 ? -0.6 : -0.35);
        if (z.Jammed) v -= 0.35;
        int was = z.Level;
        if (v > 0 && z.Level < MaxLevel(z.Kind) && w.Dice.Chance(0.08 + v * 0.7)) z.Level++;
        else if (v < -0.05 && z.Level > 0 && w.Dice.Chance(-v * 0.5)) z.Level--;
        z.Trend = Math.Sign(z.Level - was);
        if (z.Level != was)
        {
            var (cx, cy) = ((int)z.Centre.X, (int)z.Centre.Y);
            int lv = w.LandValue[cx, cy];
            z.Wealth = lv < 85 ? 0 : lv < 150 ? 1 : 2;
            if (z.Level == 0 || was == 0) z.Variant = w.Dice.Next(4);
        }
        z.Pop = PopOf(z);
    }

    // ───────────────────────────── the maps ─────────────────────────────

    /// <summary>Draws the month's maps afresh: traffic from the trips, people, the services' reach, pollution, land value and crime.</summary>
    public static void Maps(World w)
    {
        const int W = Field.W, H = Field.H;
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            w.Traffic[x, y] = (byte)((w.Traffic[x, y] + Math.Min(255, (int)w.Trips[x, y])) / 2);
            w.Trips[x, y] = 0;
            if ((w.Net[x, y] & (Net.Road | Net.Rail)) == 0) w.Traffic[x, y] = 0;
        }
        var f = new double[W, H];
        // People: where they live and work.
        foreach (var z in w.Zones)
        {
            double d = z.Kind switch
            {
                ZoneKind.Residential => z.Pop / ResidentsAt[^1] * 255,
                ZoneKind.Commercial => z.Pop / ShopJobsAt[^1] * 200,
                _ => 0,
            };
            if (d > 0) Stamp(f, z, d);
        }
        Store(w.Density, Blur(f, 1));
        // The police and fire stations' reach, weaker for too little money or no power.
        Cover(w, ZoneKind.Police, w.PoliceCover, Service.Police);
        Cover(w, ZoneKind.FireStation, w.FireCover, Service.Fire);
        // Pollution: industry and the coal plant most, ports and traffic some; parks and trees take a little away.
        f = new double[W, H];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            double p = w.Ground[x, y] switch
            {
                Ground.Fire => 120, Ground.Radiation => 255, Ground.Trees => -12, Ground.Park => -24, _ => 0,
            };
            if ((w.Net[x, y] & Net.Road) != 0) p += w.Traffic[x, y] * 0.45;
            f[x, y] = p;
        }
        foreach (var z in w.Zones)
        {
            double p = z.Kind switch
            {
                ZoneKind.Industrial => z.Level == 0 ? 0 : 50 + z.Level * 35,
                ZoneKind.Coal => 220, ZoneKind.Seaport => 90, ZoneKind.Airport => 90,
                _ => 0,
            };
            if (p > 0) Stamp(f, z, p);
        }
        Store(w.Pollution, Blur(Blur(Blur(f, 1), 1), 1));
        LandValues(w);
        // Crime: where people are crowded on poor land, and the police don't reach.
        f = new double[W, H];
        foreach (var z in w.Zones)
        {
            if (z.Kind is not (ZoneKind.Residential or ZoneKind.Commercial) || z.Level == 0) continue;
            var (cx, cy) = ((int)z.Centre.X, (int)z.Centre.Y);
            double c = 30 + w.Density[cx, cy] * 0.75 + Math.Max(0, 150 - w.LandValue[cx, cy]) * 0.7 - w.PoliceCover[cx, cy] * 0.9;
            Stamp(f, z, Math.Max(0, c));
        }
        Store(w.Crime, Blur(f, 1));
    }

    private static void Stamp(double[,] f, Zone z, double v)
    {
        for (int u = z.X; u < z.X + z.Size; u++)
        for (int v2 = z.Y; v2 < z.Y + z.Size; v2++) f[u, v2] += v;
    }

    private static void Store(byte[,] to, double[,] f)
    {
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++) to[x, y] = (byte)Math.Clamp(f[x, y], 0, 255);
    }

    /// <summary>Each tile the average of the square round it (<paramref name="r"/> tiles each way).</summary>
    private static double[,] Blur(double[,] f, int r)
    {
        const int W = Field.W, H = Field.H;
        var a = new double[W, H];
        var b = new double[W, H];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            double s = 0;
            int n = 0;
            for (int u = Math.Max(0, x - r); u <= Math.Min(W - 1, x + r); u++) { s += f[u, y]; n++; }
            a[x, y] = s / n;
        }
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            double s = 0;
            int n = 0;
            for (int v = Math.Max(0, y - r); v <= Math.Min(H - 1, y + r); v++) { s += a[x, v]; n++; }
            b[x, y] = s / n;
        }
        return b;
    }

    /// <summary>How much of what a service asks for it is getting, 0 to 1.</summary>
    public static double Funded(World w, Service s) => w.Funding[(int)s] / 100.0 * (w.Funds < 0 ? 0.5 : 1);

    public const int PoliceReach = 16, FireReach = 14;

    private static void Cover(World w, ZoneKind kind, byte[,] map, Service s)
    {
        var f = new double[Field.W, Field.H];
        int reach = kind == ZoneKind.Police ? PoliceReach : FireReach;
        foreach (var z in w.Zones)
        {
            if (z.Kind != kind) continue;
            double strength = 300 * Funded(w, s) * (z.Powered ? 1 : 0.4);
            var (cx, cy) = z.Centre;
            for (int u = (int)cx - reach; u <= (int)cx + reach; u++)
            for (int v = (int)cy - reach; v <= (int)cy + reach; v++)
            {
                if (!Field.In(u, v)) continue;
                double d = Math.Sqrt((u + 0.5 - cx) * (u + 0.5 - cx) + (v + 0.5 - cy) * (v + 0.5 - cy));
                if (d < reach) f[u, v] += strength * (1 - d / reach);
            }
        }
        Store(map, f);
    }

    /// <summary>
    /// Land value: highest near the middle of town, higher by the water, the trees and the parks; lower for
    /// pollution and crime.
    /// </summary>
    private static void LandValues(World w)
    {
        const int W = Field.W, H = Field.H;
        // Running sums of water, trees and parks, so the neighbourhood counts are quick.
        var water = new int[W + 1, H + 1];
        var green = new int[W + 1, H + 1];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            var g = w.Ground[x, y];
            water[x + 1, y + 1] = water[x, y + 1] + water[x + 1, y] - water[x, y] + (g == Ground.Water ? 1 : 0);
            green[x + 1, y + 1] = green[x, y + 1] + green[x + 1, y] - green[x, y] + (g == Ground.Trees ? 1 : g == Ground.Park ? 4 : 0);
        }
        int Count(int[,] s, int x, int y, int r)
        {
            int x0 = Math.Max(0, x - r), y0 = Math.Max(0, y - r), x1 = Math.Min(W, x + r + 1), y1 = Math.Min(H, y + r + 1);
            return s[x1, y1] - s[x0, y1] - s[x1, y0] + s[x0, y0];
        }
        var f = new double[W, H];
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
        {
            if (w.Ground[x, y] == Ground.Water) { f[x, y] = 0; continue; }
            double d = Math.Sqrt((x - w.CentreX) * (x - w.CentreX) + (y - w.CentreY) * (y - w.CentreY));
            double v = 40 + Math.Max(0, 110 - d * 2.4);
            v += Math.Min(40, Count(water, x, y, 4) * 2.0);
            v += Math.Min(60, Count(green, x, y, 3) * 2.5);
            v -= w.Pollution[x, y] * 0.75 + w.Crime[x, y] * 0.25;
            if (w.Ground[x, y] == Ground.Radiation) v = 0;
            f[x, y] = v;
        }
        Store(w.LandValue, Blur(f, 1));
    }

    // ───────────────────────────── the people ─────────────────────────────

    /// <summary>Counts the people and the jobs, and finds the middle of town.</summary>
    public static void Census(World w)
    {
        double res = 0, com = 0, ind = 0, sx = 0, sy = 0, sw = 0;
        foreach (var z in w.Zones)
        {
            switch (z.Kind)
            {
                case ZoneKind.Residential: res += z.Pop; break;
                case ZoneKind.Commercial: com += z.Pop; break;
                case ZoneKind.Industrial: ind += z.Pop; break;
            }
            double weight = z.Kind is ZoneKind.Residential or ZoneKind.Commercial ? z.Pop + 1 : 0.5;
            sx += z.Centre.X * weight;
            sy += z.Centre.Y * weight;
            sw += weight;
        }
        w.Residents = (int)res;
        w.ComJobs = (int)com;
        w.IndJobs = (int)ind;
        if (sw > 0) { w.CentreX = sx / sw; w.CentreY = sy / sw; }
    }

    /// <summary>What the outside world buys from the city's industry, by difficulty.</summary>
    public static double Market(Difficulty d) => d switch { Difficulty.Easy => 1.25, Difficulty.Medium => 1.0, _ => 0.8 };

    public const int StadiumNeeded = 30000, SeaportNeeded = 4000, AirportNeeded = 5000;

    /// <summary>
    /// The demand for each kind of zone. People come where there are jobs; shops come where there are people;
    /// industry sells to the outside world and needs workers. High taxes cool all three; a big city wants a
    /// stadium, its industry a seaport, its commerce an airport.
    /// </summary>
    public static void Demand(World w)
    {
        double res = w.Residents, workers = res * 0.5, jobs = w.Jobs;
        double r = workers < 1 ? 0.9 : Math.Clamp((jobs - workers) / Math.Max(workers, 250), -1, 1) + 0.25;
        double wantC = res * 0.2 + w.IndJobs * 0.12 + 30;
        double c = Math.Clamp((wantC - w.ComJobs) / Math.Max(wantC, 180), -1, 1);
        double wantI = res * 0.3 * Market(w.Difficulty) + 160;
        double i = Math.Clamp((wantI - w.IndJobs) / Math.Max(wantI, 180), -1, 1);
        double tax = (w.TaxRate - 7) * 0.08;
        r -= tax;
        c -= tax;
        i -= tax;
        bool Has(ZoneKind k) => w.Zones.Any(z => z.Kind == k);
        if (w.Residents > StadiumNeeded && !Has(ZoneKind.Stadium)) { r = Math.Min(r, -0.1); w.Tell("RESIDENTS DEMAND A STADIUM"); }
        if (w.IndJobs > SeaportNeeded && !Has(ZoneKind.Seaport)) { i = Math.Min(i, -0.1); w.Tell("INDUSTRY NEEDS A SEAPORT"); }
        if (w.ComJobs > AirportNeeded && !Has(ZoneKind.Airport)) { c = Math.Min(c, -0.1); w.Tell("COMMERCE NEEDS AN AIRPORT"); }
        w.DemandR = Math.Clamp(w.DemandR * 0.6 + r * 0.4, -1, 1);
        w.DemandC = Math.Clamp(w.DemandC * 0.6 + c * 0.4, -1, 1);
        w.DemandI = Math.Clamp(w.DemandI * 0.6 + i * 0.4, -1, 1);
    }

    /// <summary>
    /// What the people think: the problems they name, the mayor's score (0 to 1000, moving a quarter of the way to
    /// how things are each month), and how many approve.
    /// </summary>
    public static void Evaluate(World w)
    {
        double crime = 0, pol = 0, lv = 0, people = 0;
        foreach (var z in w.Zones)
        {
            if (z.Kind != ZoneKind.Residential || z.Pop <= 0) continue;
            var (cx, cy) = ((int)z.Centre.X, (int)z.Centre.Y);
            crime += w.Crime[cx, cy] * z.Pop;
            pol += w.Pollution[cx, cy] * z.Pop;
            lv += w.LandValue[cx, cy] * z.Pop;
            people += z.Pop;
        }
        double traffic = 0, roads = 0;
        int fires = 0;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
        {
            if ((w.Net[x, y] & Net.Road) != 0 && w.Traffic[x, y] > 0) { traffic += w.Traffic[x, y]; roads++; }
            if (w.Ground[x, y] == Ground.Fire) fires++;
        }
        double workers = w.Residents * 0.5;
        var p = w.Problems;
        p[(int)Problem.Crime] = people > 0 ? (int)Math.Clamp(crime / people / 255 * 140, 0, 100) : 0;
        p[(int)Problem.Pollution] = people > 0 ? (int)Math.Clamp(pol / people / 255 * 160, 0, 100) : 0;
        p[(int)Problem.Housing] = people > 0 ? (int)Math.Clamp(lv / people / 255 * 70, 0, 100) : 0;
        p[(int)Problem.Taxes] = Math.Clamp((w.TaxRate - 4) * 5, 0, 100);
        p[(int)Problem.Traffic] = roads > 0 ? (int)Math.Clamp(traffic / roads / 255 * 150, 0, 100) : 0;
        p[(int)Problem.Unemployment] = workers > 50 ? (int)Math.Clamp((workers - w.Jobs) / workers * 100, 0, 100) : 0;
        p[(int)Problem.Fires] = Math.Clamp(fires * 3, 0, 100);
        int growing = w.Zones.Count(z => z.Grows), dark = w.Zones.Count(z => z.Grows && !z.Powered), cut = w.Zones.Count(z => z.Grows && z.NoRoad);
        double target = 1000
                        - 2.2 * p[(int)Problem.Crime] - 2.2 * p[(int)Problem.Pollution] - 0.9 * p[(int)Problem.Housing]
                        - 1.6 * p[(int)Problem.Taxes] - 1.6 * p[(int)Problem.Traffic] - 2.0 * p[(int)Problem.Unemployment]
                        - 2.5 * p[(int)Problem.Fires];
        if (growing > 0) target -= 300.0 * dark / growing + 200.0 * cut / growing;
        if (w.Funds < 0) target -= 150;
        if (w.History.Count > 0 && w.Residents < w.History[^1]) target -= 60;
        target = Math.Clamp(target, 0, 1000);
        w.Score = (int)Math.Round((w.Score * 3 + target) / 4);
        w.Approval = (int)Math.Clamp(w.Score / 10.0 + (7 - w.TaxRate) * 2.5, 0, 100);
    }

    /// <summary>The worst problems, the worst first, with how many name each.</summary>
    public static IEnumerable<(Problem What, int Share)> WorstProblems(World w) =>
        Enum.GetValues<Problem>().Select(p => (p, w.Problems[(int)p])).Where(t => t.Item2 > 0).OrderByDescending(t => t.Item2).Take(4);

    // ───────────────────────────── money ─────────────────────────────

    /// <summary>What each service asks for a year: a dollar a tile of road, two of rail, a hundred a station.</summary>
    public static (long Roads, long Police, long Fire) Asks(World w)
    {
        long roads = 0;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
        {
            var n = w.Net[x, y];
            if ((n & Net.Road) != 0) roads += w.IsWater(x, y) ? 4 : 1;
            if ((n & Net.Rail) != 0) roads += w.IsWater(x, y) ? 6 : 2;
        }
        long police = 100L * w.Zones.Count(z => z.Kind == ZoneKind.Police);
        long fire = 100L * w.Zones.Count(z => z.Kind == ZoneKind.FireStation);
        return (roads, police, fire);
    }

    /// <summary>A year's taxes: from the people and the jobs, at the tax rate, more where the land is worth more.</summary>
    public static long Taxes(World w)
    {
        double lv = 0, n = 0;
        foreach (var z in w.Zones)
            if (z.Grows && z.Pop > 0) { lv += w.LandValue[(int)z.Centre.X, (int)z.Centre.Y] * z.Pop; n += z.Pop; }
        double avg = n > 0 ? lv / n : 0;
        double rate = w.Difficulty switch { Difficulty.Easy => 1.2, Difficulty.Medium => 1.0, _ => 0.85 };
        return (long)((w.Residents + w.Jobs * 0.4) * w.TaxRate / 100.0 * (0.6 + avg / 150) * 3.0 * rate);
    }

    /// <summary>The year's end: the taxes come in and the services are paid what they're funded, as far as the money goes.</summary>
    public static void YearEnd(World w)
    {
        long taxes = Taxes(w);
        var (roads, police, fire) = Asks(w);
        long pr = roads * w.Funding[0] / 100, pp = police * w.Funding[1] / 100, pf = fire * w.Funding[2] / 100;
        long have = Math.Max(0, w.Funds + taxes), want = pr + pp + pf;
        if (want > have && want > 0)
        {
            pr = pr * have / want;
            pp = pp * have / want;
            pf = pf * have / want;
        }
        w.Funds += taxes - pr - pp - pf;
        w.LastYear = new Accounts(w.Year - 1, taxes, roads, police, fire, pr, pp, pf);
        w.History.Add(w.Residents);
        w.BudgetDue = true;
        w.Heard.Add(Sfx.Chime);
    }

    /// <summary>Roads the city doesn't pay to keep up fall to rubble, a few each month.</summary>
    private static void Decay(World w)
    {
        double paid = w.LastYear.RoadsAsked > 0 ? (double)w.LastYear.RoadsPaid / w.LastYear.RoadsAsked : w.Funding[0] / 100.0;
        paid = Math.Min(paid, w.Funding[0] / 100.0);
        if (paid >= 0.95) return;
        int roads = 0;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
            if ((w.Net[x, y] & Net.Road) != 0) roads++;
        double share = roads * (1 - paid) / 40;
        int broken = (int)share + (w.Dice.Chance(share % 1) ? 1 : 0);
        for (int tries = 0; broken > 0 && tries < 4000; tries++)
        {
            int x = w.Dice.Next(Field.W), y = w.Dice.Next(Field.H);
            if ((w.Net[x, y] & Net.Road) == 0) continue;
            w.Net[x, y] &= ~Net.Road;
            if (w.Net[x, y] == Net.None && w.Ground[x, y] == Ground.Land) w.Ground[x, y] = Ground.Rubble;
            broken--;
        }
        w.Tell("ROADS ARE FALLING APART: FUND THEM", months: 12);
    }

    /// <summary>The month's news: milestones, and what the people are asking for.</summary>
    private static void Tidings(World w)
    {
        string was = World.ClassOf(w.Record), now = World.ClassOf(w.Residents);
        if (w.Residents > w.Record)
        {
            w.Record = w.Residents;
            if (now != was)
            {
                w.Tell($"POPULATION {w.Residents:N0}: {w.Name} IS NOW A {now}", months: 1200);
                w.Heard.Add(Sfx.Fanfare);
            }
        }
        int growing = w.Zones.Count(z => z.Grows);
        if (growing == 0) return;
        if (w.Zones.Count(z => z.Grows && z.NoRoad) * 4 > growing) w.Tell("ZONES NEED ROADS");
        else if (w.Zones.Count(z => z.Grows && !z.Powered) * 4 > growing) w.Tell("ZONES NEED POWER");
        if (w.Zones.Count(z => z.Jammed) * 6 > growing && w.Tell("TRAFFIC JAMS REPORTED: BUILD MORE ROADS OR RAIL", months: 9)) w.Heard.Add(Sfx.Horn);
        if (w.Residents > 1500 && !w.Zones.Any(z => z.Kind == ZoneKind.Police)) w.Tell("CITIZENS ASK FOR A POLICE STATION", months: 12);
        if (w.Residents > 2500 && !w.Zones.Any(z => z.Kind == ZoneKind.FireStation)) w.Tell("CITIZENS ASK FOR A FIRE DEPARTMENT", months: 12);
        if (w.Problems[(int)Problem.Pollution] > 50) w.Tell("POLLUTION IS VERY HIGH", months: 12);
        if (w.Problems[(int)Problem.Crime] > 50) w.Tell("CRIME IS VERY HIGH", months: 12);
        if (w.DemandR > 0.5 && w.Residents > 0) w.Tell("MORE RESIDENTIAL ZONES NEEDED", months: 10);
        else if (w.DemandC > 0.5) w.Tell("MORE COMMERCIAL ZONES NEEDED", months: 10);
        else if (w.DemandI > 0.5) w.Tell("MORE INDUSTRIAL ZONES NEEDED", months: 10);
        if (w.Funds < 0) w.Tell("THE CITY IS IN DEBT", months: 6);
    }

    // ───────────────────────────── disasters ─────────────────────────────

    private static void RandomDisaster(World w)
    {
        int built = w.Zones.Count;
        if (built < 8) return;
        // A fire breaking out on its own, likelier where the fire department doesn't reach.
        double pFire = w.Difficulty switch { Difficulty.Easy => 0.04, Difficulty.Medium => 0.07, _ => 0.1 };
        if (w.Dice.Chance(pFire))
        {
            var z = w.Zones[w.Dice.Next(w.Zones.Count)];
            var (cx, cy) = ((int)z.Centre.X, (int)z.Centre.Y);
            if (z.Grows && z.Level > 0 && !w.Dice.Chance(w.FireCover[cx, cy] / 255.0)) StartFire(w, cx, cy);
        }
        double p = w.Difficulty switch { Difficulty.Easy => 0.01, Difficulty.Medium => 0.018, _ => 0.03 };
        if (!w.Dice.Chance(p)) return;
        var pick = w.Dice.Next(100) switch
        {
            < 35 => Disaster.Fire, < 52 => Disaster.Flood, < 68 => Disaster.Tornado, < 78 => Disaster.Earthquake,
            < 88 => Disaster.Monster, < 95 => Disaster.PlaneCrash, _ => Disaster.Meltdown,
        };
        Start(w, pick);
    }

    /// <summary>Sets a disaster going. False if it can't happen here (a meltdown with no nuclear plant, a flood with no water).</summary>
    public static bool Start(World w, Disaster d)
    {
        switch (d)
        {
            case Disaster.Fire:
            {
                var (x, y) = Somewhere(w, (u, v) => w.Ground[u, v] is Ground.Built or Ground.Trees);
                if (x < 0) return false;
                StartFire(w, x, y);
                w.Tell("FIRE REPORTED!", x, y, months: 0);
                w.Heard.Add(Sfx.Siren);
                return true;
            }
            case Disaster.Flood:
            {
                // Where the water meets the land, nearest the middle of town.
                (int X, int Y) best = (-1, -1);
                double bd = double.MaxValue;
                for (int x = 1; x < Field.W - 1; x++)
                for (int y = 1; y < Field.H - 1; y++)
                {
                    if (w.Ground[x, y] == Ground.Water || !Near(w, x, y, Ground.Water)) continue;
                    double d2 = (x - w.CentreX) * (x - w.CentreX) + (y - w.CentreY) * (y - w.CentreY) + w.Dice.Next() * 30;
                    if (d2 < bd) { bd = d2; best = (x, y); }
                }
                if (best.X < 0) return false;
                for (int u = best.X - 4; u <= best.X + 4; u++)
                for (int v = best.Y - 4; v <= best.Y + 4; v++)
                    if (Field.In(u, v) && w.Ground[u, v] != Ground.Water && Near(w, u, v, Ground.Water)) Flood(w, u, v, 36);
                w.Tell("FLOODING REPORTED!", best.X, best.Y, months: 0);
                w.Heard.Add(Sfx.Splash);
                w.Heard.Add(Sfx.Siren);
                return true;
            }
            case Disaster.Tornado:
            {
                double x = Math.Clamp(w.CentreX + (w.Dice.Next() - 0.5) * 40, 2, Field.W - 3), y = Math.Clamp(w.CentreY + (w.Dice.Next() - 0.5) * 30, 2, Field.H - 3);
                double a = w.Dice.Next() * Math.Tau;
                w.Sprites.Add(new Sprite { Kind = SpriteKind.Tornado, X = x, Y = y, Dx = Math.Cos(a), Dy = Math.Sin(a), Life = 12 });
                w.Tell("TORNADO REPORTED!", (int)x, (int)y, months: 0);
                w.Heard.Add(Sfx.Wind);
                return true;
            }
            case Disaster.Earthquake:
                Earthquake(w);
                return true;
            case Disaster.Monster:
            {
                // Up out of the water if there is any, else in from the edge; making for the worst pollution.
                var (sx, sy) = Somewhere(w, (u, v) => w.Ground[u, v] == Ground.Water && (u < 3 || v < 3 || u > Field.W - 4 || v > Field.H - 4));
                if (sx < 0) (sx, sy) = Somewhere(w, (u, v) => w.Ground[u, v] == Ground.Water);
                if (sx < 0) (sx, sy) = (0, w.Dice.Next(Field.H));
                var (tx, ty) = Dirtiest(w);
                w.Sprites.Add(new Sprite { Kind = SpriteKind.Monster, X = sx + 0.5, Y = sy + 0.5, Tx = tx + 0.5, Ty = ty + 0.5, Life = 30 });
                w.Tell("A MONSTER HAS BEEN SIGHTED!", tx, ty, months: 0);
                w.Heard.Add(Sfx.Roar);
                return true;
            }
            case Disaster.PlaneCrash:
            {
                var port = w.Zones.FirstOrDefault(z => z.Kind == ZoneKind.Airport);
                double tx = (port?.Centre.X ?? w.CentreX) + (w.Dice.Next() - 0.5) * 16, ty = (port?.Centre.Y ?? w.CentreY) + (w.Dice.Next() - 0.5) * 16;
                tx = Math.Clamp(tx, 2, Field.W - 3);
                ty = Math.Clamp(ty, 2, Field.H - 3);
                double a = w.Dice.Next() * Math.Tau;
                w.Sprites.Add(new Sprite { Kind = SpriteKind.Plane, X = tx - Math.Cos(a) * 30, Y = ty - Math.Sin(a) * 30, Tx = tx, Ty = ty, Life = 3 });
                w.Tell("A PLANE HAS CRASHED!", (int)tx, (int)ty, months: 0);
                return true;
            }
            case Disaster.Meltdown:
            {
                var plant = w.Zones.FirstOrDefault(z => z.Kind == ZoneKind.Nuclear);
                if (plant == null) return false;
                Meltdown(w, plant);
                return true;
            }
        }
        return false;
    }

    private static bool Near(World w, int x, int y, Ground g)
    {
        for (int d = 0; d < 4; d++)
            if (Field.In(x + Dx[d], y + Dy[d]) && w.Ground[x + Dx[d], y + Dy[d]] == g) return true;
        return false;
    }

    /// <summary>A tile that fits, nearer the middle of town the likelier; (-1, -1) if none is found.</summary>
    private static (int X, int Y) Somewhere(World w, Func<int, int, bool> fits)
    {
        for (int tries = 0; tries < 4000; tries++)
        {
            int x, y;
            if (tries < 2000)
            {
                x = (int)(w.CentreX + (w.Dice.Next() - 0.5) * (12 + tries / 20.0));
                y = (int)(w.CentreY + (w.Dice.Next() - 0.5) * (12 + tries / 20.0));
            }
            else
            {
                x = w.Dice.Next(Field.W);
                y = w.Dice.Next(Field.H);
            }
            if (Field.In(x, y) && fits(x, y)) return (x, y);
        }
        return (-1, -1);
    }

    private static (int X, int Y) Dirtiest(World w)
    {
        (int, int) best = ((int)w.CentreX, (int)w.CentreY);
        int most = 0;
        for (int x = 0; x < Field.W; x += 2)
        for (int y = 0; y < Field.H; y += 2)
            if (w.Pollution[x, y] > most) { most = w.Pollution[x, y]; best = (x, y); }
        return best;
    }

    public static bool Flammable(World w, int x, int y) => Field.In(x, y) && w.Ground[x, y] is Ground.Built or Ground.Trees or Ground.Park;

    /// <summary>Sets a tile alight; a building catches whole. A nuclear plant catching fire melts down.</summary>
    public static void StartFire(World w, int x, int y)
    {
        if (!Flammable(w, x, y)) return;
        if (w.Ground[x, y] == Ground.Built)
        {
            var z = w.ZoneAt(x, y)!;
            if (z.Kind == ZoneKind.Nuclear) { Meltdown(w, z); return; }
            RemoveZone(w, z, Ground.Fire);
            return;
        }
        w.Ground[x, y] = Ground.Fire;
        w.Net[x, y] = Net.None;
        w.Timer[x, y] = (byte)(8 + w.Dice.Next(12));
    }

    private static void Flood(World w, int x, int y, int timer)
    {
        if (!Field.In(x, y) || w.Ground[x, y] is Ground.Water or Ground.Flood or Ground.Radiation) return;
        if (w.Ground[x, y] == Ground.Built) { var z = w.ZoneAt(x, y)!; RemoveZone(w, z, Ground.Rubble); }
        if ((w.Net[x, y] & Net.Wire) != 0) w.PowerDirty = true;
        w.Ground[x, y] = Ground.Flood;
        w.Net[x, y] = Net.None;
        w.Timer[x, y] = (byte)Math.Max(1, timer);
    }

    /// <summary>Fires spread to what will burn and burn out to rubble (sooner where the fire department reaches); floods rise and go down.</summary>
    private static void Burn(World w)
    {
        var fires = new List<(int X, int Y)>();
        var floods = new List<(int X, int Y)>();
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
        {
            if (w.Ground[x, y] == Ground.Fire) fires.Add((x, y));
            else if (w.Ground[x, y] == Ground.Flood) floods.Add((x, y));
        }
        foreach (var (x, y) in fires)
        {
            if (w.Ground[x, y] != Ground.Fire) continue;
            double cover = w.FireCover[x, y] / 255.0;
            for (int d = 0; d < 4; d++)
                if (Flammable(w, x + Dx[d], y + Dy[d]) && w.Dice.Chance(0.07 * (1 - 0.7 * cover))) StartFire(w, x + Dx[d], y + Dy[d]);
            if (w.Timer[x, y] > 0) w.Timer[x, y]--;
            if (w.Timer[x, y] == 0 || w.Dice.Chance(cover * 0.12)) w.Ground[x, y] = Ground.Rubble;
        }
        foreach (var (x, y) in floods)
        {
            int t = w.Timer[x, y];
            if (t > 16)
                for (int d = 0; d < 4; d++)
                {
                    int u = x + Dx[d], v = y + Dy[d];
                    if (Field.In(u, v) && w.Ground[u, v] is not (Ground.Water or Ground.Flood or Ground.Radiation) && w.Dice.Chance(0.35)) Flood(w, u, v, t - 2);
                }
            w.Timer[x, y] = (byte)Math.Max(0, t - 1);
            if (w.Timer[x, y] == 0) w.Ground[x, y] = w.Dice.Chance(0.3) ? Ground.Rubble : Ground.Land;
        }
    }

    /// <summary>Wrecks a tile: a building to rubble, a road or anything on the land to rubble. Water stays water.</summary>
    public static void Wreck(World w, int x, int y, bool fire = false)
    {
        if (!Field.In(x, y)) return;
        var g = w.Ground[x, y];
        if (g is Ground.Water) { if (w.Net[x, y] != Net.None) { w.Net[x, y] = Net.None; w.PowerDirty = true; } return; }
        if (g is Ground.Radiation or Ground.Fire or Ground.Flood) return;
        if (fire && Flammable(w, x, y)) { StartFire(w, x, y); return; }
        if (g == Ground.Built) { RemoveZone(w, w.ZoneAt(x, y)!, Ground.Rubble); return; }
        if (g == Ground.Land && w.Net[x, y] == Net.None) return;
        if ((w.Net[x, y] & Net.Wire) != 0) w.PowerDirty = true;
        w.Net[x, y] = Net.None;
        w.Ground[x, y] = Ground.Rubble;
    }

    private static void Earthquake(World w)
    {
        w.Shake = 3;
        w.Heard.Add(Sfx.Quake);
        w.Tell("MAJOR EARTHQUAKE!", (int)w.CentreX, (int)w.CentreY, months: 0);
        int n = 80 + w.Zones.Count;
        for (int k = 0; k < n; k++)
        {
            double a = w.Dice.Next() * Math.Tau, r = Math.Sqrt(w.Dice.Next()) * 26;
            int x = (int)(w.CentreX + Math.Cos(a) * r), y = (int)(w.CentreY + Math.Sin(a) * r);
            if (!Field.In(x, y) || w.Ground[x, y] == Ground.Land && w.Net[x, y] == Net.None) continue;
            Wreck(w, x, y, fire: w.Dice.Chance(0.15));
        }
    }

    public static void Meltdown(World w, Zone plant)
    {
        var (cx, cy) = plant.Centre;
        RemoveZone(w, plant, Ground.Fire);
        w.Sprites.Add(new Sprite { Kind = SpriteKind.Explosion, X = cx, Y = cy, Life = 1.5 });
        for (int u = (int)cx - 7; u <= (int)cx + 7; u++)
        for (int v = (int)cy - 7; v <= (int)cy + 7; v++)
        {
            double d = Math.Sqrt((u + 0.5 - cx) * (u + 0.5 - cx) + (v + 0.5 - cy) * (v + 0.5 - cy));
            if (!Field.In(u, v) || d > 7 || w.Ground[u, v] == Ground.Water) continue;
            if (w.Dice.Chance(0.5 - d * 0.05))
            {
                if (w.Ground[u, v] == Ground.Built) Wreck(w, u, v);
                w.Ground[u, v] = Ground.Radiation;
                w.Net[u, v] = Net.None;
            }
            else if (d < 5 && w.Dice.Chance(0.3)) Wreck(w, u, v, fire: true);
        }
        w.PowerDirty = true;
        w.Tell("NUCLEAR MELTDOWN!", (int)cx, (int)cy, months: 0);
        w.Heard.Add(Sfx.Explosion);
        w.Heard.Add(Sfx.Siren);
    }

    /// <summary>The things moving over the map: a tornado wrecks what it passes, a monster wrecks and burns on its way to the worst pollution, a falling airliner explodes where it lands.</summary>
    private static void Sprites(World w)
    {
        double dt = Field.Step;
        for (int i = w.Sprites.Count - 1; i >= 0; i--)
        {
            var s = w.Sprites[i];
            s.Age += dt;
            s.Life -= dt;
            switch (s.Kind)
            {
                case SpriteKind.Tornado:
                {
                    double turn = (w.Dice.Next() - 0.5) * 0.5;
                    (s.Dx, s.Dy) = (s.Dx * Math.Cos(turn) - s.Dy * Math.Sin(turn), s.Dx * Math.Sin(turn) + s.Dy * Math.Cos(turn));
                    s.X += s.Dx * 4 * dt;
                    s.Y += s.Dy * 4 * dt;
                    if (s.X < 1 || s.X > Field.W - 2) s.Dx = -s.Dx;
                    if (s.Y < 1 || s.Y > Field.H - 2) s.Dy = -s.Dy;
                    s.X = Math.Clamp(s.X, 1, Field.W - 2);
                    s.Y = Math.Clamp(s.Y, 1, Field.H - 2);
                    Wreck(w, (int)s.X, (int)s.Y);
                    if (w.Dice.Chance(0.3)) Wreck(w, (int)s.X + w.Dice.Next(3) - 1, (int)s.Y + w.Dice.Next(3) - 1);
                    break;
                }
                case SpriteKind.Monster:
                {
                    double dx = s.Tx - s.X, dy = s.Ty - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < 1.5)
                    {
                        var (x, y) = Somewhere(w, (u, v) => w.Ground[u, v] == Ground.Built);
                        if (x < 0) (x, y) = (w.Dice.Next(Field.W), w.Dice.Next(Field.H));
                        (s.Tx, s.Ty) = (x + 0.5, y + 0.5);
                        break;
                    }
                    s.Dx = dx / d;
                    s.Dy = dy / d;
                    s.X += s.Dx * 2.2 * dt;
                    s.Y += s.Dy * 2.2 * dt;
                    int tx = (int)s.X, ty = (int)s.Y;
                    if (Field.In(tx, ty) && w.Ground[tx, ty] != Ground.Water) Wreck(w, tx, ty, fire: w.Dice.Chance(0.04));
                    if (s.Life < 0) w.Tell("THE MONSTER HAS GONE", months: 0);
                    break;
                }
                case SpriteKind.Plane:
                {
                    double dx = s.Tx - s.X, dy = s.Ty - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    double step = Math.Max(d, 0.01) / Math.Max(s.Life, dt) * dt;
                    s.Dx = dx / Math.Max(d, 0.01);
                    s.Dy = dy / Math.Max(d, 0.01);
                    s.X += s.Dx * Math.Min(step, d);
                    s.Y += s.Dy * Math.Min(step, d);
                    if (s.Life <= 0)
                    {
                        w.Sprites.Add(new Sprite { Kind = SpriteKind.Explosion, X = s.Tx, Y = s.Ty, Life = 1.5 });
                        for (int u = (int)s.Tx - 1; u <= (int)s.Tx + 1; u++)
                        for (int v = (int)s.Ty - 1; v <= (int)s.Ty + 1; v++) Wreck(w, u, v, fire: true);
                        w.Heard.Add(Sfx.Explosion);
                    }
                    break;
                }
            }
            if (s.Life <= 0) w.Sprites.Remove(s);
        }
    }
}
