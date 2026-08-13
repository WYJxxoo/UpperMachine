using System.Collections.Concurrent;
using System.Text;

namespace UpperMachine.Services;

internal sealed class SerialLineBuffer
{
    private readonly StringBuilder _buffer = new();
    private readonly ConcurrentQueue<string> _pendingLines = new();

    public event Action<string>? LineReceived;

    public void Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        lock (_buffer)
        {
            _buffer.Append(chunk);
            string[] lines = _buffer.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            _buffer.Clear();

            for (int i = 0; i < lines.Length - 1; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                _pendingLines.Enqueue(line);
                LineReceived?.Invoke(line);
            }

            string tail = lines[^1];
            if (!string.IsNullOrWhiteSpace(tail))
            {
                _buffer.Append(tail);
            }
        }
    }

    public void ClearPendingLines()
    {
        while (_pendingLines.TryDequeue(out _))
        {
        }
    }

    public string[] DrainAvailableLines()
    {
        List<string> lines = new();
        while (_pendingLines.TryDequeue(out string? line))
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }
}
