using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DocumentIDE.Infrastructure.Diagnostics
{
    /// <summary>
    /// Helper that redacts sensitive data such as API keys, tokens, or raw document bodies
    /// from structured log messages and properties.
    /// </summary>
    public static class LogRedaction
    {
        private static readonly List<Regex> SecretPatterns = new()
        {
            // Hex secrets / tokens (>= 32 hex chars)
            new Regex(@"\b[A-Fa-f0-9]{32,}\b", RegexOptions.Compiled),
            // Bearer / JWT tokens
            new Regex(@"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\beyJ[A-Za-z0-9\-_]+\.eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\b", RegexOptions.Compiled),
            // API key parameters
            new Regex(@"(api[_-]?key|secret|token|password)\s*[:=]\s*[""]?([A-Za-z0-9\-_]{16,})[""]?", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        private static readonly List<Regex> DocumentBodyPatterns = new()
        {
            // Raw XML / HTML document tags or body content blocks
            new Regex(@"<big[‑\-]docx[‑\-]content>", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\""(documentBody|content|text|rawBody)\""\s*:\s*\"".+?\""", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase),
            new Regex(@"\""document\""\s*:\s*\"".+?\""", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase)
        };

        /// <summary>
        /// Returns true if the message or template contains any sensitive data (secrets, tokens, document bodies).
        /// </summary>
        public static bool ContainsSensitiveData(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (var pattern in SecretPatterns)
            {
                if (pattern.IsMatch(text))
                    return true;
            }

            foreach (var pattern in DocumentBodyPatterns)
            {
                if (pattern.IsMatch(text))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Redacts any sensitive data (tokens, API keys, raw document bodies) in the input text.
        /// </summary>
        public static string RedactSensitiveData(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = input;

            foreach (var pattern in SecretPatterns)
            {
                result = pattern.Replace(result, "[REDACTED_SECRET]");
            }

            foreach (var pattern in DocumentBodyPatterns)
            {
                result = pattern.Replace(result, "[REDACTED_DOCUMENT_BODY]");
            }

            return result;
        }
    }
}
