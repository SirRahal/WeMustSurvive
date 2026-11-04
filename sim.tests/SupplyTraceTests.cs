using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class SupplyTraceTests
{
    [Fact]
    public void Furnace_counts_drills_feeding_it_directly_and_through_belts()
    {
        var w = TestWorld.Create();
        // Direct: iron drill west of the furnace pointing east into it; coal drill fuelling the iron drill.
        var furnace = (Crafter)w.PlaceDirect("furnace", new Int2(48, 57)); // 3x3: 48..50 x 57..59
        w.PlaceDirect("drill", new Int2(46, 57), Direction.East); // 2x2: 46..47 x 57..58, outputs onto (48,57)
        // Indirect: another iron drill -> belt -> belt -> inserter -> furnace (from the north).
        w.PlaceDirect("drill", new Int2(50, 55), Direction.West); // 2x2: 50..51 x 55..56, outputs onto (49,55)
        w.PlaceDirect("belt", new Int2(49, 55), Direction.West);
        w.PlaceDirect("belt", new Int2(48, 55), Direction.South);
        w.PlaceDirect("inserter", new Int2(48, 56), Direction.South);
        // Not connected: a drill pointing elsewhere.
        w.PlaceDirect("drill", new Int2(51, 58), Direction.East); // east of the 3x3 furnace, facing away

        var drills = SupplyTrace.DrillsFeeding(w, furnace);
        Assert.Equal(2, drills.Count);
        Assert.All(drills, d => Assert.Equal("iron_ore", w.Map.OreAt(d.Origin)));
    }

    [Fact]
    public void Rates_are_measured_per_minute()
    {
        var w = TestWorld.Create();
        var drill = (Drill)w.PlaceDirect("drill", TestWorld.CoalTile, Direction.East);
        var furnace = (Crafter)w.PlaceDirect("furnace", TestWorld.CoalTile + new Int2(2, 0));
        w.Run(60 * World.TicksPerSecond);

        // A burner drill on coal mines one every 2 s = 30/min until the furnace's fuel slot is full (5);
        // it mines a 6th that it then holds, waiting.
        Assert.Equal(30f, drill.MaxPerMinute(w), 1);
        Assert.Equal(6f, drill.Mined.PerMinute("coal", w.Tick), 1);
        Assert.Equal(5f, furnace.Received.PerMinute("coal", w.Tick), 1);
        Assert.Equal(30f, furnace.MaxPerMinute(w, w.Data.Recipes["smelt_iron"]), 1);
    }
}
