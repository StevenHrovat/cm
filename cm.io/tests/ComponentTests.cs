using Xunit;

namespace cm.io.Tests;

public class ComponentTests
{
    [Fact]
    public void Name_is_the_place_in_the_tree()
    {
        Assert.Equal(Component.Name, typeof(Component).Namespace);
    }
}

public class SqlServerSessionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_refuses_an_empty_connection_string(string connectionString)
    {
        Assert.Throws<ArgumentException>(() => cm.io.Database.SqlServerSession.Open(connectionString));
    }

    [Fact]
    public void AsText_formats_with_the_invariant_culture()
    {
        Assert.Equal("12.50", cm.io.Database.SqlServerSession.AsText(12.50m));
        Assert.Null(cm.io.Database.SqlServerSession.AsText(null));
    }
}
