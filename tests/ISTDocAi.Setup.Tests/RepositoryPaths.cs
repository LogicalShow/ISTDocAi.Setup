using System.Reflection;
namespace ISTDocAi.Setup.Tests;
internal static class RepositoryPaths
{
    internal static string ISTDocAiRoot
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("ISTDOCAI_ROOT");
            if (string.IsNullOrWhiteSpace(root)) root = typeof(RepositoryPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute => attribute.Key == "ISTDocAiRoot").Value;
            if (root is null || !File.Exists(Path.Combine(root, "ISTDocAi.sln")))
                throw new InvalidOperationException("Set ISTDOCAI_ROOT to the ISTDocAi repository folder containing ISTDocAi.sln.");
            return Path.GetFullPath(root);
        }
    }
    internal static string SetupRoot
    {
        get
        {
            for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "ISTDocAi.Setup.sln"))) return folder.FullName;
            throw new InvalidOperationException("Setup repository unavailable.");
        }
    }
}
