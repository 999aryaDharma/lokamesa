using System.IO;

namespace DocumentIDE.Core.Markdown;

public interface IMarkdownStructureParser
{
    MarkdownParseResult Parse(string markdownContent);
    MarkdownParseResult Parse(TextReader reader);
}
