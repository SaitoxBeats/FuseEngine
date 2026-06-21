using Fuse.Core;
using System.IO;

internal static class Program
{
    private static void Main()
    {
        string splashPath = Path.Combine(Fuse.ResPath.Path, "splash.txt");
        if (File.Exists(splashPath))
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine(File.ReadAllText(splashPath));
            Console.WriteLine(" - GOD BLESS YOU - ");
            Console.ResetColor();
        }

        Application app = new();
        if (!app.Init()) return;
        app.Run();
    }
}
