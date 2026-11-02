using System.Text.Json;
using BeltAndBarricade.Sim;
using Godot;

namespace BeltAndBarricade.UI;

/// <summary>Persistent player progress (user://profile.json): meta points, skill tree, map records.</summary>
public sealed class Profile
{
    private const string Path = "user://profile.json";

    /// <summary>Extra meta points the first time a map is won.</summary>
    public const int FirstClearBonus = 2;

    public int MetaPoints { get; set; }
    public HashSet<string> Allocated { get; set; } = new();
    public Dictionary<string, int> Wins { get; set; } = new();
    public Dictionary<string, int> BestWave { get; set; } = new();
    public bool TutorialDone { get; set; }

    public int SpentPoints => Allocated.Count;

    public static Profile Load()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(Path)) return new Profile();
            return JsonSerializer.Deserialize<Profile>(Godot.FileAccess.GetFileAsString(Path)) ?? new Profile();
        }
        catch (Exception e)
        {
            GD.PushWarning($"Could not read profile, starting fresh: {e.Message}");
            return new Profile();
        }
    }

    public void Save()
    {
        using var f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Records a finished game. Returns meta points earned (only wins award points).</summary>
    public int RecordResult(MapDef map, bool won, int wavesReached)
    {
        BestWave[map.Id] = Math.Max(BestWave.GetValueOrDefault(map.Id), wavesReached);
        int earned = 0;
        if (won)
        {
            earned = map.MetaPoints + (Wins.GetValueOrDefault(map.Id) == 0 ? FirstClearBonus : 0);
            Wins[map.Id] = Wins.GetValueOrDefault(map.Id) + 1;
            MetaPoints += earned;
        }
        Save();
        return earned;
    }

    public bool TryAllocate(SkillTree tree, string node)
    {
        if (MetaPoints <= 0 || !tree.CanAllocate(Allocated, node)) return false;
        Allocated.Add(node);
        MetaPoints--;
        Save();
        return true;
    }

    public bool TryRefund(SkillTree tree, string node)
    {
        if (!tree.CanRefund(Allocated, node)) return false;
        Allocated.Remove(node);
        MetaPoints++;
        Save();
        return true;
    }

    public void ResetTree()
    {
        MetaPoints += Allocated.Count;
        Allocated.Clear();
        Save();
    }
}
