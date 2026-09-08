using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;


namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentLibraryDomainValidation
    {
        internal static void AddDomainValidatorIssues(GameContentLibraryItem item)
        {
            Type validatorType = FindValidatorType(item.Asset.GetType());
            if (validatorType == null) return;

            MethodInfo validateMethod = validatorType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => string.Equals(method.Name, "Validate", StringComparison.Ordinal) && HasSingleAssignableParameter(method, item.Asset.GetType()));
            if (validateMethod == null) return;

            try
            {
                object result = validateMethod.Invoke(null, new[] { item.Asset });
                AddIssuesFromValidationResult(item, result);
            }
            catch (Exception ex)
            {
                item.AddIssue(GameContentLibraryIssue.Warning("Domain Validator", "Could not run domain validator: " + ex.GetBaseException().Message));
            }
        }

        internal static Type FindValidatorType(Type assetType)
        {
            string[] validatorNames =
            {
                assetType.Namespace + ".AttackRecipeValidator",
                assetType.Namespace + ".EnemyDefinitionValidator",
                assetType.Namespace + ".WaveDefinitionValidator",
                assetType.Namespace + ".WeaponDefinitionValidator",
                assetType.Namespace + ".RunUpgradeDefinitionValidator",
                assetType.Namespace + ".GameContentSetValidator",
                assetType.Namespace + ".GameContentPackValidator"
            };

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < validatorNames.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(validatorNames[i])) continue;
                for (int j = 0; j < assemblies.Length; j++)
                {
                    Type type = assemblies[j].GetType(validatorNames[i], false);
                    if (type != null && HasApplicableValidateMethod(type, assetType)) return type;
                }
            }

            return null;
        }

        internal static bool HasApplicableValidateMethod(Type validatorType, Type assetType)
        {
            MethodInfo method = validatorType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, "Validate", StringComparison.Ordinal) && HasSingleAssignableParameter(candidate, assetType));
            return method != null;
        }

        internal static bool HasSingleAssignableParameter(MethodInfo method, Type assetType)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(assetType);
        }

        internal static void AddIssuesFromValidationResult(GameContentLibraryItem item, object result)
        {
            if (result == null) return;
            object issues = GameContentLibraryMemberAccess.ReadMemberValue(result, "Issues");
            if (!(issues is IEnumerable enumerable)) return;

            foreach (object issue in enumerable)
            {
                if (issue == null) continue;
                string path = GameContentLibraryMemberAccess.ReadStringMember(issue, "Path", "Domain Validator");
                string message = GameContentLibraryMemberAccess.ReadStringMember(issue, "Message", "Validation issue.");
                object severityValue = GameContentLibraryMemberAccess.ReadMemberValue(issue, "Severity");
                GameContentAuthoringValidationSeverity severity = ParseSeverity(severityValue);
                item.AddIssue(new GameContentLibraryIssue(severity, path, message));
            }
        }

        internal static GameContentAuthoringValidationSeverity ParseSeverity(object severityValue)
        {
            if (severityValue == null) return GameContentAuthoringValidationSeverity.Warning;
            string value = severityValue.ToString();
            if (string.Equals(value, "Error", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Blocker", StringComparison.OrdinalIgnoreCase))
                return GameContentAuthoringValidationSeverity.Error;
            if (string.Equals(value, "Info", StringComparison.OrdinalIgnoreCase))
                return GameContentAuthoringValidationSeverity.Info;
            return GameContentAuthoringValidationSeverity.Warning;
        }
    }
}
