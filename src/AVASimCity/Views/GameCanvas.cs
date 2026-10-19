using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVASimCity.Engine;
using Panel = AVASimCity.Engine.Panel;

namespace AVASimCity.Views;

/// <summary>
/// The screen: a fixed 1024×768 picture scaled to fit the window: the map in the middle, the tools, the demand and
/// the mini-map down the left, the city's date, money and people along the top, and the news along the bottom.
/// Runs the engine from a 60 Hz timer, and turns the mouse and the keys into the engine's calls.
/// </summary>
public sealed partial class GameCanvas : Control
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;

    private readonly DispatcherTimer? _timer;
    private DateTime _last = DateTime.Now;
    private double _clock;
    private readonly List<(Rect R, Action Do)> _hits = [];
    private (int X, int Y)? _hover;
    private Point? _panFrom;
    private bool _drawing, _miniDrag;

    public GameEngine Engine { get; }
    public SoundManager Sound { get; }
    public InputHandler Input { get; } = new();
    public MapPainter Painter { get; } = new();

    public GameCanvas(bool silent = false) : this(new GameEngine(), new SoundManager(silent), startTimer: true) { }

    /// <summary>For tests: an engine of your own, and no timer (call <see cref="Tick"/>).</summary>
    public GameCanvas(GameEngine engine, SoundManager sound, bool startTimer)
    {
        Engine = engine;
        Sound = sound;
        Engine.Sound += Sound.Play;
        Focusable = true;
        ClipToBounds = true;
        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer?.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        Sound.Dispose();
        Painter.Dispose();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Input.Clear();
    }

    /// <summary>One frame: read the keys, run the game, redraw.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        _clock += dt;
        Engine.Update(dt, Input.Player, Input.Start);
        InvalidateVisual();
    }

    /// <summary>For tests: moves the drawing clock (traffic, smoke, flames) without running the game.</summary>
    public void Advance(double dt)
    {
        _clock += dt;
        InvalidateVisual();
    }

    // ───────────────────────────── keys ─────────────────────────────

    private static readonly Dictionary<Key, Tool> ToolKeys = new()
    {
        [Key.Q] = Tool.Query, [Key.B] = Tool.Bulldozer, [Key.R] = Tool.Road, [Key.T] = Tool.Rail, [Key.L] = Tool.Wire,
        [Key.K] = Tool.Park, [Key.D1] = Tool.Residential, [Key.D2] = Tool.Commercial, [Key.D3] = Tool.Industrial,
        [Key.D4] = Tool.Police, [Key.D5] = Tool.FireStation, [Key.D6] = Tool.Stadium, [Key.D7] = Tool.Seaport,
        [Key.D8] = Tool.Airport, [Key.D9] = Tool.Coal, [Key.D0] = Tool.Nuclear,
    };

    public static string KeyFor(Tool t) => ToolKeys.FirstOrDefault(kv => kv.Value == t).Key switch
    {
        >= Key.D0 and <= Key.D9 and var k => ((int)(k - Key.D0)).ToString(),
        var k => k.ToString(),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var g = Engine;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.S && ctrl)
        {
            SaveScreenshot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"AVASimCity_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"));
            return;
        }
        // The new city's name is typed into its window.
        if (g.Panel == Panel.NewCity)
        {
            switch (e.Key)
            {
                case Key.Escape: g.Close(); return;
                case Key.Enter: g.StartCity(); return;
                case Key.Back: if (g.NewName.Length > 0) g.NewName = g.NewName[..^1]; return;
                case Key.Space: if (g.NewName.Length < 14) g.NewName += " "; return;
                case >= Key.A and <= Key.Z: if (g.NewName.Length < 14) g.NewName += (char)('A' + (e.Key - Key.A)); return;
            }
            return;
        }
        if (g.State == GameState.Attract) { g.ReturnToTitle(); return; }
        switch (e.Key)
        {
            case Key.M:
                Sound.IsMuted = !Sound.IsMuted;
                return;
            case Key.Escape:
                if (g.Panel != Panel.None) g.Close();
                else if (g.State == GameState.Playing) g.Open(Panel.Menu);
                else (TopLevel.GetTopLevel(this) as Window)?.Close();
                return;
        }
        if (g.State == GameState.Title)
        {
            Input.OnKeyDown(e.Key);
            return;
        }
        if (g.Panel is Panel.Load or Panel.Save && e.Key is >= Key.D1 and <= Key.D6)
        {
            int slot = e.Key - Key.D1;
            if (g.Panel == Panel.Save) g.SaveCity(slot);
            else g.LoadCity(slot);
            return;
        }
        if (g.Panel is not (Panel.None or Panel.Query))
        {
            if (e.Key == Key.Enter) g.Close();
            return;
        }
        if (!ctrl && ToolKeys.TryGetValue(e.Key, out var tool)) { g.Select(tool); return; }
        switch (e.Key)
        {
            case Key.P or Key.Pause or Key.Space: g.TogglePause(); return;
            case Key.OemPlus or Key.Add: g.Faster(); return;
            case Key.OemMinus or Key.Subtract: g.Slower(); return;
            case Key.O: g.Overlay = (Overlay)(((int)g.Overlay + 1) % Enum.GetValues<Overlay>().Length); return;
            case Key.Z: g.SetZoom(g.Zoom == 1 ? 2 : 1); return;
            case Key.F2: g.Open(Panel.Budget); return;
            case Key.F3: g.Open(Panel.Evaluation); return;
            case Key.F4: g.Open(Panel.Disasters); return;
            case Key.F5: g.Open(Panel.Save); return;
            case Key.F6: g.Open(Panel.Load); return;
        }
        Input.OnKeyDown(e.Key);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Input.OnKeyUp(e.Key);
        e.Handled = true;
    }

    // ───────────────────────────── the mouse ─────────────────────────────

    /// <summary>A point in the window to the fixed screen.</summary>
    private Point ToScreen(Point p)
    {
        var (s, ox, oy) = Fit();
        return s <= 0 ? new Point(-1, -1) : new Point((p.X - ox) / s, (p.Y - oy) / s);
    }

    private static readonly Rect MapRect = new(GameEngine.ViewLeft, GameEngine.ViewTop, GameEngine.ViewWidth, GameEngine.ViewHeight);

    /// <summary>The tile under a point on the screen, if it's over the map.</summary>
    public (int X, int Y)? TileAt(Point at)
    {
        if (Engine.State != GameState.Playing || !MapRect.Contains(at)) return null;
        double size = GameEngine.TileSize * Engine.Zoom;
        int x = (int)Math.Floor(Engine.ViewX + (at.X - MapRect.X) / size), y = (int)Math.Floor(Engine.ViewY + (at.Y - MapRect.Y) / size);
        return Field.In(x, y) ? (x, y) : null;
    }

    /// <summary>The button (or other place that answers a click) under a point, topmost first.</summary>
    private Action? HitAt(Point at)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].R.Contains(at)) return _hits[i].Do;
        return null;
    }

    /// <summary>A click on the screen at a point: a button, the mini-map or the map. For tests too.</summary>
    public void Click(Point at, bool right = false)
    {
        if (Engine.State == GameState.Attract) { Engine.ReturnToTitle(); return; }
        if (right)
        {
            if (Engine.Panel != Panel.None) Engine.Close();
            else _panFrom = at;
            return;
        }
        if (HitAt(at) is { } act) { act(); return; }
        if (Engine.Panel is not (Panel.None or Panel.Query)) return;
        if (MiniRect.Contains(at)) { MiniJump(at); _miniDrag = true; return; }
        if (TileAt(at) is { } t)
        {
            if (Engine.Panel == Panel.Query && Engine.Tool != Tool.Query) Engine.Close();
            Engine.Press(t.X, t.Y);
            _drawing = true;
        }
    }

    private void MiniJump(Point at)
    {
        double x = (at.X - MiniRect.X) / MiniRect.Width * Field.W, y = (at.Y - MiniRect.Y) / MiniRect.Height * Field.H;
        Engine.CenterOn(x, y);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = ToScreen(e.GetPosition(this));
        _hover = TileAt(at);
        if (_panFrom is { } from)
        {
            double size = GameEngine.TileSize * Engine.Zoom;
            Engine.Scroll((from.X - at.X) / size, (from.Y - at.Y) / size);
            _panFrom = at;
        }
        else if (_miniDrag) MiniJump(at);
        else if (_drawing && _hover is { } t) Engine.Drag(t.X, t.Y);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var at = ToScreen(e.GetPosition(this));
        var props = e.GetCurrentPoint(this).Properties;
        Click(at, right: props.IsRightButtonPressed || props.IsMiddleButtonPressed);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _panFrom = null;
        _drawing = _miniDrag = false;
        Engine.Release();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Engine.State != GameState.Playing) return;
        Engine.SetZoom(e.Delta.Y > 0 ? 2 : 1);
    }

    /// <summary>Saves what's on screen as a PNG.</summary>
    public void SaveScreenshot(string path)
    {
        var size = new PixelSize((int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>How the fixed screen fits the window: scale and letterbox offsets.</summary>
    private (double Scale, double X, double Y) Fit()
    {
        double s = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        return (s, (Bounds.Width - ScreenWidth * s) / 2, (Bounds.Height - ScreenHeight * s) / 2);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var (s, ox, oy) = Fit();
        if (s <= 0) return;
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        using (dc.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
            DrawScreen(dc);
    }
}
