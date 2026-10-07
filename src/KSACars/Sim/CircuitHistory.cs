namespace KSACars;

/// <summary>
/// A circuit being edited and every state it has been through, for undo and redo. A drag is many
/// changes and one step: it is begun once and each change after that replaces the last.
/// </summary>
internal sealed class CircuitHistory(Circuit start)
{
    private const int Kept = 200;

    private readonly List<Circuit> _past = [];
    private readonly List<Circuit> _undone = [];
    private bool _dragging;

    public Circuit Now { get; private set; } = start;

    public bool CanUndo => _past.Count > 0;

    public bool CanRedo => _undone.Count > 0;

    /// <summary>Whether what is here differs from what was last marked saved.</summary>
    public bool Unsaved => !ReferenceEquals(Now, _saved);

    private Circuit _saved = start;

    public void MarkSaved() => _saved = Now;

    /// <summary>One edit, which is one step back. An edit that changed nothing is not a step.</summary>
    public void Do(Circuit next)
    {
        if (ReferenceEquals(next, Now)) return;
        if (!_dragging) Push();
        Now = next;
        _undone.Clear();
    }

    public void BeginDrag()
    {
        if (_dragging) return;
        Push();
        _dragging = true;
    }

    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        // A drag that went nowhere is not a step.
        if (_past.Count > 0 && ReferenceEquals(_past[^1], Now)) _past.RemoveAt(_past.Count - 1);
    }

    public bool Undo()
    {
        EndDrag();
        if (_past.Count == 0) return false;
        _undone.Add(Now);
        Now = _past[^1];
        _past.RemoveAt(_past.Count - 1);
        return true;
    }

    public bool Redo()
    {
        EndDrag();
        if (_undone.Count == 0) return false;
        _past.Add(Now);
        Now = _undone[^1];
        _undone.RemoveAt(_undone.Count - 1);
        return true;
    }

    private void Push()
    {
        _past.Add(Now);
        if (_past.Count > Kept) _past.RemoveAt(0);
    }
}
