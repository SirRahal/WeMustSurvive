using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

/// <summary>View-only events for items going into and out of the storage network (drive the Nexus animations).</summary>
public class StorageEventTests
{
    private static List<SimEvent> RunCollecting(World w, int ticks)
    {
        var seen = new List<SimEvent>();
        for (int i = 0; i < ticks; i++)
        {
            w.Step();
            seen.AddRange(w.Events);
        }
        return seen;
    }

    [Fact]
    public void A_belt_delivering_into_the_nexus_reports_each_item()
    {
        var w = TestWorld.Create();
        var belt = (Belt)w.PlaceDirect("belt", new Int2(38, 64), Direction.East); // Nexus west face is (39,64)
        belt.TryInsertAt("iron_plate", 200);
        var stored = RunCollecting(w, 120).OfType<ItemStoredEvent>().ToList();
        var e = Assert.Single(stored);
        Assert.Equal("iron_plate", e.Item);
        Assert.Equal(w.Nexus.Origin, e.Storage);
        Assert.Equal(39f, e.From.X, 3); // the belt's front edge
    }

    [Fact]
    public void An_inserter_taking_from_the_nexus_reports_it()
    {
        var w = TestWorld.Create();
        // Nexus west face (39,64) <- inserter at (38,64) facing west -> belt at (37,64); filtered so it may drain storage.
        var ins = (Inserter)w.PlaceDirect("inserter", new Int2(38, 64), Direction.West);
        ins.Filter = "iron_plate";
        w.PlaceDirect("belt", new Int2(37, 64), Direction.West);
        var taken = RunCollecting(w, 60).OfType<ItemTakenEvent>().ToList();
        Assert.NotEmpty(taken);
        Assert.All(taken, e => Assert.Equal(w.Nexus.Origin, e.Storage));
        Assert.Equal("iron_plate", taken[0].Item);
    }
}
