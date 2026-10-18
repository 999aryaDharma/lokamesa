using Xunit;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// Every test that boots a real <c>WebAppHost</c> (or another in-process host)
/// shares process-wide state: the default application database path fallback,
/// Kestrel port allocation, static DI caches and the filesystem. Running several
/// host fixtures in parallel made <c>BatchPreviewHostTests</c> intermittently
/// report a completed preview with zero occurrences, which blocked the release
/// gate twice.
///
/// Host fixtures therefore run one at a time. Non-host tests keep running in
/// parallel, so this narrows the blast radius instead of serializing the whole
/// assembly.
/// </summary>
[CollectionDefinition("HostFixtures", DisableParallelization = true)]
public sealed class HostFixturesCollection
{
}
