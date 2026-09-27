using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattleSim
{
    /// <summary>Солнце, окружающий свет, туман, небо и пост-обработка под выбранное время суток.</summary>
    public static class Atmosphere
    {
        static Material skyInstance;
        static Volume volume;
        static ColorAdjustments colorAdjust;

        public static bool PostEnabled
        {
            get => volume != null && volume.enabled;
            set { if (volume != null) volume.enabled = value; }
        }

        public static void SetupCamera(Camera cam)
        {
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1400f;
            cam.fieldOfView = 50f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            if (volume != null) return;
            var go = new GameObject("PostFX");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);

            colorAdjust = profile.Add<ColorAdjustments>();
            colorAdjust.postExposure.Override(0.35f);
            colorAdjust.contrast.Override(12f);
            colorAdjust.saturation.Override(14f);
            colorAdjust.colorFilter.Override(Color.white);

            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            volume.sharedProfile = profile;
        }

        public static void Apply(WorldStyle s, Light sun, Camera cam)
        {
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(s.SunEuler);
            sun.color = s.SunColor;
            sun.intensity = s.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = s.AmbSky;
            RenderSettings.ambientEquatorColor = s.AmbEquator;
            RenderSettings.ambientGroundColor = s.AmbGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = s.Fog;
            RenderSettings.fogDensity = s.FogDensity;

            if (skyInstance == null)
            {
                Material src = RenderSettings.skybox;
                if (src == null) src = Resources.Load<Material>(GameMaterials.SkyName);
                if (src != null) skyInstance = new Material(src);
            }
            if (skyInstance != null)
            {
                RenderSettings.skybox = skyInstance;
                SetF(skyInstance, "_SunSize", 0.04f);
                SetF(skyInstance, "_SunSizeConvergence", 6f);
                SetF(skyInstance, "_AtmosphereThickness", s.Atmosphere);
                SetF(skyInstance, "_Exposure", s.Exposure);
                if (skyInstance.HasProperty("_SkyTint")) skyInstance.SetColor("_SkyTint", s.SkyTint);
                if (skyInstance.HasProperty("_GroundColor")) skyInstance.SetColor("_GroundColor", s.SkyGround);
                cam.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = s.Fog;
            }

            if (colorAdjust != null)
            {
                Color filter = s.Time == 1 ? new Color(1f, 0.94f, 0.86f) : s.Time == 2 ? new Color(0.94f, 0.97f, 1f) : Color.white;
                colorAdjust.colorFilter.Override(filter);
            }

            DynamicGI.UpdateEnvironment();
        }

        static void SetF(Material m, string p, float v)
        {
            if (m.HasProperty(p)) m.SetFloat(p, v);
        }
    }
}
