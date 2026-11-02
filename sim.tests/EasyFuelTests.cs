using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class EasyFuelTests
{
    [Fact]
    public void Furnace_and_its_iron_drill_run_on_coal_from_the_nexus()
    {
        var w = TestWorld.Create();
        // No coal drill at all: the drill and furnace both draw coal from the Nexus' starting stock.
        w.PlaceDirect("drill", TestWorld.IronTile, Direction.East);
        var furnace = (Crafter)w.PlaceDirect("furnace", TestWorld.IronTile + new Int2(1, 0));
        w.Run(1200);
        Assert.True(furnace.Output.Count("iron_plate") >= 7, $"made {furnace.Output.Count("iron_plate")}");
    }

    [Fact]
    public void Generator_runs_on_coal_from_storage()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("generator", new Int2(36, 59)); // inside the Nexus' power area, no coal delivered
        var furnaces = new[] { new Int2(36, 61), new Int2(37, 61), new Int2(38, 61) }
            .Select(t => (Crafter)w.PlaceDirect("electric_furnace", t)).ToList();
        foreach (var f in furnaces) for (int i = 0; i < 5; i++) f.TryInsert("iron_ore");
        w.Run(5);
        Assert.Equal(1f, w.Power.GridOf(w.Nexus)!.Satisfaction);
    }

    [Fact]
    public void Drills_aim_at_the_furnace_that_smelts_their_ore()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("furnace", TestWorld.IronTile + new Int2(1, 0));
        w.PlaceDirect("belt", TestWorld.IronTile + new Int2(0, 1), Direction.South);
        Assert.Equal(Direction.East, Drill.BestOutputDir(w, TestWorld.IronTile)); // furnace beats the belt
    }

    [Fact]
    public void Drills_never_aim_into_a_belt_pointing_back_at_them_or_into_other_ore_drills()
    {
        var w = TestWorld.Create();
        var tile = TestWorld.IronTile;
        w.PlaceDirect("belt", tile + new Int2(1, 0), Direction.West); // points back into the drill tile
        w.PlaceDirect("drill", tile + new Int2(0, -1), Direction.North); // another iron drill: can't take ore
        Assert.Null(Drill.BestOutputDir(w, tile));
        w.PlaceDirect("depot", tile + new Int2(-1, 0));
        Assert.Equal(Direction.West, Drill.BestOutputDir(w, tile));
    }
}
