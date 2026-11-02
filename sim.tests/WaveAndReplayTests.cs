using System.Numerics;
using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class WaveAndReplayTests
{
    [Fact]
    public void First_wave_starts_exactly_on_timer()
    {
        var w = TestWorld.Create();
        w.Run(TestWorld.Funnel.Waves.FirstWaveTicks - 1);
        Assert.Equal(0, w.Waves.WavesStarted);
        w.Step();
        Assert.Equal(1, w.Waves.WavesStarted);
        Assert.Contains(w.Events, e => e is WaveStartedEvent { Wave: 1 });
    }

    [Fact]
    public void Ending_prep_early_starts_next_wave()
    {
        var w = TestWorld.Create();
        w.Enqueue(new EndPrepCommand(0));
        w.Step();
        Assert.Equal(1, w.Waves.WavesStarted);
    }

    [Fact]
    public void Solo_scales_enemy_stats_and_counts()
    {
        var solo = TestWorld.Create(players: 1);
        var coop = TestWorld.Create(players: 2);
        foreach (var w in new[] { solo, coop })
        {
            w.Enqueue(new EndPrepCommand(0));
            w.Run(10 * World.TicksPerSecond);
        }

        // Wave 1 = 6 grunts in co-op; solo uses cbrt(0.45) per axis -> round(6 * 0.766) = 5.
        Assert.Equal(6, coop.Enemies.Count);
        Assert.Equal(5, solo.Enemies.Count);
        Assert.True(solo.Enemies[0].MaxHp < coop.Enemies[0].MaxHp);
        float threat = solo.ThreatAxis * solo.ThreatAxis * solo.ThreatAxis;
        Assert.InRange(threat, 0.449f, 0.451f);
    }

    /// <summary>Runs every wave back to back, topping turrets up with ammo as a working factory would.</summary>
    private static World RunAllWaves(World w, List<Turret> turrets)
    {
        for (int i = 0; i < 90 * 60 * World.TicksPerSecond && w.Outcome == Outcome.Playing; i++)
        {
            if (!w.Waves.IsSpawning && w.Enemies.Count == 0) w.Enqueue(new EndPrepCommand(0));
            if (i % 30 == 0)
                foreach (var t in turrets.Where(t => !t.Dead && t.UsesAmmo))
                    while (t.TryInsert(t.Def.AmmoItem!)) { }
            w.Step();
        }
        return w;
    }

    [Fact]
    public void A_fortified_choke_holds_all_ten_waves()
    {
        var w = TestWorld.Create();
        w.Research.Grant("reinforced_walls");
        w.Research.Grant("heavy_walls");
        w.Research.Grant("rocket_turrets");
        // Seal the choke (x 37..42, rows 5..8) with heavy walls and line the valley behind it with guns and rockets.
        foreach (int x in new[] { 37, 38, 39, 40, 41, 42 }) w.PlaceDirect("heavy_wall", new Int2(x, 8));
        var turrets = new List<Turret>();
        foreach (int x in new[] { 35, 37, 39, 41, 43, 45 }) turrets.Add((Turret)w.PlaceDirect("gun_turret", new Int2(x, 11)));
        foreach (int x in new[] { 36, 40, 44 }) turrets.Add((Turret)w.PlaceDirect("rocket_turret", new Int2(x, 13)));

        RunAllWaves(w, turrets);

        Assert.Equal(Outcome.Won, w.Outcome);
        Assert.Equal(10, w.Waves.WavesStarted);
    }

    [Fact]
    public void An_undefended_nexus_falls()
    {
        var w = RunAllWaves(TestWorld.Create(), new List<Turret>());
        Assert.Equal(Outcome.Lost, w.Outcome);
    }

    [Fact]
    public void Surrender_ends_the_game_as_a_loss()
    {
        var w = TestWorld.Create();
        w.Run(10);
        long tick = w.Tick;
        w.Enqueue(new SurrenderCommand(0));
        w.Step();

        Assert.Equal(Outcome.Lost, w.Outcome);
        Assert.True(w.Surrendered);
        Assert.False(w.Nexus.Dead);
        w.Run(10);
        Assert.Equal(tick, w.Tick); // simulation stops once the game is over
    }

    private static readonly ICommand[] Script =
    {
        new PlaceBuildingCommand(0, "drill", TestWorld.CoalTile, Direction.East),
        new PlaceBuildingCommand(0, "furnace", TestWorld.CoalTile + new Int2(1, 0), Direction.North, Queue: true),
        new PlaceBuildingCommand(0, "gun_turret", new Int2(40, 58), Direction.North, Queue: true),
        new MoveCommand(0, new Vector2(45.5f, 52.5f), Queue: true),
        new EndPrepCommand(0),
    };

    private static ulong RunScript(ulong seed)
    {
        var w = TestWorld.Create(seed);
        for (int tick = 0; tick < 3000; tick++)
        {
            if (tick < Script.Length) w.Enqueue(Script[tick]);
            w.Step();
        }
        return w.ComputeHash();
    }

    [Fact]
    public void Same_seed_and_commands_produce_identical_state()
    {
        Assert.Equal(RunScript(42), RunScript(42));
    }

    [Fact]
    public void Different_seed_produces_different_state()
    {
        Assert.NotEqual(RunScript(42), RunScript(43));
    }
}
