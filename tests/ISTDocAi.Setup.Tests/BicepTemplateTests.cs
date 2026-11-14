using System.Diagnostics;
using ISTDocAi.OperatorTool;
using NUnit.Framework;

namespace ISTDocAi.Setup.Tests;

[Category("Machine")]
public sealed class BicepTemplateTests
{
    [Test]
    public void Embedded_template_matches_local_bicep_compilation()
    {
        var installed = @"C:\Users\Anter\AppData\Local\Programs\Bicep CLI\bicep.exe";
        var executable = File.Exists(installed) ? installed : (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p, "bicep.exe")).FirstOrDefault(File.Exists);
        if (executable is null) Assert.Ignore("Install Bicep CLI to verify the committed template.");
        var path = Path.Combine(RepositoryPaths.SetupRoot, "TestResults", "bicep-" + Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var start = new ProcessStartInfo(executable!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "build", Path.Combine(RepositoryPaths.SetupRoot, "azure", "infra", "main.bicep"), "--outfile", path }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(true); Assert.Fail("Bicep build timed out."); }
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
            Assert.That(process.ExitCode, Is.Zero, stderr.Result);
            Assert.That(File.ReadAllText(path), Is.EqualTo(AzureInfra.Template()), "Rebuild azure/infra/main.json with the installed Bicep version.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
