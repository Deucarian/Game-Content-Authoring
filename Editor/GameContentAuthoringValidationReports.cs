using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Deucarian.GameplayFoundation;

namespace Deucarian.GameContentAuthoring.Editor
{
    public static class GameContentAuthoringValidationReports
    {
        public static GameContentAuthoringValidationResult ToAuthoringResult(ContentValidationReport report)
        {
            if (report == null || report.Issues.Count == 0)
            {
                return GameContentAuthoringValidationResult.Valid;
            }

            var issues = new GameContentAuthoringValidationIssue[report.Issues.Count];
            for (int index = 0; index < report.Issues.Count; index++)
            {
                ContentValidationIssue issue = report.Issues[index];
                issues[index] = new GameContentAuthoringValidationIssue(
                    ToAuthoringSeverity(issue.Severity),
                    issue.Path,
                    issue.Message);
            }

            return new GameContentAuthoringValidationResult(issues);
        }

        public static IReadOnlyList<GameContentAuthoringValidationIssue> GetIssues(
            ContentValidationReport report,
            GameContentAuthoringValidationSeverity severity)
        {
            return ToAuthoringResult(report)
                .Issues
                .Where(issue => issue.Severity == severity)
                .ToArray();
        }

        public static string BuildSummary(ContentValidationReport report, string validMessage = "No validation issues found.")
        {
            GameContentAuthoringValidationResult result = ToAuthoringResult(report);
            if (result.ErrorCount > 0)
            {
                return result.ErrorCount.ToString(CultureInfo.InvariantCulture) + " error(s), " +
                       result.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s), " +
                       result.InfoCount.ToString(CultureInfo.InvariantCulture) + " info item(s).";
            }

            if (result.WarningCount > 0)
            {
                return result.WarningCount.ToString(CultureInfo.InvariantCulture) + " warning(s), " +
                       result.InfoCount.ToString(CultureInfo.InvariantCulture) + " info item(s).";
            }

            if (result.InfoCount > 0)
            {
                return result.InfoCount.ToString(CultureInfo.InvariantCulture) + " info item(s).";
            }

            return string.IsNullOrWhiteSpace(validMessage) ? "No validation issues found." : validMessage;
        }

        public static string ToMarkdown(ContentValidationReport report, string title = "Content Validation")
        {
            GameContentAuthoringValidationResult result = ToAuthoringResult(report);
            var builder = new StringBuilder();
            builder.AppendLine("# " + (string.IsNullOrWhiteSpace(title) ? "Content Validation" : title.Trim()));
            builder.AppendLine();
            builder.AppendLine("- Errors: " + result.ErrorCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Warnings: " + result.WarningCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Info: " + result.InfoCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("- Total Issues: " + result.Issues.Count.ToString(CultureInfo.InvariantCulture));

            AppendBucket(builder, "Errors", result.Issues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Error));
            AppendBucket(builder, "Warnings", result.Issues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Warning));
            AppendBucket(builder, "Info", result.Issues.Where(issue => issue.Severity == GameContentAuthoringValidationSeverity.Info));
            return builder.ToString();
        }

        public static GameContentAuthoringValidationSeverity ToAuthoringSeverity(ContentValidationSeverity severity)
        {
            if (severity == ContentValidationSeverity.Error)
            {
                return GameContentAuthoringValidationSeverity.Error;
            }

            if (severity == ContentValidationSeverity.Info)
            {
                return GameContentAuthoringValidationSeverity.Info;
            }

            return GameContentAuthoringValidationSeverity.Warning;
        }

        private static void AppendBucket(
            StringBuilder builder,
            string title,
            IEnumerable<GameContentAuthoringValidationIssue> issues)
        {
            GameContentAuthoringValidationIssue[] issueArray = issues.ToArray();
            if (issueArray.Length == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## " + title);
            for (int index = 0; index < issueArray.Length; index++)
            {
                GameContentAuthoringValidationIssue issue = issueArray[index];
                string prefix = string.IsNullOrWhiteSpace(issue.Path) ? string.Empty : issue.Path + ": ";
                builder.AppendLine("- " + prefix + issue.Message);
            }
        }
    }
}
