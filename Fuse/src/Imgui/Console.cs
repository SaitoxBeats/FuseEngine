using System.Numerics;
using System.Text;
using ImGuiNET;
using Fuse.Core;
using Fuse.Player;

namespace Fuse.Imgui;

public class Console
{
    private bool _open;
    private readonly byte[] _inputBuf = new byte[256];
    private readonly List<string> _log = [];
    private bool _scrollToBottom;
    private Player.Player? _player;
    private TextWriter? _originalOut;

    public Console()
    {
        AddLog("Console initialized. Type 'help' for commands.");
    }

    public void SetPlayer(Player.Player? player) => _player = player;

    public void StartCapture()
    {
        _originalOut = System.Console.Out;
        System.Console.SetOut(new CaptureWriter(this));
    }

    public void StopCapture()
    {
        if (_originalOut != null)
            System.Console.SetOut(_originalOut);
    }

    public void Toggle() => _open = !_open;
    public bool IsOpen => _open;

    public void Draw()
    {
        if (!_open) return;

        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
        ImGui.Begin("Console", ref _open, ImGuiWindowFlags.None);

        float footerH = ImGui.GetStyle().ItemSpacing.Y + ImGui.GetFrameHeightWithSpacing();
        ImGui.BeginChild("ScrollingRegion", new Vector2(0, -footerH), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        foreach (var line in _log)
            ImGui.TextUnformatted(line);
        if (_scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
        }
        ImGui.EndChild();

        ImGui.Separator();
        if (ImGui.InputText("##cmd", _inputBuf, (uint)_inputBuf.Length, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            string cmd = Encoding.UTF8.GetString(_inputBuf).TrimEnd('\0');
            if (!string.IsNullOrEmpty(cmd))
            {
                AddLog("] " + cmd);
                Execute(cmd);
            }
            Array.Clear(_inputBuf, 0, _inputBuf.Length);
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.End();
    }

    private void Execute(string cmd)
    {
        string lower = cmd.ToLowerInvariant();
        if (lower == "noclip")
        {
            if (_player != null)
            {
                _player.ToggleNoclip();
                AddLog(_player.IsNoclip ? "noclip ON" : "noclip OFF");
            }
            return;
        }
        if (lower == "help")
        {
            AddLog("Commands:");
            AddLog("  noClip  - Toggle noclip");
            AddLog("  help    - Show this");
            AddLog("  clear   - Clear console");
            return;
        }
        if (lower == "clear")
        {
            _log.Clear();
            return;
        }
        AddLog("Unknown: " + cmd);
    }

    public void AddLog(string msg)
    {
        _log.Add(StripANSI(msg));
        _scrollToBottom = true;
        _originalOut?.WriteLine(msg);
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

        public CaptureWriter(Console console) => _console = console;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                // flush handled by WriteLine
            }
        }

        public override void WriteLine(string? value)
        {
            if (!string.IsNullOrEmpty(value))
                _console.AddLog(value);
        }
    }
}
