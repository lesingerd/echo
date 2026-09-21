namespace EchoTray;

/// <summary>
/// The last few things copied, newest first. Held in memory only and never written to disk --
/// people copy passwords, and a tray tool has no business persisting them.
/// </summary>
internal sealed class ClipboardHistory
{
    /// <summary>Anything larger than this is left out; it is clipboard history, not a document store.</summary>
    private const int MaxEntryLength = 1_000_000;

    private readonly List<string> _entries = new();
    private int _capacity = 10;

    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Clamp(value, 0, 25);
            Trim();
        }
    }

    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Adds text, or moves it back to the top if it is already known. True if anything changed.</summary>
    public bool Add(string? text)
    {
        if (_capacity == 0 || string.IsNullOrWhiteSpace(text) || text.Length > MaxEntryLength)
        {
            return false;
        }

        int existing = _entries.FindIndex(e => string.Equals(e, text, StringComparison.Ordinal));
        if (existing == 0)
        {
            return false;
        }

        if (existing > 0)
        {
            _entries.RemoveAt(existing);
        }

        _entries.Insert(0, text);
        Trim();
        return true;
    }

    public void Clear() => _entries.Clear();

    private void Trim()
    {
        if (_entries.Count > _capacity)
        {
            _entries.RemoveRange(_capacity, _entries.Count - _capacity);
        }
    }
}
