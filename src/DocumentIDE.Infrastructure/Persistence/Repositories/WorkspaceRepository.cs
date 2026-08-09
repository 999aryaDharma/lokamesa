using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Repositories;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly AppDb _appDb;

    public WorkspaceRepository(AppDb appDb)
    {
        _appDb = appDb;
    }

    public async Task<Workspace?> GetByIdAsync(string id)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, active_view, canvas_viewport_json, list_state_json, graph_viewport_json, created_at, updated_at FROM workspaces WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Workspace
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                ActiveView = reader.GetString(2),
                CanvasViewportJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                ListStateJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                GraphViewportJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.GetString(6),
                UpdatedAt = reader.GetString(7)
            };
        }

        return null;
    }

    public async Task<IEnumerable<Workspace>> GetAllAsync()
    {
        var list = new List<Workspace>();
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, active_view, canvas_viewport_json, list_state_json, graph_viewport_json, created_at, updated_at FROM workspaces ORDER BY created_at DESC;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new Workspace
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                ActiveView = reader.GetString(2),
                CanvasViewportJson = reader.IsDBNull(3) ? null : reader.GetString(3),
                ListStateJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                GraphViewportJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.GetString(6),
                UpdatedAt = reader.GetString(7)
            });
        }

        return list;
    }

    public async Task CreateAsync(Workspace workspace)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspaces (id, name, active_view, canvas_viewport_json, list_state_json, graph_viewport_json, created_at, updated_at)
            VALUES (@id, @name, @activeView, @canvasViewportJson, @listStateJson, @graphViewportJson, @createdAt, @updatedAt);
        ";
        cmd.Parameters.AddWithValue("@id", workspace.Id);
        cmd.Parameters.AddWithValue("@name", workspace.Name);
        cmd.Parameters.AddWithValue("@activeView", workspace.ActiveView);
        cmd.Parameters.AddWithValue("@canvasViewportJson", (object?)workspace.CanvasViewportJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@listStateJson", (object?)workspace.ListStateJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@graphViewportJson", (object?)workspace.GraphViewportJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", workspace.CreatedAt);
        cmd.Parameters.AddWithValue("@updatedAt", workspace.UpdatedAt);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateAsync(Workspace workspace)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE workspaces
            SET name = @name,
                active_view = @activeView,
                canvas_viewport_json = @canvasViewportJson,
                list_state_json = @listStateJson,
                graph_viewport_json = @graphViewportJson,
                updated_at = @updatedAt
            WHERE id = @id;
        ";
        cmd.Parameters.AddWithValue("@id", workspace.Id);
        cmd.Parameters.AddWithValue("@name", workspace.Name);
        cmd.Parameters.AddWithValue("@activeView", workspace.ActiveView);
        cmd.Parameters.AddWithValue("@canvasViewportJson", (object?)workspace.CanvasViewportJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@listStateJson", (object?)workspace.ListStateJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@graphViewportJson", (object?)workspace.GraphViewportJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(string id)
    {
        using var conn = _appDb.CreateConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM workspaces WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync();
    }
}
