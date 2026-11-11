using System.Collections.Generic;
using System.Linq;

namespace SubSalvage;

// Works out what to sell so exactly 1 of each trash item is left in your bags.
// No game types in here, so the Check project can run it.
public static class Planner
{
    public record Stack(uint Bag, int Slot, uint ItemId, bool Hq, int Quantity);

    // Split: move all but 1 into an empty slot first and sell that, so the 1 kept stays in place.
    public record Step(Stack Stack, bool Split);

    // Trash is in priority order. NQ and HQ are kept apart, so 1 of each quality is left.
    // Every stack but the largest is sold whole first, which frees bag slots for the splits that follow.
    // The largest is then split down to 1. On a tie, the one earliest in your bags is kept.
    public static List<Step> Plan(IEnumerable<Stack> bags, IEnumerable<uint> trash)
    {
        var whole = new List<Step>();
        var splits = new List<Step>();
        foreach (var id in trash.Distinct())
        foreach (var hq in new[] { false, true })
        {
            var stacks = bags.Where(s => s.ItemId == id && s.Hq == hq).OrderByDescending(s => s.Quantity).ToList();
            if (stacks.Count == 0) continue;
            whole.AddRange(stacks.Skip(1).Select(s => new Step(s, false)));
            if (stacks[0].Quantity > 1) splits.Add(new Step(stacks[0], true));
        }
        return [.. whole, .. splits];
    }
}
