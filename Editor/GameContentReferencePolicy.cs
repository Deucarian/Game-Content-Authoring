using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentReferencePolicy
    {
        internal static GameContentFieldDescriptor FindField(
            GameContentActiveEditSession active,
            string fieldId)
        {
            return active?.Fields.FirstOrDefault(candidate =>
                string.Equals(candidate.FieldId, fieldId, StringComparison.Ordinal));
        }

        internal static GameContentRecordReferenceFieldDescriptor ResolveReferenceDescriptor(
            GameContentFieldDescriptor field)
        {
            if (field == null) return null;
            if (field.FieldType == GameContentFieldType.RecordReference) return field.RecordReference;
            if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection)
                return field.Collection?.ItemDescriptor?.RecordReference;
            return null;
        }

        internal static GameContentFieldDescriptor ResolveStructuredRowField(
            GameContentFieldDescriptor field,
            string rowFieldId)
        {
            return field?.FieldType == GameContentFieldType.OrderedStructuredCollection
                ? field.StructuredCollection?.RowDescriptor?.FindField(rowFieldId)
                : null;
        }

        internal static bool IsDuplicateCollectionTarget(
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentRecordKey targetKey,
            GameContentCollectionItemKey ignoredItemKey)
        {
            if (field?.FieldType != GameContentFieldType.OrderedRecordReferenceCollection ||
                field.Collection == null || field.Collection.AllowDuplicates || targetKey == null)
                return false;
            GameContentOrderedCollectionValue current = active.GetEffectiveValue(field.FieldId)?.OrderedCollectionValue;
            if (current == null) return false;
            return current.Items.Any(item =>
                (ignoredItemKey == null || !item.ItemKey.Equals(ignoredItemKey)) &&
                item.Value.RecordReferenceValue != null &&
                item.Value.RecordReferenceValue.IsResolved &&
                targetKey.Equals(item.Value.RecordReferenceValue.TargetKey));
        }

        internal static GameContentRecordDescriptor ResolveReferenceRecord(
            GameContentActiveEditSession active,
            GameContentRecordReferenceValue reference)
        {
            if (active == null || reference == null || !reference.IsResolved || reference.TargetKey == null)
                return null;
            return (active.PackRecords ?? Array.Empty<GameContentRecordDescriptor>())
                .FirstOrDefault(value => value != null && value.CanonicalKey.Equals(reference.TargetKey));
        }

        internal static GameContentReferenceEvaluation EvaluateReferenceTargetCore(
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentRecordKey targetKey)
        {
            GameContentRecordReferenceFieldDescriptor referenceDescriptor = ResolveReferenceDescriptor(field);
            if (active == null || field == null || referenceDescriptor == null)
                return GameContentReferenceEvaluation.Rejected(targetKey, "The record-reference field contract is unavailable.");
            if (targetKey == null || !targetKey.IsValid)
                return GameContentReferenceEvaluation.Rejected(targetKey, "The target has no valid canonical record key.");
            if (referenceDescriptor.PackPolicy != GameContentReferencePackPolicy.SameSelectedPack)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "Only references within the selected content pack are supported.",
                    samePackPolicySatisfied: false);
            }

            bool sameOwner = string.Equals(
                targetKey.OwningPackageId,
                active.RecordKey.OwningPackageId,
                StringComparison.OrdinalIgnoreCase);
            bool samePack = string.Equals(
                targetKey.PackId,
                active.RecordKey.PackId,
                StringComparison.OrdinalIgnoreCase);
            if (!sameOwner || !samePack)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The target does not belong to the currently selected content pack.",
                    samePackPolicySatisfied: false);
            }

            GameContentRecordDescriptor target = (active.PackRecords ?? Array.Empty<GameContentRecordDescriptor>())
                .FirstOrDefault(value => value != null && value.CanonicalKey.Equals(targetKey));
            if (target == null)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The target is absent from the fresh selected-pack index.",
                    sourceClaimValid: false);
            }

            bool capabilitiesSatisfied = referenceDescriptor.RequiredCapabilities.All(target.HasCapability);
            if (!capabilitiesSatisfied)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The target does not provide every capability required by this reference.",
                    requiredCapabilitiesSatisfied: false);
            }

            if (target.Validation == null || !target.Validation.IsValid || target.HasBrokenReferences)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The target has blocking validation errors or broken references.",
                    validationState: GameContentEditValidationState.Invalid);
            }

            if (!(active.BackendSession is IGameContentRecordReferenceEditSession referenceSession))
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The editing backend does not support record-reference evaluation.");
            }

            GameContentReferenceEvaluation providerEvaluation;
            try
            {
                providerEvaluation = referenceSession.EvaluateReferenceTarget(field.FieldId, targetKey);
            }
            catch (Exception exception)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The editing backend could not evaluate the target: " + exception.GetBaseException().Message);
            }

            if (providerEvaluation == null)
                return GameContentReferenceEvaluation.Rejected(targetKey, "The editing backend returned no target evaluation.");
            if (providerEvaluation.ResolvedTargetKey == null ||
                !providerEvaluation.ResolvedTargetKey.Equals(targetKey))
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The editing backend resolved a different canonical target.");
            }

            return new GameContentReferenceEvaluation(
                providerEvaluation.IsValid,
                providerEvaluation.Reason,
                targetKey,
                capabilitiesSatisfied && providerEvaluation.RequiredCapabilitiesSatisfied,
                sameOwner && samePack && providerEvaluation.SamePackPolicySatisfied,
                providerEvaluation.SourceClaimValid,
                providerEvaluation.ProviderCompatibilitySatisfied,
                providerEvaluation.ValidationState,
                referenceDescriptor.RuntimeImpact |
                (field.Collection?.RuntimeImpact ?? GameContentReferenceRuntimeImpact.None) |
                providerEvaluation.RuntimeImpact);
        }

        internal static GameContentReferenceEvaluation EvaluateStructuredReferenceTargetCore(
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentStructuredRowKey rowKey,
            GameContentFieldDescriptor rowField,
            GameContentRecordKey targetKey)
        {
            GameContentRecordReferenceFieldDescriptor referenceDescriptor = rowField?.RecordReference;
            if (active == null || field?.FieldType != GameContentFieldType.OrderedStructuredCollection ||
                field.StructuredCollection == null || rowField == null ||
                rowField.FieldType != GameContentFieldType.RecordReference || referenceDescriptor == null)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The structured-row reference field contract is unavailable.");
            }
            if (targetKey == null || !targetKey.IsValid)
                return GameContentReferenceEvaluation.Rejected(targetKey, "The target has no valid canonical record key.");
            if (referenceDescriptor.PackPolicy != GameContentReferencePackPolicy.SameSelectedPack)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "Only references within the selected content pack are supported.",
                    samePackPolicySatisfied: false);
            }

            bool sameOwner = string.Equals(
                targetKey.OwningPackageId,
                active.RecordKey.OwningPackageId,
                StringComparison.OrdinalIgnoreCase);
            bool samePack = string.Equals(
                targetKey.PackId,
                active.RecordKey.PackId,
                StringComparison.OrdinalIgnoreCase);
            if (!sameOwner || !samePack)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The target does not belong to the currently selected content pack.",
                    samePackPolicySatisfied: false);
            }

            GameContentRecordDescriptor target = (active.PackRecords ?? Array.Empty<GameContentRecordDescriptor>())
                .FirstOrDefault(value => value != null && value.CanonicalKey.Equals(targetKey));
            if (target == null)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The target is absent from the fresh selected-pack index.",
                    sourceClaimValid: false);
            }

            bool capabilitiesSatisfied = referenceDescriptor.RequiredCapabilities.All(target.HasCapability);
            if (!capabilitiesSatisfied)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The target does not provide every capability required by this structured-row reference.",
                    requiredCapabilitiesSatisfied: false);
            }
            if (target.Validation == null || !target.Validation.IsValid || target.HasBrokenReferences)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The target has blocking validation errors or broken references.",
                    validationState: GameContentEditValidationState.Invalid);
            }
            if (!(active.BackendSession is IGameContentStructuredCollectionEditSession structuredSession))
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The editing backend does not support structured-row reference evaluation.");
            }

            GameContentReferenceEvaluation providerEvaluation;
            try
            {
                providerEvaluation = structuredSession.EvaluateStructuredRowReference(
                    field.FieldId,
                    rowKey,
                    rowField.FieldId,
                    targetKey);
            }
            catch (Exception exception)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The editing backend could not evaluate the structured-row target: " +
                    exception.GetBaseException().Message);
            }
            if (providerEvaluation == null)
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The editing backend returned no structured-row target evaluation.");
            }
            if (providerEvaluation.ResolvedTargetKey == null ||
                !providerEvaluation.ResolvedTargetKey.Equals(targetKey))
            {
                return GameContentReferenceEvaluation.Rejected(
                    targetKey,
                    "The editing backend resolved a different canonical target.");
            }

            return new GameContentReferenceEvaluation(
                providerEvaluation.IsValid,
                providerEvaluation.Reason,
                targetKey,
                capabilitiesSatisfied && providerEvaluation.RequiredCapabilitiesSatisfied,
                sameOwner && samePack && providerEvaluation.SamePackPolicySatisfied,
                providerEvaluation.SourceClaimValid,
                providerEvaluation.ProviderCompatibilitySatisfied,
                providerEvaluation.ValidationState,
                referenceDescriptor.RuntimeImpact |
                field.StructuredCollection.RuntimeImpact |
                providerEvaluation.RuntimeImpact);
        }

        internal static IReadOnlyList<GameContentAuthoringValidationIssue> EvaluateStagedReferences(
            GameContentActiveEditSession active)
        {
            var issues = new List<GameContentAuthoringValidationIssue>();
            if (active == null) return issues;
            foreach (GameContentFieldDescriptor field in active.Fields ?? Array.Empty<GameContentFieldDescriptor>())
            {
                if (field == null || field.IsReadOnly ||
                    (field.FieldType != GameContentFieldType.RecordReference &&
                     !field.FieldType.IsOrderedCollection() &&
                     field.FieldType != GameContentFieldType.OrderedStructuredCollection))
                    continue;
                GameContentFieldValue proposedValue = active.GetEffectiveValue(field.FieldId);
                if (!field.Accepts(proposedValue, out string reason))
                {
                    issues.Add(GameContentAuthoringValidationIssue.Error(field.FieldId, reason));
                    continue;
                }

                if (field.FieldType == GameContentFieldType.RecordReference)
                {
                    AddReferenceValidationIssue(
                        issues,
                        active,
                        field,
                        proposedValue.RecordReferenceValue,
                        string.Empty);
                }
                else if (field.FieldType == GameContentFieldType.OrderedRecordReferenceCollection)
                {
                    GameContentOrderedCollectionValue collection = proposedValue.OrderedCollectionValue;
                    for (int i = 0; i < collection.Items.Count; i++)
                    {
                        AddReferenceValidationIssue(
                            issues,
                            active,
                            field,
                            collection.Items[i].Value.RecordReferenceValue,
                            "Item " + (i + 1) + ": ");
                    }
                }
                else if (field.FieldType == GameContentFieldType.OrderedStructuredCollection)
                {
                    issues.AddRange(EvaluateStructuredCollectionReferences(
                        active,
                        field,
                        proposedValue.OrderedStructuredCollectionValue));
                }
            }
            return issues;
        }

        internal static IReadOnlyList<GameContentAuthoringValidationIssue> EvaluateStructuredCollectionReferences(
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentOrderedStructuredCollectionValue collection)
        {
            var issues = new List<GameContentAuthoringValidationIssue>();
            if (active == null || field?.FieldType != GameContentFieldType.OrderedStructuredCollection ||
                field.StructuredCollection == null || collection == null)
                return issues;
            for (int rowIndex = 0; rowIndex < collection.Rows.Count; rowIndex++)
            {
                GameContentStructuredRowValue row = collection.Rows[rowIndex];
                for (int fieldIndex = 0; fieldIndex < row.FieldValues.Count; fieldIndex++)
                {
                    GameContentStructuredRowFieldValue child = row.FieldValues[fieldIndex];
                    if (child.Value.FieldType != GameContentFieldType.RecordReference) continue;
                    GameContentFieldDescriptor rowField = ResolveStructuredRowField(field, child.FieldId);
                    string path = field.FieldId + "[" + (rowIndex + 1) + "]." + child.FieldId;
                    GameContentRecordReferenceValue reference = child.Value.RecordReferenceValue;
                    if (reference == null || reference.IsNone)
                    {
                        if (rowField != null && rowField.Required)
                        {
                            issues.Add(GameContentAuthoringValidationIssue.Error(
                                path,
                                "A canonical record reference is required."));
                        }
                        continue;
                    }
                    if (reference.IsBroken || reference.TargetKey == null)
                    {
                        issues.Add(GameContentAuthoringValidationIssue.Error(
                            path,
                            string.IsNullOrWhiteSpace(reference.BrokenReason)
                                ? "The structured-row record reference is broken."
                                : reference.BrokenReason));
                        continue;
                    }
                    GameContentReferenceEvaluation evaluation = EvaluateStructuredReferenceTargetCore(
                        active,
                        field,
                        row.RowKey,
                        rowField,
                        reference.TargetKey);
                    if (!evaluation.IsValid)
                    {
                        issues.Add(GameContentAuthoringValidationIssue.Error(path, evaluation.Reason));
                    }
                    else if (evaluation.ValidationState == GameContentEditValidationState.Warning)
                    {
                        issues.Add(GameContentAuthoringValidationIssue.Warning(
                            path,
                            string.IsNullOrWhiteSpace(evaluation.Reason)
                                ? "The target is compatible but has validation warnings."
                                : evaluation.Reason));
                    }
                }
            }
            return issues;
        }

        internal static void AddReferenceValidationIssue(
            ICollection<GameContentAuthoringValidationIssue> issues,
            GameContentActiveEditSession active,
            GameContentFieldDescriptor field,
            GameContentRecordReferenceValue reference,
            string prefix)
        {
            if (reference == null || reference.IsNone) return;
            if (reference.IsBroken || reference.TargetKey == null)
            {
                issues.Add(GameContentAuthoringValidationIssue.Error(
                    field.FieldId,
                    prefix + (string.IsNullOrWhiteSpace(reference.BrokenReason)
                        ? "The record reference is broken."
                        : reference.BrokenReason)));
                return;
            }

            GameContentReferenceEvaluation evaluation = EvaluateReferenceTargetCore(active, field, reference.TargetKey);
            if (!evaluation.IsValid)
            {
                issues.Add(GameContentAuthoringValidationIssue.Error(field.FieldId, prefix + evaluation.Reason));
            }
            else if (evaluation.ValidationState == GameContentEditValidationState.Warning)
            {
                issues.Add(GameContentAuthoringValidationIssue.Warning(
                    field.FieldId,
                    prefix + (string.IsNullOrWhiteSpace(evaluation.Reason)
                        ? "The target is compatible but has validation warnings."
                        : evaluation.Reason)));
            }
        }

        internal static GameContentValidationPreview MergeReferenceValidation(
            GameContentValidationPreview backendPreview,
            IReadOnlyList<GameContentAuthoringValidationIssue> referenceIssues)
        {
            backendPreview = backendPreview ?? GameContentValidationPreview.Valid;
            referenceIssues = referenceIssues ?? Array.Empty<GameContentAuthoringValidationIssue>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var merged = new List<GameContentAuthoringValidationIssue>();
            foreach (GameContentAuthoringValidationIssue issue in backendPreview.Issues.Concat(referenceIssues))
            {
                if (issue == null) continue;
                string key = ((int)issue.Severity) + "\n" + issue.Path + "\n" + issue.Message;
                if (seen.Add(key)) merged.Add(issue);
            }

            bool referenceHasErrors = referenceIssues.Any(value =>
                value != null && value.Severity == GameContentAuthoringValidationSeverity.Error);
            bool referenceHasWarnings = referenceIssues.Any(value =>
                value != null && value.Severity == GameContentAuthoringValidationSeverity.Warning);
            return new GameContentValidationPreview(
                merged,
                backendPreview.CanCommit && !referenceHasErrors,
                backendPreview.RequiresWarningConfirmation || referenceHasWarnings);
        }
    }
}
