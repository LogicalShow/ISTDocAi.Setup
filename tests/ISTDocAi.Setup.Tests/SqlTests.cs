using NUnit.Framework;
using Assert = ISTDocAi.Setup.Tests.PortAssert;
using ISTDocAi.Provisioning;
using ISTDocAi.Application.Common.Interfaces.DocAi.Persistence;
using ISTDocAi.Infrastructure.Services.DocAi.Intake;
using ISTDocAi.Infrastructure.Services.DocAi.Secrets;
using ISTDocAi.Infrastructure.Configuration.DocAi;
using ISTDocAi.Domain.Dtos.DocAi;
using ISTDocAi.Infrastructure.Data.DocAi;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ISTDocAi.Setup.Tests.Provisioning;


// Mirrors tests/DocAi.Infrastructure.Tests/SqlDatabase.cs, without its seeded consumer/models.
// Initialization is inside the cleanup scope so migration failures also drop the new database.
internal sealed class ProvisioningDatabase : IAsyncDisposable
{
    private readonly string database = "DocAiProvisioningTest_" + Guid.NewGuid().ToString("N");
    private readonly string master;
    private readonly string connectionString;
    private bool created;
    internal string ConnectionString => connectionString;
    internal ProvisioningDatabase()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("DOCAI_TEST_SQL") ?? throw new InvalidOperationException("DOCAI_TEST_SQL required.")) { InitialCatalog = "master" };
        master = builder.ConnectionString; builder.InitialCatalog = database; connectionString = builder.ConnectionString;
    }
    internal async Task Initialize()
    {
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await connection.ExecuteAsync($"CREATE DATABASE [{database}]"); created = true;
        await connection.ExecuteAsync($"ALTER DATABASE [{database}] SET READ_COMMITTED_SNAPSHOT ON");
        await using var context = new DocAiDbContext(new DbContextOptionsBuilder<DocAiDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(300)).Options);
        await context.Database.MigrateAsync();
    }
    internal async Task Execute(string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(sql);
    }
    internal async Task<string[]> Messages(string sql)
    {
        var messages = new List<string>();
        await using var connection = new SqlConnection(connectionString);
        connection.InfoMessage += (_, e) => messages.Add(e.Message);
        await connection.ExecuteAsync(sql);
        return messages.ToArray();
    }
    internal async Task<T> Scalar<T>(string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        return (await connection.ExecuteScalarAsync<T>(sql))!;
    }
    public async ValueTask DisposeAsync()
    {
        if (!created) return;
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(master);
        await connection.ExecuteAsync($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
    }
}

[Category("Sql")]
public sealed class RegistrationSqlTests : ISTDocAi.Setup.Tests.GuardedToolTests
{
    [Test]
    public async Task Run_now_path_registers_and_replays_in_disposable_database()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize();
        var console = new ISTDocAi.Setup.Tests.ServerSetupTests.Input("yes", "yes");
        var sql = Sql();
        NUnit.Framework.Assert.That(ConsumerRegistration.RunNow(db.ConnectionString, sql, new ProvisionEnvironment(), console), NUnit.Framework.Is.True);
        NUnit.Framework.Assert.That(ConsumerRegistration.RunNow(db.ConnectionString, sql, new ProvisionEnvironment(), console), NUnit.Framework.Is.True);
        NUnit.Framework.Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.Consumers"), NUnit.Framework.Is.EqualTo(1));
        NUnit.Framework.Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"), NUnit.Framework.Is.EqualTo(1));
    }
    private static string Sql(PairingData? data = null, int pages = 500) => RegisterConsumerSql.Generate(data ?? TestData.Pairing(), pages, "DOMAIN\\operator", "Initial allowance at registration");

    [Test]
    public async Task Letter_containing_setup_id_registers_and_uppercase_secret_name_is_refused()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize();
        var pairing = TestData.Pairing();
        var sql = Sql(pairing);
        var uppercaseName = "webhook-" + pairing.SetupId.ToString("N").ToUpperInvariant();
        Assert.NotEqual(pairing.WebhookSecretName, uppercaseName);
        var invalidSql = sql.Replace("N'" + pairing.WebhookSecretName + "'", "N'" + uppercaseName + "'", StringComparison.Ordinal);
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Execute(invalidSql));
        Assert.Equal(50018, error.Number);
        Assert.Equal("Invalid registration secret name.", error.Message);
        Assert.Equal(0, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.Consumers"));
        Assert.Equal(0, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"));
        Assert.Equal(0, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageQuotaOperatorRequests"));
        await db.Execute(sql);
        Assert.Equal(pairing.WebhookSecretName, await db.Scalar<string>("SELECT WebhookSecretName FROM dbo.Consumers"));
        Assert.Equal(pairing.SetupId, await db.Scalar<Guid>("SELECT RequestId FROM dbo.PageQuotaOperatorRequests"));
        Assert.Equal(500, await db.Scalar<long>("SELECT AvailablePages FROM dbo.ConsumerPageBalance"));
    }

    [Test]
    public async Task Fresh_registration_replay_and_changed_pages()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize();
        var sql = Sql(); await db.Execute(sql);
        Assert.Equal("Prepaid", await db.Scalar<string>("SELECT QuotaMode FROM dbo.Consumers"));
        Assert.Equal(10, await db.Scalar<int>("SELECT QuotaWarningPercent FROM dbo.Consumers"));
        Assert.Equal(1, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants WHERE Kind='TopUp'"));
        Assert.Equal(500, await db.Scalar<long>("SELECT AvailablePages FROM dbo.ConsumerPageBalance"));
        Assert.Contains("already registered", await db.Messages(sql));
        Assert.Equal(1, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"));
        Assert.Equal(500, await db.Scalar<long>("SELECT AvailablePages FROM dbo.ConsumerPageBalance"));
        await Assert.ThrowsAsync<SqlException>(() => db.Execute(Sql(pages: 501)));
        Assert.Equal(1, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageQuotaOperatorRequests"));
        Assert.Equal(1, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"));
        Assert.Equal(500, await db.Scalar<long>("SELECT AvailablePages FROM dbo.ConsumerPageBalance"));
    }

    [Test]
    public async Task Operator_and_quota_operator_can_run_generated_registration()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize();
        await db.Execute("CREATE USER registrationOperator WITHOUT LOGIN; ALTER ROLE docai_operator ADD MEMBER registrationOperator; ALTER ROLE docai_quota_operator ADD MEMBER registrationOperator;");
        Assert.Equal(2, await db.Scalar<int>("SELECT COUNT(*) FROM sys.database_role_members WHERE member_principal_id=USER_ID('registrationOperator')"));
        var sql = Sql();
        Assert.Contains("Required: db_owner OR docai_operator + docai_quota_operator.", sql);
        await db.Execute("EXECUTE AS USER='registrationOperator'; BEGIN TRY " + sql + " REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;");
        Assert.Equal("Prepaid", await db.Scalar<string>("SELECT QuotaMode FROM dbo.Consumers"));
        Assert.Equal(10, await db.Scalar<int>("SELECT QuotaWarningPercent FROM dbo.Consumers"));
        Assert.Equal(1, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"));
        Assert.Equal(1, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants WHERE Kind='TopUp'"));
        Assert.Equal(500, await db.Scalar<long>("SELECT AvailablePages FROM dbo.ConsumerPageBalance"));
    }

    [Test]
    public async Task Hash_in_either_slot_of_another_consumer_is_refused()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize();
        await db.Execute("INSERT dbo.Consumers(Code,Name,ProductCode,TenantCode,ApiKeyHash1,ApiKeyHash2) VALUES('FWD-OTHER','Other','FWD','OTHER',CONVERT(binary(32),1),0x" + new string('A', 64) + ");");
        await Assert.ThrowsAsync<SqlException>(() => db.Execute(Sql()));
        await db.Execute("UPDATE dbo.Consumers SET ApiKeyHash2=NULL,ApiKeyHash1=0x" + new string('A', 64) + ";");
        await Assert.ThrowsAsync<SqlException>(() => db.Execute(Sql()));
        Assert.Equal(1, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.Consumers"));
        Assert.Equal(0, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"));
    }

    [Test]
    public async Task Existing_code_with_different_hash_is_refused()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize(); await db.Execute(Sql());
        await Assert.ThrowsAsync<SqlException>(() => db.Execute(Sql(TestData.Pairing(hash: new string('B', 64)))));
        Assert.Equal(500, await db.Scalar<long>("SELECT AvailablePages FROM dbo.ConsumerPageBalance"));
        Assert.Equal(1, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"));
    }

    [Test]
    public async Task Failure_after_consumer_insert_rolls_back_all_registration_rows()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize();
        await db.Execute("CREATE TRIGGER dbo.TR_TestRejectRequest ON dbo.PageQuotaOperatorRequests AFTER INSERT AS THROW 50099,'Forced test failure.',1;");
        await Assert.ThrowsAsync<SqlException>(() => db.Execute(Sql()));
        Assert.Equal(0, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.Consumers"));
        Assert.Equal(0, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"));
        Assert.Equal(0, await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageQuotaOperatorRequests"));
    }
}
