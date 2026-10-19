namespace AVASimCity.Views;

/// <summary>
/// A little picture as an array of 0xAARRGGBB pixels, with the few things the tile art needs: rectangles, lines,
/// discs, shading, and the buildings seen from above with their south walls and their shadows.
/// </summary>
public sealed class Pix
{
    public readonly int W, H;
    public readonly uint[] P;

    public Pix(int w, int h, uint fill = 0xFF000000)
    {
        W = w;
        H = h;
        P = new uint[w * h];
        Array.Fill(P, fill);
    }

    public uint Get(int x, int y) => x >= 0 && y >= 0 && x < W && y < H ? P[y * W + x] : 0;

    public void Set(int x, int y, uint c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        uint a = c >> 24;
        if (a == 0xFF) P[y * W + x] = c;
        else if (a > 0) P[y * W + x] = Mix(P[y * W + x], c | 0xFF000000, a / 255.0);
    }

    public void Fill(int x, int y, int w, int h, uint c)
    {
        for (int j = y; j < y + h; j++)
        for (int i = x; i < x + w; i++) Set(i, j, c);
    }

    public void Frame(int x, int y, int w, int h, uint c)
    {
        HLine(x, x + w - 1, y, c);
        HLine(x, x + w - 1, y + h - 1, c);
        VLine(x, y, y + h - 1, c);
        VLine(x + w - 1, y, y + h - 1, c);
    }

    public void HLine(int x0, int x1, int y, uint c)
    {
        for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) Set(x, y, c);
    }

    public void VLine(int x, int y0, int y1, uint c)
    {
        for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++) Set(x, y, c);
    }

    public void Line(int x0, int y0, int x1, int y1, uint c)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            Set(x0, y0, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>A filled ellipse round (cx, cy), radii in pixels (half-pixel centres allowed).</summary>
    public void Disc(double cx, double cy, double rx, double ry, uint c)
    {
        for (int y = (int)Math.Floor(cy - ry); y <= (int)Math.Ceiling(cy + ry); y++)
        for (int x = (int)Math.Floor(cx - rx); x <= (int)Math.Ceiling(cx + rx); x++)
        {
            double u = (x + 0.5 - cx) / rx, v = (y + 0.5 - cy) / ry;
            if (u * u + v * v <= 1) Set(x, y, c);
        }
    }

    public void Disc(double cx, double cy, double r, uint c) => Disc(cx, cy, r, r, c);

    /// <summary>Darkens (k &lt; 1) or lightens what's already there.</summary>
    public void Shade(int x, int y, int w, int h, double k)
    {
        for (int j = y; j < y + h; j++)
        for (int i = x; i < x + w; i++)
            if (i >= 0 && j >= 0 && i < W && j < H) P[j * W + i] = Scale(P[j * W + i], k);
    }

    /// <summary>Speckles a rectangle with lighter and darker pixels.</summary>
    public void Grain(int x, int y, int w, int h, double amount, int seed)
    {
        for (int j = y; j < y + h; j++)
        for (int i = x; i < x + w; i++)
        {
            if (i < 0 || j < 0 || i >= W || j >= H) continue;
            double r = Rand(i, j, seed);
            if (r < 0.18) P[j * W + i] = Scale(P[j * W + i], 1 - amount);
            else if (r > 0.86) P[j * W + i] = Scale(P[j * W + i], 1 + amount * 0.8);
        }
    }

    public void Blit(Pix src, int x, int y)
    {
        for (int j = 0; j < src.H; j++)
        for (int i = 0; i < src.W; i++) Set(x + i, y + j, src.P[j * src.W + i]);
    }

    /// <summary>The shadow a thing of height <paramref name="h"/> standing on (x..x+w, y..y+d) casts to the south-east.</summary>
    public void Shadow(int x, int y, int w, int d, int h)
    {
        int len = Math.Clamp(h / 2 + 1, 1, 9);
        for (int j = y - h + 2; j < y + d + Math.Min(2, len / 2); j++)
        for (int i = x + w; i < x + w + len; i++) Shade(i, j, 1, 1, 0.62);
        for (int i = x + 1; i < x + w; i++)
        for (int j = y + d; j < y + d + Math.Min(2, len / 2); j++) Shade(i, j, 1, 1, 0.72);
    }

    /// <summary>
    /// A flat-roofed building on the ground rectangle (x..x+w, y..y+d), <paramref name="h"/> pixels tall: its roof
    /// (seen from above, lifted by its height) and its south wall below it, with windows.
    /// </summary>
    public void Box(int x, int y, int w, int d, int h, uint roof, uint wall, Windows windows = Windows.Rows, bool shadow = true)
    {
        if (shadow) Shadow(x, y, w, d, h);
        int top = y - h;
        // The wall.
        Fill(x, top + d, w, h, wall);
        VLine(x, top + d, y + d - 1, Scale(wall, 1.15));
        VLine(x + w - 1, top + d, y + d - 1, Scale(wall, 0.8));
        HLine(x, x + w - 1, y + d - 1, Scale(wall, 0.7));
        switch (windows)
        {
            case Windows.Rows:
                for (int j = top + d + 2; j < y + d - 2; j += 3)
                for (int i = x + 2; i < x + w - 2; i += 2)
                    Set(i, j, Rand(i, j, 5) < 0.3 ? 0xFFB8D0E8 : 0xFF34404E);
                break;
            case Windows.Glass:
                for (int j = top + d + 1; j < y + d - 1; j++)
                for (int i = x + 1; i < x + w - 1; i++)
                    Set(i, j, (j - top) % 3 == 0 ? Scale(wall, 0.7) : (i - x) % 3 == 0 ? Scale(wall, 0.85) : Scale(wall, 1 + 0.25 * Rand(i / 3, j / 3, 9)));
                break;
            case Windows.Few:
                for (int i = x + 2; i < x + w - 2; i += 3)
                    if (h >= 4) Set(i, y + d - 3, 0xFF34404E);
                break;
        }
        // The roof, lit from the north-west.
        Fill(x, top, w, d, roof);
        HLine(x, x + w - 1, top, Scale(roof, 1.25));
        VLine(x, top, top + d - 1, Scale(roof, 1.15));
        VLine(x + w - 1, top, top + d - 1, Scale(roof, 0.8));
        HLine(x, x + w - 1, top + d - 1, Scale(roof, 0.7));
    }

    /// <summary>A house with a pitched roof, its ridge running east-west, and a little chimney.</summary>
    public void House(int x, int y, int w, int d, int h, uint roof, uint wall, int seed)
    {
        Shadow(x, y, w, d, h + 2);
        int top = y - h;
        Fill(x, top + d, w, h, wall);
        HLine(x, x + w - 1, y + d - 1, Scale(wall, 0.75));
        if (w >= 6) { Set(x + 2, y + d - 3, 0xFF34404E); Set(x + w - 3, y + d - 3, 0xFF34404E); }
        if (w >= 4) VLine(x + w / 2, y + d - 3, y + d - 1, Scale(wall, 0.55));
        int half = d / 2;
        Fill(x, top, w, half, Scale(roof, 1.15));
        Fill(x, top + half, w, d - half, Scale(roof, 0.85));
        HLine(x, x + w - 1, top + half, Scale(roof, 1.35));
        VLine(x, top, top + d - 1, Scale(roof, 1.1));
        VLine(x + w - 1, top, top + d - 1, Scale(roof, 0.7));
        HLine(x, x + w - 1, top + d - 1, Scale(roof, 0.6));
        if (Rand(seed, 1, 3) < 0.6) { int cx = x + 1 + (int)(Rand(seed, 2, 3) * (w - 3)); Fill(cx, top + 1, 2, 2, 0xFF6A4E40); }
    }

    /// <summary>A round tank or tower seen from above, lit from the north-west.</summary>
    public void Tank(double cx, double cy, double r, uint c, int h = 3)
    {
        Disc(cx + h * 0.6, cy + h * 0.5, r, 0x60000000);
        Disc(cx, cy + h * 0.3, r, Scale(c, 0.7));
        Disc(cx, cy, r, c);
        Disc(cx - r * 0.3, cy - r * 0.3, r * 0.45, Scale(c, 1.18));
    }

    /// <summary>A chimney stack: a round column, banded, with soot at the top.</summary>
    public void Chimney(int x, int y, int h, uint c)
    {
        Fill(x + 2, y - h + 2, 2, h, 0x50000000);
        Fill(x, y - h, 3, h, c);
        VLine(x, y - h, y - 1, Scale(c, 1.2));
        VLine(x + 2, y - h, y - 1, Scale(c, 0.75));
        HLine(x, x + 2, y - h + 2, 0xFFD04030);
        HLine(x, x + 2, y - h, 0xFF2A2420);
    }

    public static uint Scale(uint c, double k)
    {
        uint Ch(int shift) => (uint)Math.Clamp(Math.Round(((c >> shift) & 0xFF) * k), 0, 255);
        return (c & 0xFF000000) | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    public static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    public static double Rand(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFF) / 65536.0;
    }
}

public enum Windows { None, Rows, Glass, Few }
