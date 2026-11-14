using System.Xml.Linq;
using NUnit.Framework;
namespace ISTDocAi.Setup.Tests;
public sealed class ToolBoundaryTests
{
    [Test]
    public void Source_projects_reference_only_source_projects()
    {
        var source = Path.GetFullPath(Path.Combine(RepositoryPaths.SetupRoot, "src")) + Path.DirectorySeparatorChar;
        var projects = Directory.GetFiles(source, "*.csproj", SearchOption.AllDirectories);
        Assert.That(projects, Is.Not.Empty);
        foreach (var project in projects)
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, (string)reference.Attribute("Include")!));
                Assert.That(path.StartsWith(source, StringComparison.OrdinalIgnoreCase) && File.Exists(path), Is.True, project);
            }
    }
}
