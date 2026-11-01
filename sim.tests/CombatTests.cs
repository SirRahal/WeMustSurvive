using System.Numerics;
using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class CombatTests
{
    // The choke is open where |x + 0.5 - 40| <= 3, i.e. x = 37..42, for rows 5..8.
    private static readonly int[] ChokeXs = { 37, 38, 39, 40, 41, 42 };
    private const int ChokeRow = 7;

    /// <summary>Follows the flow field from a spawn tile and counts wall tiles on the way to the Nexus.</summary>
    private static int WallsOnPath(World w)
    {
        var t = w.Map.SpawnTiles[0];
        int walls = 0;
        for (int i = 0; i < 500; i++)
        {
            var next = w.Flow.NextStep(t);
            if (next == null) break;
            t = next.Value;
            var b = w.BuildingAt(t);
            if (b is Nexus) return walls;
            if (b is Wall) walls++;
        }
        throw new Exception("Path never reached the Nexus");
    }

    [Fact]
    public void Flow_field_reaches_nexus_from_every_spawn_tile()
    {
        var w = TestWorld.Create();
        w.Step();
        Assert.NotEmpty(w.Map.SpawnTiles);
        Assert.All(w.Map.SpawnTiles, t => Assert.True(float.IsFinite(w.Flow.DistanceAt(t))));
        Assert.Equal(0, WallsOnPath(w));
    }

    [Fact]
    public void Enemies_route_around_a_partial_wall()
    {
        var w = TestWorld.Create();
        foreach (int x in ChokeXs.Take(5)) w.PlaceDirect("wall", new Int2(x, ChokeRow));
        w.Step();
        Assert.Equal(0, WallsOnPath(w));
    }

    [Fact]
    public void Enemies_break_through_a_sealed_choke()
    {
        var w = TestWorld.Create();
        foreach (int x in ChokeXs) w.PlaceDirect("wall", new Int2(x, ChokeRow));
        w.Step();
        Assert.Equal(1, WallsOnPath(w));
    }

    [Fact]
    public void Turret_with_ammo_kills_grunt()
    {
        var w = TestWorld.Create();
        var turret = (Turret)w.PlaceDirect("gun_turret", new Int2(40, 40));
        turret.TryInsert("ammo_magazine");
        var grunt = w.SpawnEnemy(w.Data.Enemies["grunt"], new Vector2(40.5f, 34.5f));

        w.Run(120);

        Assert.True(grunt.Dead);
        Assert.Contains(w.Buildings, b => b == turret);
    }

    [Fact]
    public void Turret_without_ammo_does_not_fire()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("gun_turret", new Int2(40, 40));
        var grunt = w.SpawnEnemy(w.Data.Enemies["grunt"], new Vector2(40.5f, 36.5f));
        w.Step();
        Assert.Equal(grunt.MaxHp, grunt.Hp);
    }

    [Fact]
    public void Grunt_attacks_blocking_wall()
    {
        var w = TestWorld.Create();
        foreach (int x in ChokeXs) w.PlaceDirect("wall", new Int2(x, ChokeRow));
        var grunt = w.SpawnEnemy(w.Data.Enemies["grunt"], new Vector2(40.5f, 3.5f));

        w.Run(20 * World.TicksPerSecond);

        Assert.False(grunt.Dead);
        Assert.Contains(w.Buildings, b => b is Wall && b.Hp < b.Def.MaxHp);
    }

    [Fact]
    public void Destroying_nexus_loses_the_game()
    {
        var w = TestWorld.Create();
        w.DamageBuilding(w.Nexus, 1e9f);
        w.Step();
        Assert.Equal(Outcome.Lost, w.Outcome);
    }

    [Fact]
    public void Engineer_repairs_damaged_building_using_plates()
    {
        var w = TestWorld.Create();
        var wall = w.PlaceDirect("wall", new Int2(40, 59));
        w.DamageBuilding(wall, 100);
        int plates = w.Storage.Count("iron_plate");

        w.Enqueue(new RepairCommand(0, wall.Origin));
        w.Run(60);

        Assert.Equal(wall.Def.MaxHp, wall.Hp);
        Assert.Equal(plates - 2, w.Storage.Count("iron_plate"));
    }
}
