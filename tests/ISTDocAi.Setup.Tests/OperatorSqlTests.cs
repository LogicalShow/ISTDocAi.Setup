using OperatorReports = ISTDocAi.OperatorTool.Reports;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ISTDocAi.OperatorTool;
using ISTDocAi.Provisioning;
using ISTDocAi.Setup.Tests.Provisioning;
using Microsoft.Data.SqlClient;
using NUnit.Framework;
using Dapper;
using ISTDocAi.Infrastructure;
using ISTDocAi.Application.Common.Interfaces.DocAi.Persistence;
using ISTDocAi.Infrastructure.Configuration.DocAi;
using ISTDocAi.Infrastructure.Data.Repositories.DocAi;
using ISTDocAi.Infrastructure.Data.Sql.DocAi;
using Microsoft.Extensions.Options;

namespace ISTDocAi.Setup.Tests;

// Every test creates and migrates a uniquely named disposable database. Never uses InitialCatalog from DOCAI_TEST_SQL.
[Category("Sql")]
public sealed class OperatorSqlTests : GuardedToolTests
{
    private static async Task Seed(ProvisioningDatabase db)
    {
        await db.Initialize();
        await db.Execute(RegisterConsumerSql.Generate(TestData.Pairing(), 100, "fixture-operator", "Initial allowance"));
    }
    private static PageRequest Request(string action = "TopUp") => OperatorPureTests.Request(action) with { Consumer = "FWD-UNF", RequestId = Guid.NewGuid() };
    private static Task<long> Balance(ProvisioningDatabase db) => db.Scalar<long>("SELECT AvailablePages FROM dbo.ConsumerPageBalance WHERE ConsumerId=(SELECT ConsumerId FROM dbo.Consumers WHERE Code='FWD-UNF')");
    private static IntakeRequest Intake(short consumer, short pages = 2) => new(Guid.NewGuid(), consumer, Guid.NewGuid().ToString("N"), "BL", null,
        System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()), "fixture.pdf", "application/pdf", 100, pages, "documents/fixture.pdf", "fixture-correlation");
    private static Task Reserve(ProvisioningDatabase db, IntakeRequest r) => db.Execute($"""
        SET XACT_ABORT ON; BEGIN TRANSACTION;
        EXEC dbo.AcquirePageQuotaLock {r.ConsumerId};
        INSERT dbo.Jobs(JobId,ConsumerId,DocumentTypeCode,IdemLine,FileSha256,ContentType,FileSizeBytes,PageCount,BlobPath,Stage,CorrelationId)
        VALUES('{r.JobId}',{r.ConsumerId},'BL','*',0x{Convert.ToHexString(r.FileSha256)},'application/pdf',100,{r.PageCount},'documents/fixture.pdf','Extract','fixture');
        INSERT dbo.JobRuns(JobId,RunNo,Reason,ConsumerId,PageCount,CorrelationId)
        VALUES('{r.JobId}',1,'New',{r.ConsumerId},{r.PageCount},'fixture');
        INSERT dbo.PageReservations(JobId,RunNo,ConsumerId,Pages) VALUES('{r.JobId}',1,{r.ConsumerId},{r.PageCount});
        COMMIT;
        """);
    [Test]
    public async Task Reserved_pages_refuse_reversal_and_status_matches_service()
    {
        await using var db = new ProvisioningDatabase(); await Seed(db);
        await db.Execute(ModelSql.Bl("fixture-bl", .99m, json: OperatorPureTests.Mapping("BL").ToJsonString()));
        var consumer = await db.Scalar<short>("SELECT ConsumerId FROM dbo.Consumers WHERE Code='FWD-UNF'");
        await Reserve(db, Intake(consumer, 99));
        Assert.That(await Balance(db), Is.EqualTo(1));
        var grant = await db.Scalar<long>("SELECT MIN(GrantId) FROM dbo.PageAllowanceGrants");
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute(PageSql.Generate(Request("Reverse") with { GrantId = grant })));
        await using var connection = new SqlConnection(db.ConnectionString);
        var usage = await connection.QuerySingleAsync(new SqlStatementCatalog()["S26"], new { consumerId = consumer, months = 1, asOfUtc = DateTime.UtcNow });
        var service = new ISTDocAi.Application.Services.DocAi.PageAllowanceStatusService.PageAllowanceStatusService();
        var state = service.Calculate((string)usage.QuotaMode, (long)usage.Remaining, (long?)usage.CycleBase, (byte)usage.WarningPercent);
        var status = await connection.QuerySingleAsync(PageSql.Generate(Request("Status")));
        Assert.That((string)status.Status, Is.EqualTo(state.Status).And.EqualTo("Low"));
        Assert.That((long)status.Remaining, Is.EqualTo((long)usage.Remaining)); Assert.That((long?)status.WarningAtPages, Is.EqualTo(state.WarningAtPages));
        await db.Execute(PageSql.Generate(Request("Reverse") with { GrantId = grant, AllowDebt = true }));
        Assert.That(await Balance(db), Is.EqualTo(-99));
        usage = await connection.QuerySingleAsync(new SqlStatementCatalog()["S26"], new { consumerId = consumer, months = 1, asOfUtc = DateTime.UtcNow });
        Assert.That((long?)usage.CycleBase, Is.Null); Assert.That((DateTime?)usage.LastTopUpAtUtc, Is.Null);
    }
    [Test]
    public async Task Charged_document_paging_exposes_customer_fields_only_and_reversed_cycles_remain_boundaries()
    {
        await using var db = new ProvisioningDatabase(); await Seed(db);
        await db.Execute(ModelSql.Bl("fixture-bl", .99m, json: OperatorPureTests.Mapping("BL").ToJsonString()));
        var consumer = await db.Scalar<short>("SELECT ConsumerId FROM dbo.Consumers WHERE Code='FWD-UNF'");
        var grant = await db.Scalar<long>("SELECT MIN(GrantId) FROM dbo.PageAllowanceGrants");
        for (var i = 0; i < 2; i++)
        {
            var intake = Intake(consumer); await Reserve(db, intake);
            // Synthetic successful billing, on this disposable database only; no Azure calls.
            await db.Execute($"""
                SET XACT_ABORT ON; BEGIN TRANSACTION;
                EXEC dbo.AcquirePageQuotaLock {consumer};
                INSERT dbo.AzureOperations(JobId,RunNo,Kind,ModelId,State,OperationLocation,Pages,CompletedAtUtc)
                VALUES('{intake.JobId}',1,'extract','fixture-bl','Succeeded','https://fixture.invalid/operation',2,SYSUTCDATETIME());
                DECLARE @op bigint=SCOPE_IDENTITY();
                INSERT dbo.UsageLedger(ConsumerId,JobId,RunNo,OperationId,MeterCode,ModelId,Pages,PricePer1000,OccurredAtUtc,CorrelationId)
                VALUES({consumer},'{intake.JobId}',1,@op,'DI_CUSTOM_EXTRACT','fixture-bl',2,30,SYSUTCDATETIME(),'fixture');
                UPDATE dbo.JobRuns SET Status='Completed',ResolvedDocumentTypeCode='BL' WHERE JobId='{intake.JobId}' AND RunNo=1;
                UPDATE dbo.Jobs SET Status='Completed' WHERE JobId='{intake.JobId}';
                EXEC dbo.SettlePageReservation '{intake.JobId}',1;
                COMMIT;
                """);
        }
        var r = Request("Documents") with { GrantId = grant, Limit = 1 };
        var first = OperatorReports.Parse(await db.Scalar<string>(PageSql.Generate(r)), true);
        Assert.That(((JsonArray)first["documents"]!).Count, Is.EqualTo(1));
        var cursor = first["nextAfterChargeId"]!.GetValue<long>();
        var last = OperatorReports.Parse(await db.Scalar<string>(PageSql.Generate(r with { AfterChargeId = cursor })), true);
        Assert.That(last["nextAfterChargeId"], Is.Null); Assert.That(((JsonArray)last["documents"]!).Count, Is.EqualTo(1));
        Assert.That(last["documents"]![0]!["resolvedDocumentTypeCode"]!.GetValue<string>(), Is.EqualTo("BL"));
        Assert.That((first.ToJsonString() + last.ToJsonString()), Does.Not.Match("(?i)usd|cost|price|azure|meter"));
        await db.Execute(PageSql.Generate(Request("Reverse") with { GrantId = grant, AllowDebt = true }));
        var cycles = OperatorReports.Parse(await db.Scalar<string>(PageSql.Generate(Request("Cycles") with { GrantId = grant })), true);
        Assert.That(cycles["cycles"]![0]!["isReversed"]!.GetValue<bool>(), Is.True);
        Assert.That(cycles["cycles"]![0]!["chargedPages"]!.GetValue<int>(), Is.EqualTo(4));
        Assert.That(await Balance(db), Is.EqualTo(-4));
    }
    [Test]
    public async Task Preview_rolls_back_apply_replays_and_conflicting_payload_is_refused()
    {
        await using var db = new ProvisioningDatabase(); await Seed(db);
        var r = Request();
        await db.Execute(PageSql.Generate(r, true)); Assert.That(await Balance(db), Is.EqualTo(100));
        await db.Execute(PageSql.Generate(r)); await db.Execute(PageSql.Generate(r));
        Assert.That(await Balance(db), Is.EqualTo(110));
        Assert.That(await db.Scalar<int>($"SELECT COUNT(*) FROM dbo.PageAllowanceGrants WHERE RequestId='{r.RequestId}'"), Is.EqualTo(1));
        var error = Assert.ThrowsAsync<SqlException>(async () => await db.Execute(PageSql.Generate(r with { Pages = 11 })));
        Assert.That(error!.Number, Is.EqualTo(50018)); Assert.That(await Balance(db), Is.EqualTo(110));
    }
    [Test]
    public async Task Stale_preview_and_inactive_consumer_are_refused_without_changes()
    {
        await using var db = new ProvisioningDatabase(); await Seed(db);
        var r = Request("Adjust") with { Pages = -1 };
        var marker = await db.Scalar<string>(PageSql.Generate(r, true)); var token = marker["PREVIEW_TOKEN=".Length..];
        await db.Execute(PageSql.Generate(Request()));
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute(PageSql.Generate(r, token: token)));
        Assert.That(await Balance(db), Is.EqualTo(110));
        await db.Execute("UPDATE dbo.Consumers SET IsActive=0 WHERE Code='FWD-UNF'");
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute(PageSql.Generate(Request())));
        Assert.That(await Balance(db), Is.EqualTo(110));
    }
    [Test]
    public async Task Concurrent_identical_requests_add_only_once()
    {
        await using var db = new ProvisioningDatabase(); await Seed(db); var batch = PageSql.Generate(Request());
        await Task.WhenAll(db.Execute(batch), db.Execute(batch)); Assert.That(await Balance(db), Is.EqualTo(110));
    }
    [Test]
    public async Task Debt_reversal_double_reversal_and_consumer_isolation()
    {
        await using var db = new ProvisioningDatabase(); await Seed(db);
        var r = Request(); await db.Execute(PageSql.Generate(r));
        var grant = await db.Scalar<long>($"SELECT GrantId FROM dbo.PageAllowanceGrants WHERE RequestId='{r.RequestId}'");
        var reverse = Request("Reverse") with { GrantId = grant };
        await db.Execute(PageSql.Generate(reverse)); await db.Execute(PageSql.Generate(reverse)); Assert.That(await Balance(db), Is.EqualTo(100));
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute(PageSql.Generate(reverse with { RequestId = Guid.NewGuid() })));
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute(PageSql.Generate(Request("Adjust") with { Pages = -101 })));
        await db.Execute(PageSql.Generate(Request("Adjust") with { Pages = -101, AllowDebt = true })); Assert.That(await Balance(db), Is.EqualTo(-1));
        await db.Execute("INSERT dbo.Consumers(Code,Name,ProductCode,TenantCode,ApiKeyHash1) VALUES('OTHER','Other','FWD','OTHER',CONVERT(binary(32),2))");
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute(PageSql.Generate(reverse with { Consumer = "OTHER", RequestId = Guid.NewGuid(), AllowDebt = true })));
        Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants WHERE ConsumerId=(SELECT ConsumerId FROM dbo.Consumers WHERE Code='OTHER')"), Is.Zero);
    }
    [Test]
    public async Task Mode_and_topup_are_atomic_warning_bounds_and_history_are_preserved()
    {
        await using var db = new ProvisioningDatabase(); await Seed(db);
        await db.Execute(PageSql.Generate(Request("Adjust") with { Pages = -100 }));
        await db.Execute(PageSql.Generate(Request("SetMode") with { Mode = "Unlimited" }));
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute(PageSql.Generate(Request("SetMode") with { Mode = "Prepaid" })));
        Assert.That(await db.Scalar<string>("SELECT QuotaMode FROM dbo.Consumers WHERE Code='FWD-UNF'"), Is.EqualTo("Unlimited"));
        var r = Request("SetMode") with { Mode = "Prepaid", TopUpPages = 12 }; await db.Execute(PageSql.Generate(r)); await db.Execute(PageSql.Generate(r));
        Assert.That(await Balance(db), Is.EqualTo(12));
        await db.Execute(PageSql.Generate(Request("SetWarningPercent") with { Percent = 50 }));
        Assert.That(await db.Scalar<int>("SELECT CONVERT(int,QuotaWarningPercent) FROM dbo.Consumers WHERE Code='FWD-UNF'"), Is.EqualTo(50));
        Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"), Is.EqualTo(3));
    }
    [TestCase(true)][TestCase(false)]
    public async Task Cycles_and_documents_use_canonical_SQL_and_preserve_audience(bool customer)
    {
        await using var db = new ProvisioningDatabase(); await Seed(db);
        var json = await db.Scalar<string>(PageSql.Generate(Request("Cycles") with { Customer = customer }));
        var report = OperatorReports.Parse(json, customer);
        Assert.That(report["totals"]!["grantedPages"]!.GetValue<int>(), Is.EqualTo(100));
        if (customer) Assert.That(report.ToJsonString(), Does.Not.Match("(?i)usd|cost|price|azure|meter"));
        else Assert.That(report["totals"]!["azureCostUsd"], Is.Not.Null);
        var docs = await db.Scalar<string>(PageSql.Generate(Request("Documents") with { Customer = customer, GrantId = 0 }));
        Assert.That(((JsonArray)OperatorReports.Parse(docs, customer)["documents"]!).Count, Is.Zero);
        Assert.That(await db.Scalar<string?>(PageSql.Generate(Request("Documents") with { GrantId = 0, AfterChargeId = long.MaxValue })), Is.Null);
    }
    [Test]
    public async Task Quota_role_runs_actions_runtime_cannot_grant_and_memberships_replay()
    {
        await using var db = new ProvisioningDatabase(); await Seed(db);
        await db.Execute("CREATE USER packageOperator WITHOUT LOGIN; CREATE USER packageModels WITHOUT LOGIN; CREATE USER packageQuota WITHOUT LOGIN; CREATE USER runtime_test WITHOUT LOGIN; ALTER ROLE docai_runtime ADD MEMBER runtime_test;");
        var sql = OperatorGrants.Generate("Disposable", "packageOperator", "packageModels", "packageQuota");
        var batches = Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(b => b.Contains("ALTER ROLE", StringComparison.Ordinal)).ToArray();
        Assert.That(batches, Has.Length.EqualTo(3));
        for (var run = 0; run < 2; run++) foreach (var batch in batches) await db.Execute(batch);
        Assert.That(await db.Scalar<int>("SELECT IS_ROLEMEMBER('docai_operator','packageOperator')"), Is.EqualTo(1));
        Assert.That(await db.Scalar<int>("SELECT IS_ROLEMEMBER('docai_modelteam','packageModels')"), Is.EqualTo(1));
        Assert.That(await db.Scalar<int>("SELECT IS_ROLEMEMBER('docai_quota_operator','packageQuota')"), Is.EqualTo(1));
        await db.Execute("EXECUTE AS USER='packageQuota'; BEGIN TRY " + PageSql.Generate(Request()) + " REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;");
        Assert.That(await Balance(db), Is.EqualTo(110));
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute("EXECUTE AS USER='runtime_test'; " + PageSql.Generate(Request())));
    }
    [Test]
    public async Task Model_set_activation_is_atomic_replay_is_noop_and_mapping_conflict_is_refused()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize();
        var folder = Path.Combine(RepositoryPaths.SetupRoot, "TestResults", "models-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var bl = Path.Combine(folder, "bl.json"); var manifest = Path.Combine(folder, "manifest.json");
            await File.WriteAllTextAsync(bl, OperatorPureTests.Mapping("BL").ToJsonString()); await File.WriteAllTextAsync(manifest, OperatorPureTests.Mapping().ToJsonString());
            var r = new ModelSet("operator-bl", .97m) { BlMappingFile = bl, ManifestModelId = "operator-manifest", ManifestMappingFile = manifest, ManifestTestFieldAccuracy = .92m, ClassifierModelId = "operator-router", ClassifierTestFieldAccuracy = .91m, ClassifierMinConfidence = .53m };
            Assert.ThrowsAsync<SqlException>(async () => await db.Execute(ModelSql.Set(r with { BlTestFieldAccuracy = .94m })));
            Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ModelRegistry WHERE ModelId LIKE 'operator-%'"), Is.Zero);
            var sql = ModelSql.Set(r); await db.Execute(sql);
            Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ModelRegistry WHERE ModelId LIKE 'operator-%' AND Status='Active'"), Is.EqualTo(3));
            Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ClassifierClasses WHERE ClassifierModelId='operator-router'"), Is.EqualTo(3));
            Assert.That(await db.Scalar<decimal>("SELECT MinFieldConfidence FROM dbo.ModelRegistry WHERE ModelId='operator-router'"), Is.EqualTo(.53m));
            const string snapshot = "SELECT (SELECT * FROM dbo.ModelRegistry ORDER BY ModelId FOR JSON PATH) m,(SELECT * FROM dbo.MappingProfiles ORDER BY ProfileId FOR JSON PATH) p,(SELECT * FROM dbo.MappingFields ORDER BY ProfileId,MapKind,CanonicalKey FOR JSON PATH) f,(SELECT * FROM dbo.ModelMappingBindings ORDER BY ModelId FOR JSON PATH) b,(SELECT * FROM dbo.ClassifierClasses ORDER BY ClassifierModelId,ClassName FOR JSON PATH) c FOR JSON PATH";
            var before = await db.Scalar<string>(snapshot); await db.Execute(sql); Assert.That(await db.Scalar<string>(snapshot), Is.EqualTo(before));
            var changed = OperatorPureTests.Mapping(); changed["Rows"]![0]!["AzureName"] = "Changed";
            var error = Assert.ThrowsAsync<SqlException>(async () => await db.Execute(ModelSql.Mapping("operator-manifest", "MANIFEST", changed.ToJsonString())));
            Assert.That(error!.Number, Is.EqualTo(51205)); Assert.That(await db.Scalar<string>(snapshot), Is.EqualTo(before));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Test]
    public async Task BL_replacement_retired_rollback_and_missing_binding_rollback()
    {
        await using var db = new ProvisioningDatabase(); await db.Initialize();
        var json = OperatorPureTests.Mapping("BL").ToJsonString();
        await db.Execute(ModelSql.Bl("operator-old", .99m, json: json));
        await db.Execute(ModelSql.Bl("operator-new", .98m, json: json));
        Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='operator-old'"), Is.EqualTo("Retired"));
        await db.Execute(ModelSql.Bl("operator-old", .99m));
        Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='operator-new'"), Is.EqualTo("Retired"));
        Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='operator-old'"), Is.EqualTo("Active"));
        Assert.ThrowsAsync<SqlException>(async () => await db.Execute(ModelSql.Bl("no-profile", .99m)));
        Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ModelRegistry WHERE ModelId='no-profile'"), Is.Zero);
    }
}
