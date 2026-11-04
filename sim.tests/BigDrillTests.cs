using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

/// <summary>Drills are 2x2: they mine every ore tile under them and output from the middle of their facing edge.</summary>
public class BigDrillTests
{
    /// <summary>Clear ground on the Funnel map (no ore, no buildings), north of the Nexus.</summary>
    private static readonly Int2 Clear = new(36, 50);

    [Fact]
    public void A_drill_mines_every_ore_tile_under_it()
    {
        var w = TestWorld.Create();
        // Two tiles of iron (2 each) under the drill's top row; the bottom row has none.
        w.Map.SetOre(Clear, "iron_ore", 2, claimZone: false);
        w.Map.SetOre(Clear + new Int2(1, 0), "iron_ore", 2, claimZone: false);
        var drill = (Drill)w.PlaceDirect("drill", Clear, Direction.East);
        var depot = (Depot)w.PlaceDirect("depot", Clear + new Int2(2, 0));
        drill.TryInsert("coal"); // one coal lasts 20 s, plenty for 4 ore at 2 s each
        w.Run(10);
        Assert.Equal(4, drill.OreStart);
        Assert.Equal(4, drill.OreLeft);

        w.Run(600);

        Assert.Equal(4, depot.Storage.Count("iron_ore")); // more than either tile held on its own
        Assert.Equal(0, w.Map.OreAmount(Clear));
        Assert.Equal(0, w.Map.OreAmount(Clear + new Int2(1, 0)));
        Assert.True(drill.Depleted);
    }

    [Fact]
    public void A_drill_facing_east_outputs_two_tiles_right_of_its_origin()
    {
        var w = TestWorld.Create();
        w.Map.SetOre(Clear, "coal", 50, claimZone: false);
        var drill = (Drill)w.PlaceDirect("drill", Clear, Direction.East);
        var depot = (Depot)w.PlaceDirect("depot", Clear + new Int2(2, 0));
        Assert.Equal(Clear + new Int2(2, 0), drill.Front);
        Assert.Same(depot, w.BuildingAt(drill.Front));

        w.Run(250); // on coal it fuels itself: 2 coal at 120 ticks each
        Assert.Equal(2, depot.Storage.Count("coal"));
    }

    [Fact]
    public void A_drill_needs_ore_under_at_least_one_of_its_four_tiles()
    {
        var w = TestWorld.Create();
        var def = w.Data.Buildings["drill"];
        Assert.Equal(2, def.Size);
        Assert.Equal("Drills must be placed on ore", w.CheckPlacement(0, def, Clear));

        w.Map.SetOre(Clear + new Int2(1, 1), "iron_ore", 10, claimZone: false); // only the bottom-right tile
        Assert.Null(w.CheckPlacement(0, def, Clear));
    }
}
