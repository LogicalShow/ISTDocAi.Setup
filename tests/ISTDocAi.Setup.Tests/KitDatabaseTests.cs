using System.Reflection;
using ISTDocAi.Provisioning;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace ISTDocAi.Setup.Tests;

[Category("Pure")]
public sealed class KitDatabaseTests
{
    private sealed class Fake(bool refused = false) : ProvisionEnvironment
    {
        public string? Executed, Connection;
        public override string Read(string path) => "<configuration><connectionStrings><add name='ConnectionString' connectionString='Server=example;Database=company;User ID=secret-user;Password=never-display' /></connectionStrings></configuration>";
        public override void Sql(string connection, string sql)
        {
            Connection = connection; Executed = sql;
            if (refused) throw Refusal();
        }
        public override CompanyIdentity Identity(string connection, string product) => new("company", "Example", "FWD-CPW");
    }
    internal static SqlException Refusal(int number = 50018)
    {
        var ctor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).First(c => c.GetParameters().Length == 9);
        var parameters = ctor.GetParameters().Select(p => p.ParameterType == typeof(string) ? (object)"" : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        for (var i=0; i<parameters.Length; i++)
        {
            var p = ctor.GetParameters()[i];
            if (p.Name == "infoNumber") parameters[i] = number;
            if (p.Name == "errorClass") parameters[i] = (byte)16;
            if (p.Name == "errorMessage") parameters[i] = "Company identity differs.";
        }
        var error = (SqlError)ctor.Invoke(parameters);
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(errors, [error]);
        return (SqlException)typeof(SqlException).GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic, [typeof(SqlErrorCollection), typeof(string)])!.Invoke(null, [errors, "10"])!;
    }
    [Test]
    public void Cancelled_status_and_idempotent_checked_upgrade_are_embedded()
    {
        var sql = KitDatabase.Sql("FWD", "UNF");
        Assert.That(sql, Does.Contain("'Pending', 'Submitted', 'Completed', 'Failed', 'Cancelled'"));
        Assert.That(sql, Does.Contain("definition NOT LIKE '%''Cancelled''%'"));
        Assert.That(sql, Does.Contain("DROP CONSTRAINT CK_AIDocumentExtraction_ExtractionStatus"));
        Assert.That(sql, Does.Contain("WITH CHECK ADD CONSTRAINT CK_AIDocumentExtraction_ExtractionStatus"));
    }

    [Test]
    public void Runtime_sql_replaces_both_customer_declarations()
    {
        var sql = KitDatabase.Sql("ABC", "XYZ");
        Assert.That(sql, Does.Contain("@ProductCode varchar(100) = 'ABC'").And.Contain("@TenantCode  varchar(100) = 'XYZ'").And.Not.Contain("@ProductCode varchar(100) = 'FWD'").And.Not.Contain("@TenantCode  varchar(100) = 'UNF'"));
    }
    [TestCase("missing")][TestCase("duplicate")]
    public void Changed_embedded_format_is_refused(string change)
    {
        var sql = System.Text.Encoding.UTF8.GetString(KitDatabase.Bytes());
        sql = change == "missing" ? sql.Replace("@TenantCode  varchar(100) = 'UNF'", "changed") : sql + "@ProductCode varchar(100) = 'FWD'";
        Assert.That(Assert.Throws<CliRefusal>(() => KitDatabase.Sql("ABC", "XYZ", sql))!.Message, Is.EqualTo("Embedded kit SQL format changed; update KitSetup."));
    }
    [Test]
    public void Embedded_and_exported_kit_sql_are_byte_identical()
    {
        Assert.That(KitDatabase.Bytes(), Is.EqualTo(File.ReadAllBytes(Path.Combine(RepositoryPaths.SetupRoot, "sql", "kit", "CreateAIDocumentTables.sql"))));
        var folder = Path.Combine(RepositoryPaths.SetupRoot, "TestResults", "kit-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            KitDatabase.Export(folder);
            Assert.That(File.ReadAllBytes(Path.Combine(folder, "CreateAIDocumentTables.sql")), Is.EqualTo(KitDatabase.Bytes()));
            Assert.Throws<CliRefusal>(() => KitDatabase.Export(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
    [TestCase(false, 0)][TestCase(true, 1)]
    public async Task Database_never_prints_connection_credentials_and_reports_refused_sql(bool refused, int code)
    {
        var env = new Fake(refused); var console = new ServerSetupTests.Input();
        Assert.That(await ProvisionRunner.Run(["database", "--path", @"C:\fake-kit", "--product", "FWD", "--tenant", "CPW", "--yes"], true, console, env), Is.EqualTo(code), string.Join('\n', console.Output));
        var output = string.Join('\n', console.Output);
        Assert.That(output, Does.Contain("example").And.Contain("company").And.Not.Contain("never-display").And.Not.Contain("secret-user").And.Not.Contain("Password="));
        Assert.That(env.Executed, Does.Contain("@TenantCode  varchar(100) = 'CPW'"));
        if (refused) Assert.That(console.Output, Does.Contain("REFUSED: Company identity differs."));
    }
    [Test]
    public void Registration_run_now_uses_windows_login_and_separate_confirmation()
    {
        var env = new Fake(); var console = new ServerSetupTests.Input("no");
        Assert.That(ConsumerRegistration.RunNow("Server=example;Database=company;User ID=u;Password=hidden", "SQL", env, console), Is.False);
        Assert.That(env.Executed, Is.Null);
        Assert.That(ConsumerRegistration.RunNow("Server=example;Database=company;User ID=u;Password=hidden", "SQL", env, new ServerSetupTests.Input("yes")), Is.True);
        var connection = new SqlConnectionStringBuilder(env.Connection); Assert.That(connection.IntegratedSecurity, Is.True); Assert.That(connection.Password, Is.Empty); Assert.That(connection.UserID, Is.Empty);
    }
}
