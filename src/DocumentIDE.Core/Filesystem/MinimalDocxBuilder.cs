using System.IO;
using System.IO.Compression;
using System.Text;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Helper to construct a valid minimal OpenXML DOCX ZIP package.
/// FS-07: Explicit Physical File Operations.
/// </summary>
public static class MinimalDocxBuilder
{
    public static byte[] CreateMinimalDocxBytes()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // [Content_Types].xml
            var contentTypesEntry = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(contentTypesEntry.Open(), Encoding.UTF8))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                    "</Types>");
            }

            // _rels/.rels
            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(relsEntry.Open(), Encoding.UTF8))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "</Relationships>");
            }

            // word/document.xml
            var docEntry = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(docEntry.Open(), Encoding.UTF8))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                    "<w:body><w:p/><w:sectPr/></w:body>" +
                    "</w:document>");
            }
        }

        return memoryStream.ToArray();
    }
}
