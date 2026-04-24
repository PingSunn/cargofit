using System;
using System.IO;

namespace logistic;

internal static class AppPaths
{
    // Repo root when running from dev environment; falls back to exe directory when deployed.
    internal static readonly string DataDir = FindDataDir();

    private static string FindDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
