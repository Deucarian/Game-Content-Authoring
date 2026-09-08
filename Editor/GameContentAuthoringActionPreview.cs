using System;
using System.Collections.Generic;
using System.Linq;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.GameContentAuthoring.Editor
{
    public sealed class GameContentAuthoringActionPreview
    {
        private readonly List<GameContentAuthoringActionPreviewRole> _roles = new List<GameContentAuthoringActionPreviewRole>();

        public UnityEngine.Object PrimaryAsset { get; set; }
        public GameObject SourcePrefab { get; set; }
        public GameObject ProjectilePrefab { get; set; }
        public GameObject BeamVfxPrefab { get; set; }
        public GameObject CastVfxPrefab { get; set; }
        public GameObject ImpactVfxPrefab { get; set; }
        public GameObject FireVfxPrefab { get; set; }
        public GameObject TickVfxPrefab { get; set; }
        public GameObject ExpireVfxPrefab { get; set; }
        public GameObject TargetPrefab { get; set; }
        public GameContentAuthoringActionPreviewMode Mode { get; set; }
        public GameContentAuthoringActionPreviewRenderMode RenderMode { get; set; } = GameContentAuthoringActionPreviewRenderMode.Game;
        public bool IncludeStatusEffect { get; set; }
        public bool Playing { get; set; }
        public bool Loop { get; set; } = true;
        public float Speed { get; set; } = 1f;
        public double StartTime { get; set; }
        public float DurationSeconds { get; set; } = 2.4f;
        public float StaticNormalizedTime { get; set; }
        public string Label { get; set; }
        public string DeliveryTypeLabel { get; set; }
        public string SourceContextLabel { get; set; }
        public string TargetContextLabel { get; set; }
        public bool Muted { get; set; }
        public Color AccentColor { get; set; } = new Color(0.12f, 0.78f, 0.86f, 1f);
        public IList<GameContentAuthoringActionPreviewRole> Roles => _roles;

        public float GetNormalizedTime(double now)
        {
            float duration = Mathf.Max(0.001f, DurationSeconds);
            if (!Playing)
            {
                return Mathf.Clamp01(StaticNormalizedTime);
            }

            double elapsed = Math.Max(0d, now - StartTime);
            elapsed *= Mathf.Max(0.01f, Speed);
            if (Loop)
            {
                return (float)(elapsed % duration / duration);
            }

            return Mathf.Clamp01((float)(elapsed / duration));
        }

        public string GetPhaseLabel(double now)
        {
            float time = GetNormalizedTime(now);
            if (time < 0.14f) return "OnCast";
            if (time < 0.28f) return "OnFire";
            if (time < 0.72f) return GetDeliveryLabel();
            if (time < 0.88f) return "OnImpact";
            return IncludeStatusEffect ? "Status / Expire" : "Resolved";
        }

        private string GetDeliveryLabel()
        {
            switch (Mode)
            {
                case GameContentAuthoringActionPreviewMode.Projectile:
                    return "Projectile travel";
                case GameContentAuthoringActionPreviewMode.Hitscan:
                    return "Beam trace";
                case GameContentAuthoringActionPreviewMode.Area:
                    return "Area burst";
                case GameContentAuthoringActionPreviewMode.Aura:
                    return "Aura tick";
                default:
                    return "Delivery";
            }
        }
    }
}
