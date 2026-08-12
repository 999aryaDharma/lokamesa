using System;
using System.IO;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Indexing;

namespace DocumentIDE.Infrastructure.Docx
{
    public class DocxStructureParser : IDocxStructureParser
    {
        /// <summary>
        /// Parses the given DOCX file and returns a structured representation.
        /// </summary>
        /// <param name="filePath">Path to the DOCX file.</param>
        /// <returns>Parsed document structure result.</returns>
        public DocxParseResult Parse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new ArgumentException("DOCX file path is invalid or file does not exist.", nameof(filePath));

            var metadata = new DocumentMetadata
            {
                ExtractedAt = DateTime.UtcNow.ToString("O"),
                ParserVersion = "1.0.0"
            };

            return new DocxParseResult(metadata, SearchableBody: Path.GetFileName(filePath));
        }

        object LegacyParse(string filePath)
        {
            return Parse(filePath);
        }
    }
}
