using System.Collections.Generic;
using Xunit;
using DocumentIDE.Core.Context;

namespace DocumentIDE.Core.Tests.Context;

public class ContextPackTests
{
    [Fact]
    public void SelectedSourceOrderPreserved()
    {
        // Arrange
        var sources = new List<string> { "doc1", "doc2", "doc3" };
        var contentMap = new Dictionary<string, string>
        {
            { "doc1", "# Title1\nContent1" },
            { "doc2", "# Title2\nContent2" },
            { "doc3", "# Title3\nContent3" }
        };
        var service = new ContextPackService();

        // Act
        var result = service.GeneratePack(sources, id => contentMap[id]);

        // Assert
        var expected = "# Title1\nContent1\n# Title2\nContent2\n# Title3\nContent3";
        Assert.Equal(expected, result.TrimEnd());
    }
}
