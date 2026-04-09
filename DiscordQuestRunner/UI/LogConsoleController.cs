using System.Text;

namespace DiscordQuestRunner.UI
{
    /// <summary>
    /// Buffers log output for a MAUI label and keeps the scroll position pinned to the latest entry.
    /// </summary>
    public sealed class LogConsoleController
    {
        private readonly Label _outputLabel;
        private readonly ScrollView _scrollView;
        private readonly Label? _lineCountLabel;
        private readonly StringBuilder _buffer = new();

        /// <summary>
        /// Gets the current number of rendered log lines.
        /// </summary>
        public int LineCount { get; private set; }

        /// <summary>
        /// Gets the complete buffered log text.
        /// </summary>
        public string Text => _buffer.ToString();

        /// <summary>
        /// Initializes the log controller for a page-specific output surface.
        /// </summary>
        /// <param name="outputLabel">Label that renders the buffered log text.</param>
        /// <param name="scrollView">Scroll container that should follow appended log entries.</param>
        /// <param name="lineCountLabel">Optional label that displays the current line count.</param>
        public LogConsoleController(
            Label outputLabel,
            ScrollView scrollView,
            Label? lineCountLabel = null)
        {
            _outputLabel = outputLabel;
            _scrollView = scrollView;
            _lineCountLabel = lineCountLabel;

            var initialText = outputLabel.Text ?? string.Empty;
            _buffer.Append(initialText);
            LineCount = CountLines(initialText);
            UpdateLineCountLabel();
        }

        /// <summary>
        /// Replaces the current log buffer with a single starting line.
        /// </summary>
        /// <param name="firstLine">Initial line that should replace the current buffer.</param>
        /// <returns>A task that completes after the UI has been updated on the main thread.</returns>
        public Task ResetAsync(string firstLine) =>
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                _buffer.Clear();
                _buffer.Append(firstLine);
                LineCount = CountLines(firstLine);
                _outputLabel.Text = _buffer.ToString();
                UpdateLineCountLabel();
            });

        /// <summary>
        /// Appends a line to the buffer and scrolls the output view to the end.
        /// </summary>
        /// <param name="message">Message text to append.</param>
        /// <param name="prefix">Optional prefix rendered in brackets ahead of the message.</param>
        /// <returns>A task that completes after the UI has been updated and scrolled.</returns>
        public Task AppendLineAsync(string message, string prefix = "") =>
            MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var line = string.IsNullOrEmpty(prefix)
                    ? message
                    : $"[{prefix}] {message}";

                if (_buffer.Length > 0)
                {
                    _buffer.Append('\n');
                    LineCount++;
                }
                else
                {
                    LineCount = 1;
                }

                _buffer.Append(line);
                _outputLabel.Text = _buffer.ToString();
                UpdateLineCountLabel();
                await _scrollView.ScrollToAsync(
                    _outputLabel,
                    ScrollToPosition.End,
                    animated: true);
            });

        /// <summary>
        /// Updates the optional line count label to match the current buffer.
        /// </summary>
        private void UpdateLineCountLabel()
        {
            if (_lineCountLabel is null)
            {
                return;
            }

            _lineCountLabel.Text = LineCount == 1
                ? "1 line"
                : $"{LineCount} lines";
        }

        /// <summary>
        /// Counts logical lines in the buffered output.
        /// </summary>
        /// <param name="text">Buffered text to inspect.</param>
        /// <returns>The number of newline-delimited lines.</returns>
        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            return text.Split('\n').Length;
        }
    }
}
