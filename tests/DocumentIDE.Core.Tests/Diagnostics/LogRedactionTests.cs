using DocumentIDE.Infrastructure.Diagnostics;
using Xunit;

namespace DocumentIDE.Core.Tests.Diagnostics
{
    public class LogRedactionTests
    {
        [Theory]
        [InlineData("User token: ABCDEF1234567890ABCDEF1234567890")]
        [InlineData("Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signature")]
        [InlineData("api_key=sk-1234567890abcdef1234567890abcdef")]
        [InlineData("{ \"document\": \"<big‑docx‑content>\" }")]
        [InlineData("{ \"documentBody\": \"Sensitive raw text content here\" }")]
        public void SensitivePatterns_AreDetected(string message)
        {
            Assert.True(LogRedaction.ContainsSensitiveData(message));
        }

        [Theory]
        [InlineData("User logged in successfully")]
        [InlineData("File moved from A to B")]
        [InlineData("DocId: 12345, Path: C:\\Users\\Docs\\file.md")]
        public void NonSensitiveMessages_PassThrough(string message)
        {
            Assert.False(LogRedaction.ContainsSensitiveData(message));
        }

        [Fact]
        public void RedactSensitiveData_ReplacesSecretsAndDocumentBodies()
        {
            var rawInput = "Config loaded with api_key=sk-1234567890abcdef1234567890abcdef and content: { \"documentBody\": \"Secret document contents\" }";
            var redacted = LogRedaction.RedactSensitiveData(rawInput);

            Assert.DoesNotContain("sk-1234567890abcdef1234567890abcdef", redacted);
            Assert.DoesNotContain("Secret document contents", redacted);
            Assert.Contains("[REDACTED_SECRET]", redacted);
            Assert.Contains("[REDACTED_DOCUMENT_BODY]", redacted);
        }
    }
}
