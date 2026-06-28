using System.Collections.Generic;
using System.Linq;
using CcUsageMonitor.Core.Models;

namespace CcUsageMonitor.Core.Services;

public readonly record struct TrayAddPlan(IReadOnlyList<LimitKind> Order, int GapMs);

public static class TrayPlanner
{
    public static TrayAddPlan PlanTrayAdds(IReadOnlyList<LimitKind> desired, bool isWindows)
    {
        // System.Linq Reverse()/ToList() never mutate the source (satisfies A6.5).
        var order = isWindows ? desired.Reverse().ToList() : desired.ToList();
        return new TrayAddPlan(order, isWindows ? 1500 : 0);
    }
}
