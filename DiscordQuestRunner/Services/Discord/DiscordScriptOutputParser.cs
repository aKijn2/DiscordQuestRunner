using System.Text.RegularExpressions;

namespace DiscordQuestRunner.Services
{
    public static partial class DiscordScriptOutputParser
    {
        [GeneratedRegex(@"COUNT_RESULT:(\d+)")]
        private static partial Regex CountResultRegex();

        [GeneratedRegex(@"\[(\d+)/(\d+)\]\s+Purged message:")]
        private static partial Regex PurgeProgressRegex();

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
