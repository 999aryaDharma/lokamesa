using System;
using System.Collections.Generic;
using System.Linq;

namespace DocumentIDE.Core.Context
{
    /// <summary>
    /// Service responsible for generating a deterministic context pack from an explicit ordered list of source identifiers.
    /// The implementation follows the minimal requirements for CTX-01 sprint: preserve the order of sources and concatenate their markdown content.
    /// </summary>
    public class ContextPackService
    {
        /// <summary>
        /// Generates a context pack by concatenating the markdown content of the provided source identifiers.
        /// </summary>
        /// <param name="sourceIds">Ordered collection of source identifiers (e.g., document IDs).</param>
        /// <param name="getContent">Delegate that returns the markdown content for a given source identifier.</param>
        /// <returns>The combined markdown string representing the context pack.</returns>
        public string GeneratePack(IEnumerable<string> sourceIds, Func<string, string> getContent)
        {
            if (sourceIds == null) throw new ArgumentNullException(nameof(sourceIds));
            if (getContent == null) throw new ArgumentNullException(nameof(getContent));

            // Preserve the explicit order of sourceIds and concatenate their content.
            var pieces = new List<string>();
            foreach (var id in sourceIds)
            {
                var content = getContent(id) ?? string.Empty;
                pieces.Add(content);
            }

            // Join with a newline to separate documents; TrimEnd to avoid trailing newline.
            return string.Join("\n", pieces).TrimEnd();
        }
    }
}
