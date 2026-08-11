using System.Collections.Generic;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain.Sharing;

namespace DocumentIDE.Core.Domain.Repositories;

public interface IShareRepository
{
    Task<Share?> GetByIdAsync(string id);
    Task<Share?> GetByTokenHashAsync(string tokenHash);
    Task<IReadOnlyList<Share>> GetAllAsync();
    Task CreateAsync(Share share);
    Task UpdateAsync(Share share);
    Task DeleteAsync(string id);

    Task AddMemberAsync(ShareMember member);
    Task RemoveMemberAsync(string shareId, string documentId);
    Task<IReadOnlyList<ShareMember>> GetMembersAsync(string shareId);

    Task AddSnapshotArtifactAsync(ShareSnapshotArtifact artifact);
    Task<IReadOnlyList<ShareSnapshotArtifact>> GetSnapshotArtifactsAsync(string shareId);
}
