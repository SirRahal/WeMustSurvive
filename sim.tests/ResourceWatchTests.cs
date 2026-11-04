using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class ResourceWatchTests
{
    private static void RunWatched(World w, ResourceWatch watch, int seconds)
    {
        watch.Sample(w);
        for (int i = 0; i < seconds * World.TicksPerSecond; i++)
        {
            w.Step();
            watch.Sample(w);
        }
    }

    [Fact]
    public void No_coal_with_a_hungry_drill_is_empty_and_says_how_to_fix_it()
    {
        var w = TestWorld.Create();
        w.Storage.Remove("coal", w.Storage.Count("coal"));
        w.PlaceDirect("drill", TestWorld.IronTile, Direction.East);
        var watch = new ResourceWatch();
        RunWatched(w, watch, 3);

        var coal = watch.Report(w).Single(r => r.Item == "coal");
        Assert.Equal(ResourceLevel.Empty, coal.Level);
        Assert.Contains(coal.Advice, a => a.StartsWith("STALLED: 1 Burner Drill"));
        Assert.Contains(coal.Advice, a => a.Contains("build a Burner Drill on a Coal patch"));
    }

    [Fact]
    public void Coal_delivered_to_storage_shows_as_income()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("drill", TestWorld.CoalTile, Direction.East);
        w.PlaceDirect("depot", TestWorld.CoalTile + new Int2(2, 0));
        var watch = new ResourceWatch();
        RunWatched(w, watch, 30);

        var coal = watch.Status(w, "coal");
        Assert.InRange(coal.InPerMinute, 25, 35); // one coal every 2 s
        Assert.Equal(ResourceLevel.Ok, coal.Level);
    }

    [Fact]
    public void Empty_turret_with_ammo_in_storage_is_stuck_not_low()
    {
        var w = TestWorld.Create();
        w.Storage.Add("ammo_magazine", 30);
        w.PlaceDirect("gun_turret", new Int2(40, 58));
        var watch = new ResourceWatch();
        RunWatched(w, watch, 2);

        var ammo = watch.Status(w, "ammo_magazine");
        Assert.Equal(ResourceLevel.Stuck, ammo.Level);
        Assert.Contains(ammo.Advice, a => a.Contains("Point an inserter into each one"));
    }

    [Fact]
    public void Spending_faster_than_income_is_falling_or_low_with_a_time_left()
    {
        var w = TestWorld.Create();
        var watch = new ResourceWatch();
        watch.Sample(w);
        for (int s = 0; s < 20; s++)
        {
            w.Storage.Remove("iron_plate", 4);
            RunWatched(w, watch, 1);
        }
        var iron = watch.Status(w, "iron_plate");
        Assert.True(iron.NetPerMinute < -100);
        Assert.True(iron.Level >= ResourceLevel.Falling);
        Assert.Contains("runs out in", iron.Advice[0]);
    }

    [Fact]
    public void Idle_furnace_with_no_ore_points_at_mining_ore()
    {
        var w = TestWorld.Create();
        w.Storage.Remove("iron_plate", w.Storage.Count("iron_plate"));
        var watch = new ResourceWatch();
        RunWatched(w, watch, 2);

        var iron = watch.Status(w, "iron_plate");
        Assert.Equal(ResourceLevel.Empty, iron.Level);
        Assert.Contains(iron.Advice, a => a.Contains("build a Furnace"));
    }
}
