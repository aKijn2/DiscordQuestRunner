using System.Text;

namespace DiscordQuestRunner.UI
{
    public sealed class LogConsoleController
    {
        private readonly Label _outputLabel;
        private readonly ScrollView _scrollView;
        private readonly Label? _lineCountLabel;
        private readonly StringBuilder _buffer = new();

        public int LineCount { get; private set; }

        public string Text => _buffer.ToString();

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

        public Task ResetAsync(string firstLine) =>
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                _buffer.Clear();
                _buffer.Append(firstLine);
                LineCount = CountLines(firstLine);
                _outputLabel.Text = _buffer.ToString();
                UpdateLineCountLabel();
            });

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
