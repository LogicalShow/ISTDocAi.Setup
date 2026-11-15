# ISTDocAi v2 kit integration

Set `DocAi:BaseUrl` to the ISTDocAi host HTTPS origin. The kit is v2-only at the HTTP boundary; existing historical v1 stored results remain readable. See the kit [V2-QA.md](../../ForwardingUniFreight/ForwardingUniFreight.Solution/Forwarding.MvcApp/DocAi/V2-QA.md) for the shared fixtures, decoder, signature and live checks.

| Call | Route | X-Culture |
| --- | --- | --- |
| Submit/replay | POST /api/v2/jobs | en |
| Cancel (no body) | POST /api/v2/jobs/cancel?requestId={clientRequestId} | en |
| Poll | GET /api/v2/jobs/{id}?runNo={runNo} | en |
| Catalog | GET /api/v2/catalog | ar for Arabic UI, otherwise en |
| Usage | GET /api/v2/usage?months={months} | ar for Arabic UI, otherwise en |
| Top-up reports | GET /api/v2/usage/topups[/{grantId}[/documents]] | en for operator scripts |

Send X-Api-Key and set X-Culture on each request, never shared HttpClient defaults. Persisted submit/poll errors remain English. Culture does not change canonical bytes, numeric parsing or signatures.

Success is `{IsSuccess:true,IsFailure:false,Error:{Code:"",Message:"",Type:"None"},Value:...}`. Decode Value once before projecting metadata. Transport failures expose Error.Code/Message and Extensions (CorrelationId, RequiredPages, AvailablePages, RetryAfter where applicable). Preserve HTTP status and the Retry-After header; a failed job is still a successful HTTP 200 read with Value.Status=Failed and Value.Error. Preserve Result as raw UTF-8 JSON: do not deserialize/re-encode its decimal tokens, escapes or property order. Unknown routes can return an empty 404.

Cancel uses the same API key as submit. HTTP 200 Value contains JobId, RunNo and Outcome: `Cancelled`, `Cancelling`, `Continuing` or `Finished`; an unknown request returns HTTP 404 `JOB_NOT_FOUND`. The service stops only a run Azure has not billed and cannot bill. Otherwise reading finishes and is charged once; uploading the same file returns its existing result without another charge.

The kit's authenticated `POST /api/DocAi/Cancel?pOperationID={operationId}&pLocalId={localId}` uses the Status operation scope. It atomically marks Pending/Submitted as Cancelled, retains the row and clears NextSubmitAt. It returns immediately and queues one remote attempt with a 10-second timeout. A successful submit racing with cancellation makes one further attempt after its guarded store detects Cancelled. There is no retry loop. Late webhooks/polls cannot revive cancelled rows; valid signed webhook deliveries still receive 200. The dialog resets to an empty selection; choosing a file again gets a fresh intent ID.

Webhook body is `{EventId,EventType,OccurredAtUtc,Job}` (no Result envelope); Job has the same DTO shape as GET Value. The destination remains the kit's existing `api/DocAi/Webhook`. Verify the unchanged HMAC-SHA256 over `timestamp + '.' + rawBodyBytes`, with existing X-Timestamp/X-Signature headers and 300-second freshness window, before decoding. Accept either rotation slot; reject tampering, expiry and a signed v1 body without storing it. Incoming webhooks have no X-Culture rule.

# KIT result fields (contract 1.0)

SQL mapping profiles change model-to-canonical names without changing contract `1.0` or fingerprint algorithm `2`. Kits can update independently. New BL `fields`, MANIFEST `header`, and bill `fields` dictionary keys may appear at any time. Ignore unknown keys or display them generically; never reject a result because its field dictionary has extra entries. Missing optional fields remain valid.

BL result JSON contains `contractVersion`, `documentTypeCode`, `shippingLineCode`, `modelId`, `pageCount`, `fields`, `containers`, `flags`. MANIFEST contains the same metadata plus `header`, `billsOfLading` (each has `fields` and `containers`), `unidentifiedBillsOfLading` (`sourceIndex`, `fields`), `unassignedContainers`, `flags`. Field values contain `value`, `confidence`, `page`, and optional `rawText`. The v2 job DTO carries `ResultType` (`bill-of-lading.v1` or `cargo-manifest.v1`) alongside `Result`. The canonical object inside `Result` retains its camelCase contract. Arrays/dictionaries are data, not an exhaustive field-name enumeration.

Container properties are fixed, not arbitrary dictionaries: BL has `containerNumber`, `sealNumber`, `containerType`, `packages`, `grossWeightKg`, `measurementCbm`; MANIFEST has optional `billOfLadingNumber`, `containerNumber`, `sealNumber`, `containerType`, `packages`, `grossWeight`, `description`, plus `associationMethod`. A mapping row cannot add a new container contract property.

Read-only reference checked in the sibling kit repository (no kit edits):
`Forwarding.MvcApp\Scripts\DocAi\DocAiExtract.js`:

- `DocAiExtract_ManifestBillView` at line 272: MANIFEST bill cargo and routing view lists.
- `DocAiExtract_BlView` at line 279: BL cargo and routing view lists.
- Shared detail cards at lines 432-435: party / routing / cargo / terms display lists.
- `present` field lists at lines 475 and 478: MANIFEST header/bill fields recognized as displayed. Keep these aligned with cards to avoid duplicate generic display/warnings.
- MANIFEST summary card at line 485: header field display list. Add a new header field here and to the header list at line 475.
- Bill grid columns at line 513: compact MANIFEST list; add columns only when needed. For a bill detail cargo field, update the view's cargo list at line 275 and the presented bill list at line 478.
- `DocAiExtract_Label` at line 287: optional friendly labels; the fallback formats unknown camelCase names.

These line numbers refer to the inspected kit snapshot and may move with kit updates. The server never controls these UI lists. Review the normalized kit detail shape (`header`/`fields` arrays) when editing its presentation; it differs from the dictionary-based wire contract.
