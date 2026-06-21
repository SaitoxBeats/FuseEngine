using Fuse.Core;

namespace Fuse;

public static class ResPath
{
    public static string Path { get; private set; }

    static ResPath()
    {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;

        string exeRes = System.IO.Path.Combine(exeDir, "res");
        if (System.IO.Directory.Exists(exeRes))
        {
            Path = exeRes;
            return;
        }

        var dir = new System.IO.DirectoryInfo(exeDir);
        while (dir != null)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, "res");
            if (System.IO.Directory.Exists(candidate))
            {
                Path = candidate;
                return;
            }
            dir = dir.Parent;
        }

        Path = System.IO.Path.Combine(Environment.CurrentDirectory, "res");
        Logger.Warn($"ResPath fallback: {Path}");
    }
}
