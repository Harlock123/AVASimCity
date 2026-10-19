using AVASimCity.Engine;

namespace AVASimCity.Tests;

/// <summary>
/// How the computer mayor gets on (set SIMCITY_PROBE to run it): a city a year at a time, its people, its jobs,
/// its money and what the people think. SIMCITY_YEARS sets how long (20), SIMCITY_SEED the land.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_mayor_does()
    {
        if (Environment.GetEnvironmentVariable("SIMCITY_PROBE") is null) return;
        int years = int.TryParse(Environment.GetEnvironmentVariable("SIMCITY_YEARS"), out int y) ? y : 20;
        int seed = int.TryParse(Environment.GetEnvironmentVariable("SIMCITY_SEED"), out int s) ? s : 7;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var w = new World();
        Sim.Found(w, seed, Difficulty.Easy, "PROBE");
        w.Disasters = Environment.GetEnvironmentVariable("SIMCITY_PROBE") == "disasters";
        var p = new Pilot();
        for (int year = 1; year <= years; year++)
        {
            while (w.Month < year * 12)
            {
                p.Step(w);
                Sim.Step(w);
            }
            w.Heard.Clear();
            int r = w.Zones.Count(z => z.Kind == ZoneKind.Residential), c = w.Zones.Count(z => z.Kind == ZoneKind.Commercial), i = w.Zones.Count(z => z.Kind == ZoneKind.Industrial);
            int dark = w.Zones.Count(z => z.Grows && !z.Powered), cut = w.Zones.Count(z => z.Grows && z.NoRoad), jam = w.Zones.Count(z => z.Jammed);
            output.WriteLine($"{w.Year}: pop {w.Residents,7:N0} jobs {w.ComJobs,6:N0}+{w.IndJobs,6:N0} zones {r}/{c}/{i} lv {w.Zones.Where(z => z.Grows).DefaultIfEmpty().Average(z => z?.Level ?? 0):0.0} " +
                             $"$ {w.Funds,7:N0} tax {w.LastYear.Taxes,6:N0} D {w.DemandR:0.00}/{w.DemandC:0.00}/{w.DemandI:0.00} score {w.Score} ({w.Approval}%) " +
                             $"dark {dark} noroad {cut} jam {jam} pow {w.PowerUsed}/{w.PowerCapacity} other {string.Join(",", w.Zones.Where(z => !z.Grows).GroupBy(z => z.Kind).Select(g => g.Key + "=" + g.Count()))} " +
                             $"probs {string.Join(",", Sim.WorstProblems(w).Select(q => q.What + "=" + q.Share))}");
        }
        foreach (var k in new[] { ZoneKind.Residential, ZoneKind.Commercial, ZoneKind.Industrial })
        {
            var zs = w.Zones.Where(z => z.Kind == k).ToList();
            output.WriteLine($"{k}: levels {string.Join(" ", Enumerable.Range(0, Sim.MaxLevel(k) + 1).Select(l => zs.Count(z => z.Level == l)))} " +
                             $"trip {string.Join(" ", zs.GroupBy(z => Sim.Trip(w, z)).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count()))} " +
                             $"appeal {zs.DefaultIfEmpty().Average(z => z == null ? 0 : Sim.Appeal(w, z)):0.00} lv {zs.DefaultIfEmpty().Average(z => z == null ? 0 : w.LandValue[(int)z.Centre.X, (int)z.Centre.Y]):0} " +
                             $"pol {zs.DefaultIfEmpty().Average(z => z == null ? 0 : w.Pollution[(int)z.Centre.X, (int)z.Centre.Y]):0} crime {zs.DefaultIfEmpty().Average(z => z == null ? 0 : w.Crime[(int)z.Centre.X, (int)z.Centre.Y]):0}");
        }
        foreach (var z in w.Zones.Where(z => z.Kind == ZoneKind.Residential && z.Level == 0).Take(6))
        {
            var seen = new HashSet<(int, int)>();
            var q = new Queue<(int, int)>(Sim.Edge(z).Where(e => w.Has(e.X, e.Y, Net.Road | Net.Rail)));
            foreach (var e in q) seen.Add(e);
            int jobs = 0;
            while (q.Count > 0)
            {
                var (qx, qy) = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int u = qx + Sim.Dx[d], v = qy + Sim.Dy[d];
                    var o = w.ZoneAt(u, v);
                    if (o != null && o.Kind != ZoneKind.Residential && o.Grows && o.Level > 0) jobs++;
                    if (w.Has(u, v, Net.Road | Net.Rail) && seen.Add((u, v))) q.Enqueue((u, v));
                }
            }
            output.WriteLine($"stuck R at {z.X},{z.Y}: powered {z.Powered} network {seen.Count} tiles, job zone touches {jobs}, appeal {Sim.Appeal(w, z):0.00}");
        }
        output.WriteLine($"{sw.ElapsedMilliseconds} ms");
    }
}
