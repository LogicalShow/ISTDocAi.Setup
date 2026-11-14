using NUnit.Framework;
namespace ISTDocAi.Setup.Tests;
public abstract class GuardedToolTests
{
    [SetUp]
    public void RequireOptIn()
    {
        var categories = TestContext.CurrentContext.Test.Properties["Category"].Cast<string>()
            .Concat(GetType().GetCustomAttributes(typeof(CategoryAttribute), true).Cast<CategoryAttribute>().Select(x => x.Name)).ToArray();
        if (categories.Contains("Sql") && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCAI_TEST_SQL")))
            Assert.Ignore("Set DOCAI_TEST_SQL to run disposable SQL Server tests.");
        if (categories.Contains("Machine") && (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("DOCAI_TEST_MACHINE") != "1"))
            Assert.Ignore("Set DOCAI_TEST_MACHINE=1 on Windows to run certificate-store, DPAPI and TLS tests.");
    }
}
