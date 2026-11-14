using NUnit.Framework;
using Assert = ISTDocAi.Setup.Tests.PortAssert;
using ISTDocAi.Provisioning;
using ISTDocAi.Application.Common.Interfaces.DocAi.Persistence;
using ISTDocAi.Infrastructure.Services.DocAi.Intake;
using ISTDocAi.Infrastructure.Services.DocAi.Secrets;
using ISTDocAi.Infrastructure.Configuration.DocAi;
using ISTDocAi.Domain.Dtos.DocAi;
using Microsoft.Data.SqlClient;

namespace ISTDocAi.Setup.Tests.Provisioning;

public sealed class ReviewRoundOneTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public void Prepared_rerun_defaults_match_stored_values_and_mismatch_is_refused()
    {
        var file = KitSecretsFile.Parse("<appSettings/>");
        var pairing = file.Prepare(TestData.Request, TestData.Created);
        foreach (var key in new[] { "ProductCode", "TenantCode", "Name", "CallbackUrl", "BaseUrl" })
            Assert.Equal(file.Get("DocAiProvision:" + key), ProvisionRunner.PreparedDefault(file, key));
        Assert.Equal(PairingCode.Encode(pairing), PairingCode.Encode(file.Prepare(TestData.Request, TestData.Created)));
        Assert.Throws<InvalidOperationException>(() => file.Prepare(TestData.Request with { Name = "Different" }, TestData.Created));
        file.Activate(pairing.ConsumerCode);
        Assert.Null(ProvisionRunner.PreparedDefault(file, "Name"));
    }

    [Test]
    public void Pairing_reprints_identical_code_in_Prepared_and_Active_with_same_warning()
    {
        var file = KitSecretsFile.Parse("<appSettings/>");
        file.Prepare(TestData.Request, TestData.Created);
        var first = new StageBCliTests.FakeConsole("");
        ProvisionRunner.PrintPairing(file.Pairing(), first);
        file.Activate("FWD-UNF");
        var second = new StageBCliTests.FakeConsole("");
        ProvisionRunner.PrintPairing(file.Pairing(), second);
        Assert.Equal(first.Output.ToString(), second.Output.ToString());
        Assert.Contains("WARNING: SECRET PAIRING CODE", first.Output.ToString());
    }

    [Test]
    public void Pasted_pairing_trims_surrounding_whitespace()
    {
        var data = TestData.Pairing();
        Assert.Equal(PairingCode.Encode(data), PairingCode.Encode(ProvisionRunner.DecodePastedPairing(" \t" + PairingCode.Encode(data) + " \r\n")));
    }

    [TestCase(null)]
    [TestCase("FWD-OTHER")]
    public void Status_reports_identity_problem_without_refusing(string? code)
    {
        var decision = CompanyIdentityCheck.Decide(new("company", "Company", code), "FWD", "UNF");
        var console = new StageBCliTests.FakeConsole("");
        ProvisionRunner.ReportIdentity(decision, console, reportOnly: true);
        Assert.StartsWith("PROBLEM: ", console.Output.ToString());
        Assert.Throws<CliRefusal>(() => ProvisionRunner.ReportIdentity(decision, console, reportOnly: false));
    }

    [Test]
    public void Pairing_accepts_selection_and_yes_but_rejects_extra_options()
    {
        Assert.True(CliArguments.Parse(["pairing", "--site", "Kit/", "--yes"], true).Yes);
        Assert.Equal("C:\\Kit", CliArguments.Parse(["pairing", "--path", "C:\\Kit", "--pool-account", "account"], true).Get("path"));
        Assert.Throws<CliRefusal>(() => CliArguments.Parse(["pairing", "--pages", "1"], true));
        Assert.Throws<CliRefusal>(() => CliArguments.Parse(["pairing"], false));
        Assert.Contains("pairing", ProvisionHelp.Kit);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Console_waits_only_when_it_owns_the_console(bool alone)
    {
        var console = new StageBCliTests.FakeConsole("") { Line = "" };
        ConsoleLifetime.Pause(alone, console);
        Assert.Equal(alone, console.Output.ToString().Contains("Press Enter to close", StringComparison.Ordinal));
    }

    [Test]
    public void Only_library_authored_validation_errors_are_operator_errors()
    {
        Exception caught;
        try { Codes.Part("bad", "TenantCode"); throw new Exception(); }
        catch (ArgumentException error) { caught = error; }
        Assert.True(ProvisionRunner.IsOperatorError(caught));
        Assert.Equal("Invalid TenantCode.", caught.Message);
        Assert.False(ProvisionRunner.IsOperatorError(new ArgumentException("SECRET")));
        Assert.False(ProvisionRunner.IsOperatorError(new InvalidOperationException("SECRET")));
        try { KitSecretsFile.Parse("<wrong/>"); }
        catch (InvalidOperationException error) { Assert.True(ProvisionRunner.IsOperatorError(error)); return; }
        Assert.Fail("Expected library refusal");
    }

    [TestCase("Server=example;User ID=sqluser;Password=sample", false, "sqluser", "sample")]
    [TestCase("Server=example;Integrated Security=true", true, "", "")]
    public void Company_check_preserves_stored_authentication(string input, bool integrated, string user, string password)
    {
        var actual = new SqlConnectionStringBuilder(CompanyIdentityCheck.AdjustConnectionString(input));
        Assert.Equal(integrated, actual.IntegratedSecurity);
        Assert.Equal(user, actual.UserID); Assert.Equal(password, actual.Password);
        Assert.Equal(10, actual.ConnectTimeout);
    }

    [Test]
    public void Discovery_delays_unknown_and_missing_pool_failures_until_selection()
    {
        var apps = IisDiscovery.Parse("<appcmd><APP APP.NAME='Good/' APPPOOL.NAME='Good'/><APP APP.NAME='Bad/' APPPOOL.NAME='Bad'/><APP APP.NAME='Missing/' APPPOOL.NAME='Missing'/></appcmd>",
            "<appcmd/>", "<appcmd><APPPOOL APPPOOL.NAME='Good'/><APPPOOL APPPOOL.NAME='Bad'><processModel identityType='unknown'/></APPPOOL></appcmd>", "<appcmd/>");
        Assert.Equal("IIS AppPool\\Good", IisDiscovery.RequireAccount(apps[0]).Account);
        foreach (var app in apps.Skip(1)) Assert.Contains("Use --path and --pool-account", Assert.Throws<CliRefusal>(() => IisDiscovery.RequireAccount(app)).Message);
    }

    [Test]
    public void Discovery_duplicate_names_keep_first()
    {
        var app = IisDiscovery.Parse("<appcmd><APP APP.NAME='Site/' APPPOOL.NAME='Pool'/><APP APP.NAME='Site/' APPPOOL.NAME='Other'/></appcmd>",
            "<appcmd><VDIR VDIR.NAME='Site/' physicalPath='first'/><VDIR VDIR.NAME='Site/' physicalPath='second'/></appcmd>",
            "<appcmd><APPPOOL APPPOOL.NAME='Pool'/><APPPOOL APPPOOL.NAME='Pool'><processModel identityType='unknown'/></APPPOOL></appcmd>",
            "<appcmd><SITE SITE.NAME='Site' bindings='https/*:443:first.example'/><SITE SITE.NAME='Site' bindings='https/*:443:second.example'/></appcmd>").Single();
        Assert.Equal("first", app.Folder); Assert.Equal("IIS AppPool\\Pool", IisDiscovery.RequireAccount(app).Account);
        Assert.Contains("first.example", app.Callback);
    }

    [TestCase("")]
    [TestCase(" ")]
    public void Candidate_empty_folder_never_reads_current_directory(string folder) =>
        Assert.False(IisDiscovery.Candidate(folder, true, _ => throw new Exception(), _ => throw new Exception(), _ => throw new Exception()));

    [Test]
    public void Candidate_missing_folder_is_skipped() =>
        Assert.False(IisDiscovery.Candidate("missing", true, _ => false, _ => throw new Exception(), _ => throw new Exception()));

    [Test]
    public void Candidate_unreadable_and_invalid_config_are_skipped()
    {
        Assert.False(IisDiscovery.Candidate("folder", true, _ => true, _ => true, _ => throw new UnauthorizedAccessException()));
        Assert.False(IisDiscovery.Candidate("folder", true, _ => true, _ => true, _ => "<invalid"));
    }

    [Test]
    public void Recycle_failure_reports_saved_activation_and_manual_recovery()
    {
        var console = new StageBCliTests.FakeConsole("");
        Assert.False(ProvisionRunner.Recycle("Pool", console, _ => throw new CliEnvironment("SECRET")));
        Assert.Contains("DocAi is activated in the settings file", console.Output.ToString());
        Assert.Contains("'Pool' could not be recycled", console.Output.ToString());
        Assert.DoesNotContain("SECRET", console.Output.ToString());
    }

    [TestCase("bad\nname", "<invalid entry>")]
    [TestCase("", "<invalid entry>")]
    [TestCase("valid-name", "valid-name")]
    public void Status_invalid_secret_names_are_reported_without_aborting(string input, string expected) => Assert.Equal(expected, ProvisionRunner.SecretEntryName(input));
}
