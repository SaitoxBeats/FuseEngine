using System;

namespace Blowtorch;

internal static class Program
{
    private static void Main()
    {
        using var app = new EditorApplication();
        if (!app.Init()) return;
        app.Run();
    }
}