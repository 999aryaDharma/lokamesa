using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Domain.Repositories;

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(string id);
    Task<IEnumerable<Workspace>> GetAllAsync();
    Task CreateAsync(Workspace workspace);
    Task UpdateAsync(Workspace workspace);
    Task DeleteAsync(string id);
}
