using Godot;

namespace BeltAndBarricade.UI;

/// <summary>Player preferences, persisted to user://settings.cfg.</summary>
public sealed class Settings
{
    private const string Path = "user://settings.cfg";
    private const string Section = "game";

    public static readonly float[] Speeds = { 1f, 2f, 3f };

    public float GameSpeed { get; set; } = 1f;
    public bool EdgePan { get; set; } = true;
    public bool Fullscreen { get; set; }
    public bool ShowMinimap { get; set; } = true;
    public string LastJoinAddress { get; set; } = "127.0.0.1";
    public string PlayerName { get; set; } = "Engineer";

    public static Settings Load()
    {
        var s = new Settings();
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return s;
        s.GameSpeed = (float)cfg.GetValue(Section, "game_speed", 1f);
        s.EdgePan = (bool)cfg.GetValue(Section, "edge_pan", true);
        s.Fullscreen = (bool)cfg.GetValue(Section, "fullscreen", false);
        s.ShowMinimap = (bool)cfg.GetValue(Section, "show_minimap", true);
        s.LastJoinAddress = (string)cfg.GetValue(Section, "last_join", "127.0.0.1");
        s.PlayerName = (string)cfg.GetValue(Section, "player_name", "Engineer");
        if (!Speeds.Contains(s.GameSpeed)) s.GameSpeed = 1f;
        return s;
    }

    public void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "game_speed", GameSpeed);
        cfg.SetValue(Section, "edge_pan", EdgePan);
        cfg.SetValue(Section, "fullscreen", Fullscreen);
        cfg.SetValue(Section, "show_minimap", ShowMinimap);
        cfg.SetValue(Section, "last_join", LastJoinAddress);
        cfg.SetValue(Section, "player_name", PlayerName);
        cfg.Save(Path);
    }
}
