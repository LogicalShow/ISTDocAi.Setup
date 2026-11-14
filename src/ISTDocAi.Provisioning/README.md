# DocAi.Provisioning (Stage A)

Windows-only .NET 10 library. The Stage A credential/file/SQL-generation APIs below remain unchanged and have no database, IIS, console or network operations. Stage B adds explicitly invoked CLI adapters in Cli.cs, IisDiscovery.cs, ProvisionChecks.cs and ProvisionRunner.cs; these perform operator-authorized Windows, company SQL and catalog operations only when a command is run.
`PairingData`, pairing codes, `KitSecretsFile.Get`, and `ToXml()` contain secrets: front-ends must never log them.
`Activate(verifiedConsumerCode)` only performs the local transition: Stage B must first make the authenticated catalog request and pass its returned code.
Call `Load`, `Prepare`, then `Save`; print a pairing code only after the save succeeds and only on explicit request.
`Cancel()` is allowed only in `Prepared` (otherwise `InvalidOperationException`). It removes the API key, both webhook secrets, and every `DocAiProvision:*` key, sets `DocAi:Enabled=false`, and preserves all other settings. Call `Save` to persist cancellation. A subsequent `Prepare` creates a new SetupId and credentials.

`Load` records a fingerprint. `Save` holds an exclusive sidecar lock and refuses a changed file rather than overwriting another operator's work.
Use `Load` again after conflicts; `Parse` is for in-memory use or saving a new file.

`Prepare` persists `DocAiProvision:CreatedUtc` in addition to the approved metadata, so a resumed prepare produces the identical pairing code (including its timestamp).
Empty/whitespace text, controls, and SQLCMD substitution tokens are rejected. Apostrophes and double quotes are supported and SQL literals escape apostrophes.
Duplicate kit keys are rejected case-insensitively, matching appSettings lookup semantics.
Existing webhook credentials without provisioning metadata are refused too; prepare never replaces existing credentials.

Kit backups mirror `deploy/_common.ps1:20`: `%LOCALAPPDATA%\DocAi\backups\yyyyMMdd-HHmmss-<guid>-<filename>`, with protected current-user-only ACLs on folder and file.
Protected webhook backups mirror `deploy/_webhook-secrets.ps1:107`: `<path>.<guid>.bak`, encrypted, with protected SYSTEM/Administrators/current-user FullControl.
Protected-file folder/file/lock ACLs use SYSTEM + Administrators FullControl and the service account ReadAndExecute (FullControl when it is the current user).
Identical installation does not rewrite the JSON or create a backup; ACLs are still enforced.

## Public API

All members below are in `DocAi.Provisioning`; methods on static classes are static.

```csharp
static class Codes {
    string Text(string? value, string field, int maximum);
    string Part(string? value, string field);
    string ConsumerCode(string productCode, string tenantCode);
    string Name(string value);
    string CallbackUrl(string value);
    string BaseUrl(string value);
}
static class Credentials {
    string NewApiKey();
    string HashApiKey(string apiKey);
    string NewWebhookKey();
    Guid NewSetupId();
    string SecretName(Guid setupId);
}
sealed class PairingData {
    int V { get; init; } // defaults to 1
    Guid SetupId { get; init; }
    required string ProductCode { get; init; }
    required string TenantCode { get; init; }
    required string ConsumerCode { get; init; }
    required string Name { get; init; }
    required string ApiKeyHash { get; init; }
    required string CallbackUrl { get; init; }
    required string WebhookSecretName { get; init; }
    required string WebhookSecret { get; init; }
    DateTimeOffset CreatedUtc { get; init; }
    override string ToString(); // always suppresses contents
}
static class PairingCode {
    void Validate(PairingData data);
    string Encode(PairingData data);
    PairingData Decode(string code);
}
sealed record PrepareRequest(string ProductCode, string TenantCode, string Name,
    string CallbackUrl, string BaseUrl);
sealed class KitSecretsFile {
    static KitSecretsFile Parse(string xml);
    static KitSecretsFile Load(string path);
    string? Get(string key);
    PairingData Prepare(PrepareRequest request, DateTimeOffset createdUtc);
    PairingData Pairing();
    void Activate(string verifiedConsumerCode);
    void Cancel();
    string ToXml();
    void Save(string path, FileSecurity acl);
    static FileSecurity CreateAcl(string readAccount);
}
enum SecretInstallResult { Installed, AlreadyInstalled }
static class ProtectedSecretsFile {
    SecretInstallResult Install(string path, string secretName, string secret, string serviceAccount);
}
static class RegisterConsumerSql {
    string FileName(PairingData pairing);
    string Generate(PairingData pairing, int initialPages, string operatorIdentity, string note);
}
```

Stage B supplies the default operator identity (`WindowsIdentity.GetCurrent().Name`) and note (`Initial allowance at registration`), discovers account/path settings, and writes the returned SQL as UTF-8. The library does not implicitly access those settings.

## Source/design reconciliation

- `db/migrations/01_docai_schema.sql:41,61` has a nullable varchar(127) secret name and paired callback check, not a strict secret-name pattern. The library enforces the approved `webhook-<SetupId:N>` pattern.
- `db/migrations/01_docai_schema.sql:763` already includes quota objects, while migration 06 supports upgrading older schemas. Integration tests still apply all eight migrations in order, including grants, with GO batch splitting like the existing helper.
- `db/migrations/07_topup_reporting_upgrade.sql:24,46` owns the allowance insert and computes BalanceAfter. The generator omits BalanceAfter and uses the trigger, with `AcquirePageQuotaLock @ConsumerId` from migration 06 line 84.
- `deploy/_page-allowance.ps1:56,107,111` supplies the quota lock / grant / replay-request order. Registration uses the approved `Register|...` payload rather than the script's TopUp payload.
- The existing SQL helper seeds TEST and model rows (`tests/DocAi.Infrastructure.Tests/SqlDatabase.cs:43`), so the new helper mirrors its isolation/batch splitting but starts empty and encloses initialization in `await using` cleanup.

`Examples/register-consumer-FWD-UNF.sql` is a generator-verified example: fake all-A hash, fake SetupId, reserved example domains, 500 pages. Never use it as real credentials.

## Verification commands

Use `NUGET_PACKAGES=D:\unifret\DocAi\.packages`. If needed, restore offline with both `.packages` and `C:\Users\Anter\.nuget\packages` as sources; no package versions are changed.

```powershell
dotnet build tools\DocAi.Provisioning --no-restore
dotnet build tests\DocAi.Provisioning.Tests --no-restore
dotnet test tests\DocAi.Provisioning.Tests --no-build --no-restore --filter 'Category!=Machine&Category!=Sql'
# Run separately on an appropriate elevated Windows session; Machine tests own fresh temp folders.
dotnet test tests\DocAi.Provisioning.Tests --no-build --no-restore --filter 'Category=Machine'
# Set DOCAI_TEST_SQL explicitly. Each SQL test owns and drops its newly created database.
dotnet test tests\DocAi.Provisioning.Tests --no-build --no-restore --filter 'Category=Sql'
```


