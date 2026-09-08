using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    internal static class GameContentPreviewScene
    {
        internal const float MinimumPreviewBoundsAxis = 0.08f;

        internal const float MaximumPreviewBoundsAxis = 8f;

        internal const float MaximumPreviewBoundsCenterDistance = 8f;

        internal static readonly Color PreviewCameraBackground = new Color(0.045f, 0.085f, 0.105f, 1f);

        internal static PreviewRenderUtility CreatePreviewUtility()
        {
            var utility = new PreviewRenderUtility(true);
            utility.cameraFieldOfView = 27f;
            utility.camera.clearFlags = CameraClearFlags.Color;
            utility.camera.backgroundColor = PreviewCameraBackground;
            if (utility.lights.Length > 0)
            {
                utility.lights[0].intensity = 1.65f;
                utility.lights[0].transform.rotation = Quaternion.Euler(34f, 32f, 0f);
            }

            if (utility.lights.Length > 1)
            {
                utility.lights[1].intensity = 1.05f;
                utility.lights[1].transform.rotation = Quaternion.Euler(305f, 210f, 0f);
            }

            return utility;
        }

        internal static bool TryAddPrefab(
            PreviewRenderUtility utility,
            List<GameObject> roots,
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            float scale,
            float simulatedTime)
        {
            if (utility == null || roots == null || prefab == null)
                return false;

            GameObject clone = UnityEngine.Object.Instantiate(prefab);
            clone.name = prefab.name + " (Game Preview)";
            clone.transform.rotation = rotation;
            clone.transform.localScale = clone.transform.localScale * Mathf.Max(0.01f, scale);
            clone.SetActive(true);
            SetPreviewHideFlags(clone);
            SimulateParticles(clone, simulatedTime);

            Bounds bounds;
            if (!TryCalculateBounds(clone, out bounds))
            {
                GameContentAuthoringEditorAssets.DestroyTransientObject(clone);
                return false;
            }

            clone.transform.position += position - bounds.center;

            roots.Add(clone);
            utility.AddSingleGO(clone);
            return true;
        }

        internal static void AddTargetInstance(PreviewRenderUtility utility, List<GameObject> roots, GameObject targetPrefab, Vector3 position, float simulatedTime)
        {
            if (!TryAddPrefab(utility, roots, targetPrefab, position, Quaternion.identity, 0.92f, simulatedTime))
                AddPrimitive(utility, roots, PrimitiveType.Capsule, "Game Preview Target Dummy", position + new Vector3(0f, 0.28f, 0f), new Vector3(0.28f, 0.48f, 0.28f), Quaternion.identity);
        }

        internal static void AddPrimitive(
            PreviewRenderUtility utility,
            List<GameObject> roots,
            PrimitiveType primitiveType,
            string name,
            Vector3 position,
            Vector3 scale,
            Quaternion rotation)
        {
            if (utility == null || roots == null)
                return;

            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = name;
            primitive.transform.position = position;
            primitive.transform.rotation = rotation;
            primitive.transform.localScale = scale;
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
                GameContentAuthoringEditorAssets.DestroyTransientObject(collider);
            SetPreviewHideFlags(primitive);
            roots.Add(primitive);
            utility.AddSingleGO(primitive);
        }

        internal static Vector3 GetProjectilePosition(Vector3 source, Vector3 target, float travel)
        {
            Vector3 position = Vector3.Lerp(source, target, travel);
            position.y += Mathf.Sin(travel * Mathf.PI) * 0.36f;
            return position;
        }

        internal static bool TryCalculateSceneBounds(IReadOnlyList<GameObject> roots, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.one);
            bool hasBounds = false;
            if (roots == null)
                return false;

            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                Bounds rootBounds;
                if (!TryCalculateBounds(root, out rootBounds))
                    continue;

                if (!hasBounds)
                {
                    bounds = rootBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rootBounds);
                }
            }

            if (!hasBounds)
                return false;

            bounds.Expand(0.35f);
            bounds = SanitizePreviewBounds(bounds, Vector3.zero);
            return true;
        }

        internal static void ConfigurePreviewCamera(PreviewRenderUtility utility, Bounds bounds)
        {
            if (utility == null)
                return;

            float radius = Mathf.Max(0.9f, bounds.extents.magnitude);
            float distance = Mathf.Max(3.25f, radius / Mathf.Sin(utility.cameraFieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f);
            Vector3 focus = bounds.center;
            utility.camera.transform.position = focus + new Vector3(radius * 0.28f, radius * 0.22f, -distance);
            utility.camera.transform.rotation = Quaternion.LookRotation(focus - utility.camera.transform.position, Vector3.up);
            utility.camera.nearClipPlane = 0.01f;
            utility.camera.farClipPlane = distance + radius * 5f;
        }

        internal static void SetPreviewHideFlags(GameObject root)
        {
            if (root == null) return;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        internal static void SimulateParticles(GameObject root, float time)
        {
            if (root == null) return;
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Simulate(time, true, true, true);
            }
        }

        internal static bool TryCalculateBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.one);
            if (root == null) return false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds candidate = renderer.bounds;
                if (!IsFinite(candidate.center) || !IsFinite(candidate.size)) continue;
                candidate = SanitizePreviewBounds(candidate, root.transform.position);
                if (!hasBounds)
                {
                    bounds = candidate;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(candidate);
                }
            }

            if (!hasBounds)
                return false;

            bounds = SanitizePreviewBounds(bounds, root.transform.position);

            if (bounds.size.sqrMagnitude < 0.001f)
            {
                bounds.Expand(1f);
            }

            return hasBounds;
        }

        internal static Bounds SanitizePreviewBounds(Bounds bounds, Vector3 fallbackCenter)
        {
            Vector3 center = IsFinite(bounds.center) ? bounds.center : fallbackCenter;
            Vector3 offset = center - fallbackCenter;
            if (!IsFinite(offset))
                offset = Vector3.zero;
            if (offset.sqrMagnitude > MaximumPreviewBoundsCenterDistance * MaximumPreviewBoundsCenterDistance)
                offset = Vector3.ClampMagnitude(offset, MaximumPreviewBoundsCenterDistance);
            center = fallbackCenter + offset;

            Vector3 size = IsFinite(bounds.size) ? bounds.size : Vector3.one;
            size = new Vector3(
                SanitizePreviewBoundsAxis(size.x),
                SanitizePreviewBoundsAxis(size.y),
                SanitizePreviewBoundsAxis(size.z));
            return new Bounds(center, size);
        }

        internal static float SanitizePreviewBoundsAxis(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 1f;
            return Mathf.Clamp(Mathf.Abs(value), MinimumPreviewBoundsAxis, MaximumPreviewBoundsAxis);
        }

        internal static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                && !float.IsNaN(value.y)
                && !float.IsNaN(value.z)
                && !float.IsInfinity(value.x)
                && !float.IsInfinity(value.y)
                && !float.IsInfinity(value.z);
        }
    }
}
