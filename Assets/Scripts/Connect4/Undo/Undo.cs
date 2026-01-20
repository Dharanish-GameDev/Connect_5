using System.Collections.Generic;

public class Undo
{
    private Stack<Memento> history = new Stack<Memento>();

    // Push a new state
    public void AddMementoToStack(Memento memento)
    {
        if (memento == null) return;
        history.Push(memento);
    }

    // Pop last state (undo 1 move)
    public Memento Pop()
    {
        if (history.Count == 0)
            return null;

        return history.Pop();
    }

    // How many undo states exist
    public int Count()
    {
        return history.Count;
    }

    // Optional helper
    public void Clear()
    {
        history.Clear();
    }
}
