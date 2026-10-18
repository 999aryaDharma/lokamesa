namespace DocumentIDE.Core.Filesystem;

public interface IFileIdentityProvider
{
    FileIdentity? GetFileIdentity(string path);
}

public record FileIdentity(string VolumeId, string ObjectId);
