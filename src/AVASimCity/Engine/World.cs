namespace AVASimCity.Engine;

/// <summary>
/// The screen and the map: a fixed 1024×768 screen, and a map of 120 by 100 tiles seen from above. The simulation
/// runs in steps of a 60th of a second at normal speed, 192 of them to the month.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int W = 120, H = 100;
    public const int StepsPerMonth = 192;
    public const int StartYear = 1900;

    public static bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
}

/// <summary>A small random number generator whose state can be copied (and saved).</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;
    public Dice(ulong state, bool raw) => _s = raw ? state : state * 0x9E3779B97F4A7C15UL + 1;
    public readonly ulong State => _s;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public bool Chance(double p) => Next() < p;
}

/// <summary>What lies on a tile, under any road, rail or power line.</summary>
public enum Ground : byte
{
    Land, Water, Trees, Rubble, Park,
    /// <summary>Part of a zone or a building: see <see cref="World.ZoneId"/>.</summary>
    Built,
    Fire, Flood, Radiation,
}

/// <summary>The networks a tile carries: a road, a railway, a power line, or two of them crossing.</summary>
[Flags]
public enum Net : byte { None = 0, Road = 1, Rail = 2, Wire = 4 }

/// <summary>What a zone or building is.</summary>
public enum ZoneKind : byte { Residential, Commercial, Industrial, Police, FireStation, Stadium, Seaport, Airport, Coal, Nuclear }

/// <summary>The mayor's tools, in the order of the palette.</summary>
public enum Tool
{
    Query, Bulldozer, Road, Rail, Wire, Park, Residential, Commercial, Industrial,
    Police, FireStation, Stadium, Seaport, Airport, Coal, Nuclear,
}

/// <summary>The maps that can be laid over the city.</summary>
public enum Overlay { None, Population, Traffic, Pollution, Crime, LandValue, Power, Police, Fire }

public enum Disaster { Fire, Flood, Tornado, Earthquake, Monster, PlaneCrash, Meltdown }

public enum Difficulty { Easy, Medium, Hard }

/// <summary>Things that move over the map: a tornado, a monster, an airliner coming down, an explosion.</summary>
public enum SpriteKind { Tornado, Monster, Plane, Explosion }

/// <summary>The services the budget pays for.</summary>
public enum Service { Roads, Police, Fire }

/// <summary>The city's complaints, each 0 to 100: how many of the people name it as the worst problem.</summary>
public enum Problem { Crime, Pollution, Housing, Taxes, Traffic, Unemployment, Fires }

/// <summary>A zone or a building: three by three, four by four, or (the airport) six by six tiles.</summary>
public sealed class Zone
{
    public ZoneKind Kind;
    /// <summary>The top-left tile.</summary>
    public int X, Y;
    /// <summary>How far it has grown: 0 is an empty lot. Buildings are always at 1.</summary>
    public int Level;
    /// <summary>Residents (a residential zone) or jobs (commercial, industrial).</summary>
    public double Pop;
    public bool Powered;
    /// <summary>How well-off it is, from the land value: 0 poor, 1 middling, 2 wealthy.</summary>
    public int Wealth;
    /// <summary>Which of the looks for its kind and size it has.</summary>
    public int Variant;
    /// <summary>No road (or rail) to it; or its people couldn't get where they wanted to go.</summary>
    public bool NoRoad, Jammed;
    /// <summary>Its people got where they were going last month.</summary>
    public bool Reached;
    /// <summary>Which way it went last month: +1 grew, -1 declined, 0 neither.</summary>
    public int Trend;

    public int Size => Sim.SizeOf(Kind);
    public bool Grows => Kind is ZoneKind.Residential or ZoneKind.Commercial or ZoneKind.Industrial;
    public (double X, double Y) Centre => (X + Size / 2.0, Y + Size / 2.0);
    public bool Covers(int x, int y) => x >= X && y >= Y && x < X + Size && y < Y + Size;
    public Zone Copy() => (Zone)MemberwiseClone();
}

/// <summary>Something moving over the map, in tiles, with the seconds it has left.</summary>
public sealed class Sprite
{
    public SpriteKind Kind;
    public double X, Y, Dx, Dy, Life, Age;
    /// <summary>Where it is making for (a monster, a falling plane).</summary>
    public double Tx, Ty;
    public Sprite Copy() => (Sprite)MemberwiseClone();
}

/// <summary>A year's accounts: what came in, what each service asked for and what it got.</summary>
public readonly record struct Accounts(int Year, long Taxes, long RoadsAsked, long PoliceAsked, long FireAsked, long RoadsPaid, long PolicePaid, long FirePaid)
{
    public long Spent => RoadsPaid + PolicePaid + FirePaid;
}

/// <summary>
/// Everything about a city that changes, kept together so it can be copied, saved and loaded: the land and what's
/// built on it, the zones, the money, the maps the simulation draws (traffic, pollution, crime, land value and the
/// rest), the people's opinion of their mayor, and whatever disaster is under way.
/// </summary>
public sealed class World
{
    public string Name = "NEWVILLE";
    public int Seed;
    public Difficulty Difficulty = Difficulty.Easy;
    /// <summary>The scenario being played (-1: a city of one's own).</summary>
    public int Scenario = -1;
    /// <summary>The scenario's goal (what it means depends on the scenario), the month it began, and how it ended (1 won, -1 lost, 0 not yet).</summary>
    public int Goal, StartMonth, Result;

    public Ground[,] Ground = new Ground[Field.W, Field.H];
    public Net[,] Net = new Net[Field.W, Field.H];
    /// <summary>Which zone each tile belongs to (-1: none). Rebuilt from <see cref="Zones"/> by <see cref="Reindex"/>.</summary>
    public int[,] ZoneId = New(-1);
    /// <summary>How long a fire or a flood on a tile has left, in fire steps.</summary>
    public byte[,] Timer = new byte[Field.W, Field.H];
    public List<Zone> Zones = [];

    // The maps, redrawn every month, 0 to 255.
    public byte[,] Traffic = new byte[Field.W, Field.H];
    /// <summary>This month's trips over each tile so far, made into <see cref="Traffic"/> at the month's end.</summary>
    public ushort[,] Trips = new ushort[Field.W, Field.H];
    public byte[,] Pollution = new byte[Field.W, Field.H];
    public byte[,] Crime = new byte[Field.W, Field.H];
    public byte[,] LandValue = new byte[Field.W, Field.H];
    public byte[,] Density = new byte[Field.W, Field.H];
    public byte[,] PoliceCover = new byte[Field.W, Field.H];
    public byte[,] FireCover = new byte[Field.W, Field.H];
    public bool[,] Power = new bool[Field.W, Field.H];
    public bool PowerDirty = true;

    /// <summary>Months since the city was founded, and the step within this one.</summary>
    public int Month, StepInMonth;
    public int Year => Field.StartYear + Month / 12;

    public long Funds = 20000;
    public int TaxRate = 7;
    /// <summary>The share of what each service asks for that the city pays, in percent.</summary>
    public int[] Funding = [100, 100, 100];
    public Accounts LastYear;
    /// <summary>Settle each year's budget without showing the window.</summary>
    public bool AutoBudget;
    public bool Disasters = true;

    /// <summary>The demand for each kind of zone, -1 to 1.</summary>
    public double DemandR = 0.6, DemandC = 0.1, DemandI = 0.4;
    public int Residents, ComJobs, IndJobs;
    public int PowerCapacity, PowerUsed;
    /// <summary>The middle of the city: where its people are, on average.</summary>
    public double CentreX = Field.W / 2.0, CentreY = Field.H / 2.0;
    /// <summary>The mayor's score, 0 to 1000, and how many of the people think the mayor is doing a good job.</summary>
    public int Score = 500, Approval = 50;
    public int[] Problems = new int[7];
    /// <summary>The population at the end of each year.</summary>
    public List<int> History = [];
    /// <summary>The biggest the city has been, for the news when it reaches a new class.</summary>
    public int Record;

    public List<Sprite> Sprites = [];
    /// <summary>Seconds of earthquake shaking left.</summary>
    public double Shake;
    public Dice Dice = new(1);

    /// <summary>Sounds and news for the host, taken and cleared each frame (not copied or saved).</summary>
    public List<Sfx> Heard = [];
    public List<(string Text, int X, int Y)> News = [];
    /// <summary>When each piece of news was last told, so it isn't told every month.</summary>
    public Dictionary<string, int> Told = [];
    /// <summary>The year's end has come: the host may want to show the budget.</summary>
    public bool BudgetDue;

    // Scratch space for the searches (not copied or saved).
    internal int[] Seen = new int[Field.W * Field.H];
    internal int[] From = new int[Field.W * Field.H];
    internal int[] Queue = new int[Field.W * Field.H];
    internal int SeenStamp;

    private static int[,] New(int v)
    {
        var a = new int[Field.W, Field.H];
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++) a[x, y] = v;
        return a;
    }

    public World Copy() => new()
    {
        Name = Name, Seed = Seed, Difficulty = Difficulty, Scenario = Scenario, Goal = Goal, StartMonth = StartMonth, Result = Result,
        Ground = (Ground[,])Ground.Clone(), Net = (Net[,])Net.Clone(), ZoneId = (int[,])ZoneId.Clone(), Timer = (byte[,])Timer.Clone(),
        Zones = Zones.Select(z => z.Copy()).ToList(),
        Traffic = (byte[,])Traffic.Clone(), Trips = (ushort[,])Trips.Clone(), Pollution = (byte[,])Pollution.Clone(), Crime = (byte[,])Crime.Clone(),
        LandValue = (byte[,])LandValue.Clone(), Density = (byte[,])Density.Clone(), PoliceCover = (byte[,])PoliceCover.Clone(),
        FireCover = (byte[,])FireCover.Clone(), Power = (bool[,])Power.Clone(), PowerDirty = PowerDirty,
        Month = Month, StepInMonth = StepInMonth, Funds = Funds, TaxRate = TaxRate, Funding = (int[])Funding.Clone(), LastYear = LastYear,
        AutoBudget = AutoBudget, Disasters = Disasters, DemandR = DemandR, DemandC = DemandC, DemandI = DemandI,
        Residents = Residents, ComJobs = ComJobs, IndJobs = IndJobs, PowerCapacity = PowerCapacity, PowerUsed = PowerUsed,
        CentreX = CentreX, CentreY = CentreY, Score = Score, Approval = Approval, Problems = (int[])Problems.Clone(),
        History = [.. History], Record = Record, Sprites = Sprites.Select(s => s.Copy()).ToList(), Shake = Shake, Dice = Dice,
        Told = new Dictionary<string, int>(Told),
    };

    public Zone? ZoneAt(int x, int y) => Field.In(x, y) && ZoneId[x, y] >= 0 ? Zones[ZoneId[x, y]] : null;

    /// <summary>Points every tile at the zone it belongs to, after zones come and go.</summary>
    public void Reindex()
    {
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++) ZoneId[x, y] = -1;
        for (int i = 0; i < Zones.Count; i++)
        {
            var z = Zones[i];
            for (int x = z.X; x < z.X + z.Size; x++)
            for (int y = z.Y; y < z.Y + z.Size; y++) ZoneId[x, y] = i;
        }
    }

    public bool IsWater(int x, int y) => Field.In(x, y) && Ground[x, y] == Engine.Ground.Water;
    public bool Has(int x, int y, Net n) => Field.In(x, y) && (Net[x, y] & n) != 0;

    /// <summary>The city's population: everyone living in its residential zones.</summary>
    public int Population => Residents;
    public int Jobs => ComJobs + IndJobs;

    /// <summary>The city's class by its population.</summary>
    public static string ClassOf(int pop) => pop switch
    {
        >= 500000 => "MEGALOPOLIS", >= 100000 => "METROPOLIS", >= 50000 => "CAPITAL", >= 10000 => "CITY", >= 2000 => "TOWN", _ => "VILLAGE",
    };

    public string Class => ClassOf(Residents);

    public static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    public string Date => $"{MonthNames[Month % 12]} {Year}";

    /// <summary>Tells the news, unless it was told within the last <paramref name="months"/>; true if it was told.</summary>
    public bool Tell(string text, int x = -1, int y = -1, int months = 6)
    {
        if (Told.TryGetValue(text, out int when) && Month - when < months && Month >= when) return false;
        Told[text] = Month;
        News.Add((text, x, y));
        return true;
    }
}
