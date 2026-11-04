using System.Numerics;
using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

/// <summary>Skill tree, second map, command serialization and co-op lockstep.</summary>
public class MetaAndNetTests
{
    private static string Read(string f) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", f));
    private static readonly SkillTree Tree = SkillTree.Load(Read);

    // ------------------------------------------------------------------ skill tree

    [Fact]
    public void Every_skill_node_is_reachable_from_the_start()
    {
        var owned = new HashSet<string>();
        bool progress = true;
        while (progress)
        {
            progress = false;
            foreach (var id in Tree.Nodes.Keys)
                if (Tree.CanAllocate(owned, id) && owned.Add(id)) progress = true;
        }
        Assert.Equal(Tree.Nodes.Count - 1, owned.Count);
        Assert.Equal(6, Tree.Clusters.Count);
        Assert.Equal(12, Tree.Nodes.Values.Count(n => n.Notable));
    }

    [Fact]
    public void Going_deep_into_a_cluster_requires_a_neighbouring_cluster()
    {
        var owned = new HashSet<string> { "defense_1", "defense_2a", "defense_2b" };
        Assert.False(Tree.CanAllocate(owned, "defense_n1"));
        Assert.False(Tree.CanAllocate(owned, "bridge_3"));

        owned.UnionWith(new[] { "research_1", "research_2a" });
        Assert.True(Tree.CanAllocate(owned, "bridge_3"));
        owned.Add("bridge_3");
        Assert.True(Tree.CanAllocate(owned, "defense_n1"));
    }

    [Fact]
    public void Refunding_cannot_disconnect_the_tree()
    {
        var owned = new HashSet<string> { "defense_1", "defense_2a" };
        Assert.False(Tree.CanRefund(owned, "defense_1"));
        Assert.True(Tree.CanRefund(owned, "defense_2a"));
    }

    [Fact]
    public void Allocated_nodes_become_modifiers()
    {
        var mods = Tree.ToModifiers(new[] { "defense_1", "defense_2a", "defense_2b" });
        Assert.Equal(0.10f, mods.Raw(Stat.TurretDamage), 3);
        Assert.Equal(0.08f, mods.Raw(Stat.WallHp), 3);
        Assert.All(Tree.Nodes.Values.SelectMany(n => n.Stats.Keys), s => Assert.True(Stat.Labels.ContainsKey(s), s));
    }

    // ------------------------------------------------------------------ maps

    [Fact]
    public void Crossroads_has_two_lanes_that_reach_the_nexus_and_gold_to_mine()
    {
        var scenario = GameData.LoadScenario(Read, "crossroads");
        var w = new World(TestWorld.Data, scenario, 3);
        w.Step();

        Assert.Equal(15, scenario.Waves.Waves.Count);
        Assert.Contains(w.Map.SpawnTiles, t => t.X < w.Map.Width / 2);
        Assert.Contains(w.Map.SpawnTiles, t => t.X > w.Map.Width / 2);
        Assert.All(w.Map.SpawnTiles, t => Assert.True(float.IsFinite(w.Flow.DistanceAt(t)), $"spawn {t} can't reach the Nexus"));

        int gold = 0;
        for (int y = 0; y < w.Map.Height; y++)
        for (int x = 0; x < w.Map.Width; x++)
            if (w.Map.OreAt(new Int2(x, y)) == "gold_ore") gold++;
        Assert.True(gold > 10);
        foreach (var id in GameData.MapIds) Assert.Equal(id, GameData.LoadScenario(Read, id).Map.Id);
    }

    // ------------------------------------------------------------------ commands & lockstep

    [Fact]
    public void Every_command_survives_a_serialization_round_trip()
    {
        ICommand[] all =
        {
            new PlaceBuildingCommand(1, "belt", new Int2(3, 4), Direction.West),
            new MoveCommand(0, new Vector2(1.5f, 2.25f), true),
            new RepairCommand(1, new Int2(5, 6), true),
            new StopCommand(0),
            new RemoveBuildingCommand(1, new Int2(7, 8)),
            new RotateBuildingCommand(0, new Int2(9, 10)),
            new SetInserterFilterCommand(1, new Int2(1, 1), "coal"),
            new SetInserterFilterCommand(1, new Int2(1, 1), null),
            new EndPrepCommand(0),
            new SurrenderCommand(1),
            new HoldWavesCommand(0, true),
            new StartResearchCommand(0, "electricity"),
            new CancelResearchCommand(1),
            new FireSuperWeaponCommand(0, 42, new Vector2(10, 20)),
            new PingCommand(1, PingKind.NeedCopper, new Vector2(3, 4)),
            new DrawCommand(0, new[] { new Vector2(1, 1), new Vector2(2, 3) }),
            new ClearDrawingsCommand(1),
        };
        foreach (var cmd in all)
        {
            string json = CommandCodec.Serialize(cmd);
            var back = CommandCodec.Deserialize(json);
            Assert.Equal(cmd.GetType(), back.GetType());
            Assert.Equal(json, CommandCodec.Serialize(back));
        }
    }

    /// <summary>Delivers messages after a fixed number of host steps, simulating network latency.</summary>
    private sealed class Wire
    {
        private readonly List<(int At, Action Deliver)> _queue = new();
        public void Send(int now, int latency, Action deliver) => _queue.Add((now + latency, deliver));
        public void Pump(int now)
        {
            var due = _queue.Where(m => m.At <= now).ToList();
            _queue.RemoveAll(m => m.At <= now);
            foreach (var m in due) m.Deliver();
        }
        public bool Empty => _queue.Count == 0;
    }

    private static (World Host, World Client, LockstepClient Peer) PlayCoop(Action<int, World>? tamper = null)
    {
        var hw = new World(TestWorld.Data, TestWorld.Funnel, 7, playerCount: 2);
        var cw = new World(TestWorld.Data, TestWorld.Funnel, 7, playerCount: 2);
        var host = new LockstepHost(hw);
        var client = new LockstepClient(cw);
        var down = new Wire();
        var up = new Wire();
        int step = 0;
        host.BatchReady += b => down.Send(step, 3, () => client.ReceiveBatch(b));
        host.HashReady += (t, h) => down.Send(step, 3, () => client.ReceiveHash(t, h));
        client.SendToHost += json => up.Send(step, 2, () => host.ReceiveFromClient(json));

        for (step = 0; step < 2400; step++)
        {
            down.Pump(step);
            up.Pump(step);
            switch (step)
            {
                case 5: host.Submit(new PlaceBuildingCommand(0, "drill", TestWorld.CoalTile, Direction.East)); break;
                case 6: client.Submit(new PlaceBuildingCommand(1, "furnace", TestWorld.CoalTile + new Int2(2, 0), Direction.North)); break;
                case 30: client.Submit(new MoveCommand(1, new Vector2(45.5f, 55.5f), Queue: true)); break;
                case 40: host.Submit(new EndPrepCommand(0)); break;
                case 50: client.Submit(new PingCommand(1, PingKind.Danger, new Vector2(40, 10))); break;
                case 900: host.Submit(new PlaceBuildingCommand(0, "gun_turret", new Int2(40, 58), Direction.North)); break;
            }
            tamper?.Invoke(step, cw);
            host.Step();
            while (client.CanStep) client.Step();
        }
        // The client may legitimately run up to Delay ticks ahead of the host; line them up to compare.
        while (hw.Tick < cw.Tick) host.Step();
        for (int i = 0; i < 20; i++)
        {
            down.Pump(int.MaxValue);
            while (client.CanStep && cw.Tick < hw.Tick) client.Step();
        }
        return (hw, cw, client);
    }

    [Fact]
    public void Host_and_client_stay_in_sync_with_latency()
    {
        var (hw, cw, client) = PlayCoop();
        Assert.Equal(hw.Tick, cw.Tick);
        Assert.Equal(hw.ComputeHash(), cw.ComputeHash());
        Assert.Null(client.DesyncTick);
        Assert.IsType<Crafter>(hw.BuildingAt(TestWorld.CoalTile + new Int2(2, 0)));
        Assert.Equal(1, hw.Waves.WavesStarted);
    }

    [Fact]
    public void A_diverged_client_reports_a_desync()
    {
        var (_, _, client) = PlayCoop((step, cw) => { if (step == 500) cw.Storage.Add("iron_plate", 1); });
        Assert.NotNull(client.DesyncTick);
    }
}
