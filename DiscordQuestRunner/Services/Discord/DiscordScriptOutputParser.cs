using System.Text.RegularExpressions;

namespace DiscordQuestRunner.Services
{
    /// <summary>
    /// Parses structured markers emitted by the injected Discord automation scripts.
    /// </summary>
    public static partial class DiscordScriptOutputParser
    {
        /// <summary>
        /// Creates the regular expression used to extract message count results.
        /// </summary>
        /// <returns>A compiled expression for the count result marker.</returns>
        [GeneratedRegex(@"COUNT_RESULT:(\d+)")]
        private static partial Regex CountResultRegex();

        /// <summary>
        /// Creates the regular expression used to extract delete progress counters.
        /// </summary>
        /// <returns>A compiled expression for delete progress markers.</returns>
        [GeneratedRegex(@"\[(\d+)/(\d+)\]\s+Purged message:")]
        private static partial Regex PurgeProgressRegex();

        /// <summary>
        /// Determines whether a script output contains any terminal completion marker.
        /// </summary>
        /// <param name="output">Aggregated script output returned by the CDP evaluation call.</param>
        /// <param name="terminalPhrases">Phrases that indicate the automation loop has finished.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="output"/> contains a terminal phrase; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool ContainsTerminalPhrase(
            string? output,
            params string[] terminalPhrases)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            return terminalPhrases.Any(phrase =>
                output.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Attempts to parse the total message count reported by the count script.
        /// </summary>
        /// <param name="message">Console line emitted by the script.</param>
        /// <param name="count">Parsed message count when the marker is present.</param>
        /// <returns>
        /// <see langword="true"/> when the count marker is present and valid; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool TryParseCountResult(string message, out int count)
        {
            var match = CountResultRegex().Match(message);
            if (match.Success && int.TryParse(match.Groups[1].Value, out count))
            {
                return true;
            }

            count = 0;
            return false;
        }

        /// <summary>
        /// Attempts to parse purge progress counters from a delete script log line.
        /// </summary>
        /// <param name="message">Console line emitted by the script.</param>
        /// <param name="deleted">Number of messages deleted so far when parsing succeeds.</param>
        /// <param name="total">Total number of messages scheduled for deletion when parsing succeeds.</param>
        /// <returns>
        /// <see langword="true"/> when the progress marker is present and valid; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool TryParsePurgeProgress(
            string message,
            out int deleted,
            out int total)
        {
            var match = PurgeProgressRegex().Match(message);
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out deleted)
                && int.TryParse(match.Groups[2].Value, out total))
            {
                return true;
            }

            deleted = 0;
            total = 0;
            return false;
        }
    }
}
