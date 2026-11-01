using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

internal static class TestWorld
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    public static readonly GameData Data = GameData.Load(Read);
    public static readonly Scenario Funnel = GameData.LoadScenario(Read, "funnel");

    private static string Read(string file) => File.ReadAllText(Path.Combine(DataDir, file));

    public static World Create(ulong seed = 1, int players = 1) => new(Data, Funnel, seed, players);

    public static void Run(this World w, int ticks)
    {
        for (int i = 0; i < ticks; i++) w.Step();
    }

    public static World Also(this World w, Action<World> action)
    {
        action(w);
        return w;
    }

    // Landmarks on the Funnel map (see game/data/map_funnel.json).
    public static readonly Int2 NexusOrigin = new(39, 63);
    public static readonly Int2 CoalTile = new(31, 57);
    public static readonly Int2 IronTile = new(48, 57);
}
