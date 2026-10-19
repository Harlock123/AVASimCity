using Avalonia.Input;

namespace AVASimCity.Engine;

/// <summary>
/// The keys held, boiled down to the controls: which way the map scrolls. (The mouse, and the keys that do one thing
/// once, go straight to the engine.)
/// </summary>
public sealed class InputHandler
{
    private readonly List<Key> _held = [];

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    /// <summary>Let go of everything (the window lost the focus).</summary>
    public void Clear() => _held.Clear();

    /// <summary>The arrows (or WASD) scroll the map the way they point: up scrolls up the map (towards row 0).</summary>
    public Controls Player
    {
        get
        {
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            int dy = (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0) - (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0);
            return new Controls(dx, dy);
        }
    }

    /// <summary>Enter.</summary>
    public bool Start => Any(Key.Enter);
}
