using System.Text;

namespace AVASimCity.Engine;

/// <summary>
/// Cities saved to disk: everything in the <see cref="World"/> that the simulation needs to go on just as it was,
/// in a small binary file of its own. Saved cities live in six slots in the user's application-data folder.
/// </summary>
public static class CityFile
{
    private const string Magic = "AVASIMCITY";
    private const int Version = 1;
    public const int Slots = 6;

    public static readonly string DefaultFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVASimCity");

    public static string SlotPath(string folder, int slot) => Path.Combine(folder, $"city{slot + 1}.cty");

    public static void Save(World w, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var f = File.Create(path);
        using var b = new BinaryWriter(f, Encoding.UTF8);
        b.Write(Magic);
        b.Write(Version);
        b.Write(w.Name);
        b.Write(w.Seed);
        b.Write((int)w.Difficulty);
        b.Write(w.Scenario);
        b.Write(w.Goal);
        b.Write(w.StartMonth);
        b.Write(w.Result);
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
        {
            b.Write((byte)w.Ground[x, y]);
            b.Write((byte)w.Net[x, y]);
            b.Write(w.Timer[x, y]);
            b.Write(w.Traffic[x, y]);
            b.Write(w.Trips[x, y]);
            b.Write(w.Pollution[x, y]);
            b.Write(w.Crime[x, y]);
            b.Write(w.LandValue[x, y]);
            b.Write(w.Density[x, y]);
            b.Write(w.PoliceCover[x, y]);
            b.Write(w.FireCover[x, y]);
        }
        b.Write(w.Zones.Count);
        foreach (var z in w.Zones)
        {
            b.Write((byte)z.Kind);
            b.Write(z.X);
            b.Write(z.Y);
            b.Write(z.Level);
            b.Write(z.Pop);
            b.Write(z.Wealth);
            b.Write(z.Variant);
            b.Write(z.NoRoad);
            b.Write(z.Jammed);
            b.Write(z.Reached);
            b.Write(z.Trend);
        }
        b.Write(w.Month);
        b.Write(w.StepInMonth);
        b.Write(w.Funds);
        b.Write(w.TaxRate);
        foreach (int f2 in w.Funding) b.Write(f2);
        var a = w.LastYear;
        b.Write(a.Year); b.Write(a.Taxes); b.Write(a.RoadsAsked); b.Write(a.PoliceAsked); b.Write(a.FireAsked);
        b.Write(a.RoadsPaid); b.Write(a.PolicePaid); b.Write(a.FirePaid);
        b.Write(w.AutoBudget);
        b.Write(w.Disasters);
        b.Write(w.DemandR); b.Write(w.DemandC); b.Write(w.DemandI);
        b.Write(w.CentreX); b.Write(w.CentreY);
        b.Write(w.Residents); b.Write(w.ComJobs); b.Write(w.IndJobs);
        b.Write(w.Score); b.Write(w.Approval);
        foreach (int p in w.Problems) b.Write(p);
        b.Write(w.History.Count);
        foreach (int h in w.History) b.Write(h);
        b.Write(w.Record);
        b.Write(w.Sprites.Count);
        foreach (var s in w.Sprites)
        {
            b.Write((int)s.Kind);
            b.Write(s.X); b.Write(s.Y); b.Write(s.Dx); b.Write(s.Dy); b.Write(s.Life); b.Write(s.Age); b.Write(s.Tx); b.Write(s.Ty);
        }
        b.Write(w.Shake);
        b.Write(w.Dice.State);
        b.Write(w.Told.Count);
        foreach (var (k, v) in w.Told) { b.Write(k); b.Write(v); }
    }

    /// <summary>A saved city, or null if the file isn't one.</summary>
    public static World? Load(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            using var b = new BinaryReader(f, Encoding.UTF8);
            if (b.ReadString() != Magic || b.ReadInt32() != Version) return null;
            var w = new World
            {
                Name = b.ReadString(), Seed = b.ReadInt32(), Difficulty = (Difficulty)b.ReadInt32(), Scenario = b.ReadInt32(),
                Goal = b.ReadInt32(), StartMonth = b.ReadInt32(), Result = b.ReadInt32(),
            };
            for (int x = 0; x < Field.W; x++)
            for (int y = 0; y < Field.H; y++)
            {
                w.Ground[x, y] = (Ground)b.ReadByte();
                w.Net[x, y] = (Net)b.ReadByte();
                w.Timer[x, y] = b.ReadByte();
                w.Traffic[x, y] = b.ReadByte();
                w.Trips[x, y] = b.ReadUInt16();
                w.Pollution[x, y] = b.ReadByte();
                w.Crime[x, y] = b.ReadByte();
                w.LandValue[x, y] = b.ReadByte();
                w.Density[x, y] = b.ReadByte();
                w.PoliceCover[x, y] = b.ReadByte();
                w.FireCover[x, y] = b.ReadByte();
            }
            int zones = b.ReadInt32();
            for (int i = 0; i < zones; i++)
                w.Zones.Add(new Zone
                {
                    Kind = (ZoneKind)b.ReadByte(), X = b.ReadInt32(), Y = b.ReadInt32(), Level = b.ReadInt32(), Pop = b.ReadDouble(),
                    Wealth = b.ReadInt32(), Variant = b.ReadInt32(), NoRoad = b.ReadBoolean(), Jammed = b.ReadBoolean(), Reached = b.ReadBoolean(), Trend = b.ReadInt32(),
                });
            w.Reindex();
            w.Month = b.ReadInt32();
            w.StepInMonth = b.ReadInt32();
            w.Funds = b.ReadInt64();
            w.TaxRate = b.ReadInt32();
            for (int i = 0; i < w.Funding.Length; i++) w.Funding[i] = b.ReadInt32();
            w.LastYear = new Accounts(b.ReadInt32(), b.ReadInt64(), b.ReadInt64(), b.ReadInt64(), b.ReadInt64(), b.ReadInt64(), b.ReadInt64(), b.ReadInt64());
            w.AutoBudget = b.ReadBoolean();
            w.Disasters = b.ReadBoolean();
            w.DemandR = b.ReadDouble(); w.DemandC = b.ReadDouble(); w.DemandI = b.ReadDouble();
            w.CentreX = b.ReadDouble(); w.CentreY = b.ReadDouble();
            w.Residents = b.ReadInt32(); w.ComJobs = b.ReadInt32(); w.IndJobs = b.ReadInt32();
            w.Score = b.ReadInt32(); w.Approval = b.ReadInt32();
            for (int i = 0; i < w.Problems.Length; i++) w.Problems[i] = b.ReadInt32();
            int years = b.ReadInt32();
            for (int i = 0; i < years; i++) w.History.Add(b.ReadInt32());
            w.Record = b.ReadInt32();
            int sprites = b.ReadInt32();
            for (int i = 0; i < sprites; i++)
                w.Sprites.Add(new Sprite
                {
                    Kind = (SpriteKind)b.ReadInt32(), X = b.ReadDouble(), Y = b.ReadDouble(), Dx = b.ReadDouble(), Dy = b.ReadDouble(),
                    Life = b.ReadDouble(), Age = b.ReadDouble(), Tx = b.ReadDouble(), Ty = b.ReadDouble(),
                });
            w.Shake = b.ReadDouble();
            w.Dice = new Dice(b.ReadUInt64(), raw: true);
            int told = b.ReadInt32();
            for (int i = 0; i < told; i++) w.Told[b.ReadString()] = b.ReadInt32();
            // The power is worked out afresh; it depends only on what's built.
            Sim.Power(w);
            return w;
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>What's in a slot: the city's name, its date and its people; null if empty.</summary>
    public static string? Describe(string folder, int slot)
    {
        var path = SlotPath(folder, slot);
        if (!File.Exists(path)) return null;
        var w = Load(path);
        return w == null ? "(UNREADABLE)" : $"{w.Name}  {w.Date}  POP {w.Residents:N0}";
    }
}
