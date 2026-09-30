// Заглушки типов URP (пакет com.unity.render-pipelines.universal) — только для проверки компиляции вне Unity.
namespace UnityEngine.Rendering
{
    public class VolumeParameter<T> { public T value; public bool overrideState; public void Override(T x) { value = x; overrideState = true; } }
    public class FloatParameter : VolumeParameter<float> { }
    public class ClampedFloatParameter : VolumeParameter<float> { }
    public class MinFloatParameter : VolumeParameter<float> { }
    public class ColorParameter : VolumeParameter<Color> { }
    public class VolumeComponent : ScriptableObject { }
    public class VolumeProfile : ScriptableObject { public T Add<T>(bool overrides = false) where T : VolumeComponent => default; }
    public class Volume : MonoBehaviour { public bool isGlobal; public float priority, weight; public VolumeProfile sharedProfile, profile; }
}
namespace UnityEngine.Rendering.Universal
{
    public enum TonemappingMode { None, Neutral, ACES }
    public class TonemappingModeParameter : VolumeParameter<TonemappingMode> { }
    public class Tonemapping : VolumeComponent { public TonemappingModeParameter mode; }
    public class Bloom : VolumeComponent { public MinFloatParameter threshold; public MinFloatParameter intensity; public ClampedFloatParameter scatter; public ColorParameter tint; }
    public class ColorAdjustments : VolumeComponent { public FloatParameter postExposure; public ClampedFloatParameter contrast, saturation, hueShift; public ColorParameter colorFilter; }
    public class Vignette : VolumeComponent { public ClampedFloatParameter intensity, smoothness; public ColorParameter color; }
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing, TemporalAntiAliasing }
    public class UniversalAdditionalCameraData : MonoBehaviour { public bool renderPostProcessing, renderShadows; public AntialiasingMode antialiasing; }
    public static class CameraExtensions { public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera c) => null; }
}
