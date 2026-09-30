using DocumentIDE.Core.Batch;
using DocumentIDE.Infrastructure.Persistence;

namespace DocumentIDE.Infrastructure.Batch;

public sealed class BatchCollectionReader(AppDb db)
{
    public async Task<IReadOnlyList<BatchCollectionOption>> GetOptionsAsync(string workspaceId)
    {
        using var connection = db.CreateConnection(); await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name FROM collections WHERE workspace_id=@workspace ORDER BY name,id";
        command.Parameters.AddWithValue("@workspace", workspaceId);
        using var reader = await command.ExecuteReaderAsync();
        var options = new List<BatchCollectionOption>();
        while (await reader.ReadAsync()) options.Add(new(reader.GetString(0), reader.GetString(1)));
        return options;
    }

    public async Task<IReadOnlyList<BatchCollectionSnapshot>> GetByWorkspaceAsync(string workspaceId)
    {
        using var connection = db.CreateConnection(); await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT c.id,m.document_id FROM collections c LEFT JOIN collection_members m ON m.collection_id=c.id WHERE c.workspace_id=@workspace ORDER BY c.id,m.sort_order,m.document_id";
        command.Parameters.AddWithValue("@workspace", workspaceId);
        using var reader = await command.ExecuteReaderAsync();
        var collections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            if (!collections.TryGetValue(id, out var members)) collections[id] = members = new();
            if (!reader.IsDBNull(1)) members.Add(reader.GetString(1));
        }
        return collections.Select(c => new BatchCollectionSnapshot(c.Key, workspaceId, c.Value)).ToArray();
    }
}
