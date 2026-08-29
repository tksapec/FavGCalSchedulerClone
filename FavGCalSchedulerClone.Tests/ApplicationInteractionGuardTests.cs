using FavGCalSchedulerClone.App.Services;

namespace FavGCalSchedulerClone.Tests;

public sealed class ApplicationInteractionGuardTests
{
    [Fact]
    public void NestedScopesSuppressUntilLastScopeEndsAndDoubleDisposeIsSafe()
    {
        var guard = new ApplicationInteractionGuard();
        var modal = guard.EnterOwnedModal();
        var drag = guard.EnterDragOperation();
        Assert.True(guard.IsReturnToTodaySuppressed);

        modal.Dispose();
        modal.Dispose();
        Assert.True(guard.IsReturnToTodaySuppressed);

        drag.Dispose();
        drag.Dispose();
        Assert.False(guard.IsReturnToTodaySuppressed);
    }
}
