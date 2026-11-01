using System.Numerics;

namespace BeltAndBarricade.Sim;

/// <summary>One-shot notifications emitted during a tick, for the view layer (effects, sounds, toasts).</summary>
public abstract record SimEvent;

public sealed record ShotEvent(Vector2 From, Vector2 To) : SimEvent;

public sealed record BuildingDestroyedEvent(Int2 Origin, string DefId) : SimEvent;

public sealed record WaveStartedEvent(int Wave) : SimEvent;

public sealed record MessageEvent(int PlayerId, string Text) : SimEvent;
