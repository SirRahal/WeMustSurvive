using System.Numerics;
using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class ConstructionTests
{
    private static Engineer Eng(World w) => w.Engineers[0];

    [Fact]
    public void Build_order_creates_paid_site_then_engineer_walks_over_and_builds_it()
    {
        var w = TestWorld.Create();
        int plates = w.Storage.Count("iron_plate");

        w.Enqueue(new PlaceBuildingCommand(0, "drill", TestWorld.CoalTile, Direction.East));
        w.Step();

        var site = Assert.IsType<ConstructionSite>(w.BuildingAt(TestWorld.CoalTile));
        Assert.False(site.Started);
        Assert.Equal(plates - 10, w.Storage.Count("iron_plate"));

        w.Run(10 * World.TicksPerSecond);

        var drill = Assert.IsType<Drill>(w.BuildingAt(TestWorld.CoalTile));
        Assert.Equal(Direction.East, drill.Dir);
        Assert.Empty(Eng(w).Orders);
        Assert.True(Vector2.Distance(Eng(w).Pos, drill.Center) < 2.5f);
    }

    [Fact]
    public void Site_gains_hp_while_under_construction()
    {
        var w = TestWorld.Create();
        var tile = new Int2(40, 59); // within reach of the engineer's spawn
        w.Enqueue(new PlaceBuildingCommand(0, "gun_turret", tile, Direction.North));
        w.Run(30);

        var site = Assert.IsType<ConstructionSite>(w.BuildingAt(tile));
        Assert.True(site.Started);
        Assert.True(site.Hp > site.Def.MaxHp * ConstructionSite.StartHpFraction);
        Assert.True(site.Hp < site.Def.MaxHp);
    }

    [Fact]
    public void Queued_orders_run_in_sequence()
    {
        var w = TestWorld.Create();
        var a = new Int2(36, 59);
        var b = new Int2(44, 59);
        w.Enqueue(new PlaceBuildingCommand(0, "wall", a, Direction.North));
        w.Enqueue(new PlaceBuildingCommand(0, "wall", b, Direction.North, Queue: true));
        w.Step();
        Assert.Equal(2, Eng(w).Orders.Count);

        w.Run(10 * World.TicksPerSecond);

        Assert.IsType<Wall>(w.BuildingAt(a));
        Assert.IsType<Wall>(w.BuildingAt(b));
    }

    [Fact]
    public void Shift_chains_orders_in_sequence()
    {
        var w = TestWorld.Create();
        var wall = new Int2(36, 59);
        var detour = new Vector2(45.5f, 61.5f);
        var turret = new Int2(44, 59);
        w.Enqueue(new PlaceBuildingCommand(0, "wall", wall, Direction.North));
        w.Enqueue(new MoveCommand(0, detour, Queue: true));
        w.Enqueue(new PlaceBuildingCommand(0, "gun_turret", turret, Direction.North, Queue: true));
        w.Step();

        Assert.Collection(Eng(w).Orders,
            o => Assert.IsType<BuildOrder>(o),
            o => Assert.IsType<MoveOrder>(o),
            o => Assert.IsType<BuildOrder>(o));

        w.Run(15 * World.TicksPerSecond);
        Assert.IsType<Wall>(w.BuildingAt(wall));
        Assert.IsType<Turret>(w.BuildingAt(turret));
        Assert.Empty(Eng(w).Orders);
    }

    [Fact]
    public void Unqueued_order_replaces_the_plan_and_refunds_unstarted_blueprints()
    {
        var w = TestWorld.Create();
        int plates = w.Storage.Count("iron_plate");
        var far = new Int2(40, 20);
        w.Enqueue(new PlaceBuildingCommand(0, "gun_turret", far, Direction.North));
        w.Step();
        w.Enqueue(new MoveCommand(0, new Vector2(45.5f, 61.5f)));
        w.Step();

        Assert.Null(w.BuildingAt(far));
        Assert.Equal(plates, w.Storage.Count("iron_plate"));
        Assert.IsType<MoveOrder>(Assert.Single(Eng(w).Orders));
        Assert.Contains(w.Events.OfType<MessageEvent>(), m => m.Text.Contains("hold Shift"));
    }

    [Fact]
    public void Unqueued_order_keeps_half_built_sites()
    {
        var w = TestWorld.Create();
        var near = new Int2(40, 59);
        w.Enqueue(new PlaceBuildingCommand(0, "gun_turret", near, Direction.North));
        w.Run(30); // construction under way
        w.Enqueue(new PlaceBuildingCommand(0, "wall", new Int2(36, 59), Direction.North));
        w.Step();

        Assert.IsType<ConstructionSite>(w.BuildingAt(near));
    }

    [Fact]
    public void Downed_engineer_keeps_its_build_queue()
    {
        var w = TestWorld.Create();
        var tile = new Int2(40, 20);
        w.Enqueue(new PlaceBuildingCommand(0, "wall", tile, Direction.North));
        w.Step();
        Eng(w).TakeDamage(w, 1000);

        Assert.False(Eng(w).Alive);
        Assert.Single(Eng(w).Orders);
        Assert.IsType<ConstructionSite>(w.BuildingAt(tile));
    }

    [Fact]
    public void Stop_refunds_unstarted_sites_but_keeps_started_ones()
    {
        var w = TestWorld.Create();
        int plates = w.Storage.Count("iron_plate");
        var near = new Int2(40, 59);
        var far = new Int2(40, 20);
        w.Enqueue(new PlaceBuildingCommand(0, "gun_turret", near, Direction.North));
        w.Enqueue(new PlaceBuildingCommand(0, "gun_turret", far, Direction.North, Queue: true));
        w.Run(20);
        w.Enqueue(new StopCommand(0));
        w.Step();

        Assert.IsType<ConstructionSite>(w.BuildingAt(near));
        Assert.Null(w.BuildingAt(far));
        Assert.Equal(plates - 20, w.Storage.Count("iron_plate"));
        Assert.Empty(Eng(w).Orders);
    }

    [Fact]
    public void Right_click_on_unfinished_site_resumes_construction()
    {
        var w = TestWorld.Create();
        var tile = new Int2(40, 59);
        w.Enqueue(new PlaceBuildingCommand(0, "wall", tile, Direction.North));
        w.Run(10);
        w.Enqueue(new StopCommand(0));
        w.Step();
        Assert.IsType<ConstructionSite>(w.BuildingAt(tile));

        w.Enqueue(new RepairCommand(0, tile));
        w.Run(2 * World.TicksPerSecond);
        Assert.IsType<Wall>(w.BuildingAt(tile));
    }

    [Fact]
    public void Removing_site_refunds_full_cost()
    {
        var w = TestWorld.Create();
        int plates = w.Storage.Count("iron_plate");
        w.Enqueue(new PlaceBuildingCommand(0, "gun_turret", new Int2(40, 20), Direction.North));
        w.Step();
        w.Enqueue(new RemoveBuildingCommand(0, new Int2(40, 20)));
        w.Step();
        Assert.Equal(plates, w.Storage.Count("iron_plate"));
    }

    [Fact]
    public void Unstarted_sites_do_not_block_enemies()
    {
        var w = TestWorld.Create();
        foreach (int x in new[] { 37, 38, 39, 40, 41, 42 })
            w.Enqueue(new PlaceBuildingCommand(0, "wall", new Int2(x, 7), Direction.North, Queue: true));
        w.Step();

        var site = Assert.IsType<ConstructionSite>(w.BuildingAt(new Int2(40, 7)));
        Assert.False(site.BlocksEnemies);
        // Entering the choke costs nothing extra: distance from spawn equals the open-map distance.
        float sealedDist = w.Flow.DistanceAt(w.Map.SpawnTiles[0]);
        Assert.Equal(TestWorld.Create().Also(o => o.Step()).Flow.DistanceAt(w.Map.SpawnTiles[0]), sealedDist);
    }

    [Fact]
    public void Move_order_paths_around_walls()
    {
        var w = TestWorld.Create();
        // A wall line directly between the engineer (40.5, 61.5) and the target; open ground past both ends.
        for (int x = 34; x <= 46; x++) w.PlaceDirect("wall", new Int2(x, 58));
        var target = new Vector2(40.5f, 54.5f);
        w.Enqueue(new MoveCommand(0, target));

        for (int i = 0; i < 10 * World.TicksPerSecond; i++)
        {
            w.Step();
            Assert.IsNotType<Wall>(w.BuildingAt(Int2.Floor(Eng(w).Pos)));
        }

        Assert.True(Vector2.Distance(Eng(w).Pos, target) < 0.01f);
    }

    [Fact]
    public void Move_to_rock_is_rejected()
    {
        var w = TestWorld.Create();
        w.Enqueue(new MoveCommand(0, new Vector2(1.5f, 40.5f)));
        w.Step();
        Assert.Contains(w.Events.OfType<MessageEvent>(), m => m.Text == "Can't move there");
    }
}
