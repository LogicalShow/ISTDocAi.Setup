# On-premises Azure infrastructure

`ISTDocAi.OperatorTool.exe azure infra` uses browser authentication and the embedded
`main.json`. It asks for all values in `onprem.bicepparam`, fixes hostingMode to
`onPremises`, lists subscriptions, previews ARM what-if, confirms, deploys and polls.
No Azure CLI or PowerShell is required by the product. The operator needs resource
deployment and role/policy assignment rights at the target resource group, plus
role-assignment rights on the existing Document Intelligence resource group.

Replace the object-id placeholders in `onprem.bicepparam` for a reviewed example.
The executable asks for values interactively; it does not load that example file.
Use the service principal OBJECT id, not the application/client id. Developer group
must be empty in prod. SQL admin identifiers are retained for template parity;
on-premises SQL/IIS setup remains ServerSetup's responsibility.

Budgets notify and do not stop spending. Review budget start dates and subscription
quotas before deployment. Enable alerts only with monitored email recipients.
Enabling purge protection is irreversible. Incremental deployments retain resources
absent from the template, including older App Service/SQL resources.

The template creates storage, Key Vault, monitoring and role assignments and uses
existing Document Intelligence. It does not deploy application binaries, provision
secret values, or replace the local protected-file webhook secret provider.

After changing main.bicep or a module, rebuild main.json with the locally installed
Bicep CLI (`bicep build azure/infra/main.bicep --outfile azure/infra/main.json`).
The Machine-category BicepTemplateTests test recompiles and compares the embedded
JSON. Only local modules are used; compilation does not contact Azure.

Outputs are keyless ARM output objects: blobEndpoint.value,
documentIntelligenceEndpoint.value, keyVaultEndpoint and hosting. Choose an output
file that does not exist, then give that file to ServerSetup package.

If a request fails or times out, inspect the named deployment in Azure before
retrying. Requests that change Azure are not automatically retried. Partial
infrastructure deployments remain in Azure for inspection and recovery.