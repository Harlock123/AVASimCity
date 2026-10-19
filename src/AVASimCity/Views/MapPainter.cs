using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AVASimCity.Engine;

namespace AVASimCity.Views;

/// <summary>
/// The whole map as one picture, 16 pixels a tile, kept up to date tile by tile: each frame every tile's key is
/// worked out, and only the tiles whose keys changed are drawn again. Also the mini-map, and the map overlays as a
/// small picture of their own that's stretched (and smoothed) over the view.
/// </summary>
public sealed class MapPainter : IDisposable
{
    public const int T = TileArt.T;
    public WriteableBitmap Map { get; } = Bitmap(Field.W * T, Field.H * T);
    public WriteableBitmap Mini { get; } = Bitmap(Field.W, Field.H);
    public WriteableBitmap Shade { get; } = Bitmap(Field.W, Field.H);

    private readonly ulong[] _keys = new ulong[Field.W * Field.H];
    private World? _world;
    private int _miniMonth = -1, _shadeMonth = -1, _miniStep = -1;
    private Overlay _miniOverlay = (Overlay)(-1), _shadeOverlay = (Overlay)(-1);

    private static WriteableBitmap Bitmap(int w, int h) => new(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    public MapPainter() => Array.Fill(_keys, ulong.MaxValue);

    /// <summary>Redraws the tiles that have changed; true if any did.</summary>
    public unsafe bool Update(World w)
    {
        if (!ReferenceEquals(w, _world))
        {
            _world = w;
            Array.Fill(_keys, ulong.MaxValue);
            _miniMonth = _shadeMonth = -1;
        }
        bool any = false;
        using var fb = Map.Lock();
        uint* dst = (uint*)fb.Address;
        int stride = fb.RowBytes / 4;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
        {
            ulong key = TileArt.Key(w, x, y);
            int i = y * Field.W + x;
            if (_keys[i] == key) continue;
            _keys[i] = key;
            any = true;
            var art = TileArt.Tile(key);
            for (int j = 0; j < T; j++)
            {
                uint* row = dst + (y * T + j) * stride + x * T;
                for (int k = 0; k < T; k++) row[k] = art[j * T + k];
            }
        }
        return any;
    }

    /// <summary>The mini-map: the city, or the overlay's map over a dimmed city.</summary>
    public unsafe void UpdateMini(World w, Overlay o)
    {
        // A few times a month is plenty (and whenever the overlay changes).
        int step = w.StepInMonth / 48;
        if (w.Month == _miniMonth && o == _miniOverlay && step == _miniStep && ReferenceEquals(w, _world)) return;
        _miniMonth = w.Month;
        _miniStep = step;
        _miniOverlay = o;
        using var fb = Mini.Lock();
        uint* dst = (uint*)fb.Address;
        int stride = fb.RowBytes / 4;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++)
        {
            uint c = TileArt.MiniColour(w, x, y);
            if (o != Overlay.None)
            {
                uint s = OverlayColour(w, o, x, y);
                double a = (s >> 24) / 255.0;
                c = Pix.Mix(Pix.Scale(c, 0.55), Unpremultiply(s), a);
            }
            dst[y * stride + x] = c;
        }
    }

    public unsafe void UpdateShade(World w, Overlay o)
    {
        int step = w.StepInMonth / 48;
        if (w.Month == _shadeMonth && o == _shadeOverlay && step == _miniStep && ReferenceEquals(w, _world)) return;
        _shadeMonth = w.Month;
        _shadeOverlay = o;
        using var fb = Shade.Lock();
        uint* dst = (uint*)fb.Address;
        int stride = fb.RowBytes / 4;
        for (int x = 0; x < Field.W; x++)
        for (int y = 0; y < Field.H; y++) dst[y * stride + x] = OverlayColour(w, o, x, y);
    }

    private static uint Unpremultiply(uint c)
    {
        uint a = c >> 24;
        if (a == 0) return 0xFF000000;
        uint Ch(int shift) => Math.Min(255, ((c >> shift) & 0xFF) * 255 / a);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Premul(uint rgb, double alpha)
    {
        uint a = (uint)Math.Clamp(alpha * 255, 0, 255);
        uint Ch(int shift) => ((rgb >> shift) & 0xFF) * a / 255;
        return a << 24 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>A heat scale: pale yellow through orange to deep red.</summary>
    public static uint Heat(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? Pix.Mix(0xFFF8E858, 0xFFF08020, t * 2) : Pix.Mix(0xFFF08020, 0xFFC01818, (t - 0.5) * 2);
    }

    /// <summary>A tile's colour (premultiplied, with its alpha) in an overlay.</summary>
    public static uint OverlayColour(World w, Overlay o, int x, int y)
    {
        double Ramp(byte v, double scale) => Math.Clamp(v / 255.0 * scale, 0, 1);
        switch (o)
        {
            case Overlay.Population:
            {
                double t = Ramp(w.Density[x, y], 1.6);
                return t < 0.03 ? 0 : Premul(Heat(t), 0.25 + t * 0.55);
            }
            case Overlay.Traffic:
            {
                if ((w.Net[x, y] & (Net.Road | Net.Rail)) == 0) return Premul(0xFF000000, 0.35);
                double t = Ramp(w.Traffic[x, y], 1.3);
                return Premul(t < 0.05 ? 0xFF50C050 : Heat(t), 0.85);
            }
            case Overlay.Pollution:
            {
                double t = Ramp(w.Pollution[x, y], 1.8);
                return t < 0.04 ? 0 : Premul(Pix.Mix(0xFFC8B850, 0xFF5A3A1A, t), 0.25 + t * 0.6);
            }
            case Overlay.Crime:
            {
                double t = Ramp(w.Crime[x, y], 1.6);
                return t < 0.04 ? 0 : Premul(Pix.Mix(0xFFE070D0, 0xFF900030, t), 0.25 + t * 0.6);
            }
            case Overlay.LandValue:
            {
                if (w.Ground[x, y] == Ground.Water) return 0;
                double t = Ramp(w.LandValue[x, y], 1.3);
                uint c = t < 0.5 ? Pix.Mix(0xFF4060C0, 0xFF40C060, t * 2) : Pix.Mix(0xFF40C060, 0xFFF0E040, (t - 0.5) * 2);
                return Premul(c, 0.6);
            }
            case Overlay.Power:
            {
                var z = w.ZoneAt(x, y);
                if (z != null) return Premul(z.Powered ? 0xFFF8E040 : 0xFFE02020, 0.7);
                if ((w.Net[x, y] & Net.Wire) != 0) return Premul(w.Power[x, y] ? 0xFFF8E040 : 0xFF802020, 0.8);
                return Premul(0xFF000000, 0.4);
            }
            case Overlay.Police:
            {
                double t = Ramp(w.PoliceCover[x, y], 1.2);
                return t < 0.03 ? 0 : Premul(Pix.Mix(0xFF80B0F0, 0xFF1830B0, t), 0.2 + t * 0.6);
            }
            case Overlay.Fire:
            {
                double t = Ramp(w.FireCover[x, y], 1.2);
                return t < 0.03 ? 0 : Premul(Pix.Mix(0xFFF0B090, 0xFFC02010, t), 0.2 + t * 0.6);
            }
        }
        return 0;
    }

    public void Dispose()
    {
        Map.Dispose();
        Mini.Dispose();
        Shade.Dispose();
    }
}
