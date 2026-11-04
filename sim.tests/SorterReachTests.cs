using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

/// <summary>Sorters reach over belts behind them, so one sorter can pull from several parallel belts.</summary>
public class SorterReachTests
{
    /// <summary>
    /// Two parallel belts running south at x=30 (far) and x=31 (near); a sorter at (32,61) faces east into a turret.
    /// </summary>
    private static (World W, Inserter Sorter, Turret Turret, Belt Far, Belt Near) TwoBelts(string sorter)
    {
        var w = TestWorld.Create();
        var far = (Belt)w.PlaceDirect("belt", new Int2(30, 61), Direction.South);
        var near = (Belt)w.PlaceDirect("belt", new Int2(31, 61), Direction.South);
        var ins = (Inserter)w.PlaceDirect(sorter, new Int2(32, 61), Direction.East);
        var turret = (Turret)w.PlaceDirect("gun_turret", new Int2(33, 61));
        return (w, ins, turret, far, near);
    }

    [Fact]
    public void Sorter_reaches_over_a_belt_to_the_one_behind()
    {
        var (w, ins, turret, far, _) = TwoBelts("inserter");
        far.TryInsertAt("ammo_magazine", 0);
        w.Run(180);
        Assert.Equal(1, turret.Ammo);
        Assert.Equal(2, ins.TookDistance);
    }

    [Fact]
    public void Sorter_takes_from_both_belts_nearest_first()
    {
        var (w, ins, turret, far, near) = TwoBelts("inserter");
        far.TryInsertAt("ammo_magazine", 0);
        near.TryInsertAt("ammo_magazine", 0);
        w.Run(30);
        Assert.Equal(1, ins.TookDistance);
        w.Run(300);
        Assert.Equal(2, turret.Ammo);
    }

    [Fact]
    public void Inserter_does_not_reach_past_its_reach()
    {
        var w = TestWorld.Create();
        var far = (Belt)w.PlaceDirect("belt", new Int2(29, 61), Direction.South); // 3 tiles behind
        var ins = (Inserter)w.PlaceDirect("inserter", new Int2(32, 61), Direction.East);
        var turret = (Turret)w.PlaceDirect("gun_turret", new Int2(33, 61));
        far.TryInsertAt("ammo_magazine", 0);
        Assert.Equal(2, ins.Reach);
        w.Run(240);
        Assert.Equal(0, turret.Ammo);
    }

    [Fact]
    public void Belt_picked_up_from_two_tiles_away_is_connected()
    {
        var (w, _, _, far, _) = TwoBelts("inserter");
        w.Step();
        Assert.False(LogisticsCheck.Analyze(w).ContainsKey(far.Id));
    }
}
