using System.Numerics;
using System.Text;
using System.Runtime.InteropServices;
using ImGuiNET;
using Fuse.Core;
using Fuse.Player;

namespace Fuse.Imgui;

public struct LogEntry
{
    public string Text;
    public Vector4 Color;

    public LogEntry(string text, Vector4 color)
    {
        Text = text;
        Color = color;
    }
}

public class Console
{
    private bool _open;
    private readonly byte[] _inputBuf = new byte[256];
    private readonly List<LogEntry> _log = [];
    private readonly List<string> _commandHistory = [];
    private int _historyPos = -1;
    
    private readonly List<string> _commands = ["loadmap", "loadsky", "noclip", "help", "clear"];

    private bool _scrollToBottom;
    private Player.Player? _player;
    private TextWriter? _originalOut;
    private TextWriter? _originalError;
    
    public Action<string>? OnLoadMap { get; set; }
    public Action<string>? OnLoadSky { get; set; }

    public Console()
    {
        AddLog("Console initialized. Type 'help' for commands.", new Vector4(0, 1, 0, 1));
    }

    public void SetPlayer(Player.Player? player) => _player = player;

    public void StartCapture()
    {
        _originalOut = System.Console.Out;
        _originalError = System.Console.Error;
        var outWriter = new CaptureWriter(this, false);
        var errWriter = new CaptureWriter(this, true);
        System.Console.SetOut(outWriter);
        System.Console.SetError(errWriter);
    }

    public void StopCapture()
    {
        if (_originalOut != null)
            System.Console.SetOut(_originalOut);
        if (_originalError != null)
            System.Console.SetError(_originalError);
    }

    public void Toggle()
    {
        _open = !_open;
        if (!_open)
            ImGui.SetWindowFocus(null);
    }
    public bool IsOpen => _open;

    public unsafe void Draw()
    {
        if (!_open) return;

        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        ImGui.Begin("Console", ref _open, ImGuiWindowFlags.None);

        if (ImGui.Button("Clear")) _log.Clear();
        ImGui.SameLine();
        bool copy = ImGui.Button("Copy to Clipboard");
        ImGui.Separator();

        float footerH = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild("ScrollingRegion", new Vector2(0, -footerH), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        
        if (copy) ImGui.LogToClipboard();

        foreach (var entry in _log)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, entry.Color);
            ImGui.TextUnformatted(entry.Text);
            ImGui.PopStyleColor();
        }

        if (copy) ImGui.LogFinish();

        if (_scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
        }
        ImGui.EndChild();

        ImGui.Separator();

        bool reclaimFocus = false;
        ImGuiInputTextFlags inputFlags = ImGuiInputTextFlags.EnterReturnsTrue 
                                       | ImGuiInputTextFlags.CallbackCompletion 
                                       | ImGuiInputTextFlags.CallbackHistory;

        if (ImGui.InputText("##cmd", _inputBuf, (uint)_inputBuf.Length, inputFlags, TextEditCallback))
        {
            string cmd = Encoding.UTF8.GetString(_inputBuf).TrimEnd('\0');
            if (!string.IsNullOrEmpty(cmd))
            {
                AddLog("] " + cmd, new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
                
                // Add to history
                _commandHistory.RemoveAll(s => s == cmd);
                _commandHistory.Add(cmd);
                _historyPos = -1;

                Execute(cmd);
            }
            Array.Clear(_inputBuf, 0, _inputBuf.Length);
            reclaimFocus = true;
        }

        ImGui.SetItemDefaultFocus();
        if (reclaimFocus)
            ImGui.SetKeyboardFocusHere(-1);

        ImGui.End();
    }

    private unsafe int TextEditCallback(ImGuiInputTextCallbackData* data)
    {
        if (data->EventFlag == ImGuiInputTextFlags.CallbackCompletion)
        {
            string currentText = Marshal.PtrToStringUTF8((IntPtr)data->Buf, data->BufTextLen);
            
            // Auto-complete
            var matches = _commands.Where(c => c.StartsWith(currentText, StringComparison.InvariantCultureIgnoreCase)).ToList();
            if (matches.Count == 1)
            {
                string match = matches[0] + " ";
                byte[] matchBytes = Encoding.UTF8.GetBytes(match);
                data->BufTextLen = matchBytes.Length;
                data->CursorPos = data->SelectionStart = data->SelectionEnd = matchBytes.Length;
                data->BufDirty = 1;
                
                byte* bufPtr = data->Buf;
                for (int i = 0; i < matchBytes.Length; i++)
                    bufPtr[i] = matchBytes[i];
                bufPtr[matchBytes.Length] = 0;
            }
            else if (matches.Count > 1)
            {
                AddLog("Possible matches:", new Vector4(1, 1, 0, 1));
                foreach (var m in matches)
                    AddLog("- " + m, new Vector4(1, 1, 0, 1));
            }
        }
        else if (data->EventFlag == ImGuiInputTextFlags.CallbackHistory)
        {
            int prevHistoryPos = _historyPos;
            if (data->EventKey == ImGuiKey.UpArrow)
            {
                if (_historyPos == -1)
                    _historyPos = _commandHistory.Count - 1;
                else if (_historyPos > 0)
                    _historyPos--;
            }
            else if (data->EventKey == ImGuiKey.DownArrow)
            {
                if (_historyPos != -1)
                {
                    if (++_historyPos >= _commandHistory.Count)
                        _historyPos = -1;
                }
            }

            if (prevHistoryPos != _historyPos)
            {
                string historyStr = (_historyPos >= 0) ? _commandHistory[_historyPos] : "";
                byte[] historyBytes = Encoding.UTF8.GetBytes(historyStr);
                data->BufTextLen = historyBytes.Length;
                data->CursorPos = data->SelectionStart = data->SelectionEnd = historyBytes.Length;
                data->BufDirty = 1;
                
                byte* bufPtr = data->Buf;
                for (int i = 0; i < historyBytes.Length; i++)
                    bufPtr[i] = historyBytes[i];
                bufPtr[historyBytes.Length] = 0;
            }
        }
        
        return 0;
    }

    private void Execute(string cmd)
    {
        string lower = cmd.ToLowerInvariant();
        if (lower == "noclip")
        {
            if (_player != null)
            {
                _player.ToggleNoclip();
                AddLog(_player.IsNoclip ? "noclip ON" : "noclip OFF", new Vector4(0, 1, 1, 1));
            }
            return;
        }
        if (lower.StartsWith("loadmap "))
        {
            string fileName = cmd["loadmap ".Length..].Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                AddLog("Usage: loadMap <Filename>", new Vector4(1, 1, 0, 1));
                return;
            }
            AddLog($"Loading Map: {fileName}", new Vector4(0, 1, 0, 1));
            OnLoadMap?.Invoke(fileName);
            return;
        }
        if (lower.StartsWith("loadsky "))
        {
            string fileName = cmd["loadsky ".Length..].Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                AddLog("Usage: loadSky <nome do arquivo>", new Vector4(1, 1, 0, 1));
                return;
            }
            AddLog($"Loading skybox: {fileName}", new Vector4(0, 1, 0, 1));
            OnLoadSky?.Invoke(fileName);
            return;
        }
        if (lower == "help")
        {
            AddLog("Commands:", new Vector4(1, 1, 1, 1));
            AddLog("  loadMap  - Load a map (e.g. loadMap map.json)", new Vector4(0.8f, 0.8f, 0.8f, 1));
            AddLog("  loadSky  - Load a skybox texture (e.g. loadSky skybox_1.png)", new Vector4(0.8f, 0.8f, 0.8f, 1));
            AddLog("  noClip   - Toggle noclip", new Vector4(0.8f, 0.8f, 0.8f, 1));
            AddLog("  help     - Show this", new Vector4(0.8f, 0.8f, 0.8f, 1));
            AddLog("  clear    - Clear console", new Vector4(0.8f, 0.8f, 0.8f, 1));
            return;
        }
        if (lower == "clear")
        {
            _log.Clear();
            return;
        }
        AddLog("Unknown command: " + cmd, new Vector4(1, 0.2f, 0.2f, 1));
    }

    public void AddLog(string msg, Vector4 color)
    {
        _log.Add(new LogEntry(StripANSI(msg), color));
        _scrollToBottom = true;
    }

    public void AddLog(string msg)
    {
        AddLog(msg, new Vector4(1, 1, 1, 1));
    }

    private static string StripANSI(string s)
    {
        int i = 0;
        var sb = new StringBuilder(s.Length);
        while (i < s.Length)
        {
            if (s[i] == '\x1b')
            {
                i++;
                while (i < s.Length && s[i] != 'm') i++;
                i++;
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    private sealed class CaptureWriter : TextWriter
    {
        private readonly Console _console;
        private readonly bool _isError;

        public CaptureWriter(Console console, bool isError)
        {
            _console = console;
            _isError = isError;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (_isError)
                _console._originalError?.Write(value);
            else
                _console._originalOut?.Write(value);
        }

        public override void Write(string? value)
        {
            if (value != null)
            {
                if (_isError)
                    _console._originalError?.Write(value);
                else
                    _console._originalOut?.Write(value);
            }
        }

        public override void WriteLine()
        {
            if (_isError)
                _console._originalError?.WriteLine();
            else
                _console._originalOut?.WriteLine();
        }

        public override void WriteLine(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                Vector4 color = new Vector4(1, 1, 1, 1); // Default White

                if (_isError || value.StartsWith("[ ERROR ]"))
                    color = new Vector4(1.0f, 0.2f, 0.2f, 1.0f); // Red
                else if (value.StartsWith("[ WARN ]"))
                    color = new Vector4(1.0f, 1.0f, 0.0f, 1.0f); // Yellow
                else if (value.StartsWith("[ ASSET ]"))
                    color = new Vector4(0.2f, 1.0f, 0.2f, 1.0f); // Green
                else if (value.StartsWith("[ IMPORTANT ]"))
                    color = new Vector4(0.2f, 0.6f, 1.0f, 1.0f); // Blue
                else if (value.StartsWith("[ INFO ]"))
                    color = new Vector4(0.9f, 0.9f, 0.9f, 1.0f); // Light Gray / White

                _console.AddLog(value, color);
                
                if (_isError)
                    _console._originalError?.WriteLine(value);
                else
                    _console._originalOut?.WriteLine(value);
            }
            else
            {
                WriteLine();
            }
        }
    }
}
