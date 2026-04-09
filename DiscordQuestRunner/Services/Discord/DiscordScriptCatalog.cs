namespace DiscordQuestRunner.Services
{
    /// <summary>
    /// Defines packaged JavaScript asset names and common template substitution helpers.
    /// </summary>
    public static class DiscordScriptCatalog
    {
        /// <summary>
        /// Gets the packaged auto-enrollment script filename.
        /// </summary>
        public const string AutoAccept = "auto_accept_v2.js";

        /// <summary>
        /// Gets the packaged quest execution script filename.
        /// </summary>
        public const string QuestRunner = "quest_runner_v2.js";

        /// <summary>
        /// Gets the packaged message counting script filename.
        /// </summary>
        public const string CountMessages = "count_messages.js";

        /// <summary>
        /// Gets the packaged message deletion script filename.
        /// </summary>
        public const string DeleteMessages = "delete_messages.js";

        /// <summary>
        /// Gets the packaged startup probe script filename.
        /// </summary>
        public const string PreflightProbe = "preflight_probe.js";

        /// <summary>
        /// Replaces placeholder tokens in a script template with runtime values.
        /// </summary>
        /// <param name="template">Script template that contains replacement markers.</param>
        /// <param name="replacements">Placeholder and replacement value pairs.</param>
        /// <returns>The script with all requested placeholders substituted.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="template"/> or <paramref name="replacements"/> is <see langword="null"/>.
        /// </exception>
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
