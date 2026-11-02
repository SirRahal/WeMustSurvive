using System.Numerics;
using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class EngineerReachTests
{
    [Fact]
    public void Engineer_at_a_tile_edge_still_reaches_a_site_whose_tile_centre_is_in_reach()
    {
        var w = TestWorld.Create();
        // Walk to the west edge of tile (44,61): its centre is exactly 1.5 from a site at (46,61), the edge is 2.0.
        w.Enqueue(new MoveCommand(0, new Vector2(44.01f, 61.9f)));
        w.Run(10 * World.TicksPerSecond);
        w.Enqueue(new PlaceBuildingCommand(0, "wall", new Int2(46, 61), Direction.North));
        w.Run(5 * World.TicksPerSecond);
        Assert.IsType<Wall>(w.BuildingAt(new Int2(46, 61)));
    }
}
