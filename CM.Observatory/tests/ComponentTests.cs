using Xunit;

namespace CM.Observatory.Tests;

public class ComponentTests
{
    [Fact]
    public void Name_is_the_place_in_the_tree()
    {
        Assert.Equal(Component.Name, typeof(Component).Namespace);
    }
}
