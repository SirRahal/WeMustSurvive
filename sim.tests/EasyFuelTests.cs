using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class EasyFuelTests
{
    // Issue #4: burners only burn coal delivered to them; they never pull it from storage on their own.

    [Fact]
    public void Furnace_and_its_iron_drill_do_not_take_coal_from_the_nexus()
    {
        var w = TestWorld.Create();
        int coal = w.Storage.Count("coal");
        var drill = (Drill)w.PlaceDirect("drill", TestWorld.IronTile, Direction.East);
        var furnace = (Crafter)w.PlaceDirect("furnace", TestWorld.IronTile + new Int2(1, 0));
        w.Run(1200);
        Assert.Equal(0, furnace.Output.Count("iron_plate"));
        Assert.Equal(coal, w.Storage.Count("coal"));
        Assert.Equal(FuelTank.StarvedText, drill.Status);
    }

    [Fact]
    public void Generator_without_delivered_coal_makes_no_power()
    {
        var w = TestWorld.Create();
        var gen = (Generator)w.PlaceDirect("generator", new Int2(36, 59)); // inside the Nexus' power area
        w.Run(5);
        Assert.Equal(0f, gen.AvailableOutput(w));
        Assert.Equal(FuelTank.StarvedText, gen.Status);
        Assert.Equal(20, w.Storage.Count("coal"));
    }

    [Fact]
    public void Inserter_from_the_nexus_fuels_a_furnace()
    {
        var w = TestWorld.Create();
        int coal = w.Storage.Count("coal");
        w.PlaceDirect("inserter", TestWorld.NexusOrigin + new Int2(0, -1), Direction.North);
        var furnace = (Crafter)w.PlaceDirect("furnace", TestWorld.NexusOrigin + new Int2(0, -2));
        for (int i = 0; i < 5; i++) furnace.TryInsert("iron_ore");
        w.Run(900);
        Assert.True(furnace.Output.Count("iron_plate") > 0, $"made {furnace.Output.Count("iron_plate")}");
        Assert.True(w.Storage.Count("coal") < coal, "the inserter should have carried coal out of the Nexus");
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
