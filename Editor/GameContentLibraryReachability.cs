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
    internal static class GameContentLibraryReachability
    {
        internal static HashSet<GameContentLibraryItem> GetContentSetMembership(GameContentLibraryItem contentSet)
        {
            HashSet<GameContentLibraryItem> membership = new HashSet<GameContentLibraryItem>();
            if (contentSet == null) return membership;
            membership.Add(contentSet);
            for (int i = 0; i < contentSet.DirectReferences.Count; i++)
            {
                GameContentLibraryItem direct = contentSet.DirectReferences[i].Target;
                if (direct == null) continue;
                membership.Add(direct);
                if (direct.Kind != GameContentLibraryKind.Weapon && direct.Kind != GameContentLibraryKind.Wave)
                    continue;
                for (int j = 0; j < direct.DirectReferences.Count; j++)
                    membership.Add(direct.DirectReferences[j].Target);
            }

            return membership;
        }

        internal static HashSet<GameContentLibraryItem> GetContentPackMembership(GameContentLibraryItem contentPack)
        {
            HashSet<GameContentLibraryItem> membership = new HashSet<GameContentLibraryItem>();
            if (contentPack == null) return membership;
            membership.Add(contentPack);
            for (int i = 0; i < contentPack.DirectReferences.Count; i++)
            {
                GameContentLibraryItem direct = contentPack.DirectReferences[i].Target;
                if (direct == null) continue;
                membership.Add(direct);
                if (direct.Kind != GameContentLibraryKind.ContentSet) continue;
                membership.UnionWith(GetContentSetMembership(direct));
            }

            return membership;
        }

        internal static HashSet<GameContentLibraryItem> GetReachableItems(GameContentLibraryItem root, int depth)
        {
            HashSet<GameContentLibraryItem> visited = new HashSet<GameContentLibraryItem>();
            if (root == null) return visited;
            CollectReachable(root, depth, visited);
            return visited;
        }

        internal static void CollectReachable(GameContentLibraryItem item, int depth, HashSet<GameContentLibraryItem> visited)
        {
            if (item == null || depth < 0 || !visited.Add(item)) return;
            for (int i = 0; i < item.DirectReferences.Count; i++)
                CollectReachable(item.DirectReferences[i].Target, depth - 1, visited);
        }
    }
}
