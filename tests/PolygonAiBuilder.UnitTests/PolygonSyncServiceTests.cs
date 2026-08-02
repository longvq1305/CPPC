using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;

namespace PolygonAiBuilder.UnitTests;

public sealed class PolygonSyncServiceTests
{
    [Fact]
    public void PackageResumeUsesPersistedPreBuildBaseline()
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new SyncOperationInfo(Guid.NewGuid(), PolygonSyncPhase.PackageBuildStarted,
            "problem.buildPackage", SyncOperationStatus.Succeeded, now, now,
            "baselinePackageId=734; Đã bắt đầu standard package build với verify=true.", null, null, 0);
        var snapshot = new PolygonSyncSnapshot(Guid.NewGuid(), 10, 1, PolygonSyncPhase.PackageBuildStarted,
            ProjectStatus.Syncing, [operation]);

        Assert.Equal(734, PolygonSyncService.ReadPackageBaseline(snapshot));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Đã bắt đầu package")]
    [InlineData("baselinePackageId=invalid; package")]
    [InlineData("baselinePackageId=-1; package")]
    public void PackageResumeRejectsMissingOrInvalidBaseline(string summary)
    {
        var now = DateTimeOffset.UtcNow;
        var operation = new SyncOperationInfo(Guid.NewGuid(), PolygonSyncPhase.PackageBuildStarted,
            "problem.buildPackage", SyncOperationStatus.Succeeded, now, now, summary, null, null, 0);
        var snapshot = new PolygonSyncSnapshot(Guid.NewGuid(), 10, 1, PolygonSyncPhase.PackageBuildStarted,
            ProjectStatus.Syncing, [operation]);

        Assert.Null(PolygonSyncService.ReadPackageBaseline(snapshot));
    }
}
