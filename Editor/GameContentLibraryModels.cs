using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentLibraryReport
    {
        private readonly List<GameContentLibraryGroup> _groups = new List<GameContentLibraryGroup>();
        private readonly List<GameContentLibraryContentSetSummary> _contentSetSummaries = new List<GameContentLibraryContentSetSummary>();

        internal GameContentLibraryReport(string rootPath, IReadOnlyList<GameContentLibraryItem> items, IReadOnlyList<GameContentLibraryIssue> reportIssues)
        {
            RootPath = rootPath ?? string.Empty;
            Items = items == null ? Array.Empty<GameContentLibraryItem>() : items.ToArray();
            ReportIssues = reportIssues == null ? Array.Empty<GameContentLibraryIssue>() : reportIssues.ToArray();
        }

        public string RootPath { get; }
        public IReadOnlyList<GameContentLibraryItem> Items { get; }
        public IReadOnlyList<GameContentLibraryIssue> ReportIssues { get; }
        public IReadOnlyList<GameContentLibraryGroup> Groups => _groups;
        public IReadOnlyList<GameContentLibraryContentSetSummary> ContentSetSummaries => _contentSetSummaries;
        public IEnumerable<GameContentLibraryIssue> AllIssues => ReportIssues.Concat(Items.SelectMany(item => item.Issues));
        public int BlockerCount => AllIssues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error);
        public int WarningCount => AllIssues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning);
        public int InfoCount => AllIssues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Info);
        public int ReadyContentSetCount => _contentSetSummaries.Count(summary => summary.Ready);

        public GameContentAuthoringValidationResult ToValidationResult()
        {
            return new GameContentAuthoringValidationResult(AllIssues
                .Select(issue => new GameContentAuthoringValidationIssue(issue.Severity, issue.Path, issue.Message))
                .ToArray());
        }

        public GameContentLibraryContentSetSummary GetContentSetSummary(GameContentLibraryItem item)
        {
            if (item == null) return null;
            return _contentSetSummaries.FirstOrDefault(summary => ReferenceEquals(summary.Item, item));
        }

        internal void RebuildGroups(IReadOnlyList<GameContentLibraryTypeInfo> knownTypes)
        {
            _groups.Clear();
            HashSet<GameContentLibraryKind> added = new HashSet<GameContentLibraryKind>();
            for (int i = 0; i < knownTypes.Count; i++)
            {
                GameContentLibraryTypeInfo typeInfo = knownTypes[i];
                if (!added.Add(typeInfo.Kind)) continue;
                _groups.Add(new GameContentLibraryGroup(typeInfo.Category, Items.Where(item => item.Kind == typeInfo.Kind).ToArray()));
            }
        }

        internal void RebuildContentSetSummaries()
        {
            _contentSetSummaries.Clear();
            foreach (GameContentLibraryItem contentSet in Items.Where(item => item.Kind == GameContentLibraryKind.ContentSet))
            {
                HashSet<GameContentLibraryItem> membership = GameContentLibraryService.GetContentSetMembership(contentSet);
                int weaponCount = membership.Count(item => item.Kind == GameContentLibraryKind.Weapon);
                int enemyCount = membership.Count(item => item.Kind == GameContentLibraryKind.Enemy);
                int waveCount = membership.Count(item => item.Kind == GameContentLibraryKind.Wave);
                int upgradeCount = membership.Count(item => item.Kind == GameContentLibraryKind.Upgrade);
                bool ready = contentSet.ErrorCount == 0 && weaponCount > 0 && enemyCount > 0 && waveCount > 0;
                string message = ready
                    ? "Ready to play: all required authored content is connected."
                    : "Not ready: resolve blocker issues or add required authored content.";
                _contentSetSummaries.Add(new GameContentLibraryContentSetSummary(contentSet, ready, message, weaponCount, enemyCount, waveCount, upgradeCount));
            }
        }
    }

    public sealed class GameContentLibraryItem
    {
        private readonly List<GameContentLibraryIssue> _issues = new List<GameContentLibraryIssue>();
        private readonly List<GameContentLibraryReference> _directReferences = new List<GameContentLibraryReference>();
        private readonly List<GameContentLibraryReference> _reverseReferences = new List<GameContentLibraryReference>();

        internal GameContentLibraryItem(string key, UnityEngine.Object asset, GameContentLibraryKind kind, string category, string path, string id, string displayName)
        {
            Key = key ?? string.Empty;
            Asset = asset;
            Kind = kind;
            Category = category ?? string.Empty;
            Path = path ?? string.Empty;
            string folder = string.IsNullOrWhiteSpace(Path) ? string.Empty : System.IO.Path.GetDirectoryName(Path);
            Folder = string.IsNullOrWhiteSpace(folder) ? "Assets" : folder.Replace("\\", "/");
            Id = id ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? asset != null ? asset.name : "(unnamed)" : displayName;
        }

        public string Key { get; }
        public UnityEngine.Object Asset { get; }
        public GameContentLibraryKind Kind { get; }
        public string Category { get; }
        public string Path { get; }
        public string Folder { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<GameContentLibraryIssue> Issues => _issues;
        public IReadOnlyList<GameContentLibraryReference> DirectReferences => _directReferences;
        public IReadOnlyList<GameContentLibraryReference> ReverseReferences => _reverseReferences;
        public int ErrorCount => _issues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error);
        public int WarningCount => _issues.Count(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning);
        public string IdAndPathLabel => (string.IsNullOrWhiteSpace(Id) ? "(missing id)" : Id) + " - " + Path;
        public string ValidationLabel => ErrorCount > 0 ? ErrorCount.ToString(CultureInfo.InvariantCulture) + " blocker(s)" : WarningCount > 0 ? WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s)" : "Ready";

        internal void AddIssue(GameContentLibraryIssue issue)
        {
            if (issue != null) _issues.Add(issue);
        }

        internal void AddDirectReference(GameContentLibraryReference reference)
        {
            if (reference == null || reference.Target == null) return;
            if (_directReferences.Any(existing => ReferenceEquals(existing.Target, reference.Target) && string.Equals(existing.PropertyPath, reference.PropertyPath, StringComparison.Ordinal)))
                return;
            _directReferences.Add(reference);
        }

        internal void AddReverseReference(GameContentLibraryReference reference)
        {
            if (reference == null || reference.Target == null) return;
            if (_reverseReferences.Any(existing => ReferenceEquals(existing.Target, reference.Target) && string.Equals(existing.PropertyPath, reference.PropertyPath, StringComparison.Ordinal)))
                return;
            _reverseReferences.Add(reference);
        }
    }

    public sealed class GameContentLibraryReference
    {
        public GameContentLibraryReference(GameContentLibraryItem target, string propertyPath)
        {
            Target = target;
            PropertyPath = propertyPath ?? string.Empty;
        }

        public GameContentLibraryItem Target { get; }
        public string PropertyPath { get; }
    }

    public sealed class GameContentLibraryIssue
    {
        public GameContentLibraryIssue(GameContentAuthoringValidationSeverity severity, string path, string message)
        {
            Severity = severity;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public GameContentAuthoringValidationSeverity Severity { get; }
        public string Path { get; }
        public string Message { get; }

        public static GameContentLibraryIssue Info(string path, string message)
        {
            return new GameContentLibraryIssue(GameContentAuthoringValidationSeverity.Info, path, message);
        }

        public static GameContentLibraryIssue Warning(string path, string message)
        {
            return new GameContentLibraryIssue(GameContentAuthoringValidationSeverity.Warning, path, message);
        }

        public static GameContentLibraryIssue Error(string path, string message)
        {
            return new GameContentLibraryIssue(GameContentAuthoringValidationSeverity.Error, path, message);
        }
    }

    public sealed class GameContentLibraryGroup
    {
        public GameContentLibraryGroup(string name, IReadOnlyList<GameContentLibraryItem> items)
        {
            Name = name ?? string.Empty;
            Items = items == null ? Array.Empty<GameContentLibraryItem>() : items.ToArray();
        }

        public string Name { get; }
        public IReadOnlyList<GameContentLibraryItem> Items { get; }
    }

    public sealed class GameContentLibraryContentSetSummary
    {
        public GameContentLibraryContentSetSummary(GameContentLibraryItem item, bool ready, string message, int weaponCount, int enemyCount, int waveCount, int upgradeCount)
        {
            Item = item;
            Ready = ready;
            Message = message ?? string.Empty;
            WeaponCount = weaponCount;
            EnemyCount = enemyCount;
            WaveCount = waveCount;
            UpgradeCount = upgradeCount;
        }

        public GameContentLibraryItem Item { get; }
        public bool Ready { get; }
        public string Message { get; }
        public int WeaponCount { get; }
        public int EnemyCount { get; }
        public int WaveCount { get; }
        public int UpgradeCount { get; }
    }

    public enum GameContentLibraryKind
    {
        Attack = 0,
        Enemy = 1,
        Wave = 2,
        Weapon = 3,
        Upgrade = 4,
        ContentSet = 5
    }

    internal sealed class GameContentLibraryTypeInfo
    {
        public GameContentLibraryTypeInfo(string typeName, GameContentLibraryKind kind, string category)
        {
            TypeName = typeName;
            Kind = kind;
            Category = category;
        }

        public string TypeName { get; }
        public GameContentLibraryKind Kind { get; }
        public string Category { get; }
    }
}
