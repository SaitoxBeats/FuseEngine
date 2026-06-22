namespace Fuse.Core;

public static class Logger
{
    public static void Info(string message)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"[ INFO ] {message}");
        Console.ResetColor();
    }
    public static void Important(string message)
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"[ IMPORTANT ] {message}");
        Console.ResetColor();
    }
    public static void Asset(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[ ASSET ] {message}");
        Console.ResetColor();
    }

    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[ WARN ] {message}");
        Console.ResetColor();
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ ERROR ] {message}");
        Console.ResetColor();
    }
}
