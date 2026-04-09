namespace DiscordQuestRunner.Services
{
    public static class DiscordScriptCatalog
    {
        public const string AutoAccept = "auto_accept_v2.js";
        public const string QuestRunner = "quest_runner_v2.js";
        public const string CountMessages = "count_messages.js";
        public const string DeleteMessages = "delete_messages.js";

        public static string BindPlaceholders(
            string template,
            params (string Placeholder, string Value)[] replacements)
        {
            var result = template;

            foreach (var (placeholder, value) in replacements)
            {
                result = result.Replace(placeholder, value, StringComparison.Ordinal);
            }

            return result;
        }
    }
}
