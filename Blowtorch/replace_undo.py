import re

file_path = r"E:\DEV\Csharp\FuseEngine\Blowtorch\EditorUI.cs"

with open(file_path, "r", encoding="utf-8") as f:
    content = f.read()

content = content.replace(
    "history.PushCommand(new SnapshotCommand(sceneService, assetService, _preEditState, sceneService.Document.Serialize()));",
    "Undo.ForceEnd(history, sceneService, assetService);"
)

with open(file_path, "w", encoding="utf-8") as f:
    f.write(content)
print("Fixed remaining _preEditState")
