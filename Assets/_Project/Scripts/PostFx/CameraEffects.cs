using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using NaughtyAttributes;

namespace Pistol.PostFx
{
    // The only owner of runtime screen effects (Post Processing volumes) on the camera.
    // Aiming, cylinder inspect etc. don't create volumes themselves: they get a request with their
    // ScreenEffectsProfile and set its strength (and focus) every frame. Each request turns its profile
    // into volumes with the profile priority, and Post Processing blends them as usual.
    // Everything that is on right now is listed in the Inspector (Active Effects).
    //
    // Goes on the camera, next to the Post-process Layer.
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class CameraEffects : MonoBehaviour
    {
        [BoxGroup("Отладка"), SerializeField, Tooltip("Выключить все эффекты, которые включают прицел, осмотр и т.п. (то же делает клавиша в оверлее производительности)")]
        bool suppressed;

        readonly List<ScreenEffectsRequest> requests = new List<ScreenEffectsRequest>();
        int layerIndex = -1;
        bool layerFailed;

        public bool Suppressed => suppressed;
        public void SetSuppressed(bool on) => suppressed = on;

        [ShowNativeProperty]
        public string ActiveEffects
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var r in requests)
                    if (r.IsOn) sb.Append(sb.Length > 0 ? "; " : "").Append(r.Describe());
                return sb.Length > 0 ? sb.ToString() : (suppressed ? "suppressed" : "none");
            }
        }

        // A request for the given profile. Call Set every frame you need the effects, Release when done.
        public ScreenEffectsRequest CreateRequest(ScreenEffectsProfile profile, string name)
        {
            var request = new ScreenEffectsRequest(this, profile, name);
            requests.Add(request);
            return request;
        }

        internal void Remove(ScreenEffectsRequest request) => requests.Remove(request);

        // Volumes go to the first layer the camera's Post-process Layer listens to
        internal bool TryGetLayer(out int index)
        {
            index = layerIndex;
            if (layerIndex >= 0) return true;
            if (layerFailed) return false;

            var layer = GetComponent<PostProcessLayer>();
            int mask = layer != null ? layer.volumeLayer.value : 0;
            int i = 0;
            while (i < 32 && (mask & (1 << i)) == 0) i++;
            if (i >= 32)
            {
                Debug.LogWarning("CameraEffects: no Post-process Layer with a volume layer on the camera, screen effects are off.", this);
                layerFailed = true;
                return false;
            }
            index = layerIndex = i;
            return true;
        }

        void OnDestroy()
        {
            for (int i = requests.Count - 1; i >= 0; i--) requests[i].Release();
        }
    }

    // Screen effects asked for by one owner (aim, inspect...). Volumes are created on first use.
    public sealed class ScreenEffectsRequest
    {
        public string Name { get; }
        public ScreenEffectsProfile Profile { get; }
        public float Weight { get; private set; }

        readonly CameraEffects owner;
        PostProcessVolume focusVolume, vignetteVolume, edgeVolume;
        DepthOfField dof;
        Vignette vignette;
        EdgeBlur edge;
        bool focusOn, vignetteOn, edgeOn;

        internal ScreenEffectsRequest(CameraEffects owner, ScreenEffectsProfile profile, string name)
        {
            this.owner = owner;
            Profile = profile;
            Name = name;
        }

        internal bool IsOn => focusOn || vignetteOn || edgeOn;

        internal string Describe()
        {
            var parts = new List<string>();
            if (focusOn) parts.Add("focus");
            if (vignetteOn) parts.Add("vignette");
            if (edgeOn) parts.Add("edge blur");
            return $"{Name} {Weight:0.00}: {string.Join(" + ", parts)}";
        }

        // weight: 0..1 how strong the effects are now;
        // focusDistance: where the eye focuses, m; edgeRadiusShrink: how much narrower the sharp zone is (tired tunnel)
        public void Set(float weight, float focusDistance = 10f, float edgeRadiusShrink = 0f)
        {
            Weight = weight;
            var p = Profile;
            bool on = p != null && owner != null && !owner.Suppressed && weight > 0.001f;

            focusOn = on && p.focus && Ensure(ref focusVolume, ref dof, CreateDof);
            if (focusOn)
            {
                dof.focusDistance.value = Mathf.Max(0.1f, focusDistance);
                // Fading by aperture keeps the volume at full weight: the focus distance never blends with the default
                dof.aperture.value = p.fadeByAperture ? Mathf.Lerp(p.closedAperture, p.aperture, weight) : p.aperture;
                dof.focalLength.value = p.focalLength;
                dof.kernelSize.value = p.kernelSize;
            }
            Show(focusVolume, focusOn, p != null && p.fadeByAperture ? 1f : weight);

            vignetteOn = on && p.vignette && Ensure(ref vignetteVolume, ref vignette, CreateVignette);
            if (vignetteOn)
            {
                vignette.intensity.value = p.vignetteIntensity;
                vignette.smoothness.value = p.vignetteSmoothness;
                vignette.roundness.value = p.vignetteRoundness;
            }
            Show(vignetteVolume, vignetteOn, weight);

            edgeOn = on && p.edgeBlur && Ensure(ref edgeVolume, ref edge, CreateEdge);
            if (edgeOn)
            {
                edge.intensity.value = p.edgeBlurIntensity;
                edge.radius.value = Mathf.Max(0f, p.edgeBlurRadius - edgeRadiusShrink);
                edge.softness.value = p.edgeBlurSoftness;
                edge.spread.value = p.edgeBlurSpread;
            }
            Show(edgeVolume, edgeOn, weight);
        }

        // Switch the effects off and give the volumes back
        public void Release()
        {
            Destroy(ref focusVolume);
            Destroy(ref vignetteVolume);
            Destroy(ref edgeVolume);
            focusOn = vignetteOn = edgeOn = false;
            owner?.Remove(this);
        }

        void Show(PostProcessVolume volume, bool on, float weight)
        {
            if (volume == null) return;
            volume.enabled = on;
            if (!on) return;
            volume.weight = weight;
            volume.priority = Profile.priority;   // lets the priority be tuned live
        }

        bool Ensure<T>(ref PostProcessVolume volume, ref T settings, System.Func<T> create) where T : PostProcessEffectSettings
        {
            if (volume != null) return true;
            if (!owner.TryGetLayer(out int layer)) return false;
            settings = create();
            volume = PostProcessManager.instance.QuickVolume(layer, Profile.priority, settings);
            volume.weight = 0f;
            return true;
        }

        static void Destroy(ref PostProcessVolume volume)
        {
            if (volume != null) RuntimeUtilities.DestroyVolume(volume, true, true);
            volume = null;
        }

        static DepthOfField CreateDof()
        {
            var s = ScriptableObject.CreateInstance<DepthOfField>();
            s.enabled.Override(true);
            s.focusDistance.Override(10f);
            s.aperture.Override(16f);
            s.focalLength.Override(30f);
            s.kernelSize.Override(KernelSize.Medium);
            return s;
        }

        static Vignette CreateVignette()
        {
            var s = ScriptableObject.CreateInstance<Vignette>();
            s.enabled.Override(true);
            s.mode.Override(VignetteMode.Classic);
            s.color.Override(Color.black);
            s.center.Override(new Vector2(0.5f, 0.5f));
            s.intensity.Override(0f);
            s.smoothness.Override(0.45f);
            s.roundness.Override(1f);
            return s;
        }

        static EdgeBlur CreateEdge()
        {
            if (Shader.Find("Hidden/Custom/EdgeBlur") == null)
                Debug.LogWarning("CameraEffects: EdgeBlur.shader not found. Add it to the project, otherwise edges won't blur.");
            var s = ScriptableObject.CreateInstance<EdgeBlur>();
            s.enabled.Override(true);
            s.intensity.Override(0f);
            s.radius.Override(0.3f);
            s.softness.Override(0.45f);
            s.spread.Override(0.025f);
            return s;
        }
    }
}
