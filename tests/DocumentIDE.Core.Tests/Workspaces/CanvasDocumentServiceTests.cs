using System.Reflection;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Workspaces;
using Xunit;

namespace DocumentIDE.Core.Tests.Workspaces;

/// <summary>
/// CAN-04: Folder Picker & File Release — canvas release service tests.
///
/// The service only creates/activates WorkspaceDocument objects; it MUST NOT
/// move, rename, or otherwise mutate any physical file.
/// </summary>
public class CanvasDocumentServiceTests
{
    private const string WorkspaceId = "ws-1";

    private readonly CanvasDocumentService _service = new();

    [Fact]
    public void ReleaseAll_Creates_WorkspaceDocuments_For_Supported_Files()
    {
        var listing = new List<CanvasFileCandidate>
        {
            new() { DocumentId = "doc-a", Filename = "BA Site A.docx" },
            new() { DocumentId = "doc-b", Filename = "BA Site B.docx" },
            new() { DocumentId = "doc-c", Filename = "catatan.md" },
        };

        var result = _service.ReleaseAll(WorkspaceId, listing);

        Assert.Equal(3, result.Released.Count);
        Assert.Empty(result.Rejected);
        Assert.Equal(
            new[] { "doc-a", "doc-b", "doc-c" },
            result.Released.Select(document => document.DocumentId));
        Assert.All(result.Released, document => Assert.Equal(WorkspaceId, document.WorkspaceId));
        Assert.All(result.Released, document => Assert.Equal("auto", document.PositionMode));
        Assert.All(result.Released, document => Assert.False(document.Pinned));
    }

    [Fact]
    public void ReleaseAll_Rejects_Unsupported_Files_With_Typed_Outcome()
    {
        var listing = new List<CanvasFileCandidate>
        {
            new() { DocumentId = "doc-a", Filename = "BA Site A.docx" },
            new() { DocumentId = "doc-txt", Filename = "notes.txt" },
            new() { DocumentId = "doc-noext", Filename = "README" },
        };

        var result = _service.ReleaseAll(WorkspaceId, listing);

        // Only the supported .docx candidate is released.
        Assert.Single(result.Released);
        Assert.Equal("doc-a", result.Released[0].DocumentId);

        // Unsupported files produce deterministic, inspectable rejections.
        Assert.Equal(2, result.Rejected.Count);
        Assert.Contains(
            result.Rejected,
            rejection => rejection.DocumentId == "doc-txt" && rejection.Reason == CanvasReleaseRejectionReasons.UnsupportedFileType);
        Assert.Contains(
            result.Rejected,
            rejection => rejection.DocumentId == "doc-noext" && rejection.Reason == CanvasReleaseRejectionReasons.UnsupportedFileType);
    }

    [Fact]
    public void ReleaseAll_Rejects_Missing_And_Duplicate_DocumentIds_Deterministically()
    {
        var listing = new List<CanvasFileCandidate>
        {
            new() { DocumentId = "doc-a", Filename = "A.docx" },
            new() { DocumentId = "doc-a", Filename = "A.docx" },
            new() { DocumentId = "  ", Filename = "Unnamed.docx" },
        };

        var result = _service.ReleaseAll(WorkspaceId, listing);

        Assert.Single(result.Released);
        Assert.Equal("doc-a", result.Released[0].DocumentId);
        Assert.Equal(2, result.Rejected.Count);
        Assert.Contains(
            result.Rejected,
            rejection => rejection.Filename == "A.docx" && rejection.Reason == CanvasReleaseRejectionReasons.DuplicateDocumentId);
        Assert.Contains(
            result.Rejected,
            rejection => rejection.Filename == "Unnamed.docx" && rejection.Reason == CanvasReleaseRejectionReasons.MissingDocumentId);
    }

    [Fact]
    public void ReleaseSelected_Releases_Only_Selected_DocumentIds()
    {
        var listing = new List<CanvasFileCandidate>
        {
            new() { DocumentId = "doc-a", Filename = "BA Site A.docx" },
            new() { DocumentId = "doc-b", Filename = "BA Site B.docx" },
            new() { DocumentId = "doc-c", Filename = "catatan.md" },
        };

        var result = _service.ReleaseSelected(WorkspaceId, listing, new[] { "doc-a", "doc-c" });

        Assert.Equal(new[] { "doc-a", "doc-c" }, result.Released.Select(document => document.DocumentId));
        Assert.Empty(result.Rejected);
        Assert.DoesNotContain(result.Released, document => document.DocumentId == "doc-b");
    }

    [Fact]
    public void ReleaseSelected_Rejects_Selected_Ids_Not_In_Folder_Listing()
    {
        var listing = new List<CanvasFileCandidate>
        {
            new() { DocumentId = "doc-a", Filename = "BA Site A.docx" },
        };

        var result = _service.ReleaseSelected(WorkspaceId, listing, new[] { "doc-a", "ghost" });

        Assert.Single(result.Released);
        Assert.Equal("doc-a", result.Released[0].DocumentId);

        var rejection = Assert.Single(result.Rejected);
        Assert.Equal("ghost", rejection.DocumentId);
        Assert.Equal(CanvasReleaseRejectionReasons.NotInFolderListing, rejection.Reason);
    }

    // -----------------------------------------------------------------------
    // Acceptance guards — "No physical move occurs. The Core service must only
    // create/activate workspace objects."
    // -----------------------------------------------------------------------

    [Fact]
    public void Release_Service_Makes_No_Filesystem_Calls()
    {
        var workspaceTypes = typeof(CanvasDocumentService).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "DocumentIDE.Core.Workspaces");

        var findings = workspaceTypes.SelectMany(FindSystemIoCalls).ToList();

        Assert.True(
            findings.Count == 0,
            "Canvas release must never touch the filesystem: " + string.Join("; ", findings));
    }

    [Fact]
    public void Guard_Scanner_Detects_SystemIO_References()
    {
        // Positive control: the scanner must flag a known System.IO call,
        // otherwise the guard above could pass vacuously.
        var findings = FindSystemIoCalls(typeof(CanvasDocumentServiceTests));

        Assert.Contains(
            findings,
            finding => finding.Contains("System.IO.Path.GetExtension", StringComparison.Ordinal));
    }

    [Fact]
    public void Service_Contract_Has_No_Move_Rename_Delete_Or_Write_Surface()
    {
        string[] mutationVerbs =
        {
            "move", "rename", "delete", "remove", "copy", "write", "replace", "truncate",
        };

        var surface = typeof(CanvasDocumentService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name);

        Assert.DoesNotContain(
            surface,
            name => mutationVerbs.Any(verb => name.Contains(verb, StringComparison.OrdinalIgnoreCase)));

        // No injected filesystem collaborator: parameterless ctor, zero instance state.
        var constructor = Assert.Single(typeof(CanvasDocumentService).GetConstructors());
        Assert.Empty(constructor.GetParameters());
        Assert.Empty(typeof(CanvasDocumentService)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    [Fact]
    public void Released_Workspace_Objects_Expose_No_Physical_Path_Surface()
    {
        var listing = new List<CanvasFileCandidate>
        {
            new() { DocumentId = "doc-a", Filename = "BA Site A.docx" },
        };

        var result = _service.ReleaseSelected(WorkspaceId, listing, new[] { "doc-a" });

        var document = Assert.Single(result.Released);
        Assert.Equal("doc-a", document.DocumentId);

        // WorkspaceDocument cannot express a path change — the canvas release
        // contract literally has no physical-path field.
        Assert.DoesNotContain(
            typeof(WorkspaceDocument).GetProperties().Select(property => property.Name),
            name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReleaseAll_Does_Not_Require_The_Files_To_Exist()
    {
        // Filenames deliberately reference nothing that exists on disk:
        // release still succeeds because the service performs zero I/O.
        string unique = Guid.NewGuid().ToString("N");
        var listing = new List<CanvasFileCandidate>
        {
            new() { DocumentId = "doc-a", Filename = $"ghost-a-{unique}.docx" },
            new() { DocumentId = "doc-b", Filename = $"ghost-b-{unique}.md" },
        };

        var result = _service.ReleaseAll(WorkspaceId, listing);

        Assert.Equal(2, result.Released.Count);
        Assert.Empty(result.Rejected);
        Assert.All(result.Released, document => Assert.Equal(0, document.CanvasX));
        Assert.All(result.Released, document => Assert.Equal(0, document.CanvasY));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Scans the IL of every declared method/constructor for metadata tokens
    /// that resolve to members declared in the <c>System.IO</c> namespace.
    /// Any call into System.IO (including a File.Move/rename) surfaces here.
    /// </summary>
    private static IEnumerable<string> FindSystemIoCalls(Type type)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var methods = type.GetMethods(flags)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));

        var findings = new List<string>();
        foreach (var method in methods)
        {
            byte[]? il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (InvalidOperationException)
            {
                continue; // abstract/runtime-provided method bodies
            }

            if (il is null)
            {
                continue;
            }

            for (int i = 1; i <= il.Length - 4; i++)
            {
                // Only positions immediately after a call-like opcode can be
                // method tokens: call=0x28, callvirt=0x6F, newobj=0x73.
                byte opcode = il[i - 1];
                if (opcode is not (0x28 or 0x6F or 0x73))
                {
                    continue;
                }

                int token = il[i] | (il[i + 1] << 8) | (il[i + 2] << 16) | (il[i + 3] << 24);
                if ((token & 0x00FFFFFF) == 0)
                {
                    continue;
                }

                try
                {
                    var member = method.Module.ResolveMember(token);
                    if (member is null)
                    {
                        continue;
                    }

                    string? declaring = member.DeclaringType?.FullName;
                    if (declaring is not null && declaring.StartsWith("System.IO", StringComparison.Ordinal))
                    {
                        findings.Add($"{type.FullName}::{method.Name} -> {declaring}.{member.Name}");
                    }
                }
                catch (ArgumentException)
                {
                    // Not a resolvable member token at this position.
                }
                catch (BadImageFormatException)
                {
                    // Not a metadata token at this position.
                }
            }
        }

        return findings.Distinct().ToList();
    }

    /// <summary>Positive control target for the IL scanner guard test.</summary>
    private static string ScannerPositiveControl() => System.IO.Path.GetExtension("file.docx");
}
