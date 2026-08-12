using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public class MigrationRunner
{
    private readonly AppDb _appDb;
    private readonly List<IMigration> _migrations;

    public MigrationRunner(AppDb appDb)
    {
        _appDb = appDb;
        _migrations = new List<IMigration>
        {
            new InitialMigration_001(),
            new Migration_002_FolderSourceFields(),
            new Migration_003_DocumentIdentityFields(),
            new Migration_004_ShareDomain(),
            new Migration_005_MetadataPersistence(),
            new Migration_006_RelationStore(),
            new Migration_007_BatchOperationPlans(),
            new Migration_008_FtsIndex()
        }.OrderBy(m => m.Version).ToList();
    }

    public async Task RunMigrationsAsync()
    {
        using var connection = _appDb.CreateConnection();
        await connection.OpenAsync();

        using (var createHistoryCmd = connection.CreateCommand())
        {
            createHistoryCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at TEXT NOT NULL
                );
            ";
            await createHistoryCmd.ExecuteNonQueryAsync();
        }

        var appliedVersions = new HashSet<int>();
        using (var getAppliedCmd = connection.CreateCommand())
        {
            getAppliedCmd.CommandText = "SELECT version FROM schema_migrations;";
            using var reader = await getAppliedCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                appliedVersions.Add(reader.GetInt32(0));
            }
        }

        foreach (var migration in _migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                await migration.UpAsync(connection, transaction);

                using var recordCmd = connection.CreateCommand();
                recordCmd.Transaction = transaction;
                recordCmd.CommandText = "INSERT INTO schema_migrations (version, name, applied_at) VALUES (@version, @name, @appliedAt);";
                recordCmd.Parameters.AddWithValue("@version", migration.Version);
                recordCmd.Parameters.AddWithValue("@name", migration.Name);
                recordCmd.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow.ToString("O"));
                await recordCmd.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
