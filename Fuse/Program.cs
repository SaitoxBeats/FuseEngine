using Fuse.Core;
using System.IO;

internal static class Program
{
    private static void Main(string[] args)
    {
        string splashPath = Path.Combine(Fuse.ResPath.Path, "splash.txt");
        if (File.Exists(splashPath))
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine(File.ReadAllText(splashPath));
            Console.WriteLine(" - GOD BLESS YOU - ");
            Console.ResetColor();
        }

        Application app = new();
        string mapNameArg = args.Length > 0 ? args[0] : "default.bth";
        if (!app.Init(mapNameArg)) return;
        app.Run();
    }
}
