using System;
using System.Collections.Generic;
using System.Linq;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentRecordKey : IEquatable<GameContentRecordKey>
    {
        public GameContentRecordKey(
            string owningPackageId,
            string packId,
            string sourceRecordId,
            string sourceId = null,
            string logicalLocator = null)
        {
            OwningPackageId = Normalize(owningPackageId);
            PackId = Normalize(packId);
            SourceRecordId = Normalize(sourceRecordId);
            SourceId = Normalize(sourceId);
            LogicalLocator = Normalize(logicalLocator);
        }

        public string OwningPackageId { get; }
        public string PackId { get; }
        public string SourceRecordId { get; }
        public string SourceId { get; }
        public string LogicalLocator { get; }
        public string StableKey => BuildStableKey(OwningPackageId, PackId, SourceRecordId, SourceId);
        public bool IsValid => !string.IsNullOrWhiteSpace(OwningPackageId) &&
                               !string.IsNullOrWhiteSpace(PackId) &&
                               !string.IsNullOrWhiteSpace(SourceRecordId);

        public bool Equals(GameContentRecordKey other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(OwningPackageId, other.OwningPackageId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(PackId, other.PackId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(SourceRecordId, other.SourceRecordId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(SourceId, other.SourceId, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as GameContentRecordKey);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(StableKey);
        }

        public override string ToString()
        {
            return StableKey;
        }

        public static string BuildStableKey(
            string owningPackageId,
            string packId,
            string sourceRecordId,
            string sourceId = null)
        {
            string prefix = Normalize(owningPackageId).ToLowerInvariant() + "::" +
                            Normalize(packId).ToLowerInvariant() + "::";
            string normalizedSource = Normalize(sourceId).ToLowerInvariant();
            return string.IsNullOrWhiteSpace(normalizedSource)
                ? prefix + Normalize(sourceRecordId).ToLowerInvariant()
                : prefix + normalizedSource + "::" + Normalize(sourceRecordId).ToLowerInvariant();
        }

        public static GameContentRecordKey FromLegacy(string packScopedId, string sourceRecordId)
        {
            string normalized = Normalize(packScopedId);
            int separator = normalized.IndexOf("::", StringComparison.Ordinal);
            string packId = separator > 0 ? normalized.Substring(0, separator) : "project-content";
            return new GameContentRecordKey("legacy-project", packId, sourceRecordId);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public readonly struct GameContentRecordCapability : IEquatable<GameContentRecordCapability>
    {
        public GameContentRecordCapability(string capabilityId)
        {
            Id = Normalize(capabilityId);
        }

        public string Id { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Id);

        public bool Equals(GameContentRecordCapability other)
        {
            return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is GameContentRecordCapability other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Id ?? string.Empty);
        }

        public override string ToString()
        {
            return Id ?? string.Empty;
        }

        public static bool operator ==(GameContentRecordCapability left, GameContentRecordCapability right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GameContentRecordCapability left, GameContentRecordCapability right)
        {
            return !left.Equals(right);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }
    }

    public static class GameContentRecordCapabilities
    {
        public static readonly GameContentRecordCapability Attack = new GameContentRecordCapability("attack");
        public static readonly GameContentRecordCapability Enemy = new GameContentRecordCapability("enemy");
        public static readonly GameContentRecordCapability Encounter = new GameContentRecordCapability("encounter");
        public static readonly GameContentRecordCapability Wave = new GameContentRecordCapability("wave");
        public static readonly GameContentRecordCapability TimedMilestone = new GameContentRecordCapability("timed-milestone");
        public static readonly GameContentRecordCapability HordeEvent = new GameContentRecordCapability("horde-event");
        public static readonly GameContentRecordCapability EliteEvent = new GameContentRecordCapability("elite-event");
        public static readonly GameContentRecordCapability BossEvent = new GameContentRecordCapability("boss-event");
        public static readonly GameContentRecordCapability RunProfile = new GameContentRecordCapability("run-profile");
        public static readonly GameContentRecordCapability Weapon = new GameContentRecordCapability("weapon");
        public static readonly GameContentRecordCapability Tower = new GameContentRecordCapability("tower");
        public static readonly GameContentRecordCapability Upgrade = new GameContentRecordCapability("upgrade");
        public static readonly GameContentRecordCapability WeaponUpgrade = new GameContentRecordCapability("weapon-upgrade");
        public static readonly GameContentRecordCapability Passive = new GameContentRecordCapability("passive");
        public static readonly GameContentRecordCapability PickupMagnet = new GameContentRecordCapability("pickup-magnet");
        public static readonly GameContentRecordCapability Mutation = new GameContentRecordCapability("mutation");
        public static readonly GameContentRecordCapability Evolution = new GameContentRecordCapability("evolution");
        public static readonly GameContentRecordCapability MetaUpgrade = new GameContentRecordCapability("meta-upgrade");
        public static readonly GameContentRecordCapability Projectile = new GameContentRecordCapability("projectile");
        public static readonly GameContentRecordCapability Reward = new GameContentRecordCapability("reward");
        public static readonly GameContentRecordCapability Theme = new GameContentRecordCapability("theme");
        public static readonly GameContentRecordCapability Elite = new GameContentRecordCapability("elite");
        public static readonly GameContentRecordCapability MajorThreat = new GameContentRecordCapability("major-threat");
        public static readonly GameContentRecordCapability Miniboss = new GameContentRecordCapability("miniboss");
        public static readonly GameContentRecordCapability Boss = new GameContentRecordCapability("boss");
    }

    public sealed class GameContentLensDescriptor
    {
        public GameContentLensDescriptor(
            string lensId,
            string displayName,
            string groupName,
            string iconOrStyleToken,
            int sortOrder,
            IEnumerable<GameContentRecordCapability> supportedCapabilities,
            bool matchesAllRecords = false)
        {
            LensId = Normalize(lensId);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? LensId : displayName.Trim();
            GroupName = string.IsNullOrWhiteSpace(groupName) ? "Other" : groupName.Trim();
            IconOrStyleToken = Normalize(iconOrStyleToken);
            SortOrder = sortOrder;
            SupportedCapabilities = supportedCapabilities == null
                ? Array.Empty<GameContentRecordCapability>()
                : supportedCapabilities.Where(value => value.IsValid).Distinct().ToArray();
            MatchesAllRecords = matchesAllRecords;
        }

        public string LensId { get; }
        public string DisplayName { get; }
        public string GroupName { get; }
        public string IconOrStyleToken { get; }
        public int SortOrder { get; }
        public IReadOnlyList<GameContentRecordCapability> SupportedCapabilities { get; }
        public bool MatchesAllRecords { get; }

        public bool Matches(GameContentRecordDescriptor record)
        {
            if (record == null) return false;
            return MatchesAllRecords || SupportedCapabilities.Any(record.HasCapability);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    public interface IGameContentAuthoringLensProvider
    {
        GameContentLensDescriptor Lens { get; }
    }

    public interface IGameContentAuthoringProviderVisibility
    {
        bool VisibleInNavigation { get; }
    }

    public interface IGameContentRecordProjectionAdapter<TProjection> where TProjection : class
    {
        string AdapterId { get; }
        int SortOrder { get; }
        bool TryProject(GameContentRecordDescriptor record, out TProjection projection);
    }

    public static class GameContentRecordProjectionRegistry<TProjection> where TProjection : class
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<IGameContentRecordProjectionAdapter<TProjection>> AdaptersInternal =
            new List<IGameContentRecordProjectionAdapter<TProjection>>();

        public static IReadOnlyList<IGameContentRecordProjectionAdapter<TProjection>> Adapters
        {
            get
            {
                lock (SyncRoot) return AdaptersInternal.ToArray();
            }
        }

        public static bool Register(IGameContentRecordProjectionAdapter<TProjection> adapter)
        {
            if (adapter == null || string.IsNullOrWhiteSpace(adapter.AdapterId)) return false;
            lock (SyncRoot)
            {
                if (AdaptersInternal.Any(existing => string.Equals(
                        existing.AdapterId,
                        adapter.AdapterId,
                        StringComparison.OrdinalIgnoreCase)))
                    return false;

                AdaptersInternal.Add(adapter);
                AdaptersInternal.Sort((left, right) =>
                {
                    int order = left.SortOrder.CompareTo(right.SortOrder);
                    return order != 0
                        ? order
                        : string.Compare(left.AdapterId, right.AdapterId, StringComparison.OrdinalIgnoreCase);
                });
                return true;
            }
        }

        public static bool Unregister(string adapterId)
        {
            if (string.IsNullOrWhiteSpace(adapterId)) return false;
            lock (SyncRoot)
            {
                int index = AdaptersInternal.FindIndex(existing => string.Equals(
                    existing.AdapterId,
                    adapterId,
                    StringComparison.OrdinalIgnoreCase));
                if (index < 0) return false;
                AdaptersInternal.RemoveAt(index);
                return true;
            }
        }

        public static bool TryProject(GameContentRecordDescriptor record, out TProjection projection)
        {
            IGameContentRecordProjectionAdapter<TProjection>[] adapters;
            lock (SyncRoot) adapters = AdaptersInternal.ToArray();
            for (int i = 0; i < adapters.Length; i++)
                if (adapters[i].TryProject(record, out projection) && projection != null)
                    return true;

            projection = null;
            return false;
        }
    }
}
