using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal sealed class GameContentReferenceDropdown : AdvancedDropdown
    {
        private readonly string _targetLabel;
        private readonly GameContentReferenceCandidateSet _targets;
        private readonly bool _allowNone;
        private readonly Action<GameContentRecordReferenceValue> _selected;

        public GameContentReferenceDropdown(
            string targetLabel,
            GameContentReferenceCandidateSet targets,
            bool allowNone,
            Action<GameContentRecordReferenceValue> selected)
            : base(new AdvancedDropdownState())
        {
            _targetLabel = string.IsNullOrWhiteSpace(targetLabel) ? "Record" : targetLabel.Trim();
            _targets = targets ?? new GameContentReferenceCandidateSet(string.Empty, null, null);
            _allowNone = allowNone;
            _selected = selected;
            minimumSize = new Vector2(520f, 280f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Select " + _targetLabel);
            if (_allowNone)
                root.AddChild(new GameContentReferenceDropdownItem("None", null, true));
            for (int i = 0; i < _targets.Candidates.Count; i++)
            {
                GameContentReferenceCandidate candidate = _targets.Candidates[i];
                root.AddChild(new GameContentReferenceDropdownItem(
                    BuildCandidateLabel(candidate),
                    candidate,
                    false));
            }
            if (_targets.Candidates.Count == 0)
            {
                string message = string.IsNullOrWhiteSpace(_targets.Message)
                    ? "No compatible targets"
                    : _targets.Message;
                root.AddChild(new GameContentReferenceDropdownItem(message, null, false));
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (!(item is GameContentReferenceDropdownItem referenceItem)) return;
            if (referenceItem.IsNone)
            {
                _selected?.Invoke(GameContentRecordReferenceValue.None());
                return;
            }
            if (referenceItem.Candidate?.Record?.CanonicalKey == null) return;
            GameContentRecordDescriptor record = referenceItem.Candidate.Record;
            _selected?.Invoke(GameContentRecordReferenceValue.Resolved(
                record.CanonicalKey,
                record.DisplayName,
                record.SourcePath));
        }

        private static string BuildCandidateLabel(GameContentReferenceCandidate candidate)
        {
            GameContentRecordDescriptor record = candidate.Record;
            string capabilities = string.Join(",", record.Capabilities.Select(value => value.Id).ToArray());
            string validation = candidate.Evaluation.ValidationState.ToString();
            string source = string.IsNullOrWhiteSpace(record.SourcePath) ? string.Empty : " | " + record.SourcePath;
            return record.DisplayName + " | " + record.CanonicalKey.SourceRecordId + " | " +
                   record.CanonicalKey.PackId + " | " + capabilities + " | " + validation + source;
        }
    }

    internal sealed class GameContentReferenceDropdownItem : AdvancedDropdownItem
    {
        public GameContentReferenceDropdownItem(
            string name,
            GameContentReferenceCandidate candidate,
            bool isNone)
            : base(name)
        {
            Candidate = candidate;
            IsNone = isNone;
        }

        public GameContentReferenceCandidate Candidate { get; }
        public bool IsNone { get; }
    }

}
