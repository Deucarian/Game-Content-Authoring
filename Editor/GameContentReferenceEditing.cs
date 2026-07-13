using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public enum GameContentRecordReferenceState
    {
        None = 0,
        Resolved = 1,
        Broken = 2
    }

    public enum GameContentReferencePackPolicy
    {
        SameSelectedPack = 0
    }

    [Flags]
    public enum GameContentReferenceRuntimeImpact
    {
        None = 0,
        Refresh = 1 << 0,
        Rebind = 1 << 1,
        Restart = 1 << 2
    }

    public interface IGameContentRecordReferenceEditSession
    {
        GameContentReferenceEvaluation EvaluateReferenceTarget(
            string fieldId,
            GameContentRecordKey targetKey);
    }

    public sealed class GameContentRecordReferenceValue : IEquatable<GameContentRecordReferenceValue>
    {
        private GameContentRecordReferenceValue(
            GameContentRecordReferenceState state,
            GameContentRecordKey targetKey,
            string targetDisplayName,
            string targetSourceLabel,
            string originalReference,
            string brokenReason)
        {
            State = state;
            TargetKey = targetKey;
            TargetDisplayName = Normalize(targetDisplayName);
            TargetSourceLabel = Normalize(targetSourceLabel);
            OriginalReference = Normalize(originalReference);
            BrokenReason = Normalize(brokenReason);
        }

        public GameContentRecordReferenceState State { get; }
        public GameContentRecordKey TargetKey { get; }
        public string TargetDisplayName { get; }
        public string TargetSourceLabel { get; }
        public string OriginalReference { get; }
        public string BrokenReason { get; }
        public bool IsNone => State == GameContentRecordReferenceState.None;
        public bool IsResolved => State == GameContentRecordReferenceState.Resolved;
        public bool IsBroken => State == GameContentRecordReferenceState.Broken;

        public static GameContentRecordReferenceValue None()
        {
            return new GameContentRecordReferenceValue(
                GameContentRecordReferenceState.None,
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        public static GameContentRecordReferenceValue Resolved(
            GameContentRecordKey targetKey,
            string targetDisplayName = null,
            string targetSourceLabel = null)
        {
            if (targetKey == null || !targetKey.IsValid)
                throw new ArgumentException("A resolved record reference requires a valid canonical target key.", nameof(targetKey));
            return new GameContentRecordReferenceValue(
                GameContentRecordReferenceState.Resolved,
                targetKey,
                targetDisplayName,
                targetSourceLabel,
                string.Empty,
                string.Empty);
        }

        public static GameContentRecordReferenceValue Broken(
            string originalReference,
            string brokenReason,
            GameContentRecordKey targetKey = null)
        {
            if (string.IsNullOrWhiteSpace(originalReference))
                throw new ArgumentException("A broken record reference requires its original display value.", nameof(originalReference));
            if (string.IsNullOrWhiteSpace(brokenReason))
                throw new ArgumentException("A broken record reference requires an actionable reason.", nameof(brokenReason));
            return new GameContentRecordReferenceValue(
                GameContentRecordReferenceState.Broken,
                targetKey,
                string.Empty,
                string.Empty,
                originalReference,
                brokenReason);
        }

        public string ToDisplayString()
        {
            if (IsNone) return "None";
            if (IsBroken) return "Broken: " + OriginalReference;
            if (!string.IsNullOrWhiteSpace(TargetDisplayName)) return TargetDisplayName;
            return TargetKey == null ? string.Empty : TargetKey.SourceRecordId;
        }

        public bool Equals(GameContentRecordReferenceValue other)
        {
            if (other == null || State != other.State) return false;
            switch (State)
            {
                case GameContentRecordReferenceState.None:
                    return true;
                case GameContentRecordReferenceState.Resolved:
                    return Equals(TargetKey, other.TargetKey);
                default:
                    return Equals(TargetKey, other.TargetKey) &&
                           string.Equals(OriginalReference, other.OriginalReference, StringComparison.Ordinal) &&
                           string.Equals(BrokenReason, other.BrokenReason, StringComparison.Ordinal);
            }
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentRecordReferenceValue);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)State;
                hash = (hash * 397) ^ (TargetKey == null ? 0 : TargetKey.GetHashCode());
                if (IsBroken)
                {
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(OriginalReference);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(BrokenReason);
                }
                return hash;
            }
        }

        public override string ToString()
        {
            return ToDisplayString();
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public sealed class GameContentRecordReferenceFieldDescriptor
    {
        public GameContentRecordReferenceFieldDescriptor(
            string targetLabel,
            IEnumerable<GameContentRecordCapability> requiredCapabilities,
            GameContentReferencePackPolicy packPolicy = GameContentReferencePackPolicy.SameSelectedPack,
            GameContentReferenceRuntimeImpact runtimeImpact = GameContentReferenceRuntimeImpact.None,
            bool allowClear = true)
        {
            TargetLabel = string.IsNullOrWhiteSpace(targetLabel) ? "Record" : targetLabel.Trim();
            RequiredCapabilities = requiredCapabilities == null
                ? Array.Empty<GameContentRecordCapability>()
                : requiredCapabilities.Where(value => value.IsValid).Distinct().ToArray();
            PackPolicy = packPolicy;
            RuntimeImpact = runtimeImpact;
            AllowClear = allowClear;
        }

        public string TargetLabel { get; }
        public IReadOnlyList<GameContentRecordCapability> RequiredCapabilities { get; }
        public GameContentReferencePackPolicy PackPolicy { get; }
        public GameContentReferenceRuntimeImpact RuntimeImpact { get; }
        public bool AllowClear { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(TargetLabel) &&
                               Enum.IsDefined(typeof(GameContentReferencePackPolicy), PackPolicy);
    }

    public sealed class GameContentReferenceEvaluation
    {
        public GameContentReferenceEvaluation(
            bool valid,
            string reason,
            GameContentRecordKey resolvedTargetKey,
            bool requiredCapabilitiesSatisfied,
            bool samePackPolicySatisfied,
            bool sourceClaimValid,
            bool providerCompatibilitySatisfied,
            GameContentEditValidationState validationState,
            GameContentReferenceRuntimeImpact runtimeImpact = GameContentReferenceRuntimeImpact.None)
        {
            ResolvedTargetKey = resolvedTargetKey;
            RequiredCapabilitiesSatisfied = requiredCapabilitiesSatisfied;
            SamePackPolicySatisfied = samePackPolicySatisfied;
            SourceClaimValid = sourceClaimValid;
            ProviderCompatibilitySatisfied = providerCompatibilitySatisfied;
            ValidationState = validationState;
            RuntimeImpact = runtimeImpact;
            IsValid = valid && resolvedTargetKey != null && resolvedTargetKey.IsValid &&
                      requiredCapabilitiesSatisfied && samePackPolicySatisfied && sourceClaimValid &&
                      providerCompatibilitySatisfied && validationState != GameContentEditValidationState.Invalid;
            Reason = IsValid ? Normalize(reason) : Normalize(reason, "The target is not compatible with this reference.");
        }

        public bool IsValid { get; }
        public string Reason { get; }
        public GameContentRecordKey ResolvedTargetKey { get; }
        public bool RequiredCapabilitiesSatisfied { get; }
        public bool SamePackPolicySatisfied { get; }
        public bool SourceClaimValid { get; }
        public bool ProviderCompatibilitySatisfied { get; }
        public GameContentEditValidationState ValidationState { get; }
        public GameContentReferenceRuntimeImpact RuntimeImpact { get; }

        public static GameContentReferenceEvaluation Approved(
            GameContentRecordKey targetKey,
            GameContentReferenceRuntimeImpact runtimeImpact = GameContentReferenceRuntimeImpact.None,
            GameContentEditValidationState validationState = GameContentEditValidationState.Valid,
            string reason = null)
        {
            return new GameContentReferenceEvaluation(
                true,
                reason,
                targetKey,
                true,
                true,
                true,
                true,
                validationState,
                runtimeImpact);
        }

        public static GameContentReferenceEvaluation Rejected(
            GameContentRecordKey targetKey,
            string reason,
            bool requiredCapabilitiesSatisfied = true,
            bool samePackPolicySatisfied = true,
            bool sourceClaimValid = true,
            bool providerCompatibilitySatisfied = false,
            GameContentEditValidationState validationState = GameContentEditValidationState.Invalid)
        {
            return new GameContentReferenceEvaluation(
                false,
                reason,
                targetKey,
                requiredCapabilitiesSatisfied,
                samePackPolicySatisfied,
                sourceClaimValid,
                providerCompatibilitySatisfied,
                validationState);
        }

        internal GameContentReferenceEvaluation WithRuntimeImpact(GameContentReferenceRuntimeImpact runtimeImpact)
        {
            return new GameContentReferenceEvaluation(
                IsValid,
                Reason,
                ResolvedTargetKey,
                RequiredCapabilitiesSatisfied,
                SamePackPolicySatisfied,
                SourceClaimValid,
                ProviderCompatibilitySatisfied,
                ValidationState,
                RuntimeImpact | runtimeImpact);
        }

        private static string Normalize(string value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    public sealed class GameContentReferenceCandidate
    {
        public GameContentReferenceCandidate(
            GameContentRecordDescriptor record,
            GameContentReferenceEvaluation evaluation)
        {
            Record = record;
            Evaluation = evaluation;
        }

        public GameContentRecordDescriptor Record { get; }
        public GameContentReferenceEvaluation Evaluation { get; }
    }

    public sealed class GameContentReferenceCandidateRejection
    {
        public GameContentReferenceCandidateRejection(GameContentRecordKey targetKey, string reason)
        {
            TargetKey = targetKey;
            Reason = string.IsNullOrWhiteSpace(reason) ? "The target was rejected." : reason.Trim();
        }

        public GameContentRecordKey TargetKey { get; }
        public string Reason { get; }
    }

    public sealed class GameContentReferenceCandidateSet
    {
        public GameContentReferenceCandidateSet(
            string fieldId,
            IEnumerable<GameContentReferenceCandidate> candidates,
            IEnumerable<GameContentReferenceCandidateRejection> rejections,
            string message = null)
        {
            FieldId = string.IsNullOrWhiteSpace(fieldId) ? string.Empty : fieldId.Trim();
            Candidates = candidates == null
                ? Array.Empty<GameContentReferenceCandidate>()
                : candidates.Where(value => value != null && value.Record != null && value.Evaluation != null).ToArray();
            Rejections = rejections == null
                ? Array.Empty<GameContentReferenceCandidateRejection>()
                : rejections.Where(value => value != null).ToArray();
            Message = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        }

        public string FieldId { get; }
        public IReadOnlyList<GameContentReferenceCandidate> Candidates { get; }
        public IReadOnlyList<GameContentReferenceCandidateRejection> Rejections { get; }
        public string Message { get; }
    }

    public sealed class GameContentReferenceChangeReview
    {
        public GameContentReferenceChangeReview(
            GameContentRecordKey sourceRecordKey,
            string fieldId,
            GameContentRecordReferenceValue oldValue,
            GameContentRecordReferenceValue newValue,
            GameContentRecordDescriptor oldTarget,
            GameContentRecordDescriptor newTarget,
            int sourceInboundReferenceCount,
            int oldTargetInboundDelta,
            int newTargetInboundDelta,
            GameContentReferenceRuntimeImpact runtimeImpact)
        {
            SourceRecordKey = sourceRecordKey;
            FieldId = string.IsNullOrWhiteSpace(fieldId) ? string.Empty : fieldId.Trim();
            OldValue = oldValue;
            NewValue = newValue;
            OldTarget = oldTarget;
            NewTarget = newTarget;
            SourceInboundReferenceCount = Math.Max(0, sourceInboundReferenceCount);
            OldTargetInboundDelta = oldTargetInboundDelta;
            NewTargetInboundDelta = newTargetInboundDelta;
            RuntimeImpact = runtimeImpact;
        }

        public GameContentRecordKey SourceRecordKey { get; }
        public string FieldId { get; }
        public GameContentRecordReferenceValue OldValue { get; }
        public GameContentRecordReferenceValue NewValue { get; }
        public GameContentRecordDescriptor OldTarget { get; }
        public GameContentRecordDescriptor NewTarget { get; }
        public int SourceInboundReferenceCount { get; }
        public int OldTargetInboundDelta { get; }
        public int NewTargetInboundDelta { get; }
        public GameContentReferenceRuntimeImpact RuntimeImpact { get; }
    }
}
