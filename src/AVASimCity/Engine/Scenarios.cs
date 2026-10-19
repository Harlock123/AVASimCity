namespace AVASimCity.Engine;

/// <summary>What a scenario asks of the mayor.</summary>
public enum GoalKind { Population, Traffic, Crime, Score }

/// <summary>A ready-made city in trouble: its story, what it asks of you and how long you have.</summary>
public sealed record ScenarioInfo(string Name, int Year, string Trouble, string[] Story, GoalKind Goal, int Years, long Funds, int Seed, Lay Land, int Zones);

/// <summary>
/// The scenarios: five cities in trouble, each laid out by the computer mayor on its own land, each with a disaster
/// on the way or a problem already out of hand, and a goal to meet in so many years.
/// </summary>
public static class Scenarios
{
    public static readonly ScenarioInfo[] All =
    [
        new("HAVERFORD", 1961, "THE RIVER RISES",
            ["THE RIVER THAT MADE HAVERFORD RICH IS ABOUT TO BREAK ITS BANKS.", "WHEN THE WATER GOES DOWN, BUILD THE TOWN BACK BIGGER THAN BEFORE."],
            GoalKind.Population, 5, 15000, 1961, new Lay(true, 0, 0, 0.35), 70),
        new("NEW AMSTEL", 1975, "GRIDLOCK",
            ["THE CAPITAL'S STREETS ARE JAMMED FROM DAWN TO DUSK.", "BUILD ROADS AND RAILWAYS TO GET THE CITY MOVING AGAIN."],
            GoalKind.Traffic, 5, 20000, 1975, new Lay(false, 1, 1, 0.25), 120),
        new("PORT SABLE", 1988, "THE CRIME WAVE",
            ["THE HARBOUR TOWN NEVER PAID FOR A POLICE FORCE, AND NOW", "NOBODY IS SAFE AFTER DARK. BRING THE CRIME RATE DOWN."],
            GoalKind.Crime, 8, 12000, 1988, new Lay(false, 1, 0, 0.2), 90),
        new("CEDAR FALLS", 1979, "MELTDOWN",
            ["CEDAR FALLS RUNS ON A NUCLEAR PLANT THAT SHOULD NEVER", "HAVE BEEN BUILT SO CLOSE TO TOWN. KEEP THE PEOPLE FROM LEAVING."],
            GoalKind.Population, 10, 15000, 1979, new Lay(true, 0, 2, 0.45), 70),
        new("BAYPORT", 1957, "THE THING FROM THE BAY",
            ["SOMETHING BIG HAS BEEN SEEN IN THE BAY. WHATEVER COMES", "ASHORE, KEEP THE PEOPLE HAPPY: A SCORE OF 600 OR MORE."],
            GoalKind.Score, 5, 15000, 1957, new Lay(false, 2, 0, 0.3), 80),
    ];

    /// <summary>The goal in words, with its number.</summary>
    public static string GoalText(World w)
    {
        var s = All[w.Scenario];
        return s.Goal switch
        {
            GoalKind.Population => $"POPULATION OF {w.Goal:N0} BY {s.Year + s.Years}",
            GoalKind.Traffic => $"TRAFFIC PROBLEM UNDER {w.Goal}% BY {s.Year + s.Years}",
            GoalKind.Crime => $"CRIME PROBLEM UNDER {w.Goal}% BY {s.Year + s.Years}",
            _ => $"SCORE OF {w.Goal} OR MORE IN {s.Year + s.Years}",
        };
    }

    /// <summary>Lays the scenario's city out: its land, the computer mayor's city grown on it, and its trouble.</summary>
    public static void Setup(World w, int id)
    {
        var s = All[id];
        w.Name = s.Name;
        w.Seed = s.Seed;
        w.Difficulty = Difficulty.Medium;
        w.Dice = new Dice(s.Seed);
        Terrain.Make(w, s.Seed, s.Land);
        w.Zones.Clear();
        w.Reindex();
        var pilot = new Pilot();
        if (id == 2) pilot.Forbidden.Add(ZoneKind.Police);
        if (id == 3) pilot.PreferredPlant = ZoneKind.Nuclear;
        Raise(w, pilot, s.Zones);
        if (id == 1) Choke(w);
        w.Month = (s.Year - Field.StartYear) * 12;
        w.StartMonth = w.Month;
        w.Disasters = false;
        Sim.Months(w, 4);
        w.Disasters = true;
        w.Month = (s.Year - Field.StartYear) * 12;
        w.StartMonth = w.Month;
        w.History.Clear();
        w.Record = w.Residents;
        w.Funds = s.Funds;
        w.Scenario = id;
        w.Result = 0;
        w.News.Clear();
        w.Told.Clear();
        w.Heard.Clear();
        w.Goal = s.Goal switch
        {
            GoalKind.Population => (int)Math.Round(w.Residents * 1.1 / 500) * 500,
            GoalKind.Traffic => Math.Max(10, Math.Min(25, w.Problems[(int)Problem.Traffic] / 2)),
            GoalKind.Crime => Math.Max(10, Math.Min(20, w.Problems[(int)Problem.Crime] / 2)),
            _ => 600,
        };
    }

    /// <summary>The computer mayor's city, put up at no cost and grown at once: tall in the middle, lower further out.</summary>
    public static void Raise(World w, Pilot pilot, int zones)
    {
        w.Funds = 1_000_000;
        w.AutoBudget = true;
        int acts = 0;
        while (w.Zones.Count(z => z.Grows) < zones && acts < zones * 40)
        {
            acts++;
            var before = w.Zones.Count;
            if (w.PowerDirty) Sim.Power(w);
            pilot.Act(w, free: true);
            if (w.Zones.Count > before)
            {
                var z = w.Zones[^1];
                if (z.Grows)
                {
                    double d = Math.Sqrt((z.Centre.X - w.CentreX) * (z.Centre.X - w.CentreX) + (z.Centre.Y - w.CentreY) * (z.Centre.Y - w.CentreY));
                    int top = Sim.MaxLevel(z.Kind);
                    z.Level = Math.Clamp((int)Math.Round(top - d / 9 + w.Dice.Next(3) - 1), 1, top);
                    if (z.Kind == ZoneKind.Industrial) z.Level = Math.Min(top, 1 + w.Dice.Next(top));
                    z.Wealth = d < 10 ? 2 : d < 22 ? 1 : 0;
                    z.Pop = Sim.PopOf(z);
                }
            }
            if (acts % 25 == 0)
            {
                Sim.Power(w);
                Sim.Census(w);
                Sim.Demand(w);
                // Without a month passing, nothing would ever be counted as needing a station.
                if (acts % 100 == 0) Sim.Maps(w);
            }
        }
        Sim.Power(w);
        Sim.Maps(w);
        Sim.Census(w);
    }

    /// <summary>The capital's trouble: half its north-south streets dug up, so everything crowds onto the rest.</summary>
    private static void Choke(World w)
    {
        for (int x = 1; x < Field.W - 1; x++)
        {
            int roads = 0;
            for (int y = 0; y < Field.H; y++)
                if (w.Has(x, y, Net.Road) && w.Has(x, y - 1, Net.Road) && w.Has(x, y + 1, Net.Road)) roads++;
            if (roads < 12 || x % 14 >= 7) continue;
            for (int y = 0; y < Field.H; y++)
            {
                if (!w.Has(x, y, Net.Road) || w.Has(x - 1, y, Net.Road) || w.Has(x + 1, y, Net.Road)) continue;
                w.Net[x, y] &= ~Net.Road;
            }
        }
        w.PowerDirty = true;
    }

    /// <summary>Each month of a scenario: its disaster when it's due, and at the end, whether the goal was met.</summary>
    public static void Month(World w)
    {
        if (w.Scenario < 0 || w.Result != 0) return;
        int m = w.Month - w.StartMonth;
        switch (w.Scenario)
        {
            case 0 when m == 1: Sim.Start(w, Disaster.Flood); break;
            case 3 when m == 2: Sim.Start(w, Disaster.Meltdown); break;
            case 4 when m == 2: Sim.Start(w, Disaster.Monster); break;
        }
        var s = All[w.Scenario];
        if (m < s.Years * 12) return;
        w.Result = Met(w) ? 1 : -1;
        w.Heard.Add(w.Result > 0 ? Sfx.Fanfare : Sfx.Refused);
    }

    public static bool Met(World w) => All[w.Scenario].Goal switch
    {
        GoalKind.Population => w.Residents >= w.Goal,
        GoalKind.Traffic => w.Problems[(int)Problem.Traffic] < w.Goal,
        GoalKind.Crime => w.Problems[(int)Problem.Crime] < w.Goal,
        _ => w.Score >= w.Goal,
    };
}
