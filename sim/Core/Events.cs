using System.Numerics;

namespace BeltAndBarricade.Sim;

/// <summary>One-shot notifications emitted during a tick, for the view layer (effects, sounds, toasts).</summary>
public abstract record SimEvent;

/// <param name="Kind">bullet, laser, rocket, flame, heavy, acid.</param>
public sealed record ShotEvent(Vector2 From, Vector2 To, string Kind = "bullet") : SimEvent;

public sealed record ExplosionEvent(Vector2 Pos, float Radius, string Kind) : SimEvent;

/// <summary>A super weapon was fired; the strike lands after <paramref name="DelayTicks"/>.</summary>
public sealed record StrikeIncomingEvent(int PlayerId, Vector2 Pos, float Radius, int DelayTicks, string Kind) : SimEvent;

public sealed record BuildingDestroyedEvent(Int2 Origin, string DefId) : SimEvent;

public sealed record WaveStartedEvent(int Wave) : SimEvent;

public sealed record ResearchCompletedEvent(string TechId) : SimEvent;

public sealed record MessageEvent(int PlayerId, string Text) : SimEvent;

public enum PingKind { LookHere, Defend, Danger, NeedIron, NeedCopper, NeedSilver, OnMyWay, BuildHere }

public sealed record PingEvent(int PlayerId, PingKind Kind, Vector2 Pos) : SimEvent;

public sealed record DrawEvent(int PlayerId, Vector2[] Points) : SimEvent;

public sealed record ClearDrawingsEvent(int PlayerId) : SimEvent;
