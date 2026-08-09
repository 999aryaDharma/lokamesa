namespace DocumentIDE.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void CoreAssembly_ResolvesBasicType()
    {
        Assert.Equal("DocumentIDE.Core", DocumentIdeCoreMarker.Name);
    }
}
