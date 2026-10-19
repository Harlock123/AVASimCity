using Avalonia.Input;
using AVASimCity.Engine;

namespace AVASimCity.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avasimcity-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(TempFolder(), demoSeed: 3) { Silent = true };

    /// <summary>Bare land everywhere, plenty of money, no disasters.</summary>
    private static World Flat()
    {
        var w = new World { Funds = 1_000_000, Disasters = false };
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++) w.Ground[x, y] = Ground.Land;
        Sim.Maps(w);
        return w;
    }

    private static void Road(World w, int x0, int y0, int x1, int y1)
    {
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) Sim.Apply(w, Tool.Road, x, y, free: true);
    }

    private static void Wire(World w, int x0, int y0, int x1, int y1)
    {
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) Sim.Apply(w, Tool.Wire, x, y, free: true);
    }

    /// <summary>Puts a zone (by its middle tile) down and gives it back.</summary>
    private static Zone Put(World w, Tool t, int x, int y)
    {
        Assert.Equal(Outcome.Done, Sim.Apply(w, t, x, y, free: true));
        return w.ZoneAt(x, y)!;
    }

    // ───────────────────────────── the tools ─────────────────────────────

    [Fact]
    public void Each_tool_costs_its_price_bridges_cost_more_and_with_no_money_nothing_is_built()
    {
        var w = Flat();
        w.Funds = 1000;
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Road, 10, 10));
        Assert.Equal(990, w.Funds);
        Assert.Equal(Outcome.Nothing, Sim.Apply(w, Tool.Road, 10, 10));
        w.Ground[11, 10] = Ground.Water;
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Road, 11, 10));
        Assert.Equal(940, w.Funds);
        Assert.True(w.Has(11, 10, Net.Road) && w.IsWater(11, 10));
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Residential, 20, 20));
        Assert.Equal(840, w.Funds);
        Assert.Equal(Outcome.NoMoney, Sim.Apply(w, Tool.Coal, 40, 40));
        Assert.Equal(840, w.Funds);
        Assert.Null(w.ZoneAt(40, 40));
    }

    [Fact]
    public void Zones_go_only_on_clear_land_trees_and_rubble_are_cleared_and_a_seaport_must_be_on_the_water()
    {
        var w = Flat();
        Road(w, 30, 10, 30, 10);
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Residential, 31, 11, free: true));
        w.Ground[10, 10] = Ground.Water;
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Commercial, 11, 11, free: true));
        w.Ground[50, 50] = Ground.Trees;
        w.Ground[51, 51] = Ground.Rubble;
        var z = Put(w, Tool.Industrial, 51, 51);
        Assert.Equal((50, 50), (z.X, z.Y));
        Assert.All(new[] { (50, 50), (51, 51), (52, 52) }, t => Assert.Equal(Ground.Built, w.Ground[t.Item1, t.Item2]));
        // Zones can't overlap.
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Residential, 53, 52, free: true));
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Seaport, 80, 80, free: true));
        for (int y = 0; y < Field.H; y++) w.Ground[90, y] = Ground.Water;
        var port = Put(w, Tool.Seaport, 87, 40);
        Assert.Equal(4, port.Size);
        var air = Put(w, Tool.Airport, 20, 70);
        Assert.Equal((18, 68, 6), (air.X, air.Y, air.Size));
    }

    [Fact]
    public void Roads_rails_and_power_lines_cross_two_at_a_time_and_the_bulldozer_clears_everything_but_fire_and_radiation()
    {
        var w = Flat();
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Road, 5, 5, free: true));
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Wire, 5, 5, free: true));
        Assert.Equal(Net.Road | Net.Wire, w.Net[5, 5]);
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Rail, 5, 5, free: true));
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Bulldozer, 5, 5, free: true));
        Assert.Equal(Net.None, w.Net[5, 5]);
        Assert.Equal(Outcome.Nothing, Sim.Apply(w, Tool.Bulldozer, 5, 5, free: true));
        var z = Put(w, Tool.Residential, 20, 20);
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Bulldozer, 21, 21, free: true));
        Assert.Empty(w.Zones);
        Assert.Equal(Ground.Rubble, w.Ground[20, 20]);
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Bulldozer, 20, 20, free: true));
        Assert.Equal(Ground.Land, w.Ground[20, 20]);
        w.Ground[30, 30] = Ground.Radiation;
        w.Ground[31, 30] = Ground.Fire;
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Bulldozer, 30, 30, free: true));
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Bulldozer, 31, 30, free: true));
        Assert.Equal(Outcome.Done, Sim.Apply(w, Tool.Park, 40, 40, free: true));
        Assert.Equal(Ground.Park, w.Ground[40, 40]);
        _ = z;
    }

    // ───────────────────────────── power ─────────────────────────────

    [Fact]
    public void Power_runs_from_a_plant_along_power_lines_and_from_zone_to_touching_zone_and_no_further()
    {
        var w = Flat();
        var plant = Put(w, Tool.Coal, 10, 10);
        Wire(w, 12, 10, 20, 10);
        var a = Put(w, Tool.Residential, 22, 10);
        var b = Put(w, Tool.Commercial, 25, 10);
        var lonely = Put(w, Tool.Industrial, 40, 40);
        var gap = Put(w, Tool.Residential, 29, 10); // one tile of bare land from b
        Sim.Power(w);
        Assert.True(plant.Powered);
        Assert.True(a.Powered);
        Assert.True(b.Powered);
        Assert.False(lonely.Powered);
        Assert.False(gap.Powered);
        Assert.True(w.Power[15, 10]);
        Assert.False(w.Power[15, 11]);
        Assert.Equal(Sim.CoalPower, w.PowerCapacity);
        Assert.Equal(18, w.PowerUsed);
        // Cut the line: the zones go dark.
        Sim.Apply(w, Tool.Bulldozer, 15, 10, free: true);
        Assert.True(w.PowerDirty);
        Sim.Power(w);
        Assert.False(a.Powered);
        Assert.False(b.Powered);
        Assert.True(plant.Powered);
    }

    [Fact]
    public void A_plant_powers_only_as_many_zones_as_its_capacity_and_the_rest_are_in_a_brownout()
    {
        var w = Flat();
        Put(w, Tool.Coal, 2, 50);
        // A long row of zones, each touching the next.
        var zones = new List<Zone>();
        for (int k = 0; k < 11; k++)
        for (int r = 0; r < 9; r++) zones.Add(Put(w, Tool.Residential, 5 + k * 3, 2 + r * 3 + (r >= 8 ? 0 : 0)));
        Wire(w, 4, 26, 4, 50);
        Sim.Power(w);
        Assert.Equal(Sim.CoalPower / 9, zones.Count(z => z.Powered));
        Assert.True(w.PowerUsed <= w.PowerCapacity);
        Assert.Contains(w.News, n => n.Text.StartsWith("BROWNOUTS"));
        Put(w, Tool.Nuclear, 2, 60);
        Wire(w, 1, 52, 1, 58);
        Wire(w, 1, 52, 3, 52);
        Sim.Power(w);
        Assert.All(zones, z => Assert.True(z.Powered));
        Assert.Equal(Sim.CoalPower + Sim.NuclearPower, w.PowerCapacity);
    }

    // ───────────────────────────── growth ─────────────────────────────

    /// <summary>A small working town: a plant, a road, a residential and an industrial zone side by side on it.</summary>
    private static (World W, Zone Home, Zone Work) Town(bool power = true, bool road = true)
    {
        var w = Flat();
        if (road) Road(w, 10, 20, 40, 20);
        var home = Put(w, Tool.Residential, 20, 22);
        var work = Put(w, Tool.Industrial, 23, 22);
        if (power)
        {
            Put(w, Tool.Coal, 26, 23);
        }
        return (w, home, work);
    }

    [Fact]
    public void A_zone_with_power_and_a_road_grows_and_one_without_either_stays_empty()
    {
        var (w, home, work) = Town();
        Sim.Months(w, 24);
        Assert.True(home.Powered);
        Assert.True(home.Level >= 3, $"home at level {home.Level}");
        Assert.True(work.Level >= 1);
        Assert.True(w.Residents > 0);

        var (dark, darkHome, _) = Town(power: false);
        Sim.Months(dark, 24);
        Assert.Equal(0, darkHome.Level);
        Assert.Equal(0, dark.Residents);

        var (cut, cutHome, _) = Town(road: false);
        Sim.Months(cut, 24);
        Assert.Equal(0, cutHome.Level);
        Assert.True(cutHome.NoRoad);
        Assert.Contains("ZONES NEED ROADS", cut.Told.Keys);
    }

    [Fact]
    public void A_grown_zone_that_loses_its_power_declines()
    {
        var (w, home, _) = Town();
        Sim.Months(w, 24);
        int grown = home.Level;
        Assert.True(grown >= 3);
        var plant = w.Zones.Single(z => z.Kind == ZoneKind.Coal);
        Sim.Apply(w, Tool.Bulldozer, plant.X, plant.Y, free: true);
        Sim.Months(w, 24);
        Assert.False(home.Powered);
        Assert.True(home.Level < grown);
    }

    [Fact]
    public void Zones_grow_into_bigger_buildings_and_hold_more_people_the_bigger_they_are()
    {
        var z = new Zone { Kind = ZoneKind.Residential };
        double last = -1;
        for (int l = 0; l <= Sim.MaxLevel(ZoneKind.Residential); l++)
        {
            z.Level = l;
            Assert.True(Sim.PopOf(z) > last);
            last = Sim.PopOf(z);
        }
        Assert.Equal(5, Sim.MaxLevel(ZoneKind.Residential));
        Assert.Equal(4, Sim.MaxLevel(ZoneKind.Commercial));
        Assert.Equal(1, Sim.MaxLevel(ZoneKind.Police));
    }

    // ───────────────────────────── traffic ─────────────────────────────

    [Fact]
    public void Trips_go_along_the_roads_to_where_the_people_want_to_go_and_leave_traffic_on_the_way()
    {
        var w = Flat();
        Road(w, 10, 30, 60, 30);
        var home = Put(w, Tool.Residential, 12, 32);
        var work = Put(w, Tool.Industrial, 58, 32);
        work.Level = 2;
        // No road at all: -1.
        var off = Put(w, Tool.Residential, 80, 80);
        Assert.Equal(-1, Sim.Trip(w, off));
        // Along the road to the factory: the road between carries the traffic.
        Assert.Equal(1, Sim.Trip(w, home));
        Assert.True(w.Trips[35, 30] > 0);
        Assert.Equal(0, w.Trips[35, 31]);
        Sim.Maps(w);
        Assert.True(w.Traffic[35, 30] > 0);
        // Somewhere to go, but not by road from here: 0.
        var island = Put(w, Tool.Residential, 80, 60);
        Road(w, 78, 58, 82, 58);
        Assert.Equal(0, Sim.Trip(w, island));
    }

    [Fact]
    public void Heavy_traffic_jams_and_pollutes_the_road()
    {
        var w = Flat();
        Road(w, 10, 30, 60, 30);
        var home = Put(w, Tool.Residential, 12, 32);
        Put(w, Tool.Industrial, 58, 32).Level = 2;
        for (int x = 15; x < 56; x++) w.Traffic[x, 30] = 250;
        Assert.Equal(1, Sim.Trip(w, home));
        Assert.True(home.Jammed);
        Sim.Maps(w);
        Assert.True(w.Pollution[35, 30] > w.Pollution[35, 60]);
    }

    // ───────────────────────────── the maps ─────────────────────────────

    [Fact]
    public void Industry_and_the_coal_plant_pollute_and_pollution_brings_land_value_down()
    {
        var w = Flat();
        Put(w, Tool.Coal, 30, 30);
        var factory = Put(w, Tool.Industrial, 34, 30);
        factory.Level = 4;
        w.CentreX = 30;
        w.CentreY = 50;
        Sim.Maps(w);
        Sim.Maps(w);
        Assert.True(w.Pollution[31, 31] > 100);
        Assert.True(w.Pollution[31, 31] > w.Pollution[31, 70]);
        // Land as far from the middle of town but clean is worth more.
        Assert.True(w.LandValue[30, 70] > w.LandValue[30, 30]);
    }

    [Fact]
    public void Land_is_worth_more_near_the_middle_of_town_by_the_water_and_by_the_parks()
    {
        var w = Flat();
        w.CentreX = 60;
        w.CentreY = 50;
        Sim.Maps(w);
        Assert.True(w.LandValue[60, 50] > w.LandValue[10, 10]);
        int before = w.LandValue[90, 50];
        for (int y = 40; y < 60; y++) w.Ground[94, y] = Ground.Water;
        for (int x = 86; x < 89; x++)
        for (int y = 47; y < 53; y++) w.Ground[x, y] = Ground.Park;
        Sim.Maps(w);
        Assert.True(w.LandValue[90, 50] > before + 20);
    }

    [Fact]
    public void Crowded_poor_neighbourhoods_breed_crime_and_a_police_station_brings_it_down()
    {
        var w = Flat();
        w.CentreX = 5;
        w.CentreY = 5;
        var zones = new List<Zone>();
        for (int k = 0; k < 4; k++)
        {
            var z = Put(w, Tool.Residential, 70 + k * 3, 70);
            z.Level = 5;
            z.Pop = Sim.PopOf(z);
            zones.Add(z);
        }
        Sim.Maps(w);
        Sim.Maps(w);
        int crime = w.Crime[73, 70];
        Assert.True(crime > 100, $"crime {crime}");
        Put(w, Tool.Police, 74, 74);
        Put(w, Tool.Coal, 77, 74);
        Sim.Power(w);
        Sim.Maps(w);
        Assert.True(w.PoliceCover[73, 70] > 100);
        Assert.True(w.Crime[73, 70] < crime - 40);
    }

    // ───────────────────────────── demand, money, opinion ─────────────────────────────

    [Fact]
    public void People_come_for_jobs_shops_come_for_people_and_high_taxes_cool_everything()
    {
        var w = Flat();
        w.Residents = 20000;
        w.ComJobs = 500;
        w.IndJobs = 500;
        for (int i = 0; i < 12; i++) Sim.Demand(w);
        Assert.True(w.DemandR < 0, "too many people for the jobs: nobody else comes");
        Assert.True(w.DemandC > 0.5);
        Assert.True(w.DemandI > 0.5);
        w.Residents = 1000;
        w.ComJobs = 3000;
        w.IndJobs = 6000;
        for (int i = 0; i < 12; i++) Sim.Demand(w);
        Assert.True(w.DemandR > 0.5);
        Assert.True(w.DemandC < 0);
        double r = w.DemandR;
        w.TaxRate = 20;
        for (int i = 0; i < 12; i++) Sim.Demand(w);
        Assert.True(w.DemandR < r - 0.5);
    }

    [Fact]
    public void The_years_end_brings_in_the_taxes_and_pays_the_services_what_they_are_funded()
    {
        var w = Flat();
        w.Funds = 10000;
        Road(w, 10, 10, 59, 10); // 50 tiles of road: $50 a year
        Put(w, Tool.Police, 20, 20);
        Put(w, Tool.FireStation, 30, 20);
        for (int k = 0; k < 4; k++) { var z = Put(w, Tool.Residential, 20 + k * 3, 30); z.Level = 4; z.Pop = Sim.PopOf(z); }
        Sim.Maps(w);
        Sim.Census(w);
        Assert.Equal((50L, 100L, 100L), Sim.Asks(w));
        long taxes = Sim.Taxes(w);
        Assert.True(taxes > 0);
        Sim.YearEnd(w);
        Assert.Equal(10000 + taxes - 250, w.Funds);
        Assert.True(w.BudgetDue);
        Assert.Equal(250, w.LastYear.Spent);
        // Half funding for the police: half the money, and less reach.
        w.Funding[(int)Service.Police] = 50;
        int cover = w.PoliceCover[21, 21];
        Sim.Maps(w);
        Assert.True(w.PoliceCover[21, 21] < cover);
        long before = w.Funds;
        Sim.YearEnd(w);
        Assert.Equal(200, w.LastYear.Spent);
        Assert.Equal(before + w.LastYear.Taxes - 200, w.Funds);
        // Higher taxes bring in more.
        w.TaxRate = 14;
        Assert.True(Sim.Taxes(w) > taxes * 19 / 10);
    }

    [Fact]
    public void Roads_the_city_will_not_pay_for_fall_apart()
    {
        var w = Flat();
        for (int y = 5; y < 95; y += 4) Road(w, 5, y, 110, y);
        int roads() { int n = 0; for (int x = 0; x < Field.W; x++) for (int y = 0; y < Field.H; y++) if (w.Has(x, y, Net.Road)) n++; return n; }
        int start = roads();
        Sim.Months(w, 3);
        Assert.Equal(start, roads());
        w.Funding[(int)Service.Roads] = 0;
        Sim.Months(w, 6);
        Assert.True(roads() < start);
    }

    [Fact]
    public void The_people_judge_the_mayor_and_high_taxes_and_crime_lose_their_approval()
    {
        var (w, _, _) = Town();
        Sim.Months(w, 12);
        int happy = w.Approval;
        Assert.True(w.Score > 500);
        w.TaxRate = 20;
        Sim.Months(w, 12);
        Assert.True(w.Approval < happy - 20);
        Assert.Contains(Sim.WorstProblems(w), p => p.What == Problem.Taxes);
    }

    [Fact]
    public void A_city_is_a_village_a_town_a_city_a_capital_a_metropolis_then_a_megalopolis()
    {
        Assert.Equal("VILLAGE", World.ClassOf(1999));
        Assert.Equal("TOWN", World.ClassOf(2000));
        Assert.Equal("CITY", World.ClassOf(10000));
        Assert.Equal("CAPITAL", World.ClassOf(50000));
        Assert.Equal("METROPOLIS", World.ClassOf(100000));
        Assert.Equal("MEGALOPOLIS", World.ClassOf(500000));
    }

    // ───────────────────────────── disasters ─────────────────────────────

    [Fact]
    public void Fire_spreads_through_the_woods_burns_out_to_rubble_and_a_fire_station_puts_it_out_sooner()
    {
        int Burnt(bool station)
        {
            var w = Flat();
            for (int x = 30; x < 70; x++)
            for (int y = 30; y < 70; y++) w.Ground[x, y] = Ground.Trees;
            if (station)
            {
                for (int x = 45; x < 48; x++)
                for (int y = 45; y < 48; y++) w.Ground[x, y] = Ground.Land;
                Put(w, Tool.FireStation, 46, 46);
                Put(w, Tool.Coal, 46, 42);
                Sim.Power(w);
                Sim.Maps(w);
            }
            Sim.StartFire(w, 50, 50);
            Assert.Equal(Ground.Fire, w.Ground[50, 50]);
            for (int i = 0; i < 60 * 120; i++) Sim.Step(w);
            int burnt = 0;
            for (int x = 30; x < 70; x++)
            for (int y = 30; y < 70; y++)
                if (w.Ground[x, y] == Ground.Rubble) burnt++;
            Assert.DoesNotContain(Ground.Fire, w.Ground.Cast<Ground>());
            return burnt;
        }
        int wild = Burnt(false), fought = Burnt(true);
        Assert.True(wild > 10, $"wild fire burnt {wild}");
        Assert.True(fought < wild, $"{fought} burnt with a station, {wild} without");
    }

    [Fact]
    public void A_building_on_fire_burns_down_whole()
    {
        var w = Flat();
        var z = Put(w, Tool.Commercial, 20, 20);
        Sim.StartFire(w, 21, 21);
        Assert.DoesNotContain(z, w.Zones);
        Assert.Equal(Ground.Fire, w.Ground[19, 19]);
        Assert.Equal(-1, w.ZoneId[20, 20]);
    }

    [Fact]
    public void A_flood_rises_from_the_water_drowns_what_it_reaches_then_goes_down()
    {
        var w = Flat();
        for (int x = 0; x < Field.W; x++)
        for (int y = 60; y < 70; y++) w.Ground[x, y] = Ground.Water;
        var z = Put(w, Tool.Residential, 60, 57);
        w.CentreX = 60;
        w.CentreY = 55;
        Assert.True(Sim.Start(w, Disaster.Flood));
        int most = 0;
        for (int i = 0; i < 60 * 60; i++)
        {
            Sim.Step(w);
            most = Math.Max(most, w.Ground.Cast<Ground>().Count(g => g == Ground.Flood));
        }
        Assert.True(most > 20);
        Assert.DoesNotContain(z, w.Zones);
        Assert.DoesNotContain(Ground.Flood, w.Ground.Cast<Ground>());
        Assert.False(Sim.Start(Flat(), Disaster.Flood));
    }

    [Fact]
    public void A_tornado_and_a_monster_wreck_what_they_pass_over()
    {
        foreach (var d in new[] { Disaster.Tornado, Disaster.Monster })
        {
            var w = Flat();
            for (int x = 20; x < 100; x += 3)
            for (int y = 20; y < 80; y += 3) Sim.Apply(w, Tool.Residential, x + 1, y + 1, free: true);
            int before = w.Zones.Count;
            w.CentreX = 60;
            w.CentreY = 50;
            Sim.Maps(w);
            Assert.True(Sim.Start(w, d));
            Assert.Single(w.Sprites);
            for (int i = 0; i < 60 * 32; i++) Sim.Step(w);
            Assert.Empty(w.Sprites);
            Assert.True(w.Zones.Count < before - 3, $"{d}: {before} zones, {w.Zones.Count} left");
        }
    }

    [Fact]
    public void An_earthquake_shakes_the_city_and_wrecks_buildings_and_roads()
    {
        var w = Flat();
        for (int x = 40; x < 80; x += 3)
        for (int y = 30; y < 70; y += 3) Sim.Apply(w, Tool.Residential, x + 1, y + 1, free: true);
        Road(w, 30, 50, 90, 50);
        int before = w.Zones.Count;
        w.CentreX = 60;
        w.CentreY = 50;
        Assert.True(Sim.Start(w, Disaster.Earthquake));
        Assert.True(w.Shake > 0);
        Assert.True(w.Zones.Count < before - 10);
        Assert.Contains(Sfx.Quake, w.Heard);
    }

    [Fact]
    public void A_plane_comes_down_in_flames()
    {
        var w = Flat();
        for (int x = 40; x < 80; x += 3)
        for (int y = 30; y < 70; y += 3) Sim.Apply(w, Tool.Commercial, x + 1, y + 1, free: true);
        int before = w.Zones.Count;
        w.CentreX = 60;
        w.CentreY = 50;
        Assert.True(Sim.Start(w, Disaster.PlaneCrash));
        Assert.Contains(w.Sprites, s => s.Kind == SpriteKind.Plane);
        for (int i = 0; i < 60 * 4; i++) Sim.Step(w);
        Assert.Contains(Sfx.Explosion, w.Heard);
        Assert.True(w.Zones.Count < before);
    }

    [Fact]
    public void Only_a_nuclear_plant_can_melt_down_and_it_leaves_the_land_round_it_radioactive_for_good()
    {
        var w = Flat();
        Assert.False(Sim.Start(w, Disaster.Meltdown));
        var plant = Put(w, Tool.Nuclear, 60, 50);
        Assert.True(Sim.Start(w, Disaster.Meltdown));
        Assert.DoesNotContain(plant, w.Zones);
        int hot = w.Ground.Cast<Ground>().Count(g => g == Ground.Radiation);
        Assert.True(hot > 20);
        Sim.Months(w, 6);
        Assert.Equal(hot, w.Ground.Cast<Ground>().Count(g => g == Ground.Radiation));
        int rx = 0, ry = 0;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
            if (w.Ground[x, y] == Ground.Radiation) (rx, ry) = (x, y);
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Bulldozer, rx, ry, free: true));
        Assert.Equal(Outcome.Blocked, Sim.Apply(w, Tool.Road, rx, ry, free: true));
    }

    [Fact]
    public void Random_disasters_come_only_when_they_are_turned_on()
    {
        int Struck(bool on)
        {
            var w = new World();
            Sim.Found(w, 5, Difficulty.Hard, "TEST");
            Scenarios.Raise(w, new Pilot(), 60);
            w.Disasters = on;
            int seen = 0;
            for (int m = 0; m < 12 * 30; m++)
            {
                Sim.Month(w);
                if (w.News.Any(n => n.Text.EndsWith('!'))) seen++;
                w.News.Clear();
            }
            return seen;
        }
        Assert.Equal(0, Struck(false));
        Assert.True(Struck(true) > 3);
    }

    // ───────────────────────────── the land ─────────────────────────────

    [Fact]
    public void The_same_seed_makes_the_same_land_with_water_and_woods_on_it()
    {
        var a = new World();
        var b = new World();
        Terrain.Make(a, 123);
        Terrain.Make(b, 123);
        Assert.Equal(a.Ground.Cast<Ground>(), b.Ground.Cast<Ground>());
        int lands = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var w = new World();
            Terrain.Make(w, seed);
            var all = w.Ground.Cast<Ground>().ToList();
            int water = all.Count(g => g == Ground.Water), trees = all.Count(g => g == Ground.Trees);
            Assert.True(trees > 300, $"seed {seed}: {trees} trees");
            Assert.True(water < all.Count / 2, $"seed {seed}: {water} water");
            if (water > 200) lands++;
        }
        Assert.True(lands >= 6);
    }

    // ───────────────────────────── the computer mayor ─────────────────────────────

    [Fact]
    public void The_computer_mayor_builds_a_powered_growing_city()
    {
        var w = new World();
        Sim.Found(w, 7, Difficulty.Easy, "TEST");
        w.Disasters = false;
        var p = new Pilot();
        while (w.Month < 12 * 6)
        {
            p.Step(w);
            Sim.Step(w);
        }
        Assert.True(w.Residents > 15000, $"{w.Residents} people");
        Assert.Contains(w.Zones, z => Sim.IsPlant(z.Kind));
        Assert.Contains(w.Zones, z => z.Kind == ZoneKind.Police);
        Assert.True(w.Zones.Count(z => z.Grows && !z.Powered) <= 2);
        Assert.True(w.Zones.Count(z => z.Grows && z.NoRoad) == 0);
        Assert.True(w.Score > 600);
        Assert.True(w.Funds >= 0);
    }

    [Fact]
    public void The_computer_mayor_builds_again_after_a_disaster()
    {
        var w = new World();
        Sim.Found(w, 7, Difficulty.Easy, "TEST");
        w.Disasters = false;
        var p = new Pilot();
        while (w.Month < 12 * 4) { p.Step(w); Sim.Step(w); }
        Sim.Start(w, Disaster.Earthquake);
        int after = w.Zones.Count;
        while (w.Month < 12 * 6) { p.Step(w); Sim.Step(w); }
        Assert.True(w.Zones.Count > after);
        Assert.True(w.Zones.Count(z => z.Grows && z.NoRoad) <= 1);
    }

    // ───────────────────────────── files and copies ─────────────────────────────

    private static World Running()
    {
        var w = new World();
        Sim.Found(w, 11, Difficulty.Medium, "SAVEVILLE");
        var p = new Pilot();
        while (w.Month < 30) { p.Step(w); Sim.Step(w); }
        Sim.Start(w, Disaster.Tornado);
        for (int i = 0; i < 37; i++) Sim.Step(w);
        return w;
    }

    private static void Same(World a, World b)
    {
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(a.Month, b.Month);
        Assert.Equal(a.StepInMonth, b.StepInMonth);
        Assert.Equal(a.Funds, b.Funds);
        Assert.Equal(a.Residents, b.Residents);
        Assert.Equal(a.Zones.Count, b.Zones.Count);
        Assert.Equal(a.Zones.Select(z => (z.Kind, z.X, z.Y, z.Level, z.Powered)), b.Zones.Select(z => (z.Kind, z.X, z.Y, z.Level, z.Powered)));
        Assert.Equal(a.Ground.Cast<Ground>(), b.Ground.Cast<Ground>());
        Assert.Equal(a.Net.Cast<Net>(), b.Net.Cast<Net>());
        Assert.Equal(a.Traffic.Cast<byte>(), b.Traffic.Cast<byte>());
        Assert.Equal(a.Sprites.Count, b.Sprites.Count);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Dice.State, b.Dice.State);
    }

    [Fact]
    public void A_saved_city_loads_just_as_it_was_and_goes_on_just_the_same()
    {
        var a = Running();
        var path = Path.Combine(TempFolder(), "test.cty");
        CityFile.Save(a, path);
        var b = CityFile.Load(path);
        Assert.NotNull(b);
        Same(a, b);
        Sim.Months(a, 4);
        Sim.Months(b, 4);
        Same(a, b);
        File.WriteAllText(path, "not a city");
        Assert.Null(CityFile.Load(path));
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void A_copied_city_goes_on_just_as_the_original()
    {
        var a = Running();
        var b = a.Copy();
        Same(a, b);
        Sim.Months(a, 6);
        Sim.Months(b, 6);
        Same(a, b);
        // And they're separate: a change to one isn't in the other.
        Sim.Apply(a, Tool.Road, 1, 1, free: true);
        Assert.False(b.Has(1, 1, Net.Road));
    }

    // ───────────────────────────── scenarios ─────────────────────────────

    [Fact]
    public void Every_scenario_starts_as_a_built_up_city_with_a_goal_and_its_trouble_comes_on_time()
    {
        for (int id = 0; id < Scenarios.All.Length; id++)
        {
            var w = new World();
            Scenarios.Setup(w, id);
            var s = Scenarios.All[id];
            Assert.Equal(s.Year, w.Year);
            Assert.True(w.Residents > 5000, $"{s.Name}: {w.Residents}");
            Assert.True(w.Goal > 0);
            Assert.Equal(0, w.Result);
            Assert.Equal(s.Funds, w.Funds);
            Assert.False(Scenarios.GoalText(w).Length == 0);
            switch (id)
            {
                case 1: Assert.True(w.Problems[(int)Problem.Traffic] > w.Goal, $"traffic {w.Problems[(int)Problem.Traffic]}, goal {w.Goal}"); break;
                case 2:
                    Assert.DoesNotContain(w.Zones, z => z.Kind == ZoneKind.Police);
                    Assert.True(w.Problems[(int)Problem.Crime] > w.Goal, $"crime {w.Problems[(int)Problem.Crime]}, goal {w.Goal}");
                    break;
                case 3: Assert.Contains(w.Zones, z => z.Kind == ZoneKind.Nuclear); break;
            }
            w.Disasters = false;
            Sim.Months(w, 3);
            switch (id)
            {
                case 0: Assert.Contains("FLOODING REPORTED!", w.Told.Keys); break;
                case 3:
                    Assert.Contains("NUCLEAR MELTDOWN!", w.Told.Keys);
                    Assert.Contains(Ground.Radiation, w.Ground.Cast<Ground>());
                    break;
                case 4: Assert.Contains("A MONSTER HAS BEEN SIGHTED!", w.Told.Keys); break;
            }
        }
    }

    [Fact]
    public void A_scenario_is_won_or_lost_when_its_time_is_up()
    {
        var w = new World();
        Scenarios.Setup(w, 4);
        w.Disasters = false;
        Sim.Months(w, Scenarios.All[4].Years * 12 - 1);
        Assert.Equal(0, w.Result);
        w.Score = 900;
        w.Goal = 0;
        Sim.Month(w);
        Assert.Equal(1, w.Result);
        var lost = new World();
        Scenarios.Setup(lost, 0);
        lost.Goal = 10_000_000;
        lost.Month = lost.StartMonth + Scenarios.All[0].Years * 12 - 1;
        Sim.Month(lost);
        Assert.Equal(-1, lost.Result);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_and_enter_opens_a_new_city()
    {
        var g = Engine();
        Assert.Equal(GameState.Title, g.State);
        int month = g.World.Month;
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        Assert.True(g.World.Month > month, "the computer mayor's city runs behind the title");
        Assert.True(g.World.Zones.Count > 0);
        g.Idle((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(Panel.NewCity, g.Panel);
        g.NewDifficulty = Difficulty.Hard;
        g.NewName = "TESTVILLE";
        g.StartCity();
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal("TESTVILLE", g.World.Name);
        Assert.Equal(5000, g.World.Funds);
        Assert.Empty(g.World.Zones);
        Assert.Equal(Field.StartYear, g.World.Year);
    }

    [Fact]
    public void Dragging_a_road_lays_every_tile_on_the_way_and_a_zone_goes_down_with_a_click()
    {
        var g = Engine();
        g.Go(seed: 1);
        var w = g.World;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++) w.Ground[x, y] = Ground.Land;
        g.Select(Tool.Road);
        Assert.Equal(Outcome.Done, g.Press(10, 10));
        g.Drag(20, 10);
        g.Drag(20, 15);
        g.Release();
        for (int x = 10; x <= 20; x++) Assert.True(w.Has(x, 10, Net.Road));
        for (int y = 10; y <= 15; y++) Assert.True(w.Has(20, y, Net.Road));
        Assert.Equal(20000 - 16 * 10, w.Funds);
        g.Select(Tool.Residential);
        Assert.Equal(Outcome.Done, g.Press(12, 12));
        g.Drag(30, 30);
        Assert.Single(w.Zones);
        Assert.Equal(Outcome.Blocked, g.Press(12, 12));
        g.Release();
        g.Select(Tool.Query);
        g.Press(12, 12);
        Assert.Equal(Panel.Query, g.Panel);
        Assert.Equal((12, 12), g.Queried);
    }

    [Fact]
    public void Time_runs_at_the_chosen_speed_stops_when_paused_and_while_a_window_is_open()
    {
        var g = Engine();
        g.Go(seed: 2);
        var w = g.World;
        g.Run(60);
        int normal = w.Month * Field.StepsPerMonth + w.StepInMonth;
        Assert.Equal(60, normal);
        g.TogglePause();
        Assert.Equal(Speed.Paused, g.Speed);
        g.Run(60);
        Assert.Equal(normal, w.Month * Field.StepsPerMonth + w.StepInMonth);
        g.TogglePause();
        g.SetSpeed(Speed.Fast);
        g.Run(60);
        Assert.Equal(normal + 240, w.Month * Field.StepsPerMonth + w.StepInMonth);
        g.Open(Panel.Evaluation);
        g.Run(60);
        Assert.Equal(normal + 240, w.Month * Field.StepsPerMonth + w.StepInMonth);
        g.Close();
        g.SetSpeed(Speed.Slow);
        g.Run(60);
        Assert.Equal(normal + 260, w.Month * Field.StepsPerMonth + w.StepInMonth);
    }

    [Fact]
    public void The_budget_window_opens_at_the_years_end_unless_the_budget_is_automatic()
    {
        var g = Engine();
        g.Go(seed: 3);
        g.SetSpeed(Speed.Fast);
        g.World.Disasters = false;
        for (int i = 0; i < 60 * 60 && g.Panel != Panel.Budget; i++) g.Tick(default);
        Assert.Equal(Panel.Budget, g.Panel);
        Assert.Single(g.World.History);
        g.SetTax(9);
        g.SetFunding(Service.Fire, 50);
        Assert.Equal(9, g.World.TaxRate);
        Assert.Equal(50, g.World.Funding[(int)Service.Fire]);
        g.SetTax(99);
        Assert.Equal(20, g.World.TaxRate);
        g.Close();
        g.World.AutoBudget = true;
        for (int i = 0; i < 60 * 60 && g.World.History.Count < 2; i++) g.Tick(default);
        Assert.Equal(2, g.World.History.Count);
        Assert.Equal(Panel.None, g.Panel);
    }

    [Fact]
    public void Cities_save_to_their_slots_and_load_again()
    {
        var g = Engine();
        g.Go(seed: 4);
        g.Autoplay(18);
        var name = g.World.Name;
        int residents = g.World.Residents;
        Assert.Null(g.SlotText(2));
        Assert.True(g.SaveCity(2));
        Assert.Contains(name, g.SlotText(2));
        g.Go(seed: 5);
        Assert.False(g.LoadCity(4));
        Assert.True(g.LoadCity(2));
        Assert.Equal(name, g.World.Name);
        Assert.Equal(residents, g.World.Residents);
        Assert.Equal(GameState.Playing, g.State);
        Directory.Delete(g.CityFolder, true);
    }

    [Fact]
    public void A_disaster_from_the_window_strikes_and_the_news_says_so()
    {
        var g = Engine();
        g.Go(seed: 6);
        g.Autoplay(24);
        Assert.False(g.Trigger(Disaster.Meltdown));
        Assert.Equal("THERE IS NO NUCLEAR PLANT", g.Message);
        Assert.True(g.Trigger(Disaster.Fire));
        Assert.Equal("FIRE REPORTED!", g.Message);
        Assert.True(g.Trigger(Disaster.Tornado));
        Assert.Contains(g.World.Sprites, s => s.Kind == SpriteKind.Tornado);
    }

    [Fact]
    public void The_keys_scroll_the_map_the_way_they_point()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.D);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyUp(Key.D);
        input.OnKeyDown(Key.Enter);
        Assert.True(input.Start);
        input.Clear();
        Assert.False(input.Start);

        var g = Engine();
        g.Go(seed: 1);
        g.CenterOn(60, 50);
        double y = g.ViewY, x = g.ViewX;
        input.OnKeyDown(Key.Up);
        g.Run(30, input.Player);
        Assert.True(g.ViewY < y);
        input.Clear();
        input.OnKeyDown(Key.Right);
        g.Run(30, input.Player);
        Assert.True(g.ViewX > x);
        // The zoom keeps the middle where it was.
        double cx = g.ViewX + g.TilesAcross / 2;
        g.SetZoom(2);
        Assert.Equal(cx, g.ViewX + g.TilesAcross / 2, 3);
    }

    [Fact]
    public void Every_sound_the_game_asks_for_has_its_file()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>())
            if (s != Sfx.MusicStop) Assert.True(File.Exists(Path.Combine(folder, s + ".wav")), $"no sound for {s}");
    }
}
