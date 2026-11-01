using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

public class TutorialTests
{
    private static readonly TutorialDef Def = TutorialDef.Load(
        f => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", f)), "funnel");

    private static IEnumerable<TutorialHint> BuildingHints =>
        Def.Steps.SelectMany(s => s.Hints).Where(h => !h.IsMarker);

    [Fact]
    public void Every_hinted_placement_is_legal_on_the_fresh_map()
    {
        var w = TestWorld.Create();
        foreach (var h in BuildingHints)
        {
            Assert.Null(w.CheckPlacement(0, w.Data.Buildings[h.Building], h.Tile));
            w.PlaceDirect(h.Building, h.Tile, h.Dir);
        }
    }

    [Fact]
    public void Suggested_starter_base_delivers_plates_and_arms_the_turret()
    {
        var w = TestWorld.Create();
        foreach (var h in BuildingHints) w.PlaceDirect(h.Building, h.Tile, h.Dir);
        w.Enqueue(new SetInserterFilterCommand(0, new Int2(38, 65), "iron_plate"));

        w.Run(90 * World.TicksPerSecond);

        Assert.True(w.Nexus.Received.Count("iron_plate") >= 20, $"only {w.Nexus.Received.Count("iron_plate")} plates delivered");
        var turret = w.Buildings.OfType<Turret>().Single();
        Assert.True(turret.Ammo > 0);
    }

    [Fact]
    public void Waves_stay_held_until_the_release_step()
    {
        var w = TestWorld.Create();
        var tut = new Tutorial(Def, w);
        int before = w.Waves.TicksUntilNextWave;
        w.Run(600);
        Assert.Equal(before, w.Waves.TicksUntilNextWave);

        w.Enqueue(new EndPrepCommand(0));
        w.Step();
        Assert.Equal(0, w.Waves.WavesStarted);
        Assert.Contains(w.Events.OfType<MessageEvent>(), m => m.Text.Contains("on hold"));
    }

    [Fact]
    public void Playing_the_tutorial_as_scripted_completes_every_step()
    {
        var w = TestWorld.Create();
        var tut = new Tutorial(Def, w);
        var seen = new List<string>();

        for (int guard = 0; guard < 200 && !tut.Finished; guard++)
        {
            var step = tut.Current;
            if (seen.LastOrDefault() != step.Id) seen.Add(step.Id);

            if (tut.NeedsNext)
            {
                tut.Next(w);
                continue;
            }

            // Do what the step asks, the way a player would (but placing buildings instantly).
            switch (step.Goal)
            {
                case "engineerAt":
                    w.Enqueue(new MoveCommand(0, step.Hints[0].Tile.Center));
                    break;
                case "nexusFilter":
                    w.Enqueue(new SetInserterFilterCommand(0, step.Hints[0].Tile, step.Item));
                    break;
                case "waveStarted":
                    w.Step(); // let the release command apply first
                    w.Enqueue(new EndPrepCommand(0));
                    break;
            }
            foreach (var h in step.Hints.Where(h => !h.IsMarker && !Tutorial.IsHintDone(w, h)))
                w.PlaceDirect(h.Building, h.Tile, h.Dir);

            int index = tut.StepIndex;
            for (int t = 0; t < 120 * World.TicksPerSecond && tut.StepIndex == index && !tut.Finished; t++)
            {
                w.Step();
                tut.Update(w);
            }
            Assert.True(tut.StepIndex != index || tut.Finished, $"stuck on step '{step.Id}'");
        }

        Assert.True(tut.Finished);
        Assert.Equal(Def.Steps.Select(s => s.Id), seen);
        Assert.False(w.Waves.Held);
        Assert.Equal(1, w.Waves.WavesStarted);
    }

    [Fact]
    public void Skipping_releases_the_waves()
    {
        var w = TestWorld.Create();
        var tut = new Tutorial(Def, w);
        w.Step();
        Assert.True(w.Waves.Held);

        tut.Skip(w);
        w.Step();
        Assert.False(w.Waves.Held);
        Assert.True(tut.Finished);
    }
}
