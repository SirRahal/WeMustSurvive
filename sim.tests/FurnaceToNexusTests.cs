using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class FurnaceToNexusTests
{
    /// <summary>Furnace (3x3) at (31..33,64..66) -> inserter (34,64) facing east -> belts (35..38,64) east -> Nexus west face (39,64).</summary>
    private static (World W, Crafter Furnace, Inserter Ins) Build(bool startEmpty)
    {
        var w = TestWorld.Create();
        var furnace = (Crafter)w.PlaceDirect("furnace", new Int2(31, 64));
        var ins = (Inserter)w.PlaceDirect("inserter", new Int2(34, 64), Direction.East);
        for (int x = 35; x <= 38; x++) w.PlaceDirect("belt", new Int2(x, 64), Direction.East);
        if (!startEmpty)
        {
            for (int i = 0; i < 5; i++) furnace.TryInsert("coal");
            for (int i = 0; i < 5; i++) furnace.TryInsert("iron_ore");
        }
        return (w, furnace, ins);
    }

    [Fact]
    public void Plates_flow_from_a_furnace_to_the_nexus()
    {
        var (w, _, _) = Build(startEmpty: false);
        w.Run(900);
        Assert.True(w.Nexus.Received.Count("iron_plate") >= 3, $"received {w.Nexus.Received.Count("iron_plate")}");
        Assert.Empty(LogisticsCheck.Analyze(w));
    }

    [Fact]
    public void Coal_drill_feeding_a_drill_that_is_also_on_coal_is_flagged()
    {
        var w = TestWorld.Create();
        // Two drills side by side on the coal patch; the left one points into the right one.
        var feeder = (Drill)w.PlaceDirect("drill", TestWorld.CoalTile, Direction.East);
        w.PlaceDirect("drill", TestWorld.CoalTile + new Int2(2, 0), Direction.North);
        w.Run(300);

        Assert.Equal("CAN'T USE", LogisticsCheck.Analyze(w)[feeder.Id].Short);
        Assert.Contains("fuels itself", feeder.Status);
    }

    [Fact]
    public void Coal_drill_waiting_on_a_full_furnace_says_why()
    {
        var w = TestWorld.Create();
        var drill = (Drill)w.PlaceDirect("drill", TestWorld.CoalTile, Direction.East);
        w.PlaceDirect("furnace", TestWorld.CoalTile + new Int2(2, 0));
        w.Run(1000); // 5 coal fills the furnace's fuel; with no ore it never burns any

        Assert.Contains("only burns coal while smelting", drill.Status);
        Assert.False(LogisticsCheck.Analyze(w).ContainsKey(drill.Id)); // valid setup, just waiting
    }

    [Fact]
    public void Plates_flow_when_the_furnace_starts_empty_and_is_fed_later()
    {
        var (w, furnace, _) = Build(startEmpty: true);
        w.Run(300); // inserter sits idle with nothing to take for a while
        for (int i = 0; i < 5; i++) furnace.TryInsert("coal");
        for (int i = 0; i < 5; i++) furnace.TryInsert("copper_ore");
        w.Run(900);
        Assert.True(w.Nexus.Received.Count("copper_plate") >= 3, $"received {w.Nexus.Received.Count("copper_plate")}");
    }
}
