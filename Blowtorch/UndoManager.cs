using Blowtorch;
using Fuse.Scene;
using ImGuiNET;

namespace Blowtorch
{
    public class UndoManager
    {
        private string _preEditState = "";
        private string _nextPreEditState = "";
        private bool _needsCommit = false;

        public void RecordState(string frameBeginState)
        {
            _nextPreEditState = frameBeginState;
        }

        public void TrackItem(string frameBeginState)
        {
            if (ImGui.IsItemActivated())
            {
                RecordState(frameBeginState);
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _needsCommit = true;
            }
        }

        public void EndFrame(CommandHistory history, EditorSceneService sceneService, EditorAssetService assetService)
        {
            if (_needsCommit)
            {
                var postEditState = sceneService.Document.Serialize();
                // Ensure we don't push a DUD command if the states are identical
                if (_preEditState != "" && _preEditState != postEditState)
                {
                    history.PushCommand(new SnapshotCommand(sceneService, assetService, _preEditState, postEditState));
                }
                _needsCommit = false;
            }

            if (_nextPreEditState != "")
            {
                _preEditState = _nextPreEditState;
                _nextPreEditState = "";
            }
        }

        public void ForceStart(string frameBeginState)
        {
            _preEditState = frameBeginState;
            _nextPreEditState = "";
        }

        public void ForceEnd(CommandHistory history, EditorSceneService sceneService, EditorAssetService assetService)
        {
            var postEditState = sceneService.Document.Serialize();
            if (_preEditState != "" && _preEditState != postEditState)
            {
                history.PushCommand(new SnapshotCommand(sceneService, assetService, _preEditState, postEditState));
            }
        }
    }
}
