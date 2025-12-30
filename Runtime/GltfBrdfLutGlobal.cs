using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class GltfBrdfLutGlobal : MonoBehaviour
{
    const string DefaultLutResourceName = "GltfBrdfLut";
    static readonly int BrdfLutId = Shader.PropertyToID("_GltfBrdfLut");

    [SerializeField] private Texture2D brdfLut;

    void Reset()
    {
        if (brdfLut == null)
        {
            brdfLut = Resources.Load<Texture2D>(DefaultLutResourceName);
        }
        Apply(brdfLut);
    }

    void OnEnable()
    {
        Apply(brdfLut);
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
    }

    void OnValidate()
    {
        Apply(brdfLut);
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
    }

    public static void Apply(Texture2D overrideLut)
    {
        var lut = overrideLut;
        if (lut != null)
        {
            Shader.SetGlobalTexture(BrdfLutId, lut);
        }
    }

#if UNITY_EDITOR
    void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.EnteredPlayMode)
        {
            Apply(brdfLut);
        }
    }
#endif
}
