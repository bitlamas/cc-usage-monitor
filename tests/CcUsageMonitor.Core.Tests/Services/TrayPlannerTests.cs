using CcUsageMonitor.Core.Models;
using CcUsageMonitor.Core.Services;

namespace CcUsageMonitor.Core.Tests.Services;

public class TrayPlannerTests
{
    [Fact]
    public void PlanTrayAdds_Windows_ReversesOrder_Gap1500()
    {
        var plan = TrayPlanner.PlanTrayAdds(
            new[] { LimitKind.Session5h, LimitKind.WeeklyAll }, isWindows: true);
        Assert.Equal(new[] { LimitKind.WeeklyAll, LimitKind.Session5h }, plan.Order);
        Assert.Equal(1500, plan.GapMs);
    }

    [Fact]
    public void PlanTrayAdds_Linux_PreservesOrder_Gap0()
    {
        var plan = TrayPlanner.PlanTrayAdds(
            new[] { LimitKind.Session5h, LimitKind.WeeklyAll }, isWindows: false);
        Assert.Equal(new[] { LimitKind.Session5h, LimitKind.WeeklyAll }, plan.Order);
        Assert.Equal(0, plan.GapMs);
    }

    [Fact]
    public void PlanTrayAdds_SingleElement_Windows_PreservesOrder_Gap1500()
    {
        var plan = TrayPlanner.PlanTrayAdds(
            new[] { LimitKind.Session5h }, isWindows: true);
        Assert.Equal(new[] { LimitKind.Session5h }, plan.Order);
        Assert.Equal(1500, plan.GapMs);
    }

    [Fact]
    public void PlanTrayAdds_SingleElement_Linux_PreservesOrder_Gap0()
    {
        var plan = TrayPlanner.PlanTrayAdds(
            new[] { LimitKind.Session5h }, isWindows: false);
        Assert.Equal(new[] { LimitKind.Session5h }, plan.Order);
        Assert.Equal(0, plan.GapMs);
    }

    [Fact]
    public void PlanTrayAdds_Empty_Windows_EmptyOrder_Gap1500()
    {
        var plan = TrayPlanner.PlanTrayAdds(Array.Empty<LimitKind>(), isWindows: true);
        Assert.Empty(plan.Order);
        Assert.Equal(1500, plan.GapMs);
    }

    [Fact]
    public void PlanTrayAdds_Empty_Linux_EmptyOrder_Gap0()
    {
        var plan = TrayPlanner.PlanTrayAdds(Array.Empty<LimitKind>(), isWindows: false);
        Assert.Empty(plan.Order);
        Assert.Equal(0, plan.GapMs);
    }

    [Fact]
    public void PlanTrayAdds_InputNotMutated()
    {
        var input = new List<LimitKind> { LimitKind.Session5h, LimitKind.WeeklyAll };
        _ = TrayPlanner.PlanTrayAdds(input, isWindows: true);
        Assert.Equal(new[] { LimitKind.Session5h, LimitKind.WeeklyAll }, input);
    }
}
