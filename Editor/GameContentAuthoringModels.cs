using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public enum GameContentAuthoringValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public sealed class GameContentAuthoringValidationIssue
    {
        public GameContentAuthoringValidationIssue(GameContentAuthoringValidationSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public GameContentAuthoringValidationSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public static GameContentAuthoringValidationIssue Info(string path, string message)
        {
            return new GameContentAuthoringValidationIssue(GameContentAuthoringValidationSeverity.Info, path, message);
        }

        public static GameContentAuthoringValidationIssue Warning(string path, string message)
        {
            return new GameContentAuthoringValidationIssue(GameContentAuthoringValidationSeverity.Warning, path, message);
        }

        public static GameContentAuthoringValidationIssue Error(string path, string message)
        {
            return new GameContentAuthoringValidationIssue(GameContentAuthoringValidationSeverity.Error, path, message);
        }
    }

    public sealed class GameContentAuthoringValidationResult
    {
        private readonly GameContentAuthoringValidationIssue[] _issues;

        public GameContentAuthoringValidationResult(IReadOnlyList<GameContentAuthoringValidationIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                _issues = Array.Empty<GameContentAuthoringValidationIssue>();
                return;
            }

            var copy = new GameContentAuthoringValidationIssue[issues.Count];
            for (int i = 0; i < issues.Count; i++)
            {
                copy[i] = issues[i] ?? GameContentAuthoringValidationIssue.Error(string.Empty, "Unknown validation issue.");
                if (copy[i].Severity == GameContentAuthoringValidationSeverity.Error) ErrorCount++;
                else if (copy[i].Severity == GameContentAuthoringValidationSeverity.Warning) WarningCount++;
                else if (copy[i].Severity == GameContentAuthoringValidationSeverity.Info) InfoCount++;
            }

            _issues = copy;
        }

        public IReadOnlyList<GameContentAuthoringValidationIssue> Issues => _issues;
        public int ErrorCount { get; }
        public int WarningCount { get; }
        public int InfoCount { get; }
        public bool IsValid => ErrorCount == 0;
        public static GameContentAuthoringValidationResult Valid { get; } = new GameContentAuthoringValidationResult(Array.Empty<GameContentAuthoringValidationIssue>());
    }

    public sealed class GameContentCreationResult
    {
        public GameContentCreationResult(bool succeeded, string message, UnityEngine.Object createdRoot)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            CreatedRoot = createdRoot;
        }

        public bool Succeeded { get; }
        public string Message { get; }
        public UnityEngine.Object CreatedRoot { get; }
    }
}
