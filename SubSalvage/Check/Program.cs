using static SubSalvage.Planner;

const uint Ring = 22500, Ruby = 1, Ore = 2, Potion = 3;
void Check(bool ok, string what) { if (!ok) throw new Exception("FAILED: " + what); Console.WriteLine("ok   " + what); }

// Whatever the plan sells, this many of each item and quality are left.
int Left(List<Stack> bags, List<Step> plan, uint id, bool hq = false) =>
    bags.Where(s => s.ItemId == id && s.Hq == hq).Sum(s => s.Quantity) -
    plan.Where(p => p.Stack.ItemId == id && p.Stack.Hq == hq).Sum(p => p.Split ? p.Stack.Quantity - 1 : p.Stack.Quantity);

var bags = new List<Stack>
{
    new(0, 0, Ring, false, 99), new(0, 1, Ring, false, 99), new(0, 2, Ring, false, 12),
    new(0, 3, Ruby, false, 40), new(0, 4, Ruby, true, 7), new(0, 5, Ruby, true, 30),
    new(1, 0, Ore, false, 1), new(1, 1, Ore, true, 1),
    new(1, 2, Potion, false, 50),
};
var plan = Plan(bags, [Ruby, Ore, Ring]);

Check(Left(bags, plan, Ring) == 1, "three stacks of rings leave exactly 1");
Check(Left(bags, plan, Ruby) == 1, "NQ rubies leave exactly 1");
Check(Left(bags, plan, Ruby, hq: true) == 1, "HQ rubies leave exactly 1 as well");
Check(plan.All(p => p.Stack.ItemId != Ore), "a single NQ and a single HQ ore are both left alone");
Check(plan.All(p => p.Stack.ItemId != Potion), "items not on the list are never sold");
Check(plan.Single(p => p.Split && p.Stack.ItemId == Ring).Stack.Slot == 0, "of two equal largest stacks, the first in the bags is split");
Check(plan.Single(p => p.Split && p.Stack.Hq).Stack.Quantity == 30, "the largest HQ stack is the one split");
var pair = Plan([new(0, 0, Ring, false, 99), new(0, 1, Ring, false, 62)], [Ring]);
Check(pair.Count == 2 && pair[0] == new Step(new(0, 1, Ring, false, 62), false) && pair[1] == new Step(new(0, 0, Ring, false, 99), true),
    "99 and 62: sells the 62 whole, then splits the 99");
Check(plan.TakeWhile(p => !p.Split).Count() == 3 && plan.Skip(3).All(p => p.Split), "whole stacks sell before any split");
Check(plan.Where(p => p.Split).Select(p => p.Stack.ItemId).SequenceEqual([Ruby, Ruby, Ring]), "splits follow the list order (your items first)");
Check(Plan(bags, [Ruby, Ruby]).Count == 3, "an item listed twice is planned once");
Check(Plan([], [Ring]).Count == 0, "empty bags sell nothing");

Console.WriteLine("All checks passed.");
