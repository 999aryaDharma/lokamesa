using System.IO;
using DocumentIDE.Core.Diagnostics;
using Xunit;

namespace DocumentIDE.Core.Tests.Diagnostics;

public class BuildInfoTests
{
    [Fact]
    public void Load_reads_build_info_json()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "build-info.json"),
            """{"version":"1.0.2.0","sha":"abc123"}""");

        BuildInfo info = BuildInfo.Load(dir);

        Assert.Equal("1.0.2.0", info.Version);
        Assert.Equal("abc123", info.Sha);
    }

    [Fact]
    public void Load_falls_back_to_assembly_version_when_the_file_is_missing()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;

        BuildInfo info = BuildInfo.Load(dir);

        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.Equal("unknown", info.Sha);
    }
}
