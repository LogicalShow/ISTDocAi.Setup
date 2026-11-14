using ISTDocAi.DevSetup;
using ISTDocAi.Provisioning;
using NUnit.Framework;

namespace ISTDocAi.Setup.Tests;

public sealed class DevSetupPathAndExportTests
{
    private string folder = null!;
    [SetUp]
    public void Create() { folder = Path.Combine(RepositoryPaths.SetupRoot, "TestResults", "paths-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); }
    [TearDown]
    public void Cleanup() { Directory.Delete(folder, true); }

    [Test]
    public void Quoted_repository_and_kit_options_validate_without_prompting()
    {
        File.WriteAllText(Path.Combine(folder, "ISTDocAi.sln"), "");
        var properties = Path.Combine(folder, "ISTDocAi.UI", "ISTDocAi.UI.Server", "Properties");
        Directory.CreateDirectory(properties); File.WriteAllText(Path.Combine(properties, "launchSettings.json"), "{}");
        var kit = Path.Combine(folder, "Forwarding.MvcApp"); Directory.CreateDirectory(kit);
        File.WriteAllText(Path.Combine(kit, "Web.config"), "<configuration><appSettings file='App_Data/docai.secrets.config'/></configuration>");
        var console = new ConsoleStub([]);
        var options = SetupOptions.Parse(["--istdocai", " \"" + folder + "\" ", "--kit", "\"" + folder + "\""]);
        Assert.That(DevSetupRunner.SelectFolder(options.ISTDocAi, false, console), Is.EqualTo(folder));
        Assert.That(DevSetupRunner.SelectFolder(options.Kit, true, console), Is.EqualTo(kit));
        Assert.That(console.Reads, Is.Zero);
    }

    [Test]
    public void Invalid_option_reprompts_and_valid_answer_recovers()
    {
        var kit = Path.Combine(folder, "Forwarding.MvcApp"); Directory.CreateDirectory(kit);
        File.WriteAllText(Path.Combine(kit, "Web.config"), "<configuration><appSettings file='App_Data/docai.secrets.config'/></configuration>");
        var console = new ConsoleStub([folder]);
        Assert.That(DevSetupRunner.SelectFolder(Path.Combine(folder, "missing"), true, console), Is.EqualTo(kit));
        Assert.That(console.Reads, Is.EqualTo(1));
        Assert.That(console.Lines.Any(line => line.StartsWith("Invalid Kit folder:", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public async Task Three_invalid_answers_exit_two_without_machine_access()
    {
        var console = new ConsoleStub([folder, folder, folder]);
        Assert.That(await DevSetupRunner.Execute([], new NoMachineEnvironment(true), console), Is.EqualTo(2));
        Assert.That(console.Reads, Is.EqualTo(3));
        Assert.That(console.Lines.Last(), Does.Contain("3 attempts"));
    }

    [Test]
    public async Task Export_without_elevation_preserves_source_bytes_and_refuses_partial_overwrite()
    {
        var target = Path.Combine(folder, "export");
        var console = new ConsoleStub([]);
        Assert.That(await DevSetupRunner.Execute(["--export-sql", target], new NoMachineEnvironment(false), console), Is.Zero);
        foreach (var pair in new[] { ("dev", "ISTDocAi-DevSeed.sql"), ("kit", "CreateAIDocumentTables.sql") })
            Assert.That(File.ReadAllBytes(Path.Combine(target, pair.Item2)), Is.EqualTo(File.ReadAllBytes(Path.Combine(RepositoryPaths.SetupRoot, "sql", pair.Item1, pair.Item2))));
        File.Delete(Path.Combine(target, "ISTDocAi-DevSeed.sql"));
        var before = File.ReadAllBytes(Path.Combine(target, "CreateAIDocumentTables.sql"));
        Assert.That(await DevSetupRunner.Execute(["--export-sql", target], new NoMachineEnvironment(false), console), Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(target, "ISTDocAi-DevSeed.sql")), Is.False);
        Assert.That(File.ReadAllBytes(Path.Combine(target, "CreateAIDocumentTables.sql")), Is.EqualTo(before));
    }

    private sealed class NoMachineEnvironment(bool elevated) : SetupEnvironment
    {
        public override bool Elevated => elevated;
        public override bool PauseAtExit => false;
        public override string Account => throw new AssertionException("No machine access expected");
        public override ProcessResult Process(string executable, params string[] arguments) => throw new AssertionException("No process expected");
    }
    private sealed class ConsoleStub(string[] answers) : IProvisionConsole
    {
        private readonly Queue<string> input = new(answers);
        public int Reads { get; private set; }
        public List<string> Lines { get; } = [];
        public string? ReadLine() { Reads++; return input.Dequeue(); }
        public ConsoleKeyInfo ReadKey() => throw new AssertionException("No secret prompt expected");
        public void Write(string text) { }
        public void WriteLine(string text) => Lines.Add(text);
    }
}
