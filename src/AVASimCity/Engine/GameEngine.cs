namespace AVASimCity.Engine;

public enum GameState
{
    /// <summary>The title, with the computer mayor building a city behind it.</summary>
    Title,
    /// <summary>The demonstration: the computer mayor's city full screen.</summary>
    Attract,
    Playing,
}

/// <summary>The windows that open over the map. All but the query window stop the clock while they're open.</summary>
public enum Panel { None, Budget, Evaluation, Query, Disasters, NewCity, Scenarios, Load, Save, Menu, ScenarioEnd }

public enum Speed { Paused, Slow, Normal, Fast }

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Bulldoze, Build, Zone, Click, Refused,
    Siren, Explosion, Roar, Horn, Train, Quake, Wind, Splash,
    Chime, Fanfare, TitleTune, MusicStop,
}

/// <summary>The keys held: which way to scroll the map.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0);

/// <summary>
/// The whole game with no UI: the title and its demonstration, the city being played, the tool in hand, the view
/// onto the map, the windows, the speed and the files. Call <see cref="Update"/> with the elapsed time and the
/// keys held; it runs in fixed 60 Hz ticks, so it plays the same at any frame rate. The mouse comes in through
/// <see cref="Press"/>, <see cref="Drag"/> and the rest.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 20;
    public const double AttractSeconds = 60;
    /// <summary>The map view on the screen, in pixels, and a tile's size in it at the normal zoom.</summary>
    public const double ViewLeft = 168, ViewTop = 34, ViewWidth = 1024 - 168, ViewHeight = 768 - 34 - 24;
    public const int TileSize = 16;
    /// <summary>The demonstration's city starts again when it's this old, in years.</summary>
    public const int DemoYears = 30;

    private double _accumulator, _idle, _slowCarry, _clickWait;
    private bool _startPrev, _tunePending = true;
    private (int X, int Y)? _dragFrom;
    private int _demoSeed;

    public GameEngine(string? cityFolder = null, int demoSeed = 0)
    {
        CityFolder = cityFolder ?? CityFile.DefaultFolder;
        _demoSeed = demoSeed != 0 ? demoSeed : (int)(DateTime.Now.Ticks % 100000) + 1;
        NewDemo();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Panel Panel { get; private set; }
    public Speed Speed { get; private set; } = Speed.Normal;
    public double StateTime { get; private set; }
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public string CityFolder { get; }

    public Tool Tool { get; private set; } = Tool.Road;
    public Overlay Overlay { get; set; }
    /// <summary>The tile at the view's top-left corner, and the zoom (1 or 2).</summary>
    public double ViewX { get; private set; }
    public double ViewY { get; private set; }
    public int Zoom { get; private set; } = 1;
    public double TilesAcross => ViewWidth / (TileSize * Zoom);
    public double TilesDown => ViewHeight / (TileSize * Zoom);

    /// <summary>The line along the bottom of the screen: the latest news or a word about the last thing tried.</summary>
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }
    /// <summary>The tile asked about with the query tool.</summary>
    public (int X, int Y) Queried { get; private set; }

    // The new-city window's choices.
    public Difficulty NewDifficulty { get; set; } = Difficulty.Easy;
    public int NewSeed { get; private set; } = 1;
    public string NewName { get; set; } = "NEWVILLE";
    /// <summary>The land the new city would be built on, for the window's preview.</summary>
    public World Preview { get; private set; } = new();

    public static readonly string[] Names =
    [
        "NEWVILLE", "MAPLETON", "RIVERBEND", "ASHFORD", "LAKEVIEW", "STONEBRIDGE", "FAIRHAVEN", "OAKRIDGE",
        "MILLBROOK", "SILVERTON", "BRIGHTWATER", "HOLLOWAY", "GREENFIELD", "PORTLAND", "KINGSLEY", "WESTMERE",
    ];

    public static string ToolName(Tool t) => t switch
    {
        Tool.Query => "QUERY", Tool.Bulldozer => "BULLDOZER", Tool.Road => "ROAD", Tool.Rail => "RAIL", Tool.Wire => "POWER LINE",
        Tool.Park => "PARK", Tool.Residential => "RESIDENTIAL", Tool.Commercial => "COMMERCIAL", Tool.Industrial => "INDUSTRIAL",
        Tool.Police => "POLICE", Tool.FireStation => "FIRE STATION", Tool.Stadium => "STADIUM", Tool.Seaport => "SEAPORT",
        Tool.Airport => "AIRPORT", Tool.Coal => "COAL POWER", _ => "NUCLEAR POWER",
    };

    public static string ProblemName(Problem p) => p switch
    {
        Problem.Crime => "CRIME", Problem.Pollution => "POLLUTION", Problem.Housing => "HOUSING COSTS", Problem.Taxes => "TAXES",
        Problem.Traffic => "TRAFFIC", Problem.Unemployment => "UNEMPLOYMENT", _ => "FIRES",
    };

    public static string DisasterName(Disaster d) => d switch
    {
        Disaster.Fire => "FIRE", Disaster.Flood => "FLOOD", Disaster.Tornado => "TORNADO", Disaster.Earthquake => "EARTHQUAKE",
        Disaster.Monster => "MONSTER", Disaster.PlaneCrash => "PLANE CRASH", _ => "MELTDOWN",
    };

    public static string OverlayName(Overlay o) => o switch
    {
        Overlay.None => "CITY", Overlay.Population => "POPULATION", Overlay.Traffic => "TRAFFIC", Overlay.Pollution => "POLLUTION",
        Overlay.Crime => "CRIME", Overlay.LandValue => "LAND VALUE", Overlay.Power => "POWER", Overlay.Police => "POLICE", _ => "FIRE COVER",
    };

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, double seconds = 4)
    {
        Message = text;
        MessageTime = seconds;
    }

    // ───────────────────────────── time ─────────────────────────────

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        if (_clickWait > 0) _clickWait -= dt;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                Demo(3);
                if (Panel != Panel.None) _idle = 0;
                _idle += dt;
                if (startPressed && Panel == Panel.None) OpenNewCity();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Demo(4);
                Follow();
                if (startPressed || StateTime > AttractSeconds) ReturnToTitle();
                break;
            case GameState.Playing:
                Scroll(c.Dx * dt * 40 / Zoom, c.Dy * dt * 40 / Zoom);
                if (Panel is Panel.None or Panel.Query) Run();
                Hear();
                break;
        }
    }

    /// <summary>The city's clock: one step a tick at normal speed, one every third tick slow, four a tick fast.</summary>
    private void Run()
    {
        double steps = Speed switch { Speed.Paused => 0, Speed.Slow => 1 / 3.0, Speed.Normal => 1, _ => 4 };
        _slowCarry += steps;
        while (_slowCarry >= 1)
        {
            _slowCarry -= 1;
            Sim.Step(World);
        }
    }

    /// <summary>The news, the sounds, the budget at the year's end, and the end of a scenario.</summary>
    private void Hear()
    {
        var w = World;
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
        foreach (var (text, x, y) in w.News)
        {
            Say(text, 6);
            if (x >= 0 && text.EndsWith('!')) CenterOn(x, y);
        }
        w.News.Clear();
        if (w.BudgetDue)
        {
            w.BudgetDue = false;
            if (!w.AutoBudget && Panel == Panel.None) Panel = Panel.Budget;
        }
        if (w.Scenario >= 0 && w.Result != 0 && Panel != Panel.ScenarioEnd && !_scenarioShown)
        {
            _scenarioShown = true;
            Panel = Panel.ScenarioEnd;
        }
    }

    private bool _scenarioShown;

    // ───────────────────────────── the title and the demonstration ─────────────────────────────

    private void NewDemo()
    {
        World = new World();
        Sim.Found(World, _demoSeed, Difficulty.Easy, Names[_demoSeed % Names.Length]);
        World.Disasters = false;
        World.AutoBudget = true;
        Pilot.Reset();
        CenterOn(Field.W / 2, Field.H / 2);
    }

    /// <summary>The computer mayor at work, some steps a tick; after enough years, a new city on new land.</summary>
    private void Demo(int steps)
    {
        var w = World;
        for (int i = 0; i < steps; i++)
        {
            Pilot.Step(w);
            Sim.Step(w);
        }
        w.Heard.Clear();
        w.News.Clear();
        w.BudgetDue = false;
        if (w.Month >= DemoYears * 12)
        {
            _demoSeed++;
            NewDemo();
        }
    }

    /// <summary>The demonstration's camera drifts after the computer mayor's work.</summary>
    private void Follow()
    {
        var w = World;
        double tx = w.CentreX, ty = w.CentreY;
        if (Pilot.Last is { } d) { tx = tx * 0.4 + d.X * 0.6; ty = ty * 0.4 + d.Y * 0.6; }
        double vx = ViewX + TilesAcross / 2, vy = ViewY + TilesDown / 2;
        CenterOn(vx + (tx - vx) * 0.01, vy + (ty - vy) * 0.01);
    }

    private void StartAttract()
    {
        State = GameState.Attract;
        StateTime = 0;
        Panel = Panel.None;
        Play(Sfx.MusicStop);
        CenterOn(World.CentreX, World.CentreY);
    }

    public void ReturnToTitle()
    {
        if (State == GameState.Playing) NewDemo();
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Panel = Panel.None;
        Message = null;
        Speed = Speed.Normal;
    }

    // ───────────────────────────── starting a city ─────────────────────────────

    public void OpenNewCity()
    {
        if (Panel != Panel.NewCity) NewTerrain();
        Panel = Panel.NewCity;
        Play(Sfx.Click);
    }

    /// <summary>Different land for the new city.</summary>
    public void NewTerrain()
    {
        NewSeed = (int)((uint)(NewSeed * 1103515245 + 12345 + (int)(StateTime * 1000)) % 999983) + 1;
        Preview = new World();
        Terrain.Make(Preview, NewSeed);
        NewName = Names[NewSeed % Names.Length];
    }

    public void UseSeed(int seed)
    {
        NewSeed = seed;
        Preview = new World();
        Terrain.Make(Preview, seed);
    }

    /// <summary>The city of one's own, on the land in the new-city window.</summary>
    public void StartCity()
    {
        var w = new World();
        Sim.Found(w, NewSeed, NewDifficulty, NewName.Length > 0 ? NewName : "NEWVILLE");
        Begin(w);
        Say($"WELCOME TO {w.Name}. BUILD A POWER PLANT, THEN ZONES AND ROADS", 8);
    }

    public void StartScenario(int id)
    {
        var w = new World();
        Scenarios.Setup(w, id);
        Begin(w);
        Say($"{Scenarios.All[id].Trouble}: {Scenarios.GoalText(w)}", 10);
    }

    private void Begin(World w)
    {
        World = w;
        State = GameState.Playing;
        StateTime = 0;
        Panel = Panel.None;
        Speed = Speed.Normal;
        Overlay = Overlay.None;
        Tool = Tool.Road;
        _scenarioShown = w.Result != 0;
        Play(Sfx.MusicStop);
        CenterOn(w.Zones.Count > 0 ? w.CentreX : Field.W / 2.0, w.Zones.Count > 0 ? w.CentreY : Field.H / 2.0);
    }

    // ───────────────────────────── files ─────────────────────────────

    public string? SlotText(int slot) => CityFile.Describe(CityFolder, slot);

    public bool SaveCity(int slot)
    {
        if (State != GameState.Playing) return false;
        try
        {
            CityFile.Save(World, CityFile.SlotPath(CityFolder, slot));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say("COULD NOT SAVE THE CITY");
            Play(Sfx.Refused);
            return false;
        }
        Panel = Panel.None;
        Say($"{World.Name} SAVED IN SLOT {slot + 1}");
        Play(Sfx.Chime);
        return true;
    }

    public bool LoadCity(int slot)
    {
        var w = CityFile.Load(CityFile.SlotPath(CityFolder, slot));
        if (w == null)
        {
            Say("NO CITY IN THAT SLOT");
            Play(Sfx.Refused);
            return false;
        }
        Begin(w);
        Say($"{w.Name}, {w.Date}");
        return true;
    }

    // ───────────────────────────── windows ─────────────────────────────

    public void Open(Panel p)
    {
        if (State == GameState.Attract) return;
        if (State == GameState.Title && p is not (Panel.NewCity or Panel.Scenarios or Panel.Load or Panel.None)) return;
        if (p == Panel.NewCity) { OpenNewCity(); return; }
        Panel = Panel == p ? Panel.None : p;
        Play(Sfx.Click);
    }

    public void Close()
    {
        Panel = Panel.None;
    }

    public void SetTax(int rate) => World.TaxRate = Math.Clamp(rate, 0, 20);

    public void SetFunding(Service s, int percent) => World.Funding[(int)s] = Math.Clamp(percent, 0, 100);

    /// <summary>Calls down a disaster from the disasters window.</summary>
    public bool Trigger(Disaster d)
    {
        if (State != GameState.Playing) return false;
        Panel = Panel.None;
        if (Sim.Start(World, d)) { Hear(); return true; }
        Say(d == Disaster.Meltdown ? "THERE IS NO NUCLEAR PLANT" : "NOTHING HAPPENS");
        Play(Sfx.Refused);
        return false;
    }

    // ───────────────────────────── the view ─────────────────────────────

    public void Scroll(double dx, double dy)
    {
        ViewX = Math.Clamp(ViewX + dx, -2, Field.W + 2 - TilesAcross);
        ViewY = Math.Clamp(ViewY + dy, -2, Field.H + 2 - TilesDown);
    }

    public void CenterOn(double x, double y)
    {
        ViewX = 0;
        ViewY = 0;
        Scroll(x - TilesAcross / 2, y - TilesDown / 2);
    }

    public void SetZoom(int zoom)
    {
        double cx = ViewX + TilesAcross / 2, cy = ViewY + TilesDown / 2;
        Zoom = Math.Clamp(zoom, 1, 2);
        CenterOn(cx, cy);
    }

    public void SetSpeed(Speed s) => Speed = s;

    public void TogglePause()
    {
        if (State != GameState.Playing) return;
        if (Speed == Speed.Paused) Speed = _unpaused;
        else
        {
            _unpaused = Speed;
            Speed = Speed.Paused;
        }
    }

    public void Faster()
    {
        if (Speed < Speed.Fast) Speed++;
    }

    public void Slower()
    {
        if (Speed > Speed.Paused) Speed--;
    }

    private Speed _unpaused = Speed.Normal;

    public void SetUnpaused(Speed s) => _unpaused = s;

    // ───────────────────────────── tools ─────────────────────────────

    public void Select(Tool t)
    {
        Tool = t;
        _dragFrom = null;
        Play(Sfx.Click);
    }

    private static bool Draws(Tool t) => t is Tool.Bulldozer or Tool.Road or Tool.Rail or Tool.Wire or Tool.Park;

    /// <summary>The mouse pressed on a tile: the tool used there (or, the query tool, the tile asked about).</summary>
    public Outcome Press(int x, int y)
    {
        if (State != GameState.Playing || !Field.In(x, y)) return Outcome.Nothing;
        if (Panel is not (Panel.None or Panel.Query)) return Outcome.Nothing;
        if (Tool == Tool.Query)
        {
            Queried = (x, y);
            Panel = Panel.Query;
            Play(Sfx.Click);
            return Outcome.Done;
        }
        _dragFrom = (x, y);
        return Use(x, y, true);
    }

    /// <summary>The mouse dragged on to a tile: roads, rails, power lines, parks and the bulldozer follow it.</summary>
    public void Drag(int x, int y)
    {
        if (_dragFrom is not { } from || !Draws(Tool) || (x, y) == from) return;
        // Every tile on the way, so a quick drag leaves no gaps; turning corners square.
        int cx = from.X, cy = from.Y;
        while ((cx, cy) != (x, y))
        {
            if (cx != x && (Math.Abs(x - cx) >= Math.Abs(y - cy) || cy == y)) cx += Math.Sign(x - cx);
            else cy += Math.Sign(y - cy);
            Use(cx, cy, false);
        }
        _dragFrom = (x, y);
    }

    public void Release() => _dragFrom = null;

    private Outcome Use(int x, int y, bool press)
    {
        var o = Sim.Apply(World, Tool, x, y);
        switch (o)
        {
            case Outcome.Done:
                if (_clickWait <= 0 || press)
                {
                    Play(Tool switch
                    {
                        Tool.Bulldozer => Sfx.Bulldoze,
                        Tool.Rail when press => Sfx.Train,
                        Tool.Road or Tool.Rail or Tool.Wire or Tool.Park => Sfx.Click,
                        Tool.Residential or Tool.Commercial or Tool.Industrial => Sfx.Zone,
                        _ => Sfx.Build,
                    });
                    _clickWait = 0.07;
                }
                break;
            case Outcome.NoMoney:
                Say("NOT ENOUGH FUNDS");
                if (press) Play(Sfx.Refused);
                break;
            case Outcome.Blocked:
                if (press)
                {
                    Say(Tool is Tool.Seaport ? "A SEAPORT MUST BE BUILT ON THE WATER'S EDGE" : "CAN'T BUILD THERE");
                    Play(Sfx.Refused);
                }
                break;
        }
        return o;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int ticks, Controls c = default)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    public void Idle(int ticks) => Run(ticks);

    /// <summary>Straight into a new city on the given land.</summary>
    public void Go(int seed = 1, Difficulty d = Difficulty.Easy)
    {
        UseSeed(seed);
        NewDifficulty = d;
        NewName = Names[seed % Names.Length];
        StartCity();
        Message = null;
    }

    /// <summary>Plays the city as the computer mayor would, for a number of the city's months.</summary>
    public void Autoplay(int months)
    {
        var w = World;
        int end = w.Month + months;
        while (w.Month < end)
        {
            Pilot.Step(w);
            Sim.Step(w);
        }
        w.Heard.Clear();
        w.News.Clear();
        w.BudgetDue = false;
    }
}
