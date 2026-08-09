using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DocumentIDE.Infrastructure.Persistence.Migrations;

public interface IMigration
{
    int Version { get; }
    string Name { get; }
    Task UpAsync(SqliteConnection connection, SqliteTransaction transaction);
}
