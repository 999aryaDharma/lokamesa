using System;
using System.Threading.Tasks;
using DocumentIDE.Infrastructure.Diagnostics;
using Xunit;

namespace DocumentIDE.Core.Tests.Diagnostics
{
    public class CorrelationIdTests
    {
        [Fact]
        public void NewId_SetsCurrentCorrelationIdInAsyncContext()
        {
            CorrelationIdProvider.Clear();
            Assert.Null(CorrelationIdProvider.CurrentId);

            var id = CorrelationIdProvider.NewId();
            Assert.NotNull(CorrelationIdProvider.CurrentId);
            Assert.Equal(id, CorrelationIdProvider.CurrentId.Value);
        }

        [Fact]
        public async Task CorrelationId_FlowsAcrossAsyncOperations()
        {
            var batchOperationId = CorrelationIdProvider.NewId();

            await Task.Run(() =>
            {
                Assert.Equal(batchOperationId, CorrelationIdProvider.CurrentId);
            });

            await Task.Delay(10);
            Assert.Equal(batchOperationId, CorrelationIdProvider.CurrentId);
        }

        [Fact]
        public void BeginScope_RestoresPreviousIdOnDispose()
        {
            var parentId = CorrelationIdProvider.NewId();

            using (var scope = CorrelationIdProvider.BeginScope())
            {
                var childId = CorrelationIdProvider.CurrentId;
                Assert.NotNull(childId);
                Assert.NotEqual(parentId, childId);
            }

            Assert.Equal(parentId, CorrelationIdProvider.CurrentId);
        }
    }
}
