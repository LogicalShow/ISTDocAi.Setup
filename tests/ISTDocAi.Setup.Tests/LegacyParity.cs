/* LEGACY CASE MAP
PageAllowanceDeploymentSqlTests.Topup_replay_conflict_status_and_isolation [1] [Test, -> LegacyParitySqlTests.Topup_status_isolation_blocked_mode_confirmation_and_mode_conflict
PageAllowanceDeploymentSqlTests.Reversed_topups_are_excluded_from_usage_and_operator_cycle [1] [TestCase(false)] -> LegacyParitySqlTests.Reversed_topups_are_excluded_from_service_and_operator_cycle
PageAllowanceDeploymentSqlTests.Reversed_topups_are_excluded_from_usage_and_operator_cycle [2] [TestCase(true)] -> LegacyParitySqlTests.Reversed_topups_are_excluded_from_service_and_operator_cycle
PageAllowanceDeploymentSqlTests.Debt_reversal_and_preview [1] [Test, -> OperatorSqlTests.Debt_reversal_double_reversal_and_consumer_isolation
PageAllowanceDeploymentSqlTests.Mode_switch_atomic_replay_and_history [1] [Test, -> LegacyParitySqlTests.Topup_status_isolation_blocked_mode_confirmation_and_mode_conflict
PageAllowanceDeploymentSqlTests.Unspent_reversal_and_reserved_pages_refusal_match_service_status [1] [Test, -> OperatorSqlTests.Reserved_pages_refuse_reversal_and_status_matches_service
PageAllowanceDeploymentSqlTests.Stale_preview_and_inactive_consumer_refuse_without_changes [1] [Test, -> OperatorSqlTests.Stale_preview_and_inactive_consumer_are_refused_without_changes
PageAllowanceDeploymentSqlTests.Fresh_grants_and_upgrade_have_identical_quota_permissions [1] [Test, -> LegacyParitySqlTests.Fresh_migrated_and_legacy_upgrade_quota_permissions_are_identical
PageAllowanceDeploymentSqlTests.Operator_can_run_actions_runtime_cannot_grant [1] [Test, -> LegacyParitySqlTests.Quota_role_all_actions_and_runtime_insert_denied_membership_replay
PageAllowanceBuilderTests.Invalid_inputs_and_scripts_parse_before_SQL [1] [Test] -> OperatorPureTests.Invalid_page_inputs_refused_before_IO
TopUpReportingScriptTests.Operator_uses_unmodified_reference_sql [1] [TestCase("Cycles", "S29")] -> OperatorPureTests.Reports_use_exact_reference_statements_and_no_mutation
TopUpReportingScriptTests.Operator_uses_unmodified_reference_sql [2] [TestCase("Documents", "S30")] -> OperatorPureTests.Reports_use_exact_reference_statements_and_no_mutation
TopUpReportingScriptTests.Reporting_scripts_parse_and_reject_invalid_parameters_without_IO [1] [Test] -> LegacyParityPureTests.Customer_invalid_parameters_refuse_before_secret_IO
TopUpReportingScriptTests.Operator_warning_exact_csv_and_customer_output_are_separate [1] [Test] -> LegacyParityPureTests.Report_CSV_escaping_exact_decimals_and_unpriced_warning
TopUpReportingScriptTests.Both_32_wrappers_read_without_preview_and_export_escaped_JSON_CSV [1] [TestCase("local")] -> LegacyParityPureTests.Report_CSV_escaping_exact_decimals_and_unpriced_warning
TopUpReportingScriptTests.Both_32_wrappers_read_without_preview_and_export_escaped_JSON_CSV [2] [TestCase("onprem")] -> LegacyParityPureTests.Report_CSV_escaping_exact_decimals_and_unpriced_warning
TopUpReportingScriptTests.Http_helper_reads_key_privately_follows_export_cursor_and_caps_at_10000 [1] [Test] -> LegacyParityPureTests.HTTP_date_and_delta_retry_after_and_cursor_export_keep_key_private
TopUpReportingScriptTests.Http_retries_429_503_only_honors_retry_after_and_never_follows_redirects [1] [Test] -> LegacyParityPureTests.HTTP_date_and_delta_retry_after_and_cursor_export_keep_key_private
OperatorPackageTests.GeneratedOperatorsJoinMigratedRolesAndCanBeAppliedTwice [1] [Test, -> LegacyParitySqlTests.Quota_role_all_actions_and_runtime_insert_denied_membership_replay
BlActivationDeploymentSqlTests.Switch_is_versioned_rerun_is_noop_rollback_works_and_AUTO_still_routes_BL [1] [Test, -> LegacyParitySqlTests.BL_only_does_not_register_router_and_switch_versions_routes_and_rolls_back
BlActivationDeploymentSqlTests.Invalid_group_or_accuracy_rolls_back_every_change [1] [TestCase("manifest-v2", "0.95", "group mismatch")] -> LegacyParitySqlTests.Invalid_BL_group_or_accuracy_preserves_complete_snapshot
BlActivationDeploymentSqlTests.Invalid_group_or_accuracy_rolls_back_every_change [2] [TestCase("bl-v2", "0.5", "accuracy gate")] -> LegacyParitySqlTests.Invalid_BL_group_or_accuracy_preserves_complete_snapshot
BlActivationDeploymentSqlTests.Invalid_group_or_accuracy_rolls_back_every_change [3] [TestCase("bl-maersk-v1", "0.5", "accuracy gate")] -> LegacyParitySqlTests.Invalid_BL_group_or_accuracy_preserves_complete_snapshot
BlActivationDeploymentSqlTests.Retired_manifest_can_be_reactivated_without_changing_BL_or_classifier [1] [Test, -> LegacyParitySqlTests.Manifest_replacement_and_retired_rollback_preserve_BL_classifier_and_classes
NewModelMappingTests.New_models_copy_existing_profiles_and_identical_BL_profiles_have_same_fingerprint [1] [Test] -> LegacyParityPureTests.Empty_maps_SQL_default_and_retained_rollback_profiles
BlActivationBuilderTests.Builder_rejects_invalid_ids_bounds_and_missing_S14_before_any_SQL [1] [Test] -> LegacyParityPureTests.BL_bounds_and_reference_are_checked_before_emitting_SQL
OnPremModelsBuilderTests.Registration_includes_binding_in_registration_transaction [1] [TestCase("")] -> LegacyParityPureTests.Binding_is_inside_registration_transaction
OnPremModelsBuilderTests.Registration_includes_binding_in_registration_transaction [2] [TestCase("-ManifestModelId custom-manifest -MappingFile deploy/mapping-example.json")] -> LegacyParityPureTests.Binding_is_inside_registration_transaction
OnPremModelsBuilderTests.Invalid_inputs_are_refused [1] [TestCase("-ManifestModelId 'bad id' -ManifestTestFieldAccuracy 0.9")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [2] [TestCase("-ManifestModelId op-bl -ManifestTestFieldAccuracy 0.9")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [3] [TestCase("-ManifestModelId op-manifest -ManifestTestFieldAccuracy 0.9 -ClassifierModelId op-manifest -ClassifierTestFieldAccuracy 0.9")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [4] [TestCase("-BillClassName other")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [5] [TestCase("-BillClassName OTHER")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [6] [TestCase("-ManifestModelId OP-BL -ManifestTestFieldAccuracy 0.9")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [7] [TestCase("-ClassifierModelId op-router -ClassifierTestFieldAccuracy 0.9")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [8] [TestCase("-ManifestModelId op-manifest")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [9] [TestCase("-ManifestModelId op-manifest -ManifestTestFieldAccuracy 0.9 -ClassifierModelId op-router")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [10] [TestCase("-ManifestTestFieldAccuracy 1.1")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [11] [TestCase("-ClassifierTestFieldAccuracy -0.1")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [12] [TestCase("-BlMinFieldConfidence 1.1")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [13] [TestCase("-ManifestMinFieldConfidence -0.1")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [14] [TestCase("-ClassifierMinConfidence 1.1")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [15] [TestCase("-RouterMinimumAccuracy -0.1")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [16] [TestCase("-BlMinimumAccuracy 1.1")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [17] [TestCase("-BillClassName 'bad;name'")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Invalid_inputs_are_refused [18] [TestCase("-BillShippingLine 'bad line'")] -> LegacyParityPureTests.Every_legacy_set_validation_is_preserved
OnPremModelsBuilderTests.Bounds_package_and_overwrite_guards_are_enforced [1] [Test] -> LegacyParityPureTests.Model_export_refuses_existing_files_without_connection
OnPremModelsBuilderTests.File_is_plain_ASCII_SQL_with_expected_batches [1] [TestCase("")] -> LegacyParityPureTests.Model_files_are_ASCII_stable_with_optional_router
OnPremModelsBuilderTests.File_is_plain_ASCII_SQL_with_expected_batches [2] [TestCase(Full)] -> LegacyParityPureTests.Model_files_are_ASCII_stable_with_optional_router
OnPremModelsBuilderTests.File_is_plain_ASCII_SQL_with_expected_batches [3] [TestCase("-ManifestModelId op-manifest -ManifestTestFieldAccuracy 0.92")] -> LegacyParityPureTests.Model_files_are_ASCII_stable_with_optional_router
OnPremModelsDeploymentSqlTests.Full_set_has_measured_values_targets_retires_old_BL_and_reruns_without_changes [1] [Test, -> LegacyParitySqlTests.Full_set_measured_values_targets_retirement_and_replay_no_writes
OnPremModelsDeploymentSqlTests.BL_below_gate_is_not_activated [1] [Test, -> LegacyParitySqlTests.Accuracy_gate_refuses_atomically_without_retiring_previous_models
OnPremModelsDeploymentSqlTests.BL_only_does_not_register_router_models [1] [Test, -> LegacyParitySqlTests.BL_only_does_not_register_router_and_switch_versions_routes_and_rolls_back
MappingRegistrationBuilderTests.Reviewed_reference_is_SQLCMD_safe_and_complete_rows_are_compared_in_both_directions [1] [Test] -> OperatorPureTests.Mapping_reference_is_hex_SQLCMD_safe_and_compared_both_directions
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [1] [TestCase("$p.DocumentTypeCode='BL';")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [2] [TestCase("$p.MappingShape='LegacyFallback';")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [3] [TestCase("$p.ContainersField='bad name';")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [4] [TestCase("$p.ContainerNumberField='x'*129;")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [5] [TestCase("$p.SealNumberField=$null;")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [6] [TestCase("$p.ChangeReference=' ';")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [7] [TestCase("$p.ChangeReference='x'*257;")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [8] [TestCase("$p.Rows[0].MapKind='field';")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [9] [TestCase("$p.Rows[0].CanonicalKey='1bad';")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [10] [TestCase("$p.Rows[0].CanonicalKey='x'*65;")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [11] [TestCase("$p.Rows[0].AzureName=([string][char]39);")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [12] [TestCase("$p.Rows[0].AzureName='x'*129;")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [13] [TestCase("$p.Rows += $p.Rows[0];")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [14] [TestCase("$p.Rows=@($p.Rows[0])*257;")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [15] [TestCase("$p.Rows=$null;")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Invalid_review_is_refused_before_SQL_is_emitted [16] [TestCase("$p | Add-Member MisspelledProperty wrong;")] -> LegacyParityPureTests.Every_legacy_mapping_validation_is_preserved
MappingRegistrationBuilderTests.Empty_effective_maps_are_allowed_and_default_is_Sql_with_rollback_maps_retained [1] [Test] -> LegacyParityPureTests.Empty_maps_SQL_default_and_retained_rollback_profiles
MappingRegistrationSqlTests.Fresh_migrated_registry_registers_seeds_reruns_without_writes_and_intake_worker_resolve_SQL [1] [Test, -> LegacyParitySqlTests.Fresh_registry_seed_bindings_replay_readiness_and_intake_use_SQL_profiles
MappingRegistrationSqlTests.Missing_binding_refuses_direct_S14_and_helper_registration_rolls_back [1] [Test, -> LegacyParitySqlTests.Missing_binding_direct_S14_and_registration_refuse_with_original_codes
MappingRegistrationSqlTests.Reviewed_profile_rerun_is_noop_and_sealed_conflict_is_refused [1] [Test, -> LegacyParitySqlTests.Reviewed_sealed_profile_replay_noop_and_conflicts_refuse_without_writes
RouterActivationDeploymentSqlTests.Generated_activation_activates_router_preserves_BL_and_rerun_is_noop [1] [Test, -> LegacyParitySqlTests.Router_activation_versions_catalog_preserves_BL_and_replays
RouterActivationDeploymentSqlTests.Model_only_switch_registers_next_version_retires_v1_preserves_classifier [1] [Test, -> LegacyParitySqlTests.Manifest_replacement_and_retired_rollback_preserve_BL_classifier_and_classes
IamDeploymentGrantTests.Runtime_grants_cover_histories_and_seeders_without_DDL_and_gate_template_tables [1] [TestCase(false)] -> n/a: owner Phase 2 decision: DBA-managed 05-runtime-grants.sql is out of scope; MigrateOnStartup only; DocAi owns its database
IamDeploymentGrantTests.Runtime_grants_cover_histories_and_seeders_without_DDL_and_gate_template_tables [2] [TestCase(true)] -> n/a: owner Phase 2 decision: DBA-managed 05-runtime-grants.sql is out of scope; MigrateOnStartup only; DocAi owns its database
ReportPdfTests.Absolute_wrapper_paths_are_not_prefixed_with_the_repository [1] [TestCase(@"C:\stage14b path\O'Brien")] -> n/a: PowerShell WrapperCommand/Join-Path substitution only; no wrapper or renderer subprocess remains
ReportPdfTests.Absolute_wrapper_paths_are_not_prefixed_with_the_repository [2] [TestCase(@"D:\stage14b path\O'Brien")] -> n/a: PowerShell WrapperCommand/Join-Path substitution only; no wrapper or renderer subprocess remains
ReportPdfTests.Pdf_scripts_route_validate_cap_and_keep_credentials_out_of_renderer [1] [Test] -> LegacyParityPureTests.PDF_output_guards_routing_cap_and_renderer_has_no_credentials
ReportPdfTests.Operator_wrapper_generates_real_pdf_with_correct_SQL [1] [TestCase("local","Ist","Cycles")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Operator_wrapper_generates_real_pdf_with_correct_SQL [2] [TestCase("onprem","Ist","Cycle")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Operator_wrapper_generates_real_pdf_with_correct_SQL [3] [TestCase("local","Ist","Documents")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Operator_wrapper_generates_real_pdf_with_correct_SQL [4] [TestCase("local","Customer","Cycles")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Operator_wrapper_generates_real_pdf_with_correct_SQL [5] [TestCase("onprem","Customer","Cycle")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Operator_wrapper_generates_real_pdf_with_correct_SQL [6] [TestCase("onprem","Customer","Documents")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Http_export_generates_customer_pdf_without_exposing_key [1] [TestCase("local","Cycles")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Http_export_generates_customer_pdf_without_exposing_key [2] [TestCase("local","Cycle")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Http_export_generates_customer_pdf_without_exposing_key [3] [TestCase("local","Documents")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Http_export_generates_customer_pdf_without_exposing_key [4] [TestCase("onprem","Cycles")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Http_export_generates_customer_pdf_without_exposing_key [5] [TestCase("onprem","Cycle")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
ReportPdfTests.Http_export_generates_customer_pdf_without_exposing_key [6] [TestCase("onprem","Documents")] -> LegacyParityPureTests.Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved
*/
// Counts use test cases (one row per Test/TestCase attribute), not just method names.
// Partial plumbing assertions retired with PowerShell: script AST parsing, IT-package folder prerequisite,
// injected Reference text (now embedded and byte-checked), local/onprem duplication, and renderer argv.
// Behavioral assertions for those mixed tests are retained in the target tests and their supporting fixtures.
// LEGACY PARITY MAP (case rows are generated below after the assertion audit).
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Headers;
using Dapper;
using ISTDocAi.OperatorTool;
using ISTDocAi.Provisioning;
using ISTDocAi.Setup.Tests.Provisioning;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ISTDocAi.Infrastructure;
using ISTDocAi.Infrastructure.Configuration.DocAi;
using ISTDocAi.Infrastructure.Data.Sql.DocAi;
using ISTDocAi.Infrastructure.Data.Repositories.DocAi;
using ISTDocAi.Infrastructure.Services.DocAi.Mapping;
using ISTDocAi.Application.Common.Interfaces.DocAi.Persistence;
using NUnit.Framework;
using UglyToad.PdfPig;
using OperatorReports = ISTDocAi.OperatorTool.Reports;

namespace ISTDocAi.Setup.Tests;

[Category("Pure")]
public sealed class LegacyParityPureTests
{
    [Test]
    public void Every_mapped_target_is_an_existing_test_and_every_legacy_case_is_listed()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.SetupRoot, "tests/ISTDocAi.Setup.Tests/LegacyParity.cs"));
        var map = source.Split("/* LEGACY CASE MAP", StringSplitOptions.None)[1].Split("*/", StringSplitOptions.None)[0];
        var entries = map.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Assert.That(entries.Distinct().Count(), Is.EqualTo(entries.Length));
        foreach (var entry in entries)
        {
            var target = entry.Split(" -> ", StringSplitOptions.None)[1];
            if (target.StartsWith("n/a:", StringComparison.Ordinal)) { Assert.That(target.Length, Is.GreaterThan(20)); continue; }
            var dot = target.LastIndexOf('.');
            var type = typeof(LegacyParityPureTests).Assembly.GetType("ISTDocAi.Setup.Tests." + target[..dot]);
            Assert.That(type, Is.Not.Null, entry);
            var method = type!.GetMethod(target[(dot+1)..]);
            Assert.That(method, Is.Not.Null, entry);
            Assert.That(method!.GetCustomAttributes().Any(a => a is TestAttribute or TestCaseAttribute or TestCaseSourceAttribute), Is.True, entry);
        }
        // The map was audited case-by-case against the legacy test sources (93 cases) before legacy/ was archived to
        // ISTDocAi-NonCode on 2026-11-14. Freeze it: rows may not be dropped or added without a new audit.
        Assert.That(entries, Has.Length.EqualTo(93), "Legacy parity map is frozen at the 93 audited cases.");
        Assert.That(entries.Count(x => x.Contains(" -> n/a:", StringComparison.Ordinal)), Is.EqualTo(4), "Exactly the four owner-approved n/a rows.");
    }

    public static IEnumerable<TestCaseData> InvalidSets()
    {
        var r = new ModelSet("op-bl", .97m);
        yield return new(r with { ManifestModelId="bad id", ManifestTestFieldAccuracy=.9m });
        yield return new(r with { ManifestModelId="op-bl", ManifestTestFieldAccuracy=.9m });
        yield return new(r with { ManifestModelId="op-manifest", ManifestTestFieldAccuracy=.9m, ClassifierModelId="op-manifest", ClassifierTestFieldAccuracy=.9m });
        yield return new(r with { BillClassName="other" }); yield return new(r with { BillClassName="OTHER" });
        yield return new(r with { ManifestModelId="OP-BL", ManifestTestFieldAccuracy=.9m });
        yield return new(r with { ClassifierModelId="op-router", ClassifierTestFieldAccuracy=.9m });
        yield return new(r with { ManifestModelId="op-manifest" });
        yield return new(r with { ManifestModelId="op-manifest", ManifestTestFieldAccuracy=.9m, ClassifierModelId="op-router" });
        yield return new(r with { ManifestTestFieldAccuracy=1.1m }); yield return new(r with { ClassifierTestFieldAccuracy=-.1m });
        yield return new(r with { BlMinFieldConfidence=1.1m }); yield return new(r with { ManifestMinFieldConfidence=-.1m });
        yield return new(r with { ClassifierMinConfidence=1.1m }); yield return new(r with { RouterMinimumAccuracy=-.1m });
        yield return new(r with { BlMinimumAccuracy=1.1m }); yield return new(r with { BillClassName="bad;name" });
        yield return new(r with { BillShippingLine="bad line" });
    }
    [TestCaseSource(nameof(InvalidSets))]
    public void Every_legacy_set_validation_is_preserved(ModelSet request) => Assert.Throws<ArgumentException>(() => ModelSql.Set(request));

    [TestCase("DocumentTypeCode", "BL")][TestCase("MappingShape", "LegacyFallback")]
    [TestCase("ContainersField", "bad name")][TestCase("ContainerNumberField", "long129")]
    [TestCase("SealNumberField", null)][TestCase("ChangeReference", " ")][TestCase("ChangeReference", "long257")]
    [TestCase("MapKind", "field")][TestCase("CanonicalKey", "1bad")][TestCase("CanonicalKey", "long65")]
    [TestCase("AzureName", "'")][TestCase("AzureName", "long129")]
    [TestCase("Rows", "duplicate")][TestCase("Rows", "many")][TestCase("Rows", null)][TestCase("MisspelledProperty", "wrong")]
    public void Every_legacy_mapping_validation_is_preserved(string field, string? value)
    {
        var p = OperatorPureTests.Mapping();
        if (value?.StartsWith("long", StringComparison.Ordinal) == true) value = new string('x', int.Parse(value[4..], System.Globalization.CultureInfo.InvariantCulture));
        if (field == "Rows") p[field] = value is null ? null : new JsonArray(Enumerable.Range(0,value=="many" ? 257 : 2).Select(_=>p["Rows"]![0]!.DeepClone()).ToArray());
        else if (field is "MapKind" or "CanonicalKey" or "AzureName") p["Rows"]![0]![field] = value;
        else p[field] = value;
        Assert.Throws<ArgumentException>(() => ModelSql.Binding("reviewed-manifest", "MANIFEST", p.ToJsonString()));
    }

    [Test]
    public void BL_bounds_and_reference_are_checked_before_emitting_SQL()
    {
        foreach (var id in new[]{"",new string('x',65),"bad id","bad'","bad\n","bad;id","bad/id"})
            Assert.Throws<ArgumentException>(()=>ModelSql.Bl(id,.95m));
        foreach (var n in new[]{-.1m,1.1m})
        {
            Assert.Throws<ArgumentException>(()=>ModelSql.Bl("valid",n));
            Assert.Throws<ArgumentException>(()=>ModelSql.Bl("valid",.95m,n));
            Assert.Throws<ArgumentException>(()=>ModelSql.Bl("valid",.95m,.6m,n));
        }
        Assert.That(SqlResources.Activation, Does.Contain("Extract activation requires a matching mapping binding"));
        // No caller-supplied Reference remains: the embedded S14 copy is checked byte-for-byte in OperatorPureTests.
    }

    [TestCase(false)][TestCase(true)]
    public void Binding_is_inside_registration_transaction(bool reviewed)
    {
        var json = reviewed ? OperatorPureTests.Mapping().ToJsonString() : "";
        var binding = ModelSql.Binding("custom-manifest", "MANIFEST", json);
        Assert.That(binding, Does.Contain("INSERT dbo.ModelMappingBindings").And.Not.Contain("GO"));
        var template = SqlResources.Read("Router2.sql");
        Assert.That(template.IndexOf("BEGIN TRANSACTION",StringComparison.Ordinal), Is.LessThan(template.IndexOf("$binding",StringComparison.Ordinal)));
        Assert.That(template.IndexOf("$binding",StringComparison.Ordinal), Is.LessThan(template.IndexOf("COMMIT;",StringComparison.Ordinal)));
    }

    [TestCase(0)][TestCase(1)][TestCase(2)]
    public void Model_files_are_ASCII_stable_with_optional_router(int shape)
    {
        var r = new ModelSet("op-bl",.97m);
        if(shape>0) r=r with { ManifestModelId="op-manifest", ManifestTestFieldAccuracy=.92m };
        if(shape==2) r=r with { ClassifierModelId="op-router", ClassifierTestFieldAccuracy=.91m, ManifestMinFieldConfidence=.76m, ClassifierMinConfidence=.53m };
        var sql=ModelSql.Set(r);
        Assert.That(sql,Does.StartWith("-- Target: DocAi").And.Contain("docai_modelteam").And.Contain("already Active; no changes").And.Contain("SELECT ModelId, DocumentTypeCode").And.Not.Contain("$("));
        Assert.That(sql.All(c=>c<=127),Is.True); Assert.That(sql,Is.EqualTo(ModelSql.Set(r)));
        if(shape<2) Assert.That(sql,Does.Not.Contain("INSERT dbo.ClassifierClasses"));
        if(shape==2) new OperatorPureTests().Model_set_is_one_outer_transaction_and_keeps_BL_MANIFEST_AUTO_order();
    }

    [Test]
    public async Task Model_export_refuses_existing_files_without_connection()
    {
        new LegacyParityPureTests().BL_bounds_and_reference_are_checked_before_emitting_SQL();
        var folder=Path.Combine(RepositoryPaths.SetupRoot,"TestResults","parity-model-"+Guid.NewGuid().ToString("N"));
        try
        {
            var console=new OperatorPureTests.ConsoleStub();
            string[] args=["models","register-set","--bl-model-id","op-bl","--bl-test-field-accuracy","0.97","--generate-sql",folder];
            Assert.That(await OperatorCli.Run(args,console),Is.Zero);
            var path=Path.Combine(folder,"05-register-models.sql"); var before=File.ReadAllBytes(path);
            Assert.That(await OperatorCli.Run(args,console),Is.EqualTo(1)); Assert.That(File.ReadAllBytes(path),Is.EqualTo(before));
            Assert.That(console.Output.ToString(),Does.Not.Contain("SQL Server:"));
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
    }

    [Test]
    public void Empty_maps_SQL_default_and_retained_rollback_profiles()
    {
        var p=OperatorPureTests.Mapping(); p["Rows"]=new JsonArray();
        Assert.DoesNotThrow(()=>ModelSql.Binding("id","MANIFEST",p.ToJsonString()));
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(RepositoryPaths.ISTDocAiRoot,"ISTDocAi.UI/ISTDocAi.UI.Server/appsettings.json")).Build();
        Assert.That(config.GetSection("DocAi:Mapping").GetChildren(),Is.Empty);
        var options=new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory,"mapping-appsettings.json")).Build().GetSection("DocAi:Mapping").Get<AnalyzeResultMappingOptions>()!;
        Assert.That(MappingProfiles.Select(options,"bl-v2","BL"),Is.SameAs(options.Models["bl-v2"]));
        Assert.That(MappingProfiles.SelectManifest(options,"manifest-v3"),Is.SameAs(options.Models["manifest-v3"]));
        var models=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"mapping-appsettings.json")))!["DocAi"]!["Mapping"]!["Models"]!;
        Assert.That(JsonNode.DeepEquals(models["bl-maersk-v1"],models["bl-v2"]),Is.True);
        Assert.That(JsonNode.DeepEquals(models["manifest-v2"],models["manifest-v3"]),Is.True);
        Assert.That(ISTDocAi.Infrastructure.Tests.DocAi.Runtime.InMemoryMappingProfileResolver.Fingerprint(options,"bl-maersk-v1","BL"),Is.EqualTo(ISTDocAi.Infrastructure.Tests.DocAi.Runtime.InMemoryMappingProfileResolver.Fingerprint(options,"bl-v2","BL")));
    }

    [TestCase("0")][TestCase("-1")][TestCase("9223372036854775808")][TestCase("documents")][TestCase("limit")]
    public async Task Customer_invalid_parameters_refuse_before_secret_IO(string invalid)
    {
        var args=new List<string>{"report","customer","--base-url","https://example.org","--kit-secrets","MISSING"};
        if(invalid=="documents")args.Add("--documents"); else if(invalid=="limit")args.AddRange(["--limit","501"]); else args.AddRange(["--grant-id",invalid]);
        var c=new OperatorPureTests.ConsoleStub(); Assert.That(await OperatorCli.Run(args.ToArray(),c),Is.EqualTo(1));
        Assert.That(c.Output.ToString(),Does.Not.Contain("Cannot read"));
    }

    [TestCase(302)][TestCase(401)][TestCase(404)][TestCase(500)][TestCase(503)]
    public void HTTP_only_retries_transient_status_and_caps_attempts(int status)
    {
        var handler=new HttpStub((_,_)=>new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent("fixture-private-key")});
        var waits=new List<TimeSpan>();
        using var client=new CustomerReports("https://example.org","fixture-private-key",handler,t=>{waits.Add(t);return Task.CompletedTask;});
        var e=Assert.ThrowsAsync<InvalidOperationException>(async()=>await client.Fetch());
        Assert.That(e!.Message,Does.Not.Contain("fixture-private-key"));
        Assert.That(handler.Calls,Is.EqualTo(status==503 ? 4 : 1)); Assert.That(waits,Has.Count.EqualTo(status==503 ? 3 : 0));
    }

    [Test]
    public async Task HTTP_date_and_delta_retry_after_and_cursor_export_keep_key_private()
    {
        var waits=new List<TimeSpan>();
        var handler=new HttpStub((call,request)=>
        {
            Assert.That(request.Headers.GetValues("X-Api-Key").Single(),Is.EqualTo("fixture-private-key"));
            var response=new HttpResponseMessage(call==1 ? HttpStatusCode.TooManyRequests : call==2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            if(call==1)response.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            if(call==2)response.Headers.RetryAfter=new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(11));
            var uri=request.RequestUri!; var cursor=Regex.Match(uri.Query,@"afterChargeId=(\d+)");
            var start=cursor.Success ? long.Parse(cursor.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture) : 0;
            var rows=new JsonArray(Enumerable.Range(1,500).Select(i=>(JsonNode)new JsonObject{["ChargeId"]=start+i,["Pages"]=2,["AzureCostUsd"]=.06m}).ToArray());
            response.Content=new StringContent(new JsonObject{["IsSuccess"]=true,["Value"]=new JsonObject{["Documents"]=rows,["NextAfterChargeId"]=start+500}}.ToJsonString());
            return response;
        });
        using var client=new CustomerReports("https://example.org","fixture-private-key",handler,t=>{waits.Add(t);return Task.CompletedTask;});
        var first=await client.Fetch("prelude",true,500); Assert.That(handler.Calls,Is.EqualTo(3));
        Assert.That(waits[0].TotalSeconds,Is.EqualTo(7)); Assert.That(waits[1].TotalSeconds,Is.InRange(10,11));
        var report=await OperatorReports.Documents((take,cursor)=>client.Fetch("prelude",true,take,cursor),500,first:first);
        Assert.That(handler.Calls,Is.EqualTo(22)); Assert.That(((JsonArray)report["documents"]!),Has.Count.EqualTo(10000));
        Assert.That(report["documents"]![9999]!["chargeId"]!.GetValue<long>(),Is.EqualTo(10000));
        Assert.That(OperatorReports.Csv(report,true,true),Does.Not.Match("(?i)azure|cost|price|meter|usd|fixture-private-key"));
        var source=File.ReadAllText(Path.Combine(RepositoryPaths.SetupRoot,"src/ISTDocAi.OperatorTool/Reports.cs"));
        Assert.That(source,Does.Contain("AllowAutoRedirect = false").And.Contain("Timeout = TimeSpan.FromSeconds(30)").And.Not.Contain("ServerCertificateCustomValidationCallback"));
        Assert.Throws<ArgumentException>(()=>OperatorArguments.Parse(["report","customer","--api-key","fixture-private-key"]));
    }
    private sealed class HttpStub(Func<int,HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(respond(++Calls,request));
    }

    [TestCase(false,"Cycles")][TestCase(false,"Cycle")][TestCase(false,"Documents")]
    [TestCase(true,"Cycles")][TestCase(true,"Cycle")][TestCase(true,"Documents")]
    public async Task Operator_and_HTTP_PDF_routes_audiences_and_company_are_preserved(bool customer,string kind)
    {
        var raw="""{"totals":{"grantedPages":1234,"azureCostUsd":12.3456,"unpricedRows":0},"cycles":[{"topUpGrantId":7,"topUpPages":1234}],"documents":[{"chargeId":42,"pages":29,"azureCostUsd":12.3456}],"nextAfterChargeId":null}""";
        JsonNode report;
        if(customer)
        {
            var handler=new HttpStub((_,request)=>
            {
                Assert.That(request.Headers.GetValues("X-Api-Key").Single(),Is.EqualTo("fixture-private-key"));
                Assert.That(request.RequestUri!.AbsolutePath,Does.StartWith("/api/v2/usage/topups"));
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(new JsonObject{["IsSuccess"]=true,["Value"]=JsonNode.Parse(raw)}.ToJsonString())};
            });
            using var client=new CustomerReports("https://example.org","fixture-private-key",handler,_=>Task.CompletedTask);
            report=await client.Fetch(kind=="Cycles" ? "" : "7",kind=="Documents");
            Assert.That(handler.Calls,Is.EqualTo(1));
            Assert.That(report.ToJsonString(),Does.Not.Contain("fixture-private-key"));
        }
        else report=OperatorReports.Parse(raw,false);
        var sql=PageSql.Generate(OperatorPureTests.Request(kind=="Documents" ? "Documents" : "Cycles") with {Customer=customer,GrantId=7});
        Assert.That(sql,Does.Contain(customer ? kind=="Documents" ? "[S28]" : "[S27]" : kind=="Documents" ? "[S30]" : "[S29]"));
        if(customer)Assert.That(sql,Does.Not.Match("UsageLedger|AzureMeterPrices|azureCostUsd"));
        using var pdf=PdfDocument.Open(OperatorReports.Pdf(report,report,customer,kind,"Forwarding","FWD-UNF"));
        var text=string.Join(" ",pdf.GetPages().Select(p=>p.Text)); Assert.That(text,Does.Contain("Forwarding"));
        if(customer)Assert.That(text,Does.Not.Match("(?i)USD|Azure|cost|price|meter|\\$|fixture-private-key"));
        else Assert.That(text,Does.Contain("12.3456").And.Contain("donotsendtothecustomer"));
    }

    [Test]
    public async Task PDF_output_guards_routing_cap_and_renderer_has_no_credentials()
    {
        new OperatorPureTests().PDF_and_CSV_paths_refuse_overwrite_and_XML_errors_never_expose_keys();
        await new OperatorReportTests().Document_exports_cap_at_10000_and_keep_cursor();
        foreach(var action in new[]{"Cycles","Documents"})
        {
            var sql=PageSql.Generate(OperatorPureTests.Request(action) with {Customer=true,GrantId=1});
            Assert.That(sql,Does.Contain(action=="Cycles" ? "[S27]" : "[S28]").And.Not.Match("UsageLedger|AzureMeterPrices|azureCostUsd"));
        }
        foreach(var args in new[]{
            new[]{"pages","status","--consumer","TEST","--pdf","existing.pdf"},
            new[]{"pages","cycles","--consumer","TEST","--force"},
            new[]{"pages","cycles","--consumer","TEST","--pdf","existing.pdf","--audience","Invalid"},
            new[]{"pages","cycles","--consumer","TEST","--pdf","existing.pdf","--generate-sql","out"}})
            Assert.Throws<ArgumentException>(()=>{var a=OperatorArguments.Parse(args);a.Page("fixture");});
        var folder=Path.Combine(RepositoryPaths.SetupRoot,"TestResults","parity-output-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"existing.pdf");await File.WriteAllTextAsync(path,"existing");
            foreach(var command in new[]{
                new[]{"pages","cycles","--consumer","TEST","--pdf",path},
                new[]{"report","customer","--base-url","https://example.org","--kit-secrets","MISSING","--pdf",path}})
            {
                var c=new OperatorPureTests.ConsoleStub();Assert.That(await OperatorCli.Run(command,c),Is.EqualTo(1));
                Assert.That(c.Output.ToString(),Does.Contain("existing files require --force").And.Not.Contain("Cannot read"));
                Assert.That(File.ReadAllText(path),Is.EqualTo("existing"));
            }
        }
        finally {Directory.Delete(folder,true);}
        var renderer=File.ReadAllText(Path.Combine(RepositoryPaths.SetupRoot,"src/ISTDocAi.OperatorTool/ReportRenderer.cs"));
        Assert.That(renderer,Does.Not.Match("ApiKey|SecretsPath|X-Api-Key"));
    }

    [Test]
    public void Report_CSV_escaping_exact_decimals_and_unpriced_warning()
    {
        var report=OperatorReports.Parse("""{"documents":[{"chargeId":1,"resolvedDocumentTypeCode":"a,\"b\"\nArabic","azureCostUsd":123456789012.1234}],"unpricedRows":1,"nextAfterChargeId":null}""",false);
        var csv=OperatorReports.Csv(report,true,false);
        Assert.That(csv,Does.Contain("123456789012.1234").And.Contain("\"a,\"\"b\"\"\nArabic\""));
        var console=new OperatorPureTests.ConsoleStub();
        var output=typeof(OperatorCli).GetMethod("Output",BindingFlags.NonPublic|BindingFlags.Static)!;
        output.Invoke(null,[OperatorArguments.Parse(["pages","documents","--consumer","TEST","--grant-id","1","--json","--audience","ist"]),console,report,null,false,true,true,"",""]);
        Assert.That(Regex.Matches(console.Output.ToString(),"WARNING: Unpriced"),Has.Count.EqualTo(1));
        console.Output.Clear(); var customer=OperatorReports.Normalize(report,true);
        output.Invoke(null,[OperatorArguments.Parse(["pages","documents","--consumer","TEST","--grant-id","1","--json"]),console,customer,null,true,true,true,"",""]);
        Assert.That(console.Output.ToString().Trim(),Is.EqualTo(customer.ToJsonString()));
    }
}

// All SQL fixtures use the existing uniquely named, EF-migrated disposable database helper.
[Category("Sql")]
public sealed class LegacyParitySqlTests : GuardedToolTests
{
    private const string Snapshot = "SELECT (SELECT * FROM dbo.ModelRegistry ORDER BY ModelId FOR JSON PATH,INCLUDE_NULL_VALUES) m,(SELECT * FROM dbo.MappingProfiles ORDER BY ProfileId FOR JSON PATH,INCLUDE_NULL_VALUES) p,(SELECT * FROM dbo.MappingFields ORDER BY ProfileId,MapKind,CanonicalKey FOR JSON PATH,INCLUDE_NULL_VALUES) f,(SELECT * FROM dbo.ModelMappingBindings ORDER BY ModelId FOR JSON PATH,INCLUDE_NULL_VALUES) b,(SELECT * FROM dbo.ClassifierClasses ORDER BY ClassifierModelId,ClassName FOR JSON PATH,INCLUDE_NULL_VALUES) c FOR JSON PATH";
    private static readonly string BlJson=OperatorPureTests.Mapping("BL").ToJsonString();
    private static string Folder()=>Path.Combine(RepositoryPaths.SetupRoot,"TestResults","parity-sql-"+Guid.NewGuid().ToString("N"));
    private static async Task<ModelSet> SetFiles(string folder)
    {
        Directory.CreateDirectory(folder);
        var bl=Path.Combine(folder,"bl.json"); var manifest=Path.Combine(folder,"manifest.json");
        await File.WriteAllTextAsync(bl,BlJson); await File.WriteAllTextAsync(manifest,OperatorPureTests.Mapping().ToJsonString());
        return new("op-bl",.97m){BlMappingFile=bl,ManifestModelId="op-manifest",ManifestMappingFile=manifest,ManifestTestFieldAccuracy=.92m,ManifestMinFieldConfidence=.76m,ClassifierModelId="op-router",ClassifierTestFieldAccuracy=.91m,ClassifierMinConfidence=.53m};
    }
    private static SqlException Refuse(Func<Task> run,int number,string fragment)
    {
        var error=Assert.ThrowsAsync<SqlException>(async()=>await run());
        Assert.That(error!.Number,Is.EqualTo(number)); Assert.That(error.Message,Does.Contain(fragment)); return error;
    }

    [Test]
    public async Task Full_set_measured_values_targets_retirement_and_replay_no_writes()
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize();
        await db.Execute(ModelSql.Bl("old-bl",.99m,json:BlJson));
        var folder=Folder();
        try
        {
            var r=await SetFiles(folder); var sql=ModelSql.Set(r); await db.Execute(sql);
            foreach(var (id,doc,accuracy,confidence) in new[]{("op-bl","BL",.97m,.8m),("op-manifest","MANIFEST",.92m,.76m),("op-router","AUTO",.91m,.53m)})
            {
                Assert.That(await db.Scalar<int>($"SELECT COUNT(*) FROM dbo.ModelRegistry WHERE ModelId='{id}' AND DocumentTypeCode='{doc}' AND Status='Active'"),Is.EqualTo(1));
                Assert.That(await db.Scalar<decimal>($"SELECT TestFieldAccuracy FROM dbo.ModelRegistry WHERE ModelId='{id}'"),Is.EqualTo(accuracy));
                Assert.That(await db.Scalar<decimal>($"SELECT MinFieldConfidence FROM dbo.ModelRegistry WHERE ModelId='{id}'"),Is.EqualTo(confidence));
            }
            Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='old-bl'"),Is.EqualTo("Retired"));
            Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ClassifierClasses WHERE ClassifierModelId='op-router'"),Is.EqualTo(3));
            Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ClassifierClasses WHERE ClassifierModelId='op-router' AND ShippingLineCode IS NULL AND ((ClassName='bill-of-lading' AND TargetDocumentTypeCode='BL') OR (ClassName='cargo-manifest' AND TargetDocumentTypeCode='MANIFEST') OR (ClassName='other' AND TargetDocumentTypeCode IS NULL))"),Is.EqualTo(3));
            var before=await db.Scalar<string>(Snapshot); await db.Execute(sql); Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
            // Active replay must ignore newly supplied low measurements without updating timestamps or thresholds.
            await db.Execute(ModelSql.Set(r with {BlTestFieldAccuracy=.1m,ManifestTestFieldAccuracy=.1m,ClassifierTestFieldAccuracy=.1m}));
            Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
        }
        finally {Directory.Delete(folder,true);}
    }

    [TestCase("bl")][TestCase("manifest")][TestCase("classifier")]
    public async Task Accuracy_gate_refuses_atomically_without_retiring_previous_models(string gate)
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize(); await db.Execute(ModelSql.Bl("old-bl",.99m,json:BlJson));
        var folder=Folder();
        try
        {
            var r=await SetFiles(folder); r=gate switch {"bl"=>r with {BlTestFieldAccuracy=.94m},"manifest"=>r with {ManifestTestFieldAccuracy=.5m},_=>r with {ClassifierTestFieldAccuracy=.5m}};
            var before=await db.Scalar<string>(Snapshot);
            Refuse(()=>db.Execute(ModelSql.Set(r)),50001,"accuracy gate");
            Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
            Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ModelRegistry WHERE ModelId LIKE 'op-%'"),Is.Zero);
            Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='old-bl'"),Is.EqualTo("Active"));
        }
        finally {Directory.Delete(folder,true);}
    }

    [Test]
    public async Task BL_only_does_not_register_router_and_switch_versions_routes_and_rolls_back()
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize();
        // Migration-seeded profiles are reused without a replacement mapping file.
        await db.Execute(ModelSql.Bl("bl-maersk-v1",.99m));
        await db.Execute("INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,Kind,Version,Status) VALUES('router-v1','AUTO','classify',1,'Active'); INSERT dbo.ClassifierClasses(ClassifierModelId,ClassName,TargetDocumentTypeCode,ShippingLineCode) VALUES('router-v1','bill-of-lading','BL',NULL);");
        var batch=ModelSql.Bl("bl-v2",.95m,confidence:.8m);
        Assert.That(batch,Does.Not.Match(@"(?i)\bPRINT\b"));
        Assert.That(await db.Scalar<string>(batch),Is.EqualTo("bl-v2 activated."));
        Assert.That(await db.Scalar<int>("SELECT Version FROM dbo.ModelRegistry WHERE ModelId='bl-v2'"),Is.EqualTo(2));
        Assert.That(await db.Scalar<decimal>("SELECT TestFieldAccuracy FROM dbo.ModelRegistry WHERE ModelId='bl-v2'"),Is.EqualTo(.95m));
        Assert.That(await db.Scalar<decimal>("SELECT MinFieldConfidence FROM dbo.ModelRegistry WHERE ModelId='bl-v2'"),Is.EqualTo(.8m));
        Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='bl-maersk-v1'"),Is.EqualTo("Retired"));
        Assert.That(await db.Scalar<string>("SELECT c.TargetDocumentTypeCode FROM dbo.ClassifierClasses c JOIN dbo.ModelRegistry r ON r.ModelId=c.ClassifierModelId AND r.Status='Active' JOIN dbo.ModelRegistry e ON e.DocumentTypeCode=c.TargetDocumentTypeCode AND e.Kind='extract' AND e.ShippingLineCode IS NULL AND e.Status='Active' WHERE c.ClassName='bill-of-lading' AND e.ModelId='bl-v2'"),Is.EqualTo("BL"));
        Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ModelRegistry WHERE DocumentTypeCode='MANIFEST'"),Is.Zero);
        var before=await db.Scalar<string>(Snapshot);
        Assert.That(await db.Scalar<string>(ModelSql.Bl("bl-v2",.1m)),Is.EqualTo("bl-v2 already Active; no changes."));
        Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
        Assert.That(await db.Scalar<string>(ModelSql.Bl("bl-maersk-v1",.95m)),Is.EqualTo("bl-maersk-v1 activated."));
        Assert.That(await db.Scalar<int>("SELECT Version FROM dbo.ModelRegistry WHERE ModelId='bl-maersk-v1'"),Is.EqualTo(1));
        Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='bl-v2'"),Is.EqualTo("Retired"));
    }

    [TestCase("manifest-v2",false,"group mismatch")][TestCase("bl-v2",false,"accuracy gate")][TestCase("bl-maersk-v1",true,"accuracy gate")]
    public async Task Invalid_BL_group_or_accuracy_preserves_complete_snapshot(string id,bool retired,string fragment)
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize(); await db.Execute(ModelSql.Bl("bl-maersk-v1",.99m));
        await db.Execute("INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,Kind,Version,Status) VALUES('manifest-v2','MANIFEST','extract',1,'Testing')");
        if(retired)await db.Execute(ModelSql.Bl("bl-v2",.95m));
        var before=await db.Scalar<string>(Snapshot);
        Refuse(()=>db.Execute(ModelSql.Bl(id,id=="manifest-v2" ? .95m : .5m)),50001,fragment);
        Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
    }

    [Test]
    public async Task Manifest_replacement_and_retired_rollback_preserve_BL_classifier_and_classes()
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize(); var folder=Folder();
        try
        {
            var r=await SetFiles(folder); await db.Execute(ModelSql.Set(r));
            const string others="SELECT (SELECT * FROM dbo.ModelRegistry WHERE DocumentTypeCode<>'MANIFEST' ORDER BY ModelId FOR JSON PATH,INCLUDE_NULL_VALUES) m,(SELECT * FROM dbo.ClassifierClasses ORDER BY ClassifierModelId,ClassName FOR JSON PATH,INCLUDE_NULL_VALUES) c FOR JSON PATH";
            var before=await db.Scalar<string>(others);
            var next=r with {ManifestModelId="manifest-next",ClassifierModelId="",ClassifierTestFieldAccuracy=null};
            await db.Execute(ModelSql.Set(next)); await db.Execute(ModelSql.Set(next));
            Assert.That(await db.Scalar<int>("SELECT Version FROM dbo.ModelRegistry WHERE ModelId='manifest-next'"),Is.EqualTo(2));
            Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='op-manifest'"),Is.EqualTo("Retired"));
            Assert.That(await db.Scalar<string>(others),Is.EqualTo(before));
            await db.Execute(ModelSql.Set(r with {ClassifierModelId="",ClassifierTestFieldAccuracy=null}));
            Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='op-manifest'"),Is.EqualTo("Active"));
            Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='manifest-next'"),Is.EqualTo("Retired"));
            Assert.That(await db.Scalar<string>(others),Is.EqualTo(before));
        }
        finally {Directory.Delete(folder,true);}
    }

    [Test]
    public async Task Missing_binding_direct_S14_and_registration_refuse_with_original_codes()
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize();
        await db.Execute("INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,Kind,Version,Status,TestFieldAccuracy) VALUES('missing-profile','BL','extract',1,'Testing',.99)");
        await using(var connection=new SqlConnection(db.ConnectionString))
        {
            var error=Assert.ThrowsAsync<SqlException>(async()=>await connection.ExecuteAsync(SqlResources.Activation,new {newModelId="missing-profile",minimumAccuracy=.6m}));
            Assert.That(error!.Number,Is.EqualTo(51205)); Assert.That(error.Message,Does.Contain("matching mapping binding"));
        }
        Assert.That(await db.Scalar<string>("SELECT Status FROM dbo.ModelRegistry WHERE ModelId='missing-profile'"),Is.EqualTo("Testing"));
        var before=await db.Scalar<string>(Snapshot);
        Refuse(()=>db.Execute(ModelSql.Bl("unknown-bl",.99m)),51205,"no mapping profile for unknown-bl; supply -MappingFile");
        Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
    }

    [TestCase("AzureName")][TestCase("ChangeReference")][TestCase("remove-row")][TestCase("add-row")]
    public async Task Reviewed_sealed_profile_replay_noop_and_conflicts_refuse_without_writes(string change)
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize();
        var profile=OperatorPureTests.Mapping();
        await db.Execute("INSERT dbo.ModelRegistry(ModelId,DocumentTypeCode,Kind,Version,Status,TestFieldAccuracy) VALUES('reviewed-manifest','MANIFEST','extract',1,'Testing',.99)");
        var sql=ModelSql.Mapping("reviewed-manifest","MANIFEST",profile.ToJsonString()); await db.Execute(sql);
        await using(var connection=new SqlConnection(db.ConnectionString)) await connection.ExecuteAsync(SqlResources.Activation,new {newModelId="reviewed-manifest",minimumAccuracy=.6m});
        var before=await db.Scalar<string>(Snapshot); await db.Execute(sql); Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
        if(change=="AzureName")profile["Rows"]![0]!["AzureName"]="Changed";
        else if(change=="ChangeReference")profile[change]=profile[change]!.GetValue<string>()+" ";
        else if(change=="remove-row")profile["Rows"]=new JsonArray();
        else ((JsonArray)profile["Rows"]!).Add(new JsonObject{["MapKind"]="Field",["CanonicalKey"]="extra",["AzureName"]="Extra"});
        Refuse(()=>db.Execute(ModelSql.Mapping("reviewed-manifest","MANIFEST",profile.ToJsonString())),51205,"Conflicting mapping profile");
        Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
    }

    [Test]
    public async Task Fresh_registry_seed_bindings_replay_readiness_and_intake_use_SQL_profiles()
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize();
        Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ModelRegistry"),Is.Zero);
        var r=new ModelSet("bl-v3",.97m){ManifestModelId="manifest-v3",ManifestTestFieldAccuracy=.92m,ClassifierModelId="IST-Classifier-v6",ClassifierTestFieldAccuracy=.91m};
        var sql=ModelSql.Set(r); await db.Execute(sql);
        Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ModelMappingBindings b JOIN dbo.MappingProfiles p ON p.ProfileId=b.ProfileId WHERE p.ProfileCode=b.ModelId COLLATE Latin1_General_100_BIN2 AND b.ModelId IN ('bl-v3','manifest-v3')"),Is.EqualTo(2));
        var before=await db.Scalar<string>(Snapshot); await db.Execute(sql); Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
        var options=Options.Create(new DocAiSqlOptions{ConnectionString=db.ConnectionString}); var connections=new SqlConnectionFactory(options);
        var resolver=new SqlMappingProfileResolver(connections,options,TimeProvider.System);
        using var readiness=new MappingProfileReadiness(connections,options,TimeProvider.System,Microsoft.Extensions.Logging.Abstractions.NullLogger<MappingProfileReadiness>.Instance);
        Assert.That(await readiness.CheckAsync(default),Is.True);
        var services=new ServiceCollection();
        ISTDocAi.Application.DependencyInjection.AddApplication(services);
        services.AddDocAiSql(o=>o.ConnectionString=db.ConnectionString);
        services.AddSingleton<IMappingProfileResolver>(resolver);
        using var provider=services.BuildServiceProvider(); using var scope=provider.CreateScope();
        var store=scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var queue=scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var operations=scope.ServiceProvider.GetRequiredService<IOperationStore>();
        var registry=new Registry(scope.ServiceProvider.GetRequiredService<IModelCatalog>(),scope.ServiceProvider.GetRequiredService<IModelThresholdSource>());
        await db.Execute(RegisterConsumerSql.Generate(TestData.Pairing(),100,"fixture","parity"));
        var consumer=await db.Scalar<short>("SELECT ConsumerId FROM dbo.Consumers WHERE Code='FWD-UNF'");
        foreach(var type in new[]{"BL","MANIFEST"})
        {
            var request=new IntakeRequest(Guid.NewGuid(),consumer,Guid.NewGuid().ToString("N"),type,null,System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()),"fixture.pdf","application/pdf",100,1,"documents/fixture.pdf","fixture");
            var result=await store.IntakeAsync(request,default); Assert.That(result.Outcome,Is.EqualTo(IntakeOutcome.Created));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
            ClaimedJob? job;
            while((job=await queue.ClaimAsync(timeout.Token)) is null)await Task.Delay(5,timeout.Token);
            Assert.That(job,Is.Not.Null);
            var fake=new ISTDocAi.Infrastructure.Tests.DocAi.Worker.WorkerFakes{MappingParser=new AnalysisParser(resolver),Raw=type=="BL" ? """{"pages":[{}],"documents":[{"fields":{"ShipperName":{"valueString":"Fixture shipper","confidence":0.99}}}]}""" : File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/Manifest/test-A-kestrel-seen.analyze.json"))};
            await fake.Processor(store,queue,operations,registry,registry).ExecuteAsync(job!,default);
            Assert.That((await store.GetRunAsync(job!.JobId,consumer,1,default))!.Status,Is.EqualTo("Completed"));
        }
    }
    private sealed class Registry(IModelCatalog catalog,IModelThresholdSource thresholds):ICatalogReader,IModelThresholdReader
    {
        public Task<IReadOnlyList<CatalogModel>> ReadAsync(CancellationToken ct)=>catalog.GetCatalogAsync(ct);
        public Task<decimal?> ReadThresholdAsync(string id,CancellationToken ct)=>thresholds.GetThresholdAsync(id,ct);
    }

    [TestCase(false)][TestCase(true)]
    public async Task Reversed_topups_are_excluded_from_service_and_operator_cycle(bool onlyTopup)
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize(); await db.Execute(RegisterConsumerSql.Generate(TestData.Pairing(),100,"fixture","parity"));
        PageRequest R(string action)=>OperatorPureTests.Request(action) with {Consumer="FWD-UNF",RequestId=Guid.NewGuid()};
        var initial=await db.Scalar<long>("SELECT MIN(GrantId) FROM dbo.PageAllowanceGrants");
        if(onlyTopup)await db.Execute(PageSql.Generate(R("Reverse") with {GrantId=initial}));
        var top=R("TopUp") with {Pages=50}; await db.Execute(PageSql.Generate(top));
        var grant=await db.Scalar<long>($"SELECT GrantId FROM dbo.PageAllowanceGrants WHERE RequestId='{top.RequestId}'");
        await db.Execute(PageSql.Generate(R("Reverse") with {GrantId=grant}));
        await using var connection=new SqlConnection(db.ConnectionString);
        var consumer=await db.Scalar<short>("SELECT ConsumerId FROM dbo.Consumers WHERE Code='FWD-UNF'");
        async Task Check(bool adjusted=false)
        {
            var usage=await connection.QuerySingleAsync(new SqlStatementCatalog()["S26"],new{consumerId=consumer,months=6,asOfUtc=DateTime.UtcNow});
            var status=await connection.QuerySingleAsync(PageSql.Generate(R("Status")));
            Assert.That((long?)usage.CycleBase,Is.EqualTo(onlyTopup ? null : (long?)100));
            Assert.That((long?)status.CycleBase,Is.EqualTo((long?)usage.CycleBase));
            Assert.That((long?)status.WarningAtPages,Is.EqualTo(onlyTopup ? null : (long?)10));
            Assert.That((string)status.Status,Is.EqualTo(onlyTopup && !adjusted ? "Exhausted" : "Ok"));
            if(onlyTopup)Assert.That((DateTime?)usage.LastTopUpAtUtc,Is.Null);
        }
        await Check(); if(onlyTopup){await db.Execute(PageSql.Generate(R("Adjust") with {Pages=5}));await Check(true);}
    }

    [Test]
    public async Task Quota_role_all_actions_and_runtime_insert_denied_membership_replay()
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize(); await db.Execute(RegisterConsumerSql.Generate(TestData.Pairing(),100,"fixture","parity"));
        await db.Execute("CREATE USER packageOperator WITHOUT LOGIN; CREATE USER packageModels WITHOUT LOGIN; CREATE USER packageQuota WITHOUT LOGIN; CREATE USER runtime_test WITHOUT LOGIN; ALTER ROLE docai_runtime ADD MEMBER runtime_test;");
        var batches=Regex.Split(OperatorGrants.Generate("Disposable","packageOperator","packageModels","packageQuota"),@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase).Where(b=>b.Contains("ALTER ROLE",StringComparison.Ordinal)).ToArray();
        for(var run=0;run<2;run++)
        {
            foreach(var batch in batches)await db.Execute(batch);
            foreach(var (role,user) in new[]{("docai_operator","packageOperator"),("docai_modelteam","packageModels"),("docai_quota_operator","packageQuota")})Assert.That(await db.Scalar<int>($"SELECT IS_ROLEMEMBER('{role}','{user}')"),Is.EqualTo(1));
            Assert.That(await db.Scalar<int>("SELECT IS_ROLEMEMBER('docai_runtime','packageOperator')"),Is.Zero);
        }
        async Task AsQuota(PageRequest r)=>await db.Execute("EXECUTE AS USER='packageQuota'; BEGIN TRY "+PageSql.Generate(r)+" REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;");
        PageRequest R(string action)=>OperatorPureTests.Request(action) with {Consumer="FWD-UNF",RequestId=Guid.NewGuid()};
        var topup=R("SetMode") with {Mode="Prepaid",TopUpPages=12};
        await AsQuota(topup); await AsQuota(R("Status")); await AsQuota(R("Adjust") with {Pages=-2});
        await AsQuota(R("SetWarningPercent") with {Percent=20}); await AsQuota(R("SetMode") with {Mode="Unlimited"});
        var grant=await db.Scalar<long>($"SELECT GrantId FROM dbo.PageAllowanceGrants WHERE RequestId='{topup.RequestId}' AND Kind='TopUp' AND ConsumerId=(SELECT ConsumerId FROM dbo.Consumers WHERE Code='{topup.Consumer}')"); await AsQuota(R("Reverse") with {GrantId=grant,AllowDebt=true});
        Assert.ThrowsAsync<SqlException>(async()=>await db.Execute("EXECUTE AS USER='runtime_test'; BEGIN TRY INSERT dbo.PageAllowanceGrants(ConsumerId,Pages,Kind,OperatorIdentity,Note,RequestId) SELECT ConsumerId,1,'TopUp','runtime','forbidden',NEWID() FROM dbo.Consumers; REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;"));
    }
    [Test]
    public async Task Fresh_migrated_and_legacy_upgrade_quota_permissions_are_identical()
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize();
        const string permissions="SELECT OBJECT_NAME(major_id) ObjectName,COL_NAME(major_id,minor_id) ColumnName,permission_name,state_desc FROM sys.database_permissions WHERE grantee_principal_id=DATABASE_PRINCIPAL_ID('docai_quota_operator') ORDER BY OBJECT_NAME(major_id),minor_id,permission_name FOR JSON PATH,INCLUDE_NULL_VALUES";
        var before=await db.Scalar<string>(permissions);
        var root=Path.Combine(RepositoryPaths.ISTDocAiRoot,"ISTDocAi.Infrastructure.Tests/Fixtures/LegacySql");
        foreach(var name in new[]{"06_page_quota_upgrade.sql","08_operator_cost_report_upgrade.sql"})
            foreach(var batch in Regex.Split(await File.ReadAllTextAsync(Path.Combine(root,name)),@"^\s*GO\s*$",RegexOptions.Multiline|RegexOptions.IgnoreCase))
                if(!string.IsNullOrWhiteSpace(batch))await db.Execute(batch);
        Assert.That(await db.Scalar<string>(permissions),Is.EqualTo(before));
        // Independent legacy fresh-grants oracle, on the same disposable migrated schema, in a separate role.
        await db.Execute("CREATE ROLE quota_oracle");
        var fresh=await File.ReadAllTextAsync(Path.Combine(root,"02_docai_grants.sql"));
        var grants=Regex.Matches(fresh,@"(?im)^(?:GRANT|DENY)\s+[^;]+?\s+TO\s+docai_quota_operator;").Select(m=>m.Value.Replace("docai_quota_operator","quota_oracle",StringComparison.Ordinal)).ToArray();
        Assert.That(grants,Is.Not.Empty); foreach(var grant in grants)await db.Execute(grant);
        Assert.That(await db.Scalar<string>(permissions.Replace("'docai_quota_operator'","'quota_oracle'",StringComparison.Ordinal)),Is.EqualTo(before));
    }

    [Test]
    public async Task Topup_status_isolation_blocked_mode_confirmation_and_mode_conflict()
    {
        await using var db=new ProvisioningDatabase(); await db.Initialize();
        await db.Execute(RegisterConsumerSql.Generate(TestData.Pairing(),100,"fixture","parity"));
        PageRequest R(string action)=>OperatorPureTests.Request(action) with {Consumer="FWD-UNF",RequestId=Guid.NewGuid()};
        await db.Execute("INSERT dbo.Consumers(Code,Name,ProductCode,TenantCode,ApiKeyHash1) VALUES('OTHER','Other','FWD','OTHER',CONVERT(binary(32),2))");
        var top=R("TopUp") with {Pages=20}; await db.Execute(PageSql.Generate(top)); await db.Execute(PageSql.Generate(top));
        Refuse(()=>db.Execute(PageSql.Generate(top with {Pages=21})),50018,"RequestId conflict");
        await using var connection=new SqlConnection(db.ConnectionString);
        var row=await connection.QuerySingleAsync(PageSql.Generate(R("Status")));
        Assert.That((long)row.CycleBase,Is.EqualTo(120)); Assert.That((long)row.WarningAtPages,Is.EqualTo(12)); Assert.That((string)row.Status,Is.EqualTo("Ok"));
        Assert.That(await db.Scalar<string>("SELECT QuotaMode FROM dbo.Consumers WHERE Code='OTHER'"),Is.EqualTo("Unlimited"));
        Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants WHERE ConsumerId=(SELECT ConsumerId FROM dbo.Consumers WHERE Code='OTHER')"),Is.Zero);
        await db.Execute(PageSql.Generate(R("Adjust") with {Pages=-120})); await db.Execute(PageSql.Generate(R("SetMode") with {Mode="Unlimited"}));
        Refuse(()=>db.Execute(PageSql.Generate(R("SetMode") with {Mode="Prepaid"})),50018,"Customer blocked immediately");
        await db.Execute(PageSql.Generate(R("SetMode") with {Mode="Prepaid",ConfirmBlocked=true}));
        var mode=R("SetMode") with {Mode="Prepaid",TopUpPages=12}; await db.Execute(PageSql.Generate(mode));await db.Execute(PageSql.Generate(mode));
        Refuse(()=>db.Execute(PageSql.Generate(mode with {Mode="Unlimited",TopUpPages=0})),50018,"RequestId conflict");
        await db.Execute(PageSql.Generate(R("SetWarningPercent") with {Percent=50}));await db.Execute(PageSql.Generate(R("SetMode") with {Mode="Unlimited"}));
        Assert.That(await db.Scalar<int>("SELECT CONVERT(int,QuotaWarningPercent) FROM dbo.Consumers WHERE Code='FWD-UNF'"),Is.EqualTo(50));
        Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.PageAllowanceGrants"),Is.EqualTo(4));
    }

    [Test]
    public async Task Router_activation_versions_catalog_preserves_BL_and_replays()
    {
        await using var db=new ProvisioningDatabase();await db.Initialize();var folder=Folder();
        try
        {
            var r=await SetFiles(folder);await db.Execute(ModelSql.Set(r));
            var bl=await db.Scalar<string>("SELECT * FROM dbo.ModelRegistry WHERE ModelId='op-bl' FOR JSON PATH,INCLUDE_NULL_VALUES");
            var next=r with {ClassifierModelId="IST-Classifier-v4",ClassifierMinConfidence=.5m};
            var sql=ModelSql.Set(next);await db.Execute(sql);
            Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ModelRegistry WHERE ModelId IN ('op-manifest','IST-Classifier-v4') AND Status='Active'"),Is.EqualTo(2));
            Assert.That(await db.Scalar<decimal>("SELECT MinFieldConfidence FROM dbo.ModelRegistry WHERE ModelId='IST-Classifier-v4'"),Is.EqualTo(.5m));
            Assert.That(await db.Scalar<int>("SELECT COUNT(*) FROM dbo.ClassifierClasses WHERE ClassifierModelId='IST-Classifier-v4'"),Is.EqualTo(3));
            Assert.That(await db.Scalar<int>("SELECT Version FROM dbo.ModelRegistry WHERE ModelId='IST-Classifier-v4'"),Is.EqualTo(2));
            var services=new ServiceCollection();services.AddDocAiSql(o=>o.ConnectionString=db.ConnectionString);
            using var provider=services.BuildServiceProvider();using var scope=provider.CreateScope();
            Assert.That(await scope.ServiceProvider.GetRequiredService<IModelCatalog>().GetCatalogAsync(default),Has.Some.Matches<CatalogModel>(m=>m.DocumentTypeCode=="AUTO"&&m.ModelId=="IST-Classifier-v4"));
            Assert.That(await db.Scalar<string>("SELECT * FROM dbo.ModelRegistry WHERE ModelId='op-bl' FOR JSON PATH,INCLUDE_NULL_VALUES"),Is.EqualTo(bl));
            var before=await db.Scalar<string>(Snapshot);await db.Execute(sql);Assert.That(await db.Scalar<string>(Snapshot),Is.EqualTo(before));
        }
        finally {Directory.Delete(folder,true);}
    }

}
