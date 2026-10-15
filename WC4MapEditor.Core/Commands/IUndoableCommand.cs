namespace WC4MapEditor.Core.Commands;

public interface IUndoableCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}