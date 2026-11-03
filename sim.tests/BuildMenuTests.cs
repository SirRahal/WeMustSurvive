using BeltAndBarricade.Sim;

namespace BeltAndBarricade.Sim.Tests;

/// <summary>Build-menu families: upgrade tiers share their base building's slot.</summary>
public class BuildMenuTests
{
    private static readonly GameData Data = TestWorld.Data;

    private static List<string> Slots(string category) => Data.FamiliesIn(category).Select(f => f[0].Id).ToList();

    [Fact]
    public void Tiers_collapse_into_one_slot_per_family()
    {
        Assert.Equal(new[] { "belt", "inserter", "tunnel_entrance", "splitter" }, Slots("logistics"));
        Assert.Equal(new[] { "drill", "furnace", "workshop", "lab" }, Slots("production"));
        Assert.Equal(new[] { "wall" }, Slots("walls"));
        // Turrets are different weapons, not tiers.
        Assert.Equal(5, Slots("turrets").Count);
    }

    [Fact]
    public void Family_members_run_lowest_tier_first()
    {
        var belts = Data.FamiliesIn("logistics")[0].Select(d => d.Id);
        Assert.Equal(new[] { "belt", "fast_belt", "express_belt" }, belts);
        var walls = Data.FamiliesIn("walls")[0].Select(d => d.Id);
        Assert.Equal(new[] { "wall", "reinforced_wall", "heavy_wall", "fortress_wall" }, walls);
    }

    [Fact]
    public void Every_family_fits_the_eight_slots_and_names_a_real_base()
    {
        foreach (var def in Data.BuildingList.Where(b => b.Family != ""))
            Assert.True(Data.Buildings.ContainsKey(def.Family), $"{def.Id} names unknown family {def.Family}");
        foreach (var cat in Data.BuildingList.Select(b => b.Category).Where(c => c != "").Distinct())
            Assert.True(Data.FamiliesIn(cat).Count <= 8, cat);
    }
}
