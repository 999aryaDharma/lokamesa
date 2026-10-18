using System.Threading.Tasks;

namespace DocumentIDE.Core.Security;

public interface ISecretStore
{
    Task SetSecretAsync(string key, SecretValue secret);
    Task<SecretValue?> GetSecretAsync(string key);
    Task DeleteSecretAsync(string key);
}
