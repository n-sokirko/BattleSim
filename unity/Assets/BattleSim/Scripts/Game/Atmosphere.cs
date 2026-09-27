using BattleSim.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattleSim
{
    /// <summary>Солнце, свет неба, туман, процедурное небо и пост-обработка под биом и время суток.</summary>
    public static class Atmosphere
    {
        static Material sky;
        static Volume volume;
        static ColorAdjustments color;

        public static bool PostEnabled
        {
            get => volume != null && volume.enabled;
            set { if (volume != null) volume.enabled = value; }
        }

        public static void SetupCamera(Camera cam, bool lowEnd)
        {
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 48f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.antialiasing = lowEnd ? AntialiasingMode.None : AntialiasingMode.FastApproximateAntialiasing;

            if (volume != null) return;
            var go = new GameObject("PostFX");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>();
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.25f);
            color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(0.4f);
            color.contrast.Override(8f);
            color.saturation.Override(10f);
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            volume.sharedProfile = profile;
        }

        public static void Apply(Style s, V3 sunDir, Light sun, Camera cam)
        {
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.LookRotation(-Conv.U(sunDir), Vector3.up);
            sun.color = Conv.Col(s.SunColor);
            // В three.js освещённость делится на π; в URP — нет
            sun.intensity = s.SunI / Mathf.PI * 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Conv.Col(s.Fog.Lerp(new Rgb(1, 1, 1), 0.4f) * 0.55f);
            RenderSettings.ambientEquatorColor = Conv.Col(s.Fog * 0.42f);
            RenderSettings.ambientGroundColor = Conv.Col(s.GrassA * 0.35f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Conv.Col(s.Fog);
            RenderSettings.fogDensity = s.FogD;

            if (sky == null)
            {
                var sh = Shader.Find("Skybox/Procedural");
                if (sh != null) sky = new Material(sh) { name = "sky" };
            }
            if (sky != null)
            {
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 6f);
                sky.SetFloat("_AtmosphereThickness", Mathf.Clamp(0.6f + s.Rayleigh * 0.28f, 0.8f, 1.7f));
                sky.SetFloat("_Exposure", 1.15f + (s.Time == 2 ? 0.2f : 0f));
                sky.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), Conv.Col(s.Fog), 0.45f));
                sky.SetColor("_GroundColor", Conv.Col(s.Fog * 0.8f));
                RenderSettings.skybox = sky;
                cam.clearFlags = CameraClearFlags.Skybox;
            }
            else
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Conv.Col(s.Fog);
            }
            if (color != null)
            {
                color.colorFilter.Override(s.Time == 1 ? new Color(1f, 0.95f, 0.88f) : s.Time == 2 ? new Color(0.95f, 0.97f, 1f) : Color.white);
                color.postExposure.Override(s.Time == 1 ? 0.55f : 0.4f);
            }
            DynamicGI.UpdateEnvironment();
        }
    }
}
