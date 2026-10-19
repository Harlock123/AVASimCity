using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AVASimCity.Engine;

namespace AVASimCity.Views;

public sealed partial class GameCanvas
{
    private WriteableBitmap? _preview;
    private World? _previewOf;

    /// <summary>
    /// A window over the map: a full-screen catch first (a click outside a modal window does nothing; outside the
    /// query window, it closes it), then the frame and its title. Returns the inside.
    /// </summary>
    private Rect Window(DrawingContext dc, double width, double height, string title, bool modal = true, double? top = null, bool catchAll = true)
    {
        double cx = Engine.State == GameState.Title ? Field.Width / 2 : MapRect.Center.X;
        var r = new Rect(cx - width / 2, top ?? MapRect.Center.Y - height / 2, width, height);
        Hit(catchAll ? new Rect(0, 0, Field.Width, Field.Height) : r, modal || !catchAll ? () => { } : Engine.Close);
        dc.FillRectangle(Brush(0x70000000), r.Translate(new Vector(6, 6)), 8);
        dc.FillRectangle(Brush(0xF0182030), r, 8);
        dc.DrawRectangle(null, Pen(0xFF8AA0C8, 2), r, 8, 8);
        dc.FillRectangle(Brush(0xFF2A3A5A), new Rect(r.X + 2, r.Y + 2, r.Width - 4, 34), 6);
        Text(dc, title, new Point(r.Center.X, r.Y + 19), 28, Yellow, center: true);
        return new Rect(r.X + 16, r.Y + 44, r.Width - 32, r.Height - 54);
    }

    private void DrawPanel(DrawingContext dc)
    {
        switch (Engine.Panel)
        {
            case Panel.Budget: DrawBudget(dc); break;
            case Panel.Evaluation: DrawEvaluation(dc); break;
            case Panel.Query: DrawQuery(dc); break;
            case Panel.Disasters: DrawDisasters(dc); break;
            case Panel.NewCity: DrawNewCity(dc); break;
            case Panel.Scenarios: DrawScenarios(dc); break;
            case Panel.Load: DrawSlots(dc, save: false); break;
            case Panel.Save: DrawSlots(dc, save: true); break;
            case Panel.Menu: DrawMenu(dc); break;
            case Panel.ScenarioEnd: DrawScenarioEnd(dc); break;
        }
    }

    // ───────────────────────────── the budget ─────────────────────────────

    private void DrawBudget(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        var r = Window(dc, 560, 446, $"BUDGET FOR {w.Year}");
        var (roads, police, fire) = Sim.Asks(w);
        long taxes = Sim.Taxes(w);
        double y = r.Y + 16;
        Text(dc, "THE CITY TREASURER REPORTS", new Point(r.Center.X, y), 22, Dim, center: true);
        y += 34;
        void Row(string label, string value, Action down, Action up, string note)
        {
            Text(dc, label, new Point(r.X, y), 23, Ink);
            Button(dc, new Rect(r.X + 196, y - 12, 26, 24), "-", down, size: 24);
            Text(dc, value, new Point(r.X + 262, y), 24, Yellow, center: true);
            Button(dc, new Rect(r.X + 302, y - 12, 26, 24), "+", up, size: 24);
            Text(dc, note, new Point(r.Right, y), 21, White, right: true);
            y += 40;
        }
        Row("TAX RATE", $"{w.TaxRate}%", () => g.SetTax(w.TaxRate - 1), () => g.SetTax(w.TaxRate + 1), $"TAXES ${taxes:N0}");
        Row("TRANSPORT", $"{w.Funding[0]}%", () => g.SetFunding(Service.Roads, w.Funding[0] - 10), () => g.SetFunding(Service.Roads, w.Funding[0] + 10),
            $"${roads * w.Funding[0] / 100:N0} OF ${roads:N0}");
        Row("POLICE", $"{w.Funding[1]}%", () => g.SetFunding(Service.Police, w.Funding[1] - 10), () => g.SetFunding(Service.Police, w.Funding[1] + 10),
            $"${police * w.Funding[1] / 100:N0} OF ${police:N0}");
        Row("FIRE", $"{w.Funding[2]}%", () => g.SetFunding(Service.Fire, w.Funding[2] - 10), () => g.SetFunding(Service.Fire, w.Funding[2] + 10),
            $"${fire * w.Funding[2] / 100:N0} OF ${fire:N0}");
        dc.DrawLine(Pen(0xFF4A5A7A, 1), new Point(r.X, y - 14), new Point(r.Right, y - 14));
        long spend = roads * w.Funding[0] / 100 + police * w.Funding[1] / 100 + fire * w.Funding[2] / 100;
        long flow = taxes - spend;
        void Sum(string label, long v, uint c)
        {
            Text(dc, label, new Point(r.X, y), 22, Dim);
            Text(dc, $"{(v < 0 ? "-" : "")}${Math.Abs(v):N0}", new Point(r.Right, y), 23, c, right: true);
            y += 28;
        }
        Sum("CASH FLOW THIS YEAR (ESTIMATED)", flow, flow < 0 ? Red : Green);
        if (w.LastYear.Year > 0)
            Sum($"LAST YEAR ({w.LastYear.Year}): TAXES ${w.LastYear.Taxes:N0}, SPENT", w.LastYear.Spent, White);
        Sum("FUNDS IN THE BANK", w.Funds, w.Funds < 0 ? Red : Yellow);
        y += 8;
        Button(dc, new Rect(r.X, y, 220, 30), w.AutoBudget ? "[X] AUTO BUDGET" : "[ ] AUTO BUDGET", () => w.AutoBudget = !w.AutoBudget, w.AutoBudget, 21);
        Button(dc, new Rect(r.Right - 240, y, 240, 30), "GO WITH THESE FIGURES", g.Close, size: 21);
    }

    // ───────────────────────────── the evaluation ─────────────────────────────

    private void DrawEvaluation(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        var r = Window(dc, 600, 400, $"EVALUATION OF {w.Name}, {w.Year}");
        double half = r.Width / 2;
        Text(dc, "PUBLIC OPINION", new Point(r.X + half / 2, r.Y + 14), 24, Cyan, center: true);
        Text(dc, "IS THE MAYOR DOING A GOOD JOB?", new Point(r.X + half / 2, r.Y + 42), 20, Dim, center: true);
        Text(dc, $"YES  {w.Approval}%", new Point(r.X + half / 2 - 60, r.Y + 70), 26, Green, center: true);
        Text(dc, $"NO  {100 - w.Approval}%", new Point(r.X + half / 2 + 60, r.Y + 70), 26, Red, center: true);
        var bar = new Rect(r.X + 20, r.Y + 90, half - 40, 10);
        dc.FillRectangle(Brush(0xFFC03030), bar, 3);
        dc.FillRectangle(Brush(0xFF40B050), new Rect(bar.X, bar.Y, bar.Width * w.Approval / 100.0, bar.Height), 3);
        Text(dc, "WHAT ARE THE WORST PROBLEMS?", new Point(r.X + half / 2, r.Y + 126), 20, Dim, center: true);
        double y = r.Y + 154;
        var worst = Sim.WorstProblems(w).ToList();
        if (worst.Count == 0) Text(dc, "NONE TO SPEAK OF", new Point(r.X + half / 2, y), 22, White, center: true);
        foreach (var (p, share) in worst)
        {
            Text(dc, GameEngine.ProblemName(p), new Point(r.X + 20, y), 23, White);
            Text(dc, $"{share}%", new Point(r.X + half - 20, y), 23, share > 40 ? Red : Yellow, right: true);
            y += 30;
        }
        dc.DrawLine(Pen(0xFF4A5A7A, 1), new Point(r.X + half, r.Y + 4), new Point(r.X + half, r.Bottom - 50));
        Text(dc, "STATISTICS", new Point(r.X + half + half / 2, r.Y + 14), 24, Cyan, center: true);
        y = r.Y + 46;
        int grew = w.History.Count switch { 0 => 0, 1 => w.History[0], _ => w.History[^1] - w.History[^2] };
        long assessed = 0;
        foreach (var z in w.Zones) assessed += (long)w.LandValue[(int)z.Centre.X, (int)z.Centre.Y] * z.Size * z.Size * 12 + Sim.Cost(Sim.ToolFor(z.Kind));
        (string, string)[] stats =
        [
            ("POPULATION", $"{w.Residents:N0}"),
            ("LAST YEAR'S GROWTH", w.History.Count > 0 ? $"{(grew >= 0 ? "+" : "")}{grew:N0}" : "-"),
            ("ASSESSED VALUE", $"${assessed:N0}"),
            ("CATEGORY", w.Class),
            ("GAME LEVEL", w.Difficulty.ToString().ToUpperInvariant()),
            ("OVERALL SCORE", $"{w.Score}"),
            ("JOBS", $"{w.Jobs:N0}"),
        ];
        foreach (var (a, b) in stats)
        {
            Text(dc, a, new Point(r.X + half + 16, y), 21, Dim);
            Text(dc, b, new Point(r.Right - 8, y), 22, White, right: true);
            y += 28;
        }
        var meter = new Rect(r.X + half + 16, y + 4, half - 30, 12);
        dc.FillRectangle(Brush(0xFF101622), meter, 3);
        dc.FillRectangle(Brush(Pix.Mix(0xFFD04040, 0xFF40C060, w.Score / 1000.0)), new Rect(meter.X, meter.Y, meter.Width * w.Score / 1000.0, meter.Height), 3);
        Button(dc, new Rect(r.Center.X - 70, r.Bottom - 36, 140, 30), "DONE", g.Close, size: 22);
    }

    // ───────────────────────────── the query ─────────────────────────────

    private static string Level(byte v) => v < 50 ? "LOW" : v < 110 ? "MEDIUM" : v < 180 ? "HIGH" : "VERY HIGH";

    public static string Describe(Zone z) => z.Kind switch
    {
        ZoneKind.Residential => z.Level switch
        {
            0 => "EMPTY RESIDENTIAL ZONE", 1 => "A FEW HOUSES", 2 => "HOUSES", 3 => "LOW-RISE APARTMENTS", 4 => "APARTMENTS", _ => "HIGH-RISE APARTMENTS",
        },
        ZoneKind.Commercial => z.Level switch { 0 => "EMPTY COMMERCIAL ZONE", 1 => "SHOPS", 2 => "SHOPPING CENTRE", 3 => "OFFICES", _ => "SKYSCRAPER" },
        ZoneKind.Industrial => z.Level switch { 0 => "EMPTY INDUSTRIAL ZONE", 1 => "WAREHOUSES", 2 => "FACTORY", 3 => "HEAVY INDUSTRY", _ => "REFINERY" },
        ZoneKind.Police => "POLICE STATION", ZoneKind.FireStation => "FIRE STATION", ZoneKind.Stadium => "STADIUM", ZoneKind.Seaport => "SEAPORT",
        ZoneKind.Airport => "AIRPORT", ZoneKind.Coal => "COAL POWER PLANT", _ => "NUCLEAR POWER PLANT",
    };

    private void DrawQuery(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        var (x, y) = g.Queried;
        var z = w.ZoneAt(x, y);
        var r = Window(dc, 380, 270, "QUERY", modal: false, top: MapRect.Y + 20, catchAll: false);
        string what = z != null ? Describe(z) : w.Ground[x, y] switch
        {
            Ground.Water => w.Net[x, y] != Net.None ? "BRIDGE" : "WATER",
            Ground.Trees => "WOODS", Ground.Rubble => "RUBBLE", Ground.Park => "PARK", Ground.Fire => "FIRE!", Ground.Flood => "FLOOD WATER",
            Ground.Radiation => "RADIOACTIVE", _ => w.Net[x, y] switch
            {
                Net.Road => "ROAD", Net.Rail => "RAILWAY", Net.Wire => "POWER LINE", Net.None => "OPEN LAND", _ => "CROSSING",
            },
        };
        double yy = r.Y + 12;
        Text(dc, what, new Point(r.Center.X, yy), 26, White, center: true);
        yy += 34;
        var (cx, cy) = z != null ? ((int)z.Centre.X, (int)z.Centre.Y) : (x, y);
        var rows = new List<(string, string)>
        {
            ("LAND VALUE", Level(w.LandValue[cx, cy])), ("CRIME", Level(w.Crime[cx, cy])), ("POLLUTION", Level(w.Pollution[cx, cy])),
        };
        if (z != null)
        {
            if (z.Grows) rows.Insert(0, (z.Kind == ZoneKind.Residential ? "RESIDENTS" : "JOBS", $"{z.Pop:N0}"));
            rows.Add(("POWER", z.Powered ? "YES" : "NO"));
            if (z.Grows) rows.Add(("GROWTH", z.NoRoad ? "NO ROAD!" : z.Trend > 0 ? "GROWING" : z.Trend < 0 ? "DECLINING" : "STEADY"));
        }
        else if ((w.Net[x, y] & (Net.Road | Net.Rail)) != 0) rows.Insert(0, ("TRAFFIC", Level(w.Traffic[x, y])));
        foreach (var (a, b) in rows)
        {
            Text(dc, a, new Point(r.X + 20, yy), 22, Dim);
            Text(dc, b, new Point(r.Right - 20, yy), 22, b is "NO" or "NO ROAD!" or "DECLINING" or "VERY HIGH" ? Red : Yellow, right: true);
            yy += 26;
        }
    }

    // ───────────────────────────── disasters ─────────────────────────────

    private void DrawDisasters(DrawingContext dc)
    {
        var g = Engine;
        var r = Window(dc, 340, 400, "DISASTERS", modal: false);
        var all = Enum.GetValues<Disaster>();
        for (int i = 0; i < all.Length; i++)
        {
            var d = all[i];
            Button(dc, new Rect(r.X + 20, r.Y + 6 + i * 40, r.Width - 40, 32), GameEngine.DisasterName(d), () => g.Trigger(d), size: 23);
        }
        var w = g.World;
        Button(dc, new Rect(r.X + 20, r.Bottom - 40, r.Width - 40, 32), w.Disasters ? "RANDOM DISASTERS: ON" : "RANDOM DISASTERS: OFF", () => w.Disasters = !w.Disasters, w.Disasters, 21);
    }

    // ───────────────────────────── new city, scenarios, files ─────────────────────────────

    private unsafe WriteableBitmap PreviewOf(World w)
    {
        _preview ??= new WriteableBitmap(new PixelSize(Field.W, Field.H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        if (!ReferenceEquals(w, _previewOf))
        {
            _previewOf = w;
            using var fb = _preview.Lock();
            uint* dst = (uint*)fb.Address;
            int stride = fb.RowBytes / 4;
            for (int x = 0; x < Field.W; x++)
            for (int y = 0; y < Field.H; y++) dst[y * stride + x] = TileArt.MiniColour(w, x, y);
        }
        return _preview;
    }

    private void DrawNewCity(DrawingContext dc)
    {
        var g = Engine;
        var r = Window(dc, 620, 420, "A NEW CITY");
        var map = new Rect(r.X, r.Y + 4, 300, 250);
        dc.FillRectangle(Brush(0xFF000000), map.Inflate(2));
        using (dc.PushRenderOptions(Crisp)) dc.DrawImage(PreviewOf(g.Preview), new Rect(0, 0, Field.W, Field.H), map);
        Button(dc, new Rect(map.X, map.Bottom + 12, map.Width, 32), "GENERATE NEW LAND", g.NewTerrain, size: 22);
        double x = map.Right + 20, right = r.Right;
        Text(dc, "CITY NAME (TYPE IT)", new Point(x, r.Y + 14), 20, Dim);
        dc.FillRectangle(Brush(0xFF0C1018), new Rect(x, r.Y + 28, right - x, 34), 4);
        Text(dc, g.NewName + ((int)(_clock * 2) % 2 == 0 ? "_" : " "), new Point(x + 10, r.Y + 45), 28, Yellow);
        Text(dc, "GAME LEVEL", new Point(x, r.Y + 90), 20, Dim);
        var levels = Enum.GetValues<Difficulty>();
        for (int i = 0; i < levels.Length; i++)
        {
            var d = levels[i];
            Button(dc, new Rect(x, r.Y + 106 + i * 40, right - x, 32), $"{d.ToString().ToUpperInvariant()}   ${Sim.StartingFunds(d):N0}", () => g.NewDifficulty = d, g.NewDifficulty == d, 22);
        }
        Button(dc, new Rect(x, r.Bottom - 40, (right - x) / 2 - 6, 34), "CANCEL", g.Close, size: 22);
        Button(dc, new Rect(x + (right - x) / 2 + 6, r.Bottom - 40, (right - x) / 2 - 6, 34), "START", g.StartCity, true, 24);
    }

    private void DrawScenarios(DrawingContext dc)
    {
        var g = Engine;
        var r = Window(dc, 660, 520, "SCENARIOS");
        for (int i = 0; i < Scenarios.All.Length; i++)
        {
            var s = Scenarios.All[i];
            int id = i;
            var b = new Rect(r.X, r.Y + i * 84, r.Width, 78);
            Button(dc, b, "", () => g.StartScenario(id));
            Text(dc, $"{s.Name}, {s.Year}", new Point(b.X + 12, b.Y + 15), 25, Yellow);
            Text(dc, s.Trouble, new Point(b.Right - 12, b.Y + 15), 22, Cyan, right: true);
            Text(dc, s.Story[0], new Point(b.X + 12, b.Y + 40), 18, Ink);
            Text(dc, s.Story[1], new Point(b.X + 12, b.Y + 60), 18, Ink);
            Text(dc, $"{s.Years} YEARS", new Point(b.Right - 12, b.Y + 60), 18, Dim, right: true);
        }
        Button(dc, new Rect(r.Center.X - 70, r.Bottom - 34, 140, 30), "CANCEL", g.Close, size: 22);
    }

    private void DrawSlots(DrawingContext dc, bool save)
    {
        var g = Engine;
        var r = Window(dc, 520, 340, save ? "SAVE CITY" : "LOAD CITY");
        for (int i = 0; i < CityFile.Slots; i++)
        {
            int slot = i;
            string? what = g.SlotText(i);
            bool can = save || what != null;
            Button(dc, new Rect(r.X, r.Y + i * 40, r.Width, 34), $"{i + 1}.  {what ?? "(EMPTY)"}", () => { if (save) g.SaveCity(slot); else g.LoadCity(slot); }, size: 22, enabled: can);
        }
        Button(dc, new Rect(r.Center.X - 70, r.Bottom - 34, 140, 30), "CANCEL", g.Close, size: 22);
    }

    private void DrawMenu(DrawingContext dc)
    {
        var g = Engine;
        var r = Window(dc, 320, 330, "FILE", modal: false);
        (string, Action)[] items =
        [
            ("NEW CITY", g.OpenNewCity), ("SCENARIOS", () => g.Open(Panel.Scenarios)), ("LOAD CITY", () => g.Open(Panel.Load)),
            ("SAVE CITY", () => g.Open(Panel.Save)), ("TO THE TITLE", g.ReturnToTitle), ("CARRY ON", g.Close),
        ];
        for (int i = 0; i < items.Length; i++)
            Button(dc, new Rect(r.X + 20, r.Y + 4 + i * 44, r.Width - 40, 36), items[i].Item1, items[i].Item2, size: 24);
    }

    private void DrawScenarioEnd(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        bool won = w.Result > 0;
        var r = Window(dc, 560, 260, won ? "YOU HAVE WON!" : "YOU HAVE LOST");
        var s = Scenarios.All[w.Scenario];
        Text(dc, $"{s.Name}: {s.Trouble}", new Point(r.Center.X, r.Y + 16), 26, Cyan, center: true);
        Text(dc, $"THE GOAL: {Scenarios.GoalText(w)}", new Point(r.Center.X, r.Y + 52), 21, Ink, center: true);
        Text(dc, won ? "THE CITIZENS THANK YOU. A STATUE IS BEING RAISED." : "THE CITIZENS HAVE RUN YOU OUT OF TOWN.", new Point(r.Center.X, r.Y + 86), 22, won ? Green : Red, center: true);
        Text(dc, $"POPULATION {w.Residents:N0}   SCORE {w.Score}", new Point(r.Center.X, r.Y + 118), 22, White, center: true);
        Button(dc, new Rect(r.X + 20, r.Bottom - 40, 220, 34), "KEEP PLAYING", g.Close, size: 22);
        Button(dc, new Rect(r.Right - 240, r.Bottom - 40, 220, 34), "TO THE TITLE", g.ReturnToTitle, size: 22);
    }

    // ───────────────────────────── the title ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.FromUInt32(0xC0081020), 0), new GradientStop(Color.FromUInt32(0x40081020), 0.45), new GradientStop(Color.FromUInt32(0xD0081020), 1)],
        }, new Rect(0, 0, Field.Width, Field.Height));
        if (g.Panel != Panel.None) return;
        var card = new Rect(cx - 400, 44, 800, 250);
        dc.FillRectangle(Brush(0xB0101828), card, 12);
        dc.DrawRectangle(null, Pen(0xFF8AA0C8, 2), card, 12, 12);
        // A skyline along the card's foot.
        for (int i = 0; i < 26; i++)
        {
            double bh = 10 + Pix.Rand(i, 3, 5) * 46, bw = 18 + Pix.Rand(i, 4, 5) * 14, bx = card.X + 16 + i * 29.5;
            dc.FillRectangle(Brush(0x803A5A8A), new Rect(bx, card.Bottom - 4 - bh, bw, bh));
            for (double wy = card.Bottom - bh + 2; wy < card.Bottom - 10; wy += 7)
                if (Pix.Rand(i, (int)wy, 6) < 0.5) dc.FillRectangle(Brush(0x90FFE080), new Rect(bx + 4, wy, 3, 3));
        }
        string title = "AVA SIMCITY";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60, ty = 108 + Math.Sin(_clock * 2.2 + i * 0.45) * 4;
            Text(dc, title[i].ToString(), new Point(tx, ty), 112, i < 3 ? 0xFFF0E0B0 : 0xFF6AD0FF, center: true);
        }
        Text(dc, "AFTER MAXIS'S 1989 CITY SIMULATION", new Point(cx, 172), 27, White, center: true);
        Text(dc, "YOU ARE THE MAYOR. ZONE THE LAND, BUILD THE ROADS AND THE POWER,", new Point(cx, 206), 23, Ink, center: true);
        Text(dc, "AND WATCH YOUR VILLAGE GROW INTO A MEGALOPOLIS.", new Point(cx, 232), 23, Ink, center: true);
        string[] labels = ["NEW CITY", "SCENARIOS", "LOAD CITY"];
        Action[] acts = [g.OpenNewCity, () => g.Open(Panel.Scenarios), () => g.Open(Panel.Load)];
        for (int i = 0; i < 3; i++)
            Button(dc, new Rect(cx - 330 + i * 230, 330, 200, 50), labels[i], acts[i], size: 32);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER FOR A NEW CITY", new Point(cx, 420), 34, Yellow, center: true);
        Text(dc, $"THE COMPUTER IS BUILDING {g.World.Name} BEHIND THIS: POPULATION {g.World.Residents:N0}, {g.World.Date}", new Point(cx, 660), 21, Ink, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 692, Field.Width, 76));
        Text(dc, "MOUSE: BUILD, DRAG FOR ROADS   RIGHT-DRAG: SCROLL   ARROWS: SCROLL   P: PAUSE   M: SOUND", new Point(cx, 712), 19, 0xFFE0D0B0, center: true);
        Text(dc, "TITLE MUSIC: MINUET IN G, FROM THE NOTEBOOK FOR ANNA MAGDALENA BACH (1725)", new Point(cx, 744), 18, Dim, center: true);
    }
}
