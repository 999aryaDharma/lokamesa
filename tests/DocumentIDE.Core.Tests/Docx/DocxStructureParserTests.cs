using System;
using System.IO;
using DocumentIDE.Core.Filesystem;
using Xunit;

namespace DocumentIDE.Core.Tests.Docx
{
    public class DocxStructureParserTests
    {
        [Fact]
        public void Parse_MinimalDocx_ReturnsNonNullResult()
        {
            // Arrange: create a temporary minimal DOCX file
            var tempPath = Path.Combine(Path.GetTempPath(), "minimal.docx");
            File.WriteAllBytes(tempPath, MinimalDocxBuilder.CreateMinimalDocxBytes());
            var parser = new DocumentIDE.Infrastructure.Docx.DocxStructureParser();

            // Act
            var result = parser.Parse(tempPath);

            // Assert
            Assert.NotNull(result);

            // Cleanup
            File.Delete(tempPath);
        }
    }
}
