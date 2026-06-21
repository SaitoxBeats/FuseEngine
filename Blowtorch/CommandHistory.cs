using System;
using System.Collections.Generic;
using Blowtorch.Model;

namespace Blowtorch;

public interface ICommand
{
    void Execute();
    void Undo();
}

public class SnapshotCommand : ICommand
{
    private readonly EditorSceneService _sceneService;
    private readonly EditorAssetService _assetService;
    private readonly string _stateBefore;
    private readonly string _stateAfter;

    public SnapshotCommand(EditorSceneService sceneService, EditorAssetService assetService, string stateBefore, string stateAfter)
    {
        _sceneService = sceneService;
        _assetService = assetService;
        _stateBefore = stateBefore;
        _stateAfter = stateAfter;
    }

    public void Execute()
    {
        RestoreState(_stateAfter);
    }

    public void Undo()
    {
        RestoreState(_stateBefore);
    }

    private void RestoreState(string json)
    {
        var doc = MapDocument.Parse(json);
        if (doc != null)
        {
            _sceneService.SetDocument(doc);
            _sceneService.PopulateScene(_assetService);
        }
    }
}

public class CommandHistory
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();

    public void PushCommand(ICommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count > 0)
        {
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
        }
    }

    public void Redo()
    {
        if (_redoStack.Count > 0)
        {
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
        }
    }
}
