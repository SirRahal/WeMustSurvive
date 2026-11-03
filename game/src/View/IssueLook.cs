using BeltAndBarricade.Sim;
using BeltAndBarricade.UI;
using Godot;

namespace BeltAndBarricade.View;

/// <summary>
/// How a logistics problem shows: an icon badge on the map (red = broken, amber = needs something) and the title of
/// its hover pop-up. The explanation itself is the issue's <see cref="LogisticsIssue.Reason"/>.
/// </summary>
public static class IssueLook
{
    public static (string Icon, Color Color, string Title) Of(LogisticsIssue issue) => issue.Short switch
    {
        "DEAD END" => ("end", HudTheme.Danger, "Dead end"),
        "WRONG WAY" => ("reverse", HudTheme.Danger, "Running the wrong way"),
        "HEAD-ON" => ("headon", HudTheme.Danger, "Belts meet head-on"),
        "LOOP" => ("loop", HudTheme.Warm, "Belt loop"),
        "NO EXIT" => ("tunnel", HudTheme.Warm, "Tunnel has no exit"),
        "NEEDS INSERTER" => ("inserter", HudTheme.Warm, "Needs an inserter"),
        "NO OUTPUT" => ("splitter", HudTheme.Warm, "Splitter has no output"),
        "NOTHING TO TAKE" => ("empty", HudTheme.Warm, "Nothing to take"),
        "NEEDS FILTER" => ("filter", HudTheme.Warm, "Needs a filter"),
        "ROTATE" => ("turn_right", HudTheme.Warm, "Rotate the drill"),
        "CAN'T TAKE ITEMS" => ("block", HudTheme.Danger, "Can't take items"),
        "CAN'T TAKE ORE" => ("block", HudTheme.Danger, "Can't take ore"),
        "CAN'T USE" => ("block", HudTheme.Danger, "Can't use this"),
        _ => ("block", HudTheme.Danger, issue.Short[..1] + issue.Short[1..].ToLowerInvariant()),
    };

    /// <summary>The reason split into what's wrong (first sentence) and what to do about it (the rest).</summary>
    public static (string What, string Fix) Explain(LogisticsIssue issue)
    {
        string r = issue.Reason;
        int dot = r.IndexOf(". ", StringComparison.Ordinal);
        if (dot < 0) dot = r.IndexOf(": ", StringComparison.Ordinal);
        return dot < 0 ? (r, "") : (r[..(dot + 1)], r[(dot + 2)..]);
    }
}
