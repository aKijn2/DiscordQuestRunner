namespace DiscordQuestRunner.UI
{
    /// <summary>
    /// Coordinates a reusable modal overlay that emulates a confirm or cancel dialog inside a MAUI page.
    /// </summary>
    public sealed class OverlayAlertController
    {
        private readonly Grid _overlay;
        private readonly Label _titleLabel;
        private readonly Label _messageLabel;
        private readonly Button _confirmButton;
        private readonly Button _cancelButton;

        private TaskCompletionSource<bool>? _pendingAlert;

        /// <summary>
        /// Initializes the controller with the visual elements that render the overlay alert.
        /// </summary>
        /// <param name="overlay">Overlay container that is shown and hidden during alert presentation.</param>
        /// <param name="titleLabel">Label that displays the alert title.</param>
        /// <param name="messageLabel">Label that displays the alert body text.</param>
        /// <param name="confirmButton">Button that resolves the alert positively.</param>
        /// <param name="cancelButton">Button that resolves the alert negatively.</param>
        public OverlayAlertController(
            Grid overlay,
            Label titleLabel,
            Label messageLabel,
            Button confirmButton,
            Button cancelButton)
        {
            _overlay = overlay;
            _titleLabel = titleLabel;
            _messageLabel = messageLabel;
            _confirmButton = confirmButton;
            _cancelButton = cancelButton;
        }

        /// <summary>
        /// Displays the configured overlay alert and awaits the user selection.
        /// </summary>
        /// <param name="title">Title rendered at the top of the alert.</param>
        /// <param name="message">Body text rendered inside the alert.</param>
        /// <param name="confirmText">Text displayed on the confirm button.</param>
        /// <param name="cancelText">Optional text displayed on the cancel button.</param>
        /// <returns>
        /// <see langword="true"/> when the confirm button is selected; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a second alert is requested while another alert is still active.
        /// </exception>
        public async Task<bool> ShowAsync(
            string title,
            string message,
            string confirmText,
            string? cancelText = null)
        {
            if (_pendingAlert is not null)
            {
                throw new InvalidOperationException("An alert is already active.");
            }

            _titleLabel.Text = title.ToUpperInvariant();
            _messageLabel.Text = message;
            _confirmButton.Text = confirmText.ToUpperInvariant();

            var hasCancel = !string.IsNullOrEmpty(cancelText);
            _cancelButton.IsVisible = hasCancel;
            _cancelButton.Text = hasCancel
                ? cancelText!.ToUpperInvariant()
                : string.Empty;
            Grid.SetColumnSpan(_confirmButton, hasCancel ? 1 : 2);

            _overlay.IsVisible = true;
            await _overlay.FadeTo(1, 200, Easing.CubicOut);

            _pendingAlert = new TaskCompletionSource<bool>();
            return await _pendingAlert.Task;
        }

        /// <summary>
        /// Resolves the active alert as confirmed.
        /// </summary>
        /// <returns>A task that completes after the overlay has been dismissed.</returns>
        public Task ConfirmAsync() => CloseAsync(true);

        /// <summary>
        /// Resolves the active alert as cancelled.
        /// </summary>
        /// <returns>A task that completes after the overlay has been dismissed.</returns>
        public Task CancelAsync() => CloseAsync(false);

        /// <summary>
        /// Hides the overlay and resolves the pending alert task.
        /// </summary>
        /// <param name="result">Result returned to the caller awaiting the alert.</param>
        /// <returns>A task that completes after the closing animation finishes.</returns>
        private async Task CloseAsync(bool result)
        {
            if (_pendingAlert is null)
            {
                return;
            }

            await _overlay.FadeTo(0, 150, Easing.CubicIn);
            _overlay.IsVisible = false;

            var pendingAlert = _pendingAlert;
            _pendingAlert = null;
            pendingAlert.TrySetResult(result);
        }
    }
}
