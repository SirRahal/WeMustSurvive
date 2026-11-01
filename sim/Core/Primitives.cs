using System.Numerics;

namespace BeltAndBarricade.Sim;

/// <summary>Integer grid coordinate. The sim grid is square; isometric is purely a render projection.</summary>
public readonly record struct Int2(int X, int Y)
{
    public static Int2 operator +(Int2 a, Int2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Int2 operator -(Int2 a, Int2 b) => new(a.X - b.X, a.Y - b.Y);

    public Vector2 Center => new(X + 0.5f, Y + 0.5f);

    public static Int2 Floor(Vector2 p) => new((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
}

public enum Direction { North, East, South, West }

public static class DirectionExtensions
{
    public static Int2 Offset(this Direction d) => d switch
    {
        Direction.North => new Int2(0, -1),
        Direction.East => new Int2(1, 0),
        Direction.South => new Int2(0, 1),
        _ => new Int2(-1, 0),
    };

    public static Direction RotateCw(this Direction d) => (Direction)(((int)d + 1) % 4);
    public static Direction Opposite(this Direction d) => (Direction)(((int)d + 2) % 4);
}

/// <summary>Seeded xorshift RNG. All sim randomness goes through this so runs are reproducible.</summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public ulong State => _state;

    public ulong NextULong()
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return _state;
    }

    public int Next(int maxExclusive) => (int)(NextULong() % (ulong)maxExclusive);

    /// <summary>Uniform float in [0, 1).</summary>
    public float NextFloat() => (NextULong() >> 40) / (float)(1 << 24);
}

/// <summary>FNV-1a style hash over sim state, used by replay/desync tests (and later, co-op desync checks).</summary>
public sealed class StateHash
{
    private ulong _h = 14695981039346656037UL;

    public ulong Value => _h;

    public void Add(long v)
    {
        for (int i = 0; i < 8; i++)
        {
            _h ^= (byte)(v >> (i * 8));
            _h *= 1099511628211UL;
        }
    }

    public void Add(float v) => Add(BitConverter.SingleToInt32Bits(v));

    public void Add(Vector2 v)
    {
        Add(v.X);
        Add(v.Y);
    }

    public void Add(string? s)
    {
        if (s == null) { Add(-1); return; }
        foreach (char c in s) Add(c);
    }
}
