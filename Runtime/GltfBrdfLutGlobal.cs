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
    const string DefaultSheenELutResourceName = "GltfSheenELut";
    const string DefaultCharlieLutResourceName = "GltfCharlieLut";
    static readonly int BrdfLutId = Shader.PropertyToID("_GltfBrdfLut");
    static readonly int SheenELutId = Shader.PropertyToID("_GltfSheenELut");
    static readonly int CharlieLutId = Shader.PropertyToID("_GltfCharlieLut");

    [SerializeField] private Texture2D brdfLut;
    [SerializeField] private Texture2D sheenELut;
    [SerializeField] private Texture2D charlieLut;

    void Reset()
    {
        if (brdfLut == null)
        {
            brdfLut = Resources.Load<Texture2D>(DefaultLutResourceName);
        }
        if (sheenELut == null)
        {
            sheenELut = Resources.Load<Texture2D>(DefaultSheenELutResourceName);
        }
        if (charlieLut == null)
        {
            charlieLut = Resources.Load<Texture2D>(DefaultCharlieLutResourceName);
        }
        ApplyAll();
    }

    void OnEnable()
    {
        ApplyAll();
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
    }

    void OnValidate()
    {
        ApplyAll();
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
    }

    public static void Apply(Texture2D overrideLut)
    {
        Apply(overrideLut, null, null);
    }

    public static void Apply(Texture2D overrideBrdfLut, Texture2D overrideSheenELut, Texture2D overrideCharlieLut)
    {
        if (overrideBrdfLut != null)
        {
            Shader.SetGlobalTexture(BrdfLutId, overrideBrdfLut);
        }
        if (overrideSheenELut != null)
        {
            Shader.SetGlobalTexture(SheenELutId, overrideSheenELut);
        }
        if (overrideCharlieLut != null)
        {
            Shader.SetGlobalTexture(CharlieLutId, overrideCharlieLut);
        }
    }

    void ApplyAll()
    {
        Apply(brdfLut, sheenELut, charlieLut);
    }

#if UNITY_EDITOR
    void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.EnteredPlayMode)
        {
            ApplyAll();
        }
    }
#endif
}
