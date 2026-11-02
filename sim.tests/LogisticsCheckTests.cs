using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class LogisticsCheckTests
{
    [Fact]
    public void Belt_line_into_the_nexus_is_connected()
    {
        var w = TestWorld.Create();
        for (int x = 34; x <= 38; x++) w.PlaceDirect("belt", new Int2(x, 64), Direction.East);
        Assert.Empty(LogisticsCheck.Analyze(w));
    }

    [Fact]
    public void Dead_end_belt_line_reports_where_the_chain_ends()
    {
        var w = TestWorld.Create();
        var belts = Enumerable.Range(30, 4).Select(x => w.PlaceDirect("belt", new Int2(x, 60), Direction.East)).ToList();
        var issues = LogisticsCheck.Analyze(w);

        Assert.Equal(4, issues.Count);
        Assert.All(belts, b => Assert.Same(belts[^1], issues[b.Id].End));
        Assert.Contains("empty ground", issues[belts[0].Id].Reason);
    }

    [Fact]
    public void Belt_into_a_furnace_needs_an_inserter()
    {
        var w = TestWorld.Create();
        var belt = w.PlaceDirect("belt", new Int2(30, 60), Direction.East);
        w.PlaceDirect("furnace", new Int2(31, 60));
        Assert.Contains("inserter", LogisticsCheck.Analyze(w)[belt.Id].Reason);
    }

    [Fact]
    public void Inserters_need_a_source_a_target_and_a_filter_on_storage()
    {
        var w = TestWorld.Create();
        var lonely = w.PlaceDirect("inserter", new Int2(30, 60), Direction.East);
        // Nexus -> belt with "any item" would empty storage, so it needs a filter...
        var ontoBelt = (Inserter)w.PlaceDirect("inserter", new Int2(38, 65), Direction.West);
        w.PlaceDirect("belt", new Int2(37, 65), Direction.West);
        w.PlaceDirect("depot", new Int2(36, 65));
        // ...but Nexus -> machine with "any item" only takes what the machine uses, so it's fine.
        var intoTurret = (Inserter)w.PlaceDirect("inserter", new Int2(38, 63), Direction.West);
        w.PlaceDirect("gun_turret", new Int2(37, 63));
        var issues = LogisticsCheck.Analyze(w);

        Assert.Contains("nothing behind", issues[lonely.Id].Reason);
        Assert.Equal("NEEDS FILTER", issues[ontoBelt.Id].Short);
        Assert.False(issues.ContainsKey(intoTurret.Id));

        ontoBelt.Filter = "iron_plate";
        Assert.False(LogisticsCheck.Analyze(w).ContainsKey(ontoBelt.Id));
    }

    [Fact]
    public void Drill_feeding_a_furnace_is_fine_but_pointing_at_nothing_is_not()
    {
        var w = TestWorld.Create();
        var fed = w.PlaceDirect("drill", TestWorld.IronTile, Direction.East);
        w.PlaceDirect("furnace", TestWorld.IronTile + new Int2(1, 0));
        var stray = w.PlaceDirect("drill", TestWorld.CoalTile, Direction.North);
        var issues = LogisticsCheck.Analyze(w);

        Assert.False(issues.ContainsKey(fed.Id));
        Assert.Contains("empty ground", issues[stray.Id].Reason);
    }

    [Fact]
    public void Tunnel_without_an_exit_is_reported()
    {
        var w = TestWorld.Create();
        var tin = w.PlaceDirect("tunnel_entrance", new Int2(31, 62), Direction.East);
        Assert.Contains("Tunnel Out", LogisticsCheck.Analyze(w)[tin.Id].Reason);
        w.PlaceDirect("tunnel_exit", new Int2(34, 62), Direction.East);
        w.PlaceDirect("depot", new Int2(35, 62));
        Assert.Empty(LogisticsCheck.Analyze(w));
    }

    [Fact]
    public void Inserters_pick_their_filter_from_their_neighbours()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("gun_turret", new Int2(37, 63));
        w.PlaceDirect("workshop", new Int2(37, 65));
        var intoWorkshop = (Inserter)w.PlaceDirect("inserter", new Int2(38, 65), Direction.West);
        var intoTurret = (Inserter)w.PlaceDirect("inserter", new Int2(37, 64), Direction.North);
        w.Step();

        // Default is "any item".
        Assert.False(intoTurret.AutoFilter);
        Assert.Null(intoTurret.Filter);

        // The Auto filter button picks from the neighbours.
        w.Enqueue(new SetInserterFilterCommand(0, intoWorkshop.Origin, null, Auto: true));
        w.Enqueue(new SetInserterFilterCommand(0, intoTurret.Origin, null, Auto: true));
        w.Run(2);
        Assert.Equal("iron_plate", intoWorkshop.Filter);
        Assert.Equal("ammo_magazine", intoTurret.Filter);
        Assert.True(intoTurret.AutoFilter);

        // Picking "any item" by hand switches auto off again.
        w.Enqueue(new SetInserterFilterCommand(0, intoTurret.Origin, null));
        w.Run(2);
        Assert.False(intoTurret.AutoFilter);
        Assert.Null(intoTurret.Filter);
    }

    [Fact]
    public void Backwards_inserter_is_called_out()
    {
        var w = TestWorld.Create();
        w.PlaceDirect("gun_turret", new Int2(37, 63));
        w.PlaceDirect("workshop", new Int2(37, 65));
        var ins = w.PlaceDirect("inserter", new Int2(37, 64), Direction.South); // takes from the turret, gives to the workshop
        var issue = LogisticsCheck.Analyze(w)[ins.Id];
        Assert.Equal("WRONG WAY", issue.Short);
        Assert.Contains("R twice", issue.Reason);
    }

    [Fact]
    public void Belts_laid_away_from_the_nexus_are_called_out()
    {
        var w = TestWorld.Create();
        // Starts beside the Nexus' west face (x=38) and runs west, away from it.
        var belts = new[] { 38, 37, 36 }.Select(x => w.PlaceDirect("belt", new Int2(x, 64), Direction.West)).ToList();
        var issue = LogisticsCheck.Analyze(w)[belts[0].Id];
        Assert.Equal("WRONG WAY", issue.Short);
        Assert.Same(belts[^1], issue.End);
    }

    [Fact]
    public void Drill_pointing_away_from_its_furnace_should_rotate()
    {
        var w = TestWorld.Create();
        var drill = w.PlaceDirect("drill", TestWorld.IronTile, Direction.North);
        w.PlaceDirect("furnace", TestWorld.IronTile + new Int2(1, 0));
        Assert.Equal("ROTATE", LogisticsCheck.Analyze(w)[drill.Id].Short);
    }

    [Fact]
    public void The_tutorial_starter_base_has_no_issues()
    {
        var w = TestWorld.Create();
        var def = TutorialDef.Load(f => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", f)), "funnel");
        foreach (var h in def.Steps.SelectMany(s => s.Hints).Where(h => !h.IsMarker)) w.PlaceDirect(h.Building, h.Tile, h.Dir);
        ((Inserter)w.BuildingAt(new Int2(38, 65))!).Filter = "iron_plate";
        Assert.Empty(LogisticsCheck.Analyze(w));
    }
}
