using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AVASimCity.Engine;

namespace AVASimCity.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASimCity/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF5A4A, Dim = 0xFF8A9AB8, Cyan = 0xFF6AE8FF, Green = 0xFF6AE070;
    private const uint PanelBack = 0xFF1A2232, PanelEdge = 0xFF3A4A66, Ink = 0xFFE8E4D8;
    private const uint ResColour = 0xFF40C060, ComColour = 0xFF4A90F0, IndColour = 0xFFF0C030;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 1.5, at.Y - ft.Height / 2 + 1.5));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static double TextWidth(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brushes.White).Width;

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private static uint Hash(int x, int y, int salt) => (uint)(Pix.Rand(x, y, salt) * 65536);

    /// <summary>A place on the screen that answers a click.</summary>
    private void Hit(Rect r, Action a) => _hits.Add((r, a));

    /// <summary>A button: its face, its label, and what it does when clicked.</summary>
    private void Button(DrawingContext dc, Rect r, string label, Action act, bool on = false, double size = 20, bool enabled = true)
    {
        dc.FillRectangle(Brush(on ? 0xFF5A4A1C : enabled ? 0xFF2A3650 : 0xFF1C2230), r, 4);
        dc.DrawRectangle(null, Pen(on ? Yellow : enabled ? 0xFF6A7EA8 : 0xFF3A4058, 1.5), r, 4, 4);
        Text(dc, label, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), size, on ? Yellow : enabled ? Ink : 0xFF5A6078, center: true);
        if (enabled) Hit(r, act);
    }

    private static void Bevel(DrawingContext dc, Rect r, uint back = PanelBack)
    {
        dc.FillRectangle(Brush(back), r);
        dc.DrawLine(Pen(0xFF4A5A7A, 1), r.TopLeft, r.TopRight);
        dc.DrawLine(Pen(0xFF4A5A7A, 1), r.TopLeft, r.BottomLeft);
        dc.DrawLine(Pen(0xFF0A0E16, 1), r.BottomLeft, r.BottomRight);
        dc.DrawLine(Pen(0xFF0A0E16, 1), r.TopRight, r.BottomRight);
    }

    private void DrawScreen(DrawingContext dc)
    {
        _hits.Clear();
        var g = Engine;
        Painter.Update(g.World);
        switch (g.State)
        {
            case GameState.Title:
            {
                // The demonstration city fills the screen behind the title.
                double across = Field.Width / GameEngine.TileSize, down = Field.Height / GameEngine.TileSize;
                double cx = g.ViewX + g.TilesAcross / 2, cy = g.ViewY + g.TilesDown / 2;
                DrawMap(dc, new Rect(0, 0, Field.Width, Field.Height), cx - across / 2, cy - down / 2, 1, false);
                DrawTitle(dc);
                break;
            }
            default:
                DrawMap(dc, MapRect, g.ViewX, g.ViewY, g.Zoom, g.State == GameState.Playing);
                DrawTop(dc);
                DrawPalette(dc);
                DrawStatus(dc);
                if (g.State == GameState.Attract)
                {
                    dc.FillRectangle(Brush(0xB0000000), new Rect(MapRect.X, 52, MapRect.Width, 34));
                    Text(dc, "DEMONSTRATION - THE COMPUTER IS MAYOR - PRESS ENTER", new Point(MapRect.Center.X, 69), 24, Yellow, center: true);
                }
                break;
        }
        DrawPanel(dc);
    }

    // ───────────────────────────── the map ─────────────────────────────

    private static readonly RenderOptions Crisp = new() { BitmapInterpolationMode = BitmapInterpolationMode.None };
    private static readonly RenderOptions Smooth = new() { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality };

    /// <summary>The part of a picture of the whole map (<paramref name="perTile"/> pixels a tile) that the view shows, drawn into it.</summary>
    private static void DrawPart(DrawingContext dc, Bitmap bmp, int perTile, Rect r, double vx, double vy, double size)
    {
        double x0 = Math.Max(0, vx), y0 = Math.Max(0, vy);
        double x1 = Math.Min(Field.W, vx + r.Width / size), y1 = Math.Min(Field.H, vy + r.Height / size);
        if (x1 <= x0 || y1 <= y0) return;
        var src = new Rect(x0 * perTile, y0 * perTile, (x1 - x0) * perTile, (y1 - y0) * perTile);
        var dst = new Rect(r.X + (x0 - vx) * size, r.Y + (y0 - vy) * size, (x1 - x0) * size, (y1 - y0) * size);
        dc.DrawImage(bmp, src, dst);
    }

    /// <summary>The map in a rectangle of the screen, from tile (vx, vy) at the top-left: the tiles, the overlay, the life of the city, the sprites and the cursor.</summary>
    private void DrawMap(DrawingContext dc, Rect r, double vx, double vy, int zoom, bool cursor)
    {
        var w = Engine.World;
        double size = GameEngine.TileSize * zoom;
        using var clip = dc.PushClip(r);
        dc.FillRectangle(Brush(0xFF14284A), r);
        if (w.Shake > 0)
        {
            vx += Math.Sin(_clock * 53) * 0.25 / zoom;
            vy += Math.Cos(_clock * 41) * 0.18 / zoom;
        }
        using (dc.PushRenderOptions(Crisp)) DrawPart(dc, Painter.Map, TileArt.T, r, vx, vy, size);
        var o = Engine.Overlay;
        if (o != Overlay.None && Engine.State == GameState.Playing)
        {
            Painter.UpdateShade(w, o);
            using (dc.PushRenderOptions(o is Overlay.Power or Overlay.Traffic ? Crisp : Smooth)) DrawPart(dc, Painter.Shade, 1, r, vx, vy, size);
        }
        Point At(double x, double y) => new(r.X + (x - vx) * size, r.Y + (y - vy) * size);
        DrawLife(dc, w, r, vx, vy, size, At);
        foreach (var s in w.Sprites) DrawSprite(dc, s, At, size);
        if (cursor) DrawCursor(dc, w, At, size);
    }

    /// <summary>What moves in the city: traffic on the roads, trains on the rails, smoke from the chimneys, flames, and the bolt over a zone with no power.</summary>
    private void DrawLife(DrawingContext dc, World w, Rect r, double vx, double vy, double size, Func<double, double, Point> at)
    {
        int x0 = Math.Max(0, (int)Math.Floor(vx)), y0 = Math.Max(0, (int)Math.Floor(vy));
        int x1 = Math.Min(Field.W - 1, (int)Math.Ceiling(vx + r.Width / size)), y1 = Math.Min(Field.H - 1, (int)Math.Ceiling(vy + r.Height / size));
        double px = size / 16;
        uint[] carColours = [0xFFE03A30, 0xFF3A6AE0, 0xFFF0F0F0, 0xFF2A2A2A, 0xFFF0C030, 0xFF40B060, 0xFFB050C0];
        for (int x = x0; x <= x1; x++)
        for (int y = y0; y <= y1; y++)
        {
            var net = w.Net[x, y];
            int traffic = w.Traffic[x, y];
            if ((net & Net.Road) != 0 && traffic > 12 && (net & Net.Rail) == 0)
            {
                bool across = w.Has(x - 1, y, Net.Road) || w.Has(x + 1, y, Net.Road) || !(w.Has(x, y - 1, Net.Road) || w.Has(x, y + 1, Net.Road));
                int cars = Math.Min(4, 1 + traffic / 55);
                double speed = Math.Max(0.15, 1.1 - traffic / 260.0);
                for (int k = 0; k < cars; k++)
                {
                    uint h = Hash(x * 4 + k, y, 7);
                    double t = (_clock * speed * (0.8 + h % 7 * 0.05) + h % 100 / 100.0) % 1;
                    bool back = k % 2 == 1;
                    if (back) t = 1 - t;
                    uint c = carColours[h % carColours.Length];
                    var p = across ? at(x + t, y + (back ? 0.34 : 0.6)) : at(x + (back ? 0.6 : 0.34), y + t);
                    var rect = across ? new Rect(p.X - 2.2 * px, p.Y - 1.2 * px, 4.4 * px, 2.4 * px) : new Rect(p.X - 1.2 * px, p.Y - 2.2 * px, 2.4 * px, 4.4 * px);
                    dc.FillRectangle(Brush(0x60000000), rect.Translate(new Vector(px * 0.8, px * 0.8)));
                    dc.FillRectangle(Brush(c), rect);
                }
            }
            if ((net & Net.Rail) != 0 && traffic > 10 && (net & Net.Road) == 0)
            {
                bool across = w.Has(x - 1, y, Net.Rail) || w.Has(x + 1, y, Net.Rail);
                // A train of three cars runs along each line now and then.
                int along = across ? x : y, line = across ? y : x;
                double head = (_clock * 5 + line * 7.3) % 40, d = head - along % 40;
                if (d is >= 0 and < 3)
                {
                    var p = at(x + 0.5, y + 0.5);
                    var rect = across ? new Rect(p.X - 7 * px, p.Y - 2.5 * px, 14 * px, 5 * px) : new Rect(p.X - 2.5 * px, p.Y - 7 * px, 5 * px, 14 * px);
                    dc.FillRectangle(Brush(0x60000000), rect.Translate(new Vector(px, px)));
                    dc.FillRectangle(Brush(d < 1 ? 0xFF2A4A8A : 0xFF8A2A2A), rect, (float)px);
                    dc.FillRectangle(Brush(0xFFD8C8A0), across ? rect.Deflate(new Thickness(px, 1.5 * px)) : rect.Deflate(new Thickness(1.5 * px, px)));
                }
            }
            if (w.Ground[x, y] == Ground.Fire) DrawFlames(dc, at(x + 0.5, y + 0.85), size, x * 31 + y);
        }
        // Smoke from the chimneys, the bolt over a dark zone.
        foreach (var z in w.Zones)
        {
            if (z.X + z.Size < x0 || z.X > x1 + 1 || z.Y + z.Size < y0 || z.Y > y1 + 1) continue;
            foreach (var (cx, cy) in Chimneys(z)) DrawSmoke(dc, at(z.X + cx / 16.0, z.Y + cy / 16.0), size, z.X * 7 + cx);
            if (!z.Powered && (z.Grows || z.Kind is ZoneKind.Police or ZoneKind.FireStation) && (int)(_clock * 2.5) % 2 == 0) DrawBolt(dc, at(z.Centre.X, z.Centre.Y), size);
        }
    }

    /// <summary>Where a building's chimneys are, in pixels of its picture.</summary>
    private static IEnumerable<(int X, int Y)> Chimneys(Zone z)
    {
        switch (z.Kind)
        {
            case ZoneKind.Industrial when z.Level == 2: yield return (38, 6); break;
            case ZoneKind.Industrial when z.Level == 3: yield return (37, 2); yield return (43, 2); break;
            case ZoneKind.Industrial when z.Level == 4: yield return (44, 2); yield return (19, 8); break;
            case ZoneKind.Coal when z.Powered: yield return (53, 4); yield return (59, 10); break;
            case ZoneKind.Nuclear: yield return (16, 17); yield return (16, 46); break;
        }
    }

    private void DrawSmoke(DrawingContext dc, Point p, double size, int seed)
    {
        double px = size / 16;
        for (int k = 0; k < 5; k++)
        {
            double t = (_clock * 0.45 + k / 5.0 + seed % 10 * 0.07) % 1;
            double x = p.X + t * 9 * px + Math.Sin(_clock * 2 + k + seed) * px, y = p.Y - t * 14 * px;
            dc.FillEllipse(x, y, (1.6 + t * 4.2) * px, Alpha(0xFFE8E8E4, 0.55 * (1 - t)));
        }
    }

    private void DrawFlames(DrawingContext dc, Point p, double size, int seed)
    {
        double px = size / 16;
        for (int k = 0; k < 3; k++)
        {
            double flick = Math.Sin(_clock * 13 + k * 2.1 + seed) * 0.5 + 0.5;
            double x = p.X + (k - 1) * 4.5 * px, h = (7 + flick * 5 + k % 2 * 2) * px;
            Poly(dc, 0xFFF06A20, null, 0, new Point(x - 3 * px, p.Y), new Point(x - 1 * px, p.Y - h * 0.6), new Point(x + 0.5 * px, p.Y - h), new Point(x + 3 * px, p.Y));
            Poly(dc, 0xFFFFD84A, null, 0, new Point(x - 1.5 * px, p.Y), new Point(x, p.Y - h * 0.55), new Point(x + 1.5 * px, p.Y));
        }
        double t = (_clock * 0.7 + seed % 7 * 0.13) % 1;
        dc.FillEllipse(p.X + t * 4 * px, p.Y - 14 * px - t * 12 * px, (2 + t * 4) * px, Alpha(0xFF3A3434, 0.6 * (1 - t)));
    }

    private static void DrawBolt(DrawingContext dc, Point c, double size)
    {
        double k = size / 16;
        dc.FillEllipse(c.X, c.Y, 7 * k, 0xC0201810);
        Poly(dc, 0xFFFFE030, 0xFF201810, 1, new Point(c.X + 1 * k, c.Y - 6 * k), new Point(c.X - 4 * k, c.Y + 1 * k), new Point(c.X - 0.5 * k, c.Y + 1 * k),
            new Point(c.X - 1.5 * k, c.Y + 6 * k), new Point(c.X + 4 * k, c.Y - 1 * k), new Point(c.X + 0.5 * k, c.Y - 1 * k));
    }

    private void DrawSprite(DrawingContext dc, Sprite s, Func<double, double, Point> at, double size)
    {
        var p = at(s.X, s.Y);
        double k = size / 16;
        switch (s.Kind)
        {
            case SpriteKind.Tornado:
            {
                dc.DrawEllipse(Brush(0x50000000), null, new Point(p.X + 6 * k, p.Y + 3 * k), 12 * k, 5 * k);
                for (int i = 0; i < 14; i++)
                {
                    double t = i / 13.0, wob = Math.Sin(_clock * 9 + i * 0.7) * (2 + t * 6) * k;
                    dc.DrawEllipse(Brush(Alpha(Pix.Mix(0xFF6A6460, 0xFFB8B4AC, t), 0.75)), Pen(Alpha(0xFF4A4440, 0.6), 1),
                        new Point(p.X + wob + t * 10 * k, p.Y - t * 60 * k), (3 + t * 15) * k, (1.5 + t * 4) * k);
                }
                for (int i = 0; i < 6; i++)
                {
                    double a = _clock * 7 + i;
                    dc.FillEllipse(p.X + Math.Cos(a) * 12 * k, p.Y - 4 * k + Math.Sin(a) * 4 * k, 1.6 * k, 0xFF6A5A48);
                }
                break;
            }
            case SpriteKind.Monster:
                DrawMonster(dc, p, k * 1.5, s.Dx < 0, s.Age);
                break;
            case SpriteKind.Plane:
            {
                double a = Math.Atan2(s.Dy, s.Dx), lift = Math.Max(0, s.Life) * 20 * k;
                var c = new Point(p.X, p.Y - lift);
                dc.DrawEllipse(Brush(0x50000000), null, p, 10 * k, 4 * k);
                using (dc.PushTransform(Matrix.CreateRotation(a) * Matrix.CreateTranslation(c.X, c.Y)))
                {
                    Poly(dc, 0xFFF0F0F0, 0xFF4A4A4A, 1, new Point(-14 * k, -2 * k), new Point(12 * k, -2 * k), new Point(16 * k, 0), new Point(12 * k, 2 * k), new Point(-14 * k, 2 * k));
                    Poly(dc, 0xFFD8D8D8, 0xFF4A4A4A, 1, new Point(-2 * k, 0), new Point(-6 * k, -14 * k), new Point(-2 * k, -14 * k), new Point(4 * k, 0), new Point(-2 * k, 14 * k), new Point(-6 * k, 14 * k));
                    Poly(dc, 0xFF3060B0, 0xFF4A4A4A, 1, new Point(-14 * k, 0), new Point(-17 * k, -6 * k), new Point(-15 * k, -6 * k), new Point(-11 * k, 0), new Point(-15 * k, 6 * k), new Point(-17 * k, 6 * k));
                }
                if ((int)(_clock * 8) % 2 == 0) dc.FillEllipse(c.X - Math.Cos(a) * 16 * k, c.Y - Math.Sin(a) * 16 * k, 3 * k, 0xC0606060);
                break;
            }
            case SpriteKind.Explosion:
            {
                double t = Math.Clamp(s.Age / 1.5, 0, 1);
                for (int i = 0; i < 3; i++)
                {
                    double r = (8 + t * 40 - i * 9) * k;
                    if (r > 0) dc.FillEllipse(p.X, p.Y, r, Alpha(i == 0 ? 0xFFFF6020 : i == 1 ? 0xFFFFB030 : 0xFFFFF0A0, (1 - t) * 0.85));
                }
                break;
            }
        }
    }

    /// <summary>The monster: a great green lizard with a row of spines and a tail, stamping along.</summary>
    private static void DrawMonster(DrawingContext dc, Point p, double k, bool left, double age)
    {
        double f = left ? -1 : 1, step = Math.Sin(age * 6) * 3 * k;
        Point P(double x, double y) => new(p.X + f * x * k, p.Y + y * k);
        dc.DrawEllipse(Brush(0x60000000), null, new Point(p.X + 4 * k, p.Y + 2 * k), 24 * k, 7 * k);
        // Tail, legs, body, arms, head.
        Poly(dc, 0xFF3A7A3A, 0xFF1A3A1A, 1.5, P(-8, -14), P(-34, -4 + step * 0.3), P(-38, 0), P(-30, -2), P(-6, -6));
        dc.FillRectangle(Brush(0xFF2E662E), new Rect(Math.Min(P(-8, 0).X, P(-2, 0).X), p.Y - 12 * k + step, 6 * k, 12 * k - step));
        dc.FillRectangle(Brush(0xFF2E662E), new Rect(Math.Min(P(4, 0).X, P(10, 0).X), p.Y - 12 * k - step, 6 * k, 12 * k + step));
        dc.DrawEllipse(Brush(0xFF3E8A3E), Pen(0xFF1A3A1A, 1.5), P(0, -24), 14 * k, 17 * k);
        dc.DrawEllipse(Brush(0xFF8AC070), null, P(4, -22), 6 * k, 11 * k);
        for (int i = 0; i < 5; i++)
        {
            var b = P(-10 + i * 3, -36 + i * 2.5);
            Poly(dc, 0xFFC8D850, 0xFF1A3A1A, 1, new Point(b.X - 3 * k, b.Y + 2 * k), new Point(b.X - f * 4 * k, b.Y - 6 * k), new Point(b.X + 3 * k, b.Y));
        }
        dc.DrawLine(Pen(0xFF2E662E, 3 * k), P(8, -28), P(18, -24 - step * 0.5));
        dc.DrawEllipse(Brush(0xFF3E8A3E), Pen(0xFF1A3A1A, 1.5), P(10, -42), 9 * k, 7 * k);
        Poly(dc, 0xFF3E8A3E, 0xFF1A3A1A, 1.5, P(14, -46), P(24, -42), P(24, -36), P(12, -37));
        dc.FillEllipse(P(13, -45).X, P(13, -45).Y, 1.8 * k, 0xFFFFE040);
        if ((int)(age * 2) % 3 == 0) Poly(dc, 0xC0FF8030, null, 0, P(24, -40), P(36, -46), P(38, -36));
    }

    private void DrawCursor(DrawingContext dc, World w, Func<double, double, Point> at, double size)
    {
        if (_hover is not { } h || Engine.Panel is not (Panel.None or Panel.Query)) return;
        var tool = Engine.Tool;
        int s = Sim.KindOf(tool) is { } k ? Sim.SizeOf(k) : 1;
        var (x0, y0) = s == 1 ? (h.X, h.Y) : Sim.Corner(s, h.X, h.Y);
        bool ok = Sim.KindOf(tool) is { } kind ? Sim.CanBuild(w, kind, h.X, h.Y) : true;
        uint c = tool == Tool.Query ? Cyan : tool == Tool.Bulldozer ? Yellow : ok ? 0xFFFFFFFF : Red;
        var a = at(x0, y0);
        var rect = new Rect(a.X, a.Y, s * size, s * size);
        dc.FillRectangle(Brush(Alpha(c, 0.18)), rect);
        dc.DrawRectangle(null, Pen(0xC0000000, 3), rect);
        dc.DrawRectangle(null, Pen(c, 1.5), rect);
        int cost = Sim.Cost(tool);
        if (cost > 0) Text(dc, $"${cost:N0}", new Point(rect.Right + 4, rect.Y + 6), 18, c);
    }

    // ───────────────────────────── the top bar ─────────────────────────────

    private void DrawTop(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        Bevel(dc, new Rect(0, 0, Field.Width, GameEngine.ViewTop), 0xFF141A28);
        Text(dc, w.Name, new Point(8, 17), 26, Yellow);
        Text(dc, w.Date, new Point(176, 17), 24, White);
        Text(dc, $"${w.Funds:N0}", new Point(262, 17), 24, w.Funds < 0 ? Red : Green);
        Text(dc, $"POP {w.Residents:N0}", new Point(380, 17), 24, White);
        Text(dc, w.Class, new Point(502, 17), 20, Cyan);
        // The speeds.
        string[] speeds = ["II", ">", ">>", ">>>"];
        for (int i = 0; i < 4; i++)
        {
            var sp = (Speed)i;
            var r = new Rect(594 + i * 34, 5, 31, 24);
            bool on = g.Speed == sp;
            Button(dc, r, speeds[i], () => { g.SetSpeed(sp); if (sp != Speed.Paused) g.SetUnpaused(sp); }, on, 20, g.State == GameState.Playing);
        }
        bool play = g.State == GameState.Playing;
        Button(dc, new Rect(734, 5, 70, 24), "BUDGET", () => g.Open(Panel.Budget), g.Panel == Panel.Budget, 19, play);
        Button(dc, new Rect(808, 5, 52, 24), "EVAL", () => g.Open(Panel.Evaluation), g.Panel == Panel.Evaluation, 19, play);
        Button(dc, new Rect(864, 5, 86, 24), "DISASTERS", () => g.Open(Panel.Disasters), g.Panel == Panel.Disasters, 19, play);
        Button(dc, new Rect(954, 5, 64, 24), "FILE", () => g.Open(Panel.Menu), g.Panel == Panel.Menu, 19, play);
        if (g.Speed == Speed.Paused && g.State == GameState.Playing && (int)(_clock * 2) % 2 == 0)
            Text(dc, "PAUSED", new Point(MapRect.Center.X, MapRect.Y + 20), 30, Yellow, center: true);
    }

    private void DrawStatus(DrawingContext dc)
    {
        var g = Engine;
        var r = new Rect(GameEngine.ViewLeft, GameEngine.ViewTop + GameEngine.ViewHeight, GameEngine.ViewWidth, 24);
        Bevel(dc, r, 0xFF141A28);
        if (g.Message is { } m) Text(dc, m, new Point(r.X + 10, r.Center.Y), 21, Yellow);
        string right = g.Overlay != Overlay.None ? $"MAP: {GameEngine.OverlayName(g.Overlay)}" : "";
        if (_hover is { } h && g.State == GameState.Playing) right += $"  {h.X},{h.Y}";
        Text(dc, right.Trim(), new Point(r.Right - 8, r.Center.Y), 19, Dim, right: true);
    }

    // ───────────────────────────── the tools, the demand and the mini-map ─────────────────────────────

    public static Rect ToolRect(Tool t) => new(3 + (int)t % 4 * 41, 40 + (int)t / 4 * 41, 39, 39);
    private static readonly Rect MiniRect = new(6, 378, 156, 130);

    private void DrawPalette(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        Bevel(dc, new Rect(0, GameEngine.ViewTop, GameEngine.ViewLeft, Field.Height - GameEngine.ViewTop));
        bool play = g.State == GameState.Playing;
        foreach (var t in Enum.GetValues<Tool>())
        {
            var r = ToolRect(t);
            bool on = g.Tool == t && play;
            dc.FillRectangle(Brush(on ? 0xFF6A5A24 : 0xFF2A3650), r, 4);
            dc.DrawRectangle(null, Pen(on ? Yellow : 0xFF55688E, on ? 2 : 1), r, 4, 4);
            DrawToolIcon(dc, t, r.Center);
            if (play) { var tt = t; Hit(r, () => g.Select(tt)); }
        }
        var tool = g.Tool;
        Text(dc, GameEngine.ToolName(tool), new Point(84, 216), 22, Yellow, center: true);
        int cost = Sim.Cost(tool);
        Text(dc, cost > 0 ? $"${cost:N0}   KEY {KeyFor(tool)}" : $"KEY {KeyFor(tool)}", new Point(84, 236), 18, Dim, center: true);
        // The demand: residential, commercial, industrial, up for wanted and down for not.
        var box = new Rect(6, 250, 156, 118);
        dc.FillRectangle(Brush(0xFF101622), box, 4);
        Text(dc, "DEMAND", new Point(box.Center.X, box.Y + 11), 18, Dim, center: true);
        double mid = box.Y + 66;
        dc.DrawLine(Pen(0xFF5A6A8A, 1), new Point(box.X + 16, mid), new Point(box.Right - 16, mid));
        (string L, double V, uint C)[] bars = [("R", w.DemandR, ResColour), ("C", w.DemandC, ComColour), ("I", w.DemandI, IndColour)];
        for (int i = 0; i < 3; i++)
        {
            var (label, v, c) = bars[i];
            double x = box.X + 32 + i * 46, hgt = Math.Clamp(v, -1, 1) * 40;
            var bar = hgt >= 0 ? new Rect(x - 10, mid - hgt, 20, hgt) : new Rect(x - 10, mid, 20, -hgt);
            dc.FillRectangle(Brush(c), bar);
            dc.DrawRectangle(null, Pen(Pix.Scale(c, 0.5), 1), bar);
            Text(dc, label, new Point(x, box.Bottom - 8), 18, c, center: true);
        }
        // The mini-map, with the view marked on it.
        Painter.UpdateMini(w, g.Overlay);
        dc.FillRectangle(Brush(0xFF000000), MiniRect.Inflate(2));
        using (dc.PushRenderOptions(Crisp)) dc.DrawImage(Painter.Mini, new Rect(0, 0, Field.W, Field.H), MiniRect);
        double sx = MiniRect.Width / Field.W, sy = MiniRect.Height / Field.H;
        var view = new Rect(MiniRect.X + g.ViewX * sx, MiniRect.Y + g.ViewY * sy, g.TilesAcross * sx, g.TilesDown * sy).Intersect(MiniRect);
        dc.DrawRectangle(null, Pen(0xFFFFFFFF, 1.5), view);
        // The maps.
        var overlays = Enum.GetValues<Overlay>();
        string[] names = ["CITY", "PEOPLE", "TRAFFIC", "POLLUTE", "CRIME", "VALUE", "POWER", "POLICE", "FIRE"];
        for (int i = 0; i < overlays.Length; i++)
        {
            var o = overlays[i];
            var r = new Rect(4 + i % 3 * 54, 514 + i / 3 * 25, 51, 22);
            Button(dc, r, names[i], () => g.Overlay = o, g.Overlay == o, 17, play);
        }
        // A few numbers.
        double y = 600;
        void Line(string a, string b, uint c)
        {
            Text(dc, a, new Point(8, y), 18, Dim);
            Text(dc, b, new Point(160, y), 18, c, right: true);
            y += 19;
        }
        Line("POWER", $"{w.PowerUsed:N0}/{w.PowerCapacity:N0}", w.PowerUsed >= w.PowerCapacity && w.PowerCapacity > 0 ? Red : White);
        Line("JOBS", $"{w.Jobs:N0}", White);
        Line("TAX RATE", $"{w.TaxRate}%", White);
        Line("SCORE", $"{w.Score}", White);
        Line("APPROVAL", $"{w.Approval}%", w.Approval >= 50 ? Green : Red);
        if (w.Scenario >= 0)
        {
            Text(dc, Scenarios.All[w.Scenario].Trouble, new Point(84, y + 4), 17, Yellow, center: true);
            var s = Scenarios.All[w.Scenario];
            int left = Math.Max(0, s.Years * 12 - (w.Month - w.StartMonth));
            Text(dc, $"{left / 12}Y {left % 12}M TO GO", new Point(84, y + 22), 17, Cyan, center: true);
        }
    }

    private static void DrawToolIcon(DrawingContext dc, Tool t, Point c)
    {
        double x = c.X, y = c.Y;
        void Letter(string s, uint col)
        {
            dc.FillRectangle(Brush(col), new Rect(x - 14, y - 14, 28, 28), 3);
            dc.DrawRectangle(null, Pen(Pix.Scale(col, 0.5), 1.5), new Rect(x - 14, y - 14, 28, 28), 3, 3);
            Text(dc, s, new Point(x, y), 30, 0xFF141A28, center: true, shadow: false);
        }
        switch (t)
        {
            case Tool.Query:
                dc.DrawEllipse(Brush(0x4080C0FF), Pen(0xFFE8E8F0, 3), new Point(x - 3, y - 3), 8, 8);
                dc.DrawLine(Pen(0xFFC8A060, 4), new Point(x + 3, y + 3), new Point(x + 11, y + 11));
                break;
            case Tool.Bulldozer:
                dc.FillRectangle(Brush(0xFFE8B020), new Rect(x - 9, y - 7, 16, 9), 2);
                dc.FillRectangle(Brush(0xFF3A3A3A), new Rect(x - 4, y - 12, 7, 6));
                dc.FillRectangle(Brush(0xFF2A2A2A), new Rect(x - 11, y + 2, 20, 6), 3);
                dc.FillRectangle(Brush(0xFFB8B8B8), new Rect(x + 8, y - 8, 4, 15));
                break;
            case Tool.Road:
                dc.FillRectangle(Brush(0xFF55585F), new Rect(x - 16, y - 7, 32, 14));
                dc.DrawLine(Pen(0xFF9A9CA2, 1), new Point(x - 16, y - 7), new Point(x + 16, y - 7));
                dc.DrawLine(Pen(0xFF9A9CA2, 1), new Point(x - 16, y + 7), new Point(x + 16, y + 7));
                for (int i = -14; i < 16; i += 9) dc.FillRectangle(Brush(0xFFE8D060), new Rect(x + i, y - 1, 5, 2));
                break;
            case Tool.Rail:
                dc.FillRectangle(Brush(0xFF8A7C6C), new Rect(x - 16, y - 6, 32, 12));
                for (int i = -15; i < 16; i += 4) dc.FillRectangle(Brush(0xFF5A4634), new Rect(x + i, y - 6, 2, 12));
                dc.DrawLine(Pen(0xFFD8DCE4, 1.5), new Point(x - 16, y - 3), new Point(x + 16, y - 3));
                dc.DrawLine(Pen(0xFFD8DCE4, 1.5), new Point(x - 16, y + 3), new Point(x + 16, y + 3));
                break;
            case Tool.Wire:
                dc.DrawLine(Pen(0xFF8A6A3A, 3), new Point(x, y - 14), new Point(x, y + 14));
                dc.DrawLine(Pen(0xFF8A6A3A, 3), new Point(x - 9, y - 10), new Point(x + 9, y - 10));
                dc.DrawLine(Pen(0xFF101010, 1), new Point(x - 16, y - 6), new Point(x - 9, y - 10));
                dc.DrawLine(Pen(0xFF101010, 1), new Point(x + 9, y - 10), new Point(x + 16, y - 6));
                Poly(dc, 0xFFFFE030, null, 0, new Point(x + 10, y - 2), new Point(x + 5, y + 5), new Point(x + 8, y + 5), new Point(x + 6, y + 12), new Point(x + 12, y + 3), new Point(x + 9, y + 3));
                break;
            case Tool.Park:
                dc.FillRectangle(Brush(0xFF78B250), new Rect(x - 15, y - 15, 30, 30), 3);
                dc.FillEllipse(x - 4, y - 3, 8, 0xFF2F6E2C);
                dc.FillEllipse(x - 6, y - 5, 4, 0xFF5AA448);
                dc.FillEllipse(x + 8, y + 7, 5, 0xFF2F6E2C);
                break;
            case Tool.Residential: Letter("R", ResColour); break;
            case Tool.Commercial: Letter("C", ComColour); break;
            case Tool.Industrial: Letter("I", IndColour); break;
            case Tool.Police:
                dc.FillRectangle(Brush(0xFF3A5A9A), new Rect(x - 14, y - 14, 28, 28), 3);
                Star(dc, x, y, 11, 0xFFF0D040);
                break;
            case Tool.FireStation:
                dc.FillRectangle(Brush(0xFFB83A2A), new Rect(x - 14, y - 14, 28, 28), 3);
                Poly(dc, 0xFFFFB030, null, 0, new Point(x - 7, y + 10), new Point(x - 8, y - 2), new Point(x - 3, y + 2), new Point(x, y - 11), new Point(x + 4, y + 1), new Point(x + 8, y - 4), new Point(x + 7, y + 10));
                Poly(dc, 0xFFFFF0A0, null, 0, new Point(x - 3, y + 10), new Point(x, y + 1), new Point(x + 3, y + 10));
                break;
            case Tool.Stadium:
                dc.DrawEllipse(Brush(0xFF8E8E98), null, c, 16, 13);
                dc.DrawEllipse(Brush(0xFF58A040), Pen(0xFFF0F0F0, 1), c, 10, 7);
                break;
            case Tool.Seaport:
                dc.FillRectangle(Brush(0xFF2F62A6), new Rect(x - 15, y + 4, 30, 11), 2);
                dc.FillRectangle(Brush(0xFFC84030), new Rect(x - 12, y - 4, 10, 6));
                dc.FillRectangle(Brush(0xFF3060B0), new Rect(x - 1, y - 4, 10, 6));
                dc.DrawLine(Pen(0xFFF0A030, 2.5), new Point(x + 11, y + 4), new Point(x + 11, y - 14));
                dc.DrawLine(Pen(0xFFF0A030, 2.5), new Point(x + 11, y - 13), new Point(x - 8, y - 13));
                break;
            case Tool.Airport:
                Poly(dc, 0xFFF0F0F0, 0xFF5A5A5A, 1, new Point(x - 2, y - 15), new Point(x + 2, y - 15), new Point(x + 2, y - 4), new Point(x + 14, y + 2), new Point(x + 14, y + 5), new Point(x + 2, y + 2),
                    new Point(x + 2, y + 10), new Point(x + 6, y + 14), new Point(x - 6, y + 14), new Point(x - 2, y + 10), new Point(x - 2, y + 2), new Point(x - 14, y + 5), new Point(x - 14, y + 2), new Point(x - 2, y - 4));
                break;
            case Tool.Coal:
                dc.FillRectangle(Brush(0xFF9AA0A8), new Rect(x - 15, y - 2, 20, 14));
                dc.FillRectangle(Brush(0xFFA08070), new Rect(x + 7, y - 14, 5, 26));
                dc.FillRectangle(Brush(0xFFD04030), new Rect(x + 7, y - 11, 5, 2));
                dc.FillEllipse(x + 12, y - 15, 4, 0xC0D8D8D8);
                dc.FillEllipse(x + 6, y - 15, 3, 0xC0D8D8D8);
                break;
            case Tool.Nuclear:
                dc.DrawEllipse(Brush(0xFFD6D6D0), Pen(0xFF8A8A84, 1), new Point(x - 4, y), 11, 11);
                dc.DrawEllipse(Brush(0xFF4A4A4C), null, new Point(x - 4, y), 6, 6);
                dc.FillEllipse(x + 10, y + 8, 5, 0xFFE4E4E4);
                dc.FillEllipse(x - 4, y - 13, 5, 0xB0F0F0F0);
                break;
        }
    }

    private static void Star(DrawingContext dc, double x, double y, double r, uint c)
    {
        var pts = new Point[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : r * 0.45;
            pts[i] = new Point(x + Math.Cos(a) * rr, y + Math.Sin(a) * rr);
        }
        Poly(dc, c, null, 0, pts);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
