using Xunit;

namespace CM.MilkyWay.Tests;

/// <summary>
/// Runs in the ordinary test step, outside the im group, and fails if the im group could silently become empty: a test filter that
/// matches nothing passes, so the trait on the classes that carry the acceptance test is checked here.
/// </summary>
public sealed class ImGroupGuardTests
{
    [Theory]
    [InlineData(typeof(ImScenarioTests))]
    [InlineData(typeof(ImEvidenceTests))]
    public void The_acceptance_tests_are_in_the_im_group(Type type)
    {
        var trait = type.GetCustomAttributes(typeof(TraitAttribute), false).Cast<TraitAttribute>().ToArray();
        Assert.NotEmpty(trait);
        var data = type.GetCustomAttributesData().Single(a => a.AttributeType == typeof(TraitAttribute));
        Assert.Equal("Category", data.ConstructorArguments[0].Value);
        Assert.Equal("im", data.ConstructorArguments[1].Value);
        Assert.True(type.GetMethods().Count(m => m.GetCustomAttributes(typeof(FactAttribute), true).Length > 0) >= 7);
    }
}
