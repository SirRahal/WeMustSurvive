using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class AmmoAndChainTests
{
    [Fact]
    public void Belt_ending_at_an_inserter_that_picks_from_it_is_connected()
    {
        var w = TestWorld.Create();
        // workshop -> inserter -> belt, belt -> inserter -> turret (belt points at the second inserter).
        w.PlaceDirect("workshop", new Int2(30, 60));
        w.PlaceDirect("inserter", new Int2(33, 60), Direction.East); // workshop covers 30-32
        var belt1 = w.PlaceDirect("belt", new Int2(34, 60), Direction.East);
        var belt2 = w.PlaceDirect("belt", new Int2(35, 60), Direction.East);
        var feeder = w.PlaceDirect("inserter", new Int2(36, 60), Direction.East);
        w.PlaceDirect("gun_turret", new Int2(37, 60));
        w.Step();

        var issues = LogisticsCheck.Analyze(w);
        Assert.False(issues.ContainsKey(belt1.Id));
        Assert.False(issues.ContainsKey(belt2.Id));
        Assert.False(issues.ContainsKey(feeder.Id));
    }

    [Fact]
    public void Belt_with_an_inserter_picking_from_its_side_is_connected()
    {
        var w = TestWorld.Create();
        var belt = w.PlaceDirect("belt", new Int2(32, 60), Direction.East); // points at empty ground
        w.PlaceDirect("inserter", new Int2(32, 61), Direction.South);        // takes from the belt above it
        w.PlaceDirect("gun_turret", new Int2(32, 62));
        w.Step();
        Assert.False(LogisticsCheck.Analyze(w).ContainsKey(belt.Id));
    }

    /// <summary>Inserter at (31,61) facing east into a turret; a belt with ammo runs past its north (left) side.</summary>
    private static (World W, Inserter Ins, Turret Turret, Belt Belt) SideFeed(InputSide side)
    {
        var w = TestWorld.Create();
        var belt = (Belt)w.PlaceDirect("belt", new Int2(31, 60), Direction.East);
        var ins = (Inserter)w.PlaceDirect("inserter", new Int2(31, 61), Direction.East);
        ins.Input = side;
        var turret = (Turret)w.PlaceDirect("gun_turret", new Int2(32, 61));
        belt.TryInsertAt("ammo_magazine", 0);
        return (w, ins, turret, belt);
    }

    [Fact]
    public void Inserter_takes_from_a_side_at_ninety_degrees()
    {
        var (w, ins, turret, belt) = SideFeed(InputSide.Auto);
        w.Run(120);
        Assert.Equal(1, turret.Ammo);
        Assert.Equal(Direction.North, ins.TookFrom);
        Assert.False(LogisticsCheck.Analyze(w).ContainsKey(belt.Id)); // the side pick-up counts as delivery
        Assert.False(LogisticsCheck.Analyze(w).ContainsKey(ins.Id));
    }

    [Fact]
    public void Fixed_back_input_ignores_the_side()
    {
        var (w, _, turret, _) = SideFeed(InputSide.Back);
        w.Run(120);
        Assert.Equal(0, turret.Ammo);
    }

    [Fact]
    public void Fixed_left_input_takes_from_the_left()
    {
        var (w, _, turret, _) = SideFeed(InputSide.Left);
        w.Run(120);
        Assert.Equal(1, turret.Ammo);
    }

    [Fact]
    public void Auto_input_prefers_behind_over_the_sides()
    {
        var (w, ins, turret, _) = SideFeed(InputSide.Auto);
        var behind = (Belt)w.PlaceDirect("belt", new Int2(30, 61), Direction.North);
        behind.TryInsertAt("ammo_magazine", Belt.DropPosition);
        w.Run(10);
        Assert.Equal(Direction.West, ins.TookFrom);
    }

    [Fact]
    public void Input_side_command_changes_the_side()
    {
        var (w, ins, _, _) = SideFeed(InputSide.Auto);
        w.Enqueue(new SetInserterInputCommand(0, ins.Origin, InputSide.Right));
        w.Step();
        Assert.Equal(InputSide.Right, ins.Input);
        var json = CommandCodec.Serialize(new SetInserterInputCommand(0, ins.Origin, InputSide.Left));
        Assert.IsType<SetInserterInputCommand>(CommandCodec.Deserialize(json));
    }

    [Fact]
    public void Turrets_hold_a_limited_amount_of_ammo()
    {
        var w = TestWorld.Create();
        var turret = (Turret)w.PlaceDirect("gun_turret", new Int2(40, 59));
        int accepted = 0;
        while (turret.TryInsert("ammo_magazine")) accepted++;
        Assert.Equal(turret.Def.AmmoCapacity, accepted);
        Assert.Contains("(full)", turret.Status);
    }

    [Fact]
    public void Munitions_works_pauses_when_storage_has_plenty_of_ammo()
    {
        var w = TestWorld.Create();
        var workshop = (Crafter)w.PlaceDirect("workshop", new Int2(35, 60));
        w.Storage.Add("ammo_magazine", 100);
        for (int i = 0; i < 4; i++) workshop.TryInsert("iron_plate");
        w.Run(300);
        Assert.Equal(0, workshop.Output.Count("ammo_magazine"));
        Assert.Contains("Paused", workshop.Status);

        w.Storage.Remove("ammo_magazine", 100);
        w.Run(300);
        Assert.True(workshop.Output.Count("ammo_magazine") > 0);
    }
}
