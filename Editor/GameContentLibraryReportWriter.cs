using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentLibraryReportWriter
    {
        public static string ToMarkdown(GameContentLibraryReport report)
        {
            if (report == null) return string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# Game Content Validation");
            builder.AppendLine();
            builder.AppendLine("- Root: " + report.RootPath);
            builder.AppendLine("- Assets: " + report.Items.Count.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Blockers: " + report.BlockerCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Warnings: " + report.WarningCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Info: " + report.InfoCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();

            foreach (GameContentLibraryContentSetSummary summary in report.ContentSetSummaries)
                builder.AppendLine("- Content Set: " + summary.Item.DisplayName + " - " + summary.Message);
            foreach (GameContentLibraryContentPackSummary summary in report.ContentPackSummaries)
                builder.AppendLine("- Content Pack: " + summary.Item.DisplayName + " - " + summary.Message);

            AppendIssues(builder, "Blockers", report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error));
            AppendIssues(builder, "Warnings", report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning));
            AppendIssues(builder, "Info", report.AllIssues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Info));
            return builder.ToString();
        }

        public static string ToContentPackMarkdown(GameContentLibraryReport report, GameContentLibraryItem contentPack)
        {
            if (report == null || contentPack == null) return string.Empty;
            GameContentLibraryContentPackSummary summary = report.GetContentPackSummary(contentPack);
            if (summary == null) return string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# " + contentPack.DisplayName);
            builder.AppendLine();
            builder.AppendLine("- ID: " + contentPack.Id);
            builder.AppendLine("- Ready: " + (summary.Ready ? "Yes" : "No"));
            builder.AppendLine("- Content Sets: " + summary.ContentSetCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Weapons: " + summary.WeaponCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Enemies: " + summary.EnemyCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Waves: " + summary.WaveCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Upgrades: " + summary.UpgradeCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            foreach (string line in BuildDependencyLines(contentPack, 4))
                builder.AppendLine("- " + line);
            return builder.ToString();
        }

        public static string ToContentSetMarkdown(GameContentLibraryReport report, GameContentLibraryItem contentSet)
        {
            if (report == null || contentSet == null) return string.Empty;
            GameContentLibraryContentSetSummary summary = report.GetContentSetSummary(contentSet);
            if (summary == null) return string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# " + contentSet.DisplayName);
            builder.AppendLine();
            builder.AppendLine("- ID: " + contentSet.Id);
            builder.AppendLine("- Ready: " + (summary.Ready ? "Yes" : "No"));
            builder.AppendLine("- Weapons: " + summary.WeaponCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Enemies: " + summary.EnemyCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Waves: " + summary.WaveCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Upgrades: " + summary.UpgradeCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
            foreach (string line in BuildDependencyLines(contentSet, 3))
                builder.AppendLine("- " + line);
            return builder.ToString();
        }

        public static List<string> BuildDependencyLines(GameContentLibraryItem item, int depth)
        {
            List<string> lines = new List<string>();
            if (item == null) return lines;
            BuildDependencyLines(item, depth, 0, new HashSet<GameContentLibraryItem>(), lines);
            return lines;
        }

        private static void BuildDependencyLines(GameContentLibraryItem item, int depth, int indent, HashSet<GameContentLibraryItem> visited, List<string> lines)
        {
            if (item == null || depth < 0) return;
            string prefix = new string(' ', indent * 2);
            lines.Add(prefix + item.Category + " -> " + item.DisplayName);
            if (!visited.Add(item))
            {
                lines.Add(prefix + "  (cycle)");
                return;
            }

            for (int i = 0; i < item.DirectReferences.Count; i++)
                BuildDependencyLines(item.DirectReferences[i].Target, depth - 1, indent + 1, visited, lines);
        }

        private static void AppendIssues(StringBuilder builder, string title, IEnumerable<GameContentLibraryIssue> issues)
        {
            GameContentLibraryIssue[] issueArray = issues.ToArray();
            if (issueArray.Length == 0) return;
            builder.AppendLine();
            builder.AppendLine("## " + title);
            for (int i = 0; i < issueArray.Length; i++)
                builder.AppendLine("- " + issueArray[i].Path + ": " + issueArray[i].Message);
        }
    }
}
