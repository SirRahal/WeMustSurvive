using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class LogisticsTests
{
    /// <summary>Six belts running east along y=64 into the Nexus' west face (x=39).</summary>
    private static List<Belt> BeltLineIntoNexus(World w)
    {
        var belts = new List<Belt>();
        for (int x = 33; x <= 38; x++)
            belts.Add((Belt)w.PlaceDirect("belt", new Int2(x, 64), Direction.East));
        return belts;
    }

    [Fact]
    public void Belt_carries_item_into_nexus_at_belt_speed()
    {
        var w = TestWorld.Create();
        var belts = BeltLineIntoNexus(w);
        int before = w.Storage.Count("iron_plate");

        Assert.True(belts[0].TryInsertAt("iron_plate", 0));
        // 6 tiles * 256 units / 8 units per tick = 192 ticks of travel.
        w.Run(190);
        Assert.Equal(before, w.Storage.Count("iron_plate"));
        w.Run(5);
        Assert.Equal(before + 1, w.Storage.Count("iron_plate"));
    }

    [Fact]
    public void Belt_keeps_spacing_and_backs_up_when_blocked()
    {
        var w = TestWorld.Create();
        // A single belt pointing at empty ground: items can't leave.
        var belt = (Belt)w.PlaceDirect("belt", new Int2(45, 62), Direction.North);
        for (int i = 0; i < 20; i++)
        {
            belt.TryInsertAt("coal", 0);
            w.Run(10);
        }

        Assert.Equal(Belt.Length / Belt.Spacing, belt.Items.Count);
        for (int i = 1; i < belt.Items.Count; i++)
            Assert.True(belt.Items[i - 1].Pos - belt.Items[i].Pos >= Belt.Spacing);
    }

    [Fact]
    public void Coal_drill_mines_at_its_rate_and_fuels_adjacent_furnace()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("drill", TestWorld.CoalTile, Direction.East);
        var furnace = (Crafter)w.PlaceDirect("furnace", TestWorld.CoalTile + new Int2(1, 0));
        int oreBefore = w.Map.OreAmount(TestWorld.CoalTile);

        w.Run(600); // mineTicks = 120 -> 5 coal

        Assert.Equal(5, furnace.Coal);
        Assert.Equal(oreBefore - 5, w.Map.OreAmount(TestWorld.CoalTile));
    }

    [Fact]
    public void Iron_drill_without_fuel_does_nothing()
    {
        var w = TestWorld.Create();
        var drill = (Drill)w.PlaceDirect("drill", TestWorld.IronTile, Direction.East);
        w.Run(600);
        Assert.Equal("No fuel", drill.Status);
    }

    [Fact]
    public void Drill_furnace_inserter_chain_produces_plates_on_belt()
    {
        var w = TestWorld.Create();
        var drill = (Drill)w.PlaceDirect("drill", TestWorld.IronTile, Direction.East);
        var furnace = (Crafter)w.PlaceDirect("furnace", TestWorld.IronTile + new Int2(1, 0));
        w.PlaceDirect("inserter", TestWorld.IronTile + new Int2(2, 0), Direction.East);
        var belt = (Belt)w.PlaceDirect("belt", TestWorld.IronTile + new Int2(3, 0), Direction.North);
        drill.TryInsert("coal");
        furnace.TryInsert("coal");

        w.Run(1200);

        // ~10 ore mined at 1 per 120 ticks; furnace runs 1:1. The belt dead-ends, so it backs up:
        // an inserter dropping mid-tile can fill 3 of the 4 slots, and the rest stays in the furnace.
        Assert.Equal(3, belt.Items.Count);
        Assert.All(belt.Items, it => Assert.Equal("iron_plate", it.Item));
        Assert.True(furnace.Output.Count("iron_plate") > 0);
    }

    [Fact]
    public void Inserter_from_nexus_requires_filter()
    {
        var w = TestWorld.Create();
        // Inserter directly above the Nexus, facing north, feeding a turret.
        var ins = (Inserter)w.PlaceDirect("inserter", TestWorld.NexusOrigin + new Int2(0, -1), Direction.North);
        var turret = (Turret)w.PlaceDirect("gun_turret", TestWorld.NexusOrigin + new Int2(0, -2));

        w.Run(120);
        Assert.Equal(0, turret.Ammo);

        w.Enqueue(new SetInserterFilterCommand(0, ins.Origin, "ammo_magazine"));
        w.Run(120);
        Assert.True(turret.Ammo >= 2);
    }

    [Fact]
    public void Placing_deducts_cost_and_removing_refunds_it()
    {
        var w = TestWorld.Create();
        int start = w.Storage.Count("iron_plate");
        var tile = new Int2(40, 60);

        w.Enqueue(new PlaceBuildingCommand(0, "gun_turret", tile, Direction.North));
        w.Step();
        Assert.Equal(start - 20, w.Storage.Count("iron_plate"));

        w.Enqueue(new RemoveBuildingCommand(0, tile));
        w.Step();
        Assert.Equal(start, w.Storage.Count("iron_plate"));
        Assert.Null(w.BuildingAt(tile));
    }

    [Fact]
    public void Placement_rejected_when_unaffordable_or_on_rock()
    {
        var w = TestWorld.Create();
        w.Storage.Remove("iron_plate", w.Storage.Count("iron_plate"));

        w.Enqueue(new PlaceBuildingCommand(0, "wall", new Int2(40, 60), Direction.North));
        w.Enqueue(new PlaceBuildingCommand(0, "wall", new Int2(2, 40), Direction.North));
        w.Step();

        var messages = w.Events.OfType<MessageEvent>().Select(m => m.Text).ToList();
        Assert.Contains("Not enough Iron Plate", messages);
        Assert.Contains("Can't build there", messages);
        Assert.Null(w.BuildingAt(new Int2(40, 60)));
    }

    [Fact]
    public void Drill_must_be_on_ore()
    {
        var w = TestWorld.Create();
        w.Enqueue(new PlaceBuildingCommand(0, "drill", new Int2(40, 60), Direction.North));
        w.Step();
        Assert.Contains(w.Events.OfType<MessageEvent>(), m => m.Text == "Drills must be placed on ore");
    }
}
