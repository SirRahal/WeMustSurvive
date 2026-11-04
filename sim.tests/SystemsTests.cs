using System.Numerics;
using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

/// <summary>Power, research, storage network, advanced logistics, super weapons and modifiers.</summary>
public class SystemsTests
{
    private static readonly Int2 NexusCenterTile = new(40, 64);

    // ------------------------------------------------------------------ power

    [Fact]
    public void Electric_drill_needs_a_pole_link_to_the_nexus()
    {
        var w = TestWorld.Create();
        var drill = (Drill)w.PlaceDirect("electric_drill", TestWorld.IronTile, Direction.East);
        var depot = (Depot)w.PlaceDirect("depot", TestWorld.IronTile + new Int2(1, 0));
        w.Run(300);
        Assert.Equal("No power", drill.Status);
        Assert.Equal(0, depot.Storage.Total);

        // Pole at (45,60) links to the Nexus (6.4 tiles) and covers the drill at (48,57).
        w.PlaceDirect("pole", new Int2(45, 60));
        w.Run(400);
        Assert.True(depot.Storage.Count("iron_ore") >= 4, $"mined {depot.Storage.Count("iron_ore")}");
    }

    [Fact]
    public void Overloaded_grid_browns_out_until_a_generator_is_added()
    {
        var w = TestWorld.Create();
        // Three busy electric furnaces (540 kW) on the Nexus' 200 kW.
        var furnaces = new[] { new Int2(33, 60), new Int2(36, 60), new Int2(39, 60) }
            .Select(t => (Crafter)w.PlaceDirect("electric_furnace", t)).ToList();
        foreach (var f in furnaces) for (int i = 0; i < 5; i++) f.TryInsert("iron_ore");
        w.Run(5);
        var grid = w.Power.GridOf(w.Nexus)!;
        Assert.InRange(grid.Satisfaction, 0.3f, 0.45f);
        Assert.All(furnaces, f => Assert.Equal(grid.Satisfaction, f.Power));

        var gen = (Generator)w.PlaceDirect("generator", new Int2(42, 60));
        for (int i = 0; i < 5; i++) gen.TryInsert("coal");
        w.Run(5);
        Assert.Equal(1f, w.Power.GridOf(w.Nexus)!.Satisfaction);
    }

    [Fact]
    public void Laser_turret_without_power_does_not_fire()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("laser_turret", new Int2(40, 40));
        var grunt = w.SpawnEnemy(w.Data.Enemies["grunt"], new Vector2(40.5f, 36.5f));
        w.Run(30);
        Assert.Equal(grunt.MaxHp, grunt.Hp);
    }

    [Fact]
    public void Laser_turret_near_the_nexus_fires_on_power()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("laser_turret", new Int2(40, 59));
        var grunt = w.SpawnEnemy(w.Data.Enemies["grunt"], new Vector2(40.5f, 52.5f));
        w.Run(60);
        Assert.True(grunt.Hp < grunt.MaxHp);
    }

    // ------------------------------------------------------------------ research

    [Fact]
    public void Research_costs_resources_needs_labs_and_unlocks_buildings()
    {
        var w = TestWorld.Create();
        w.Enqueue(new PlaceBuildingCommand(0, "splitter", new Int2(40, 59), Direction.North));
        w.Step();
        Assert.Contains(w.Events.OfType<MessageEvent>(), m => m.Text == "Research Logistics first");

        int iron = w.Storage.Count("iron_plate");
        w.Enqueue(new StartResearchCommand(0, "logistics"));
        w.Run(100);
        Assert.Equal(iron - 40, w.Storage.Count("iron_plate"));
        Assert.Equal(0, w.Research.Progress); // no labs yet

        w.PlaceDirect("lab", new Int2(33, 60));
        w.PlaceDirect("lab", new Int2(36, 60));
        w.Run(601); // 1200 lab-ticks at 2 labs
        Assert.True(w.Research.IsDone("logistics"));

        w.Enqueue(new PlaceBuildingCommand(0, "splitter", new Int2(40, 59), Direction.North));
        w.Step();
        Assert.IsType<ConstructionSite>(w.BuildingAt(new Int2(40, 59)));
    }

    [Fact]
    public void Research_requires_prerequisites()
    {
        var w = TestWorld.Create();
        w.Storage.Add("copper_plate", 500);
        w.Enqueue(new StartResearchCommand(0, "laser_turrets"));
        w.Step();
        Assert.Contains(w.Events.OfType<MessageEvent>(), m => m.Text == "Requires Electricity");
        Assert.Null(w.Research.Current);
    }

    [Fact]
    public void Every_tech_and_building_tech_reference_is_valid()
    {
        var data = TestWorld.Data;
        foreach (var t in data.TechList)
            Assert.All(t.Requires, r => Assert.True(data.Techs.ContainsKey(r), $"{t.Id} requires unknown {r}"));
        foreach (var b in data.BuildingList.Where(b => b.Tech != null))
            Assert.True(data.Techs.ContainsKey(b.Tech!), $"{b.Id} needs unknown tech {b.Tech}");
        foreach (var r in data.Recipes.Values)
            Assert.All(r.Inputs.Keys.Concat(r.Outputs.Keys), i => Assert.True(data.Items.ContainsKey(i), i));
        foreach (var b in data.BuildingList)
            Assert.All(b.Cost.Keys, i => Assert.True(data.Items.ContainsKey(i), $"{b.Id} costs unknown {i}"));
    }

    // ------------------------------------------------------------------ storage network & logistics

    [Fact]
    public void Depots_pay_for_buildings_when_the_nexus_is_empty()
    {
        var w = TestWorld.Create();
        var depot = (Depot)w.PlaceDirect("depot", new Int2(35, 60));
        depot.Storage.Add("iron_plate", 10);
        w.Storage.Remove("iron_plate", w.Storage.Count("iron_plate"));

        w.Enqueue(new PlaceBuildingCommand(0, "wall", new Int2(40, 59), Direction.North));
        w.Step();
        Assert.IsType<ConstructionSite>(w.BuildingAt(new Int2(40, 59)));
        Assert.Equal(6, depot.Storage.Count("iron_plate"));
    }

    [Fact]
    public void Splitter_deals_items_to_all_three_outputs()
    {
        var w = TestWorld.Create();
        var input = (Belt)w.PlaceDirect("belt", new Int2(30, 60), Direction.East);
        w.PlaceDirect("splitter", new Int2(31, 60), Direction.East);
        var ahead = (Belt)w.PlaceDirect("belt", new Int2(32, 60), Direction.East);
        var right = (Belt)w.PlaceDirect("belt", new Int2(31, 61), Direction.South);
        var left = (Belt)w.PlaceDirect("belt", new Int2(31, 59), Direction.North);

        for (int i = 0; i < 9; i++)
        {
            input.TryInsertAt("iron_plate", 0);
            w.Run(40);
        }
        Assert.NotEmpty(ahead.Items);
        Assert.NotEmpty(right.Items);
        Assert.NotEmpty(left.Items);
    }

    [Fact]
    public void Tunnel_carries_items_under_a_wall()
    {
        var w = TestWorld.Create();
        var input = (Belt)w.PlaceDirect("belt", new Int2(30, 62), Direction.East);
        w.PlaceDirect("tunnel_entrance", new Int2(31, 62), Direction.East);
        w.PlaceDirect("wall", new Int2(32, 62));
        w.PlaceDirect("wall", new Int2(33, 62));
        w.PlaceDirect("tunnel_exit", new Int2(34, 62), Direction.East);
        var output = (Belt)w.PlaceDirect("belt", new Int2(35, 62), Direction.East);

        input.TryInsertAt("copper_plate", 0);
        w.Run(200);
        Assert.Single(output.Items);
        Assert.Equal("copper_plate", output.Items[0].Item);
    }

    [Fact]
    public void Claim_zone_drills_yield_double()
    {
        var w = TestWorld.Create();
        var silver = new Int2(40, 13);
        Assert.True(w.Map.IsClaimZone(silver));
        var drill = (Drill)w.PlaceDirect("drill", silver, Direction.East);
        var depot = (Depot)w.PlaceDirect("depot", silver + new Int2(1, 0));
        for (int i = 0; i < 5; i++) drill.TryInsert("coal");

        w.Run(780); // 120 / 1.6 = 75 (76 with float rounding) ticks per cycle -> 10 cycles
        Assert.Equal(20, depot.Storage.Count("silver_ore"));
    }

    [Fact]
    public void Furnace_smelts_every_ore_tier()
    {
        var w = TestWorld.Create();
        var f = (Crafter)w.PlaceDirect("furnace", new Int2(35, 60));
        for (int i = 0; i < 5; i++) f.TryInsert("coal");
        foreach (var ore in new[] { "copper_ore", "silver_ore", "gold_ore" }) f.TryInsert(ore);
        w.Run(600);
        Assert.Equal(1, f.Output.Count("copper_plate"));
        Assert.Equal(1, f.Output.Count("silver_bar"));
        Assert.Equal(1, f.Output.Count("gold_bar"));
    }

    // ------------------------------------------------------------------ enemies

    [Fact]
    public void Spitter_stops_and_attacks_structures_in_range()
    {
        var w = TestWorld.Create();
        var wall = w.PlaceDirect("wall", new Int2(40, 52));
        var spitter = w.SpawnEnemy(w.Data.Enemies["spitter"], new Vector2(40.5f, 48.5f));
        w.Run(200);
        Assert.True(wall.Hp < wall.MaxHp);
        Assert.True(Vector2.Distance(spitter.Pos, new Vector2(40.5f, 48.5f)) < 0.01f);
    }

    [Fact]
    public void Sapper_hunts_logistics_and_explodes()
    {
        var w = TestWorld.Create();
        var belt = w.PlaceDirect("belt", new Int2(48, 40), Direction.South);
        var sapper = w.SpawnEnemy(w.Data.Enemies["sapper"], new Vector2(40.5f, 38.5f));
        w.Run(10 * World.TicksPerSecond);
        Assert.True(sapper.Dead || !w.Enemies.Contains(sapper));
        Assert.True(belt.Dead);
    }

    [Fact]
    public void Distractible_grunts_wreck_belts_in_their_path()
    {
        var w = TestWorld.Create();
        // Belts across the whole choke so the path must cross one.
        var belts = new[] { 37, 38, 39, 40, 41, 42 }.Select(x => w.PlaceDirect("belt", new Int2(x, 10), Direction.East)).ToList();
        for (int i = 0; i < 6; i++) w.SpawnEnemy(w.Data.Enemies["grunt"], new Vector2(38.5f + i % 3, 3.5f));
        w.Run(20 * World.TicksPerSecond);
        Assert.Contains(belts, b => b.Dead || b.Hp < b.MaxHp);
    }

    // ------------------------------------------------------------------ super weapons

    [Fact]
    public void Orbital_strike_charges_on_power_and_hits_after_a_delay()
    {
        var w = TestWorld.Create();
        var uplink = (SuperWeapon)w.PlaceDirect("satellite_uplink", new Int2(35, 63));
        var gen = (Generator)w.PlaceDirect("generator", new Int2(35, 60));
        for (int i = 0; i < 10; i++) gen.TryInsert("coal");

        w.Enqueue(new FireSuperWeaponCommand(0, uplink.Id, new Vector2(40.5f, 30.5f)));
        w.Step();
        Assert.Contains(w.Events.OfType<MessageEvent>(), m => m.Text.Contains("still charging"));

        w.Run(3600);
        Assert.True(uplink.IsReady(w));
        var grunts = Enumerable.Range(0, 4).Select(i => w.SpawnEnemy(w.Data.Enemies["brute"], new Vector2(39.5f + i, 30.5f))).ToList();
        w.Enqueue(new FireSuperWeaponCommand(0, uplink.Id, new Vector2(40.5f, 30.5f)));
        w.Step();
        Assert.Contains(w.Events, e => e is StrikeIncomingEvent);
        Assert.False(uplink.IsReady(w));
        w.Run(65);
        Assert.All(grunts, g => Assert.True(g.Dead));
    }

    // ------------------------------------------------------------------ modifiers

    [Fact]
    public void Modifiers_change_mining_speed_wall_health_and_starting_iron()
    {
        var mods = new Modifiers().Add(Stat.MiningSpeed, 1.0f).Add(Stat.WallHp, 0.5f).Add(Stat.StartingIron, 10);
        var w = new World(TestWorld.Data, TestWorld.Funnel, 1, 1, mods);

        Assert.Equal(TestWorld.Funnel.Map.StartingItems["iron_plate"] + 10, w.Storage.Count("iron_plate"));
        Assert.Equal(600, w.PlaceDirect("wall", new Int2(40, 59)).MaxHp);

        w.PlaceDirect("drill", TestWorld.CoalTile, Direction.East);
        var depot = (Depot)w.PlaceDirect("depot", TestWorld.CoalTile + new Int2(1, 0));
        w.Run(600);
        Assert.Equal(10, depot.Storage.Count("coal")); // double speed: 60 ticks per coal
    }
}
