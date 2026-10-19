using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASimCity.Engine;
using AVASimCity.Views;
using Panel = AVASimCity.Engine.Panel;

namespace AVASimCity.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SIMCITY_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private const int Seed = 4242;

    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(Path.Combine(Path.GetTempPath(), "avasimcity-ui-" + Guid.NewGuid().ToString("N")), demoSeed: 17) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("SIMCITY_SHOTS");
        if (string.IsNullOrEmpty(dir) || name == "-") return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASimCity-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>A city the computer mayor has been building for some years, with the view on its middle.</summary>
    private static (Window W, GameCanvas Canvas, GameEngine G) Grown(int years = 14)
    {
        var (w, c, g) = Open();
        g.Go(Seed);
        g.World.Disasters = false;
        g.Autoplay(12 * years);
        g.CenterOn(g.World.CentreX, g.World.CentreY);
        g.Run(10);
        c.Advance(1.3);
        return (w, c, g);
    }

    [AvaloniaFact]
    public void The_title()
    {
        var (w, c, g) = Open();
        g.Autoplay(12 * 9);
        g.CenterOn(g.World.CentreX, g.World.CentreY);
        g.Idle(30);
        c.Advance(2.1);
        Assert.Equal(GameState.Title, g.State);
        Shot(w, c, "title");
    }

    [AvaloniaFact]
    public void A_grown_city()
    {
        var (w, c, g) = Grown();
        Assert.True(g.World.Residents > 20000);
        Shot(w, c, "city");
    }

    [AvaloniaFact]
    public void The_budget()
    {
        var (w, c, g) = Grown(10);
        g.Open(Panel.Budget);
        Shot(w, c, "budget");
    }

    [AvaloniaFact]
    public void The_evaluation()
    {
        var (w, c, g) = Grown(12);
        g.Open(Panel.Evaluation);
        Shot(w, c, "evaluation");
    }

    [AvaloniaFact]
    public void The_traffic_map()
    {
        var (w, c, g) = Grown(12);
        g.Overlay = Overlay.Traffic;
        Shot(w, c, "traffic");
    }

    [AvaloniaFact]
    public void A_monster()
    {
        var (w, c, g) = Grown(13);
        Assert.True(g.Trigger(Disaster.Monster));
        var m = g.World.Sprites.Single(s => s.Kind == SpriteKind.Monster);
        for (int i = 0; i < 60 * 12 && Math.Abs(m.X - m.Tx) + Math.Abs(m.Y - m.Ty) > 6; i++) { g.Tick(default); if (i % 4 == 0) c.Advance(1 / 15.0); }
        for (int i = 0; i < 60 * 2; i++) { g.Tick(default); if (i % 4 == 0) c.Advance(1 / 15.0); }
        g.SetZoom(2);
        g.CenterOn(m.X, m.Y + 1);
        g.Run(1);
        Shot(w, c, "monster");
    }

    [AvaloniaFact]
    public void A_scenario()
    {
        var (w, c, g) = Open();
        g.StartScenario(3);
        for (int i = 0; i < 60 * 60 && !g.World.Told.ContainsKey("NUCLEAR MELTDOWN!"); i++) g.Tick(default);
        Assert.Equal("NUCLEAR MELTDOWN!", g.Message);
        for (int i = 0; i < 60 * 2; i++) { g.Tick(default); if (i % 4 == 0) c.Advance(1 / 15.0); }
        Shot(w, c, "cedar-falls");
    }

    [AvaloniaFact]
    public void The_mouse_picks_a_tool_from_the_palette_and_builds_with_it()
    {
        var (w, c, g) = Open();
        g.Go(Seed);
        g.CenterOn(60, 50);
        for (int x = 50; x < 70; x++)
        for (int y = 40; y < 60; y++) g.World.Ground[x, y] = Ground.Land;
        Shot(w, c, "-");
        c.Click(GameCanvas.ToolRect(Tool.Residential).Center);
        Assert.Equal(Tool.Residential, g.Tool);
        // The middle of tile (60, 50) on the screen.
        double size = GameEngine.TileSize * g.Zoom;
        var at = new Avalonia.Point(GameEngine.ViewLeft + (60.5 - g.ViewX) * size, GameEngine.ViewTop + (50.5 - g.ViewY) * size);
        Assert.Equal((60, 50), c.TileAt(at));
        c.Click(at);
        Assert.Equal(ZoneKind.Residential, g.World.ZoneAt(60, 50)?.Kind);
        Shot(w, c, "-");
        c.Click(new Avalonia.Point(980, 17));
        Assert.Equal(Panel.Menu, g.Panel);
    }
}
