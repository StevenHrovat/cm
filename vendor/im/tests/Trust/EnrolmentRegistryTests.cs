using Im.Trust;
using Im.Trust.Sandbox;
using Xunit;

namespace Im.Tests.Trust;

public sealed class EnrolmentRegistryTests
{
    [Fact]
    public void A_subject_is_found_until_revoked_and_unknown_subjects_are_null()
    {
        var registry = new SandboxEnrolmentRegistry();
        registry.Put(new EnrolmentRecord("dev-0001", EnrolledRole.Laptop, "ab", Revoked: false));

        Assert.False(registry.Find("dev-0001")!.Revoked);
        Assert.Null(registry.Find("dev-0002"));
        registry.Revoke("dev-0001");
        Assert.True(registry.Find("dev-0001")!.Revoked);
    }
}
