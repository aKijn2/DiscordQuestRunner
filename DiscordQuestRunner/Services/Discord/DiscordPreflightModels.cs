namespace DiscordQuestRunner.Services
{
    /// <summary>
    /// Identifies the Discord automation capabilities that a workflow requires before execution starts.
    /// </summary>
    [Flags]
    public enum DiscordAutomationCapability
    {
        /// <summary>
        /// Indicates that no capability-specific automation probe is required.
        /// </summary>
        None = 0,

        /// <summary>
        /// Requires Discord's internal REST client used by automation scripts.
        /// </summary>
        RestApi = 1,

        /// <summary>
        /// Requires Discord's internal quests store used by the quest runner.
        /// </summary>
        QuestsStore = 2,
    }

    /// <summary>
    /// Identifies a single stage inside the startup environment validation flow.
    /// </summary>
    public enum DiscordPreflightStage
    {
        /// <summary>
        /// Verifies that a Discord desktop process is running.
        /// </summary>
        Process,

        /// <summary>
        /// Verifies that the CDP debug port responds.
        /// </summary>
        DebugPort,

        /// <summary>
        /// Verifies that a usable Discord renderer target can be resolved.
        /// </summary>
        Target,

        /// <summary>
        /// Verifies that the required internal Webpack-backed automation modules are accessible.
        /// </summary>
        AutomationSurface,
    }

    /// <summary>
    /// Represents the outcome of a single preflight stage.
    /// </summary>
    /// <param name="Stage">Stage that produced the result.</param>
    /// <param name="Success">Whether the stage completed successfully.</param>
    /// <param name="Message">Human-readable detail describing the outcome.</param>
    public sealed record DiscordPreflightStep(
        DiscordPreflightStage Stage,
        bool Success,
        string Message);

    /// <summary>
    /// Aggregates all startup validation results required before automation begins.
    /// </summary>
    /// <param name="WebSocketDebuggerUrl">Resolved CDP WebSocket URL when a renderer target is available.</param>
    /// <param name="Steps">Ordered stage results emitted during the preflight run.</param>
    public sealed record DiscordPreflightReport(
        string? WebSocketDebuggerUrl,
        IReadOnlyList<DiscordPreflightStep> Steps)
    {
        /// <summary>
        /// Gets a value indicating whether every recorded stage succeeded.
        /// </summary>
        public bool Success => Steps.All(step => step.Success);

        /// <summary>
        /// Gets a value indicating whether a Discord desktop process was detected.
        /// </summary>
        public bool ProcessFound => IsStageSuccessful(DiscordPreflightStage.Process);

        /// <summary>
        /// Gets a value indicating whether the CDP debug port responded successfully.
        /// </summary>
        public bool DebugPortReady => IsStageSuccessful(DiscordPreflightStage.DebugPort);

        /// <summary>
        /// Gets a value indicating whether a usable renderer target was resolved.
        /// </summary>
        public bool TargetResolved => IsStageSuccessful(DiscordPreflightStage.Target);

        /// <summary>
        /// Gets a value indicating whether the capability probe succeeded.
        /// </summary>
        public bool AutomationSurfaceReady => IsStageSuccessful(DiscordPreflightStage.AutomationSurface);

        /// <summary>
        /// Gets the most relevant failure detail when any stage fails.
        /// </summary>
        public string FailureMessage =>
            Steps.LastOrDefault(step => !step.Success)?.Message
            ?? "Preflight environment check failed.";

        /// <summary>
        /// Determines whether the specified stage completed successfully.
        /// </summary>
        /// <param name="stage">Stage to inspect.</param>
        /// <returns>
        /// <see langword="true"/> when the stage exists and succeeded; otherwise, <see langword="false"/>.
        /// </returns>
        private bool IsStageSuccessful(DiscordPreflightStage stage) =>
            Steps.FirstOrDefault(step => step.Stage == stage)?.Success == true;
    }
}
