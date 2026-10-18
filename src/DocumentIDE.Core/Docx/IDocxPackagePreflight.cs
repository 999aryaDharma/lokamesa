using System.IO;

namespace DocumentIDE.Core.Docx;

public interface IDocxPackagePreflight
{
    DocxPreflightResult ValidatePackage(string filePath, DocxPreflightOptions? options = null);
    DocxPreflightResult ValidatePackage(Stream stream, DocxPreflightOptions? options = null);
}
