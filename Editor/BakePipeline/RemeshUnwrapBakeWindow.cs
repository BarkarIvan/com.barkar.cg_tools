#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class RemeshUnwrapBakeWindow : EditorWindow
{
    const string PrefPrefix = "RemeshUnwrapBakeWindow.";
    const string PrefHighPath = PrefPrefix + "HighPath";
    const string PrefScreenSize = PrefPrefix + "ScreenSize";
    const string PrefTexSize = PrefPrefix + "TexSize";
    const string PrefCage = PrefPrefix + "Cage";
    const string PrefSamples = PrefPrefix + "Samples";
    const string PrefUseFinalFaces = PrefPrefix + "UseFinalFaces";
    const string PrefFinalFaces = PrefPrefix + "FinalFaces";
    const string PrefRemeshOutputFilter = PrefPrefix + "RemeshOutputFilter";
    const string PrefBlenderExe = PrefPrefix + "BlenderExe";
    const string PrefBlenderScript = PrefPrefix + "BlenderScript";
    const string PrefLastOutDir = PrefPrefix + "LastOutDir";

    UnityEngine.Object highObj;
    string highAssetPath;
    bool useFinalFaces;
    RemeshUnwrapBakeRunner.RemeshUnwrapBakeOptions options;
    string blenderExeOverride;
    string blenderScriptOverride;
    string lastOutAssetDir;
    string lastError;

    [MenuItem("Tools/LowPoly/Remesh -> Unwrap+Bake Window")]
    public static void ShowWindow()
    {
        var window = GetWindow<RemeshUnwrapBakeWindow>();
        window.titleContent = new GUIContent("LowPoly Bake");
        window.minSize = new Vector2(360f, 420f);
        window.Show();
    }

    void OnEnable()
    {
        LoadPrefs();
    }

    void OnDisable()
    {
        SavePrefs();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Input", EditorStyles.boldLabel);
        var newHigh = EditorGUILayout.ObjectField("High OBJ", highObj, typeof(UnityEngine.Object), false);
        if (newHigh != highObj)
        {
            highObj = newHigh;
            highAssetPath = highObj != null ? AssetDatabase.GetAssetPath(highObj) : string.Empty;
            SavePrefs();
        }

        bool isHighObj = !string.IsNullOrEmpty(highAssetPath) &&
            highAssetPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(highAssetPath) && !isHighObj)
            EditorGUILayout.HelpBox("Select a .obj asset for the HIGH mesh.", MessageType.Error);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Remesh", EditorStyles.boldLabel);
        options.ScreenSize = EditorGUILayout.IntField("Screen Size (-n)", options.ScreenSize);
        useFinalFaces = EditorGUILayout.Toggle("Use Final Faces (-f)", useFinalFaces);
        if (useFinalFaces)
        {
            int faces = options.FinalFaceNum ?? 0;
            faces = EditorGUILayout.IntField("Final Faces", faces);
            options.FinalFaceNum = faces > 0 ? faces : (int?)null;
        }
        else
        {
            options.FinalFaceNum = null;
        }
        options.RemeshOutputNameContains = EditorGUILayout.TextField("Output Name Filter", options.RemeshOutputNameContains);
        if (!string.IsNullOrWhiteSpace(options.RemeshOutputNameContains))
            EditorGUILayout.HelpBox("Uses name substring to pick the remesh output. Example: noInterior", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Bake", EditorStyles.boldLabel);
        options.TexSize = EditorGUILayout.IntField("Texture Size", options.TexSize);
        options.Cage = EditorGUILayout.FloatField("Cage", options.Cage);
        options.Samples = EditorGUILayout.IntField("Samples", options.Samples);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Paths", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        blenderExeOverride = EditorGUILayout.TextField("Blender Exe", blenderExeOverride);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string picked = EditorUtility.OpenFilePanel("Select Blender exe", "", "exe");
            if (!string.IsNullOrEmpty(picked))
            {
                blenderExeOverride = picked;
                SavePrefs();
            }
        }
        EditorGUILayout.EndHorizontal();
        if (string.IsNullOrWhiteSpace(blenderExeOverride))
            EditorGUILayout.HelpBox("Leave empty to auto-detect or use BLENDER_EXE env var.", MessageType.Info);
        else if (!File.Exists(blenderExeOverride))
            EditorGUILayout.HelpBox("Blender exe not found at this path.", MessageType.Warning);

        EditorGUILayout.BeginHorizontal();
        blenderScriptOverride = EditorGUILayout.TextField("Bake Script", blenderScriptOverride);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string picked = EditorUtility.OpenFilePanel("Select bake_lod.py", "", "py");
            if (!string.IsNullOrEmpty(picked))
            {
                blenderScriptOverride = picked;
                SavePrefs();
            }
        }
        EditorGUILayout.EndHorizontal();
        if (string.IsNullOrWhiteSpace(blenderScriptOverride))
            EditorGUILayout.HelpBox("Leave empty to auto-detect or use BLENDER_BAKE_SCRIPT env var.", MessageType.Info);
        else if (!File.Exists(blenderScriptOverride))
            EditorGUILayout.HelpBox("Bake script not found at this path.", MessageType.Warning);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(!isHighObj))
        {
            if (GUILayout.Button("Run Pipeline"))
            {
                SavePrefs();
                RunPipeline();
            }
        }

        if (!string.IsNullOrEmpty(lastError))
            EditorGUILayout.HelpBox(lastError, MessageType.Error);

        DrawOutputs();
    }

    void RunPipeline()
    {
        lastError = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(blenderExeOverride))
            {
                if (!File.Exists(blenderExeOverride))
                {
                    lastError = "Blender exe not found at the specified path.";
                    return;
                }
                Environment.SetEnvironmentVariable("BLENDER_EXE", blenderExeOverride);
            }

            if (!string.IsNullOrWhiteSpace(blenderScriptOverride))
            {
                if (!File.Exists(blenderScriptOverride))
                {
                    lastError = "Bake script not found at the specified path.";
                    return;
                }
                Environment.SetEnvironmentVariable("BLENDER_BAKE_SCRIPT", blenderScriptOverride);
            }

            var result = RemeshUnwrapBakeRunner.RunPipelineWithOptions(highAssetPath, options);
            lastOutAssetDir = result.OutputAssetDir;
            SavePrefs();
            Repaint();
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            Debug.LogException(ex);
        }
    }

    void DrawOutputs()
    {
        if (string.IsNullOrEmpty(lastOutAssetDir))
            return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Outputs", EditorStyles.boldLabel);

        string lowPath = $"{lastOutAssetDir}/low_unwrapped.obj";
        string normalPath = $"{lastOutAssetDir}/normal.png";
        string aoPath = $"{lastOutAssetDir}/ao.png";
        string matPath = $"{lastOutAssetDir}/baked.mat";

        var lowObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lowPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        EditorGUILayout.ObjectField("Low Mesh", lowObj, typeof(UnityEngine.Object), false);
        EditorGUILayout.ObjectField("Material", mat, typeof(Material), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Textures", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        DrawTexturePreview(normal, "Normal");
        DrawTexturePreview(ao, "AO");
        EditorGUILayout.EndHorizontal();
    }

    void DrawTexturePreview(Texture2D tex, string label)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(120));
        EditorGUILayout.LabelField(label);
        var rect = GUILayoutUtility.GetRect(96, 96, GUILayout.ExpandWidth(false));
        if (tex != null)
        {
            EditorGUI.DrawPreviewTexture(rect, tex, null, ScaleMode.ScaleToFit);
        }
        else
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 1f));
            EditorGUI.LabelField(rect, "Missing", EditorStyles.centeredGreyMiniLabel);
        }
        EditorGUILayout.EndVertical();
    }

    void LoadPrefs()
    {
        options = RemeshUnwrapBakeRunner.DefaultOptions;

        highAssetPath = EditorPrefs.GetString(PrefHighPath, string.Empty);
        if (!string.IsNullOrEmpty(highAssetPath))
            highObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(highAssetPath);

        options.ScreenSize = EditorPrefs.GetInt(PrefScreenSize, options.ScreenSize);
        options.TexSize = EditorPrefs.GetInt(PrefTexSize, options.TexSize);
        options.Cage = EditorPrefs.GetFloat(PrefCage, options.Cage);
        options.Samples = EditorPrefs.GetInt(PrefSamples, options.Samples);

        useFinalFaces = EditorPrefs.GetBool(PrefUseFinalFaces, false);
        if (useFinalFaces)
            options.FinalFaceNum = EditorPrefs.GetInt(PrefFinalFaces, options.FinalFaceNum ?? 0);
        else
            options.FinalFaceNum = null;

        options.RemeshOutputNameContains = EditorPrefs.GetString(PrefRemeshOutputFilter, options.RemeshOutputNameContains ?? string.Empty);
        blenderExeOverride = EditorPrefs.GetString(PrefBlenderExe, string.Empty);
        blenderScriptOverride = EditorPrefs.GetString(PrefBlenderScript, string.Empty);
        lastOutAssetDir = EditorPrefs.GetString(PrefLastOutDir, string.Empty);
    }

    void SavePrefs()
    {
        EditorPrefs.SetString(PrefHighPath, highAssetPath ?? string.Empty);
        EditorPrefs.SetInt(PrefScreenSize, options.ScreenSize);
        EditorPrefs.SetInt(PrefTexSize, options.TexSize);
        EditorPrefs.SetFloat(PrefCage, options.Cage);
        EditorPrefs.SetInt(PrefSamples, options.Samples);
        EditorPrefs.SetBool(PrefUseFinalFaces, useFinalFaces);
        if (options.FinalFaceNum.HasValue)
            EditorPrefs.SetInt(PrefFinalFaces, options.FinalFaceNum.Value);
        EditorPrefs.SetString(PrefRemeshOutputFilter, options.RemeshOutputNameContains ?? string.Empty);
        EditorPrefs.SetString(PrefBlenderExe, blenderExeOverride ?? string.Empty);
        EditorPrefs.SetString(PrefBlenderScript, blenderScriptOverride ?? string.Empty);
        EditorPrefs.SetString(PrefLastOutDir, lastOutAssetDir ?? string.Empty);
    }
}
#endif
