using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Infrastructure.Persistence;

/// <summary>
/// Ensures the workspace id the shipped shell boots with exists before the host
/// serves requests.
///
/// The shell injects <c>__DOCUMENTIDE_WORKSPACE_ID__ = "default"</c> and the
/// folder-source POST writes <c>folder_sources.workspace_id</c>, which has a
/// foreign key to <c>workspaces</c>. On a fresh database no workspace existed,
/// so the insert violated the FK, the API returned 500, and the error handler
/// cleared the CORS headers - the UI only saw "Failed to fetch".
///
/// Seeding is deliberately limited to a database that has no workspaces at all:
/// a host/embedder (or test) that already created its own workspace keeps full
/// control and does not gain a surprise "default" row.
/// </summary>
public static class WorkspaceBootstrap
{
    public const string DefaultWorkspaceId = "default";
    public const string DefaultWorkspaceName = "Default workspace";

    public static async Task EnsureDefaultAsync(IWorkspaceRepository workspaces)
    {
        var existing = (await workspaces.GetAllAsync()).ToList();
        if (existing.Count > 0)
        {
            return;
        }

        try
        {
            await workspaces.CreateAsync(new Workspace
            {
                Id = DefaultWorkspaceId,
                Name = DefaultWorkspaceName,
            });
        }
        catch
        {
            // Two hosts may start against the same database. Only rethrow when
            // no workspace exists after the race.
            if (!(await workspaces.GetAllAsync()).Any())
            {
                throw;
            }
        }
    }
}
