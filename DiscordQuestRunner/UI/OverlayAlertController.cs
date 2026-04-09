namespace DiscordQuestRunner.UI
{
    public sealed class OverlayAlertController
    {
        private readonly Grid _overlay;
        private readonly Label _titleLabel;
        private readonly Label _messageLabel;
        private readonly Button _confirmButton;
        private readonly Button _cancelButton;

        private TaskCompletionSource<bool>? _pendingAlert;

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

        public Task ConfirmAsync() => CloseAsync(true);

        public Task CancelAsync() => CloseAsync(false);

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
