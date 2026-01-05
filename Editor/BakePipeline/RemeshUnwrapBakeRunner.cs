#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UPM = UnityEditor.PackageManager;
using UnityEngine;

public static class RemeshUnwrapBakeRunner
{
    // ---------- Настройки по умолчанию ----------
    const int DefaultScreenSize = 100;   // -n
    const int DefaultTexSize = 2048;     // bake resolution
    const float DefaultCage = 0.02f;     // bake cage extrusion
    const int DefaultSamples = 32;       // Cycles samples

    public struct RemeshUnwrapBakeOptions
    {
        public int ScreenSize;
        public int TexSize;
        public float Cage;
        public int Samples;
        public int? FinalFaceNum;
    }

    public struct RemeshUnwrapBakeResult
    {
        public string OutputAssetDir;
        public string LowUnwrappedPath;
        public string NormalPath;
        public string AoPath;
        public string MaterialPath;
    }

    public static RemeshUnwrapBakeOptions DefaultOptions => new RemeshUnwrapBakeOptions
    {
        ScreenSize = DefaultScreenSize,
        TexSize = DefaultTexSize,
        Cage = DefaultCage,
        Samples = DefaultSamples,
        FinalFaceNum = null
    };

    [MenuItem("Tools/LowPoly/Remesh -> Unwrap+Bake (OBJ)")]
    public static void RunPipeline()
    {
        // 1) Берём один input mesh (желательно OBJ, потому что ремешер работает с OBJ)
        var selected = Selection.objects
            .Select(o => AssetDatabase.GetAssetPath(o))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToArray();

        if (selected.Length != 1)
        {
            UnityEngine.Debug.LogError("Выбери ОДИН asset в Project: HIGH mesh. Лучше .obj (ремешер ожидает obj).");
            return;
        }

        string highAssetPath = selected[0];
        if (!highAssetPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
        {
            UnityEngine.Debug.LogError("Сейчас пайплайн ожидает HIGH в формате .obj (потому что ремешер obj-only).");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;

        string highAbs = Path.GetFullPath(Path.Combine(projectRoot, highAssetPath));

        // 2) Папка вывода (внутри Assets, чтобы Unity сама импортнула)
        string outAssetDir = $"Assets/Generated/LowPolyBakes/{Path.GetFileNameWithoutExtension(highAssetPath)}";
        string outAbsDir = Path.GetFullPath(Path.Combine(projectRoot, outAssetDir));
        Directory.CreateDirectory(outAbsDir);

        // 3) Копируем вход в предсказуемое имя рядом с output
        string remeshInputAbs = Path.Combine(outAbsDir, "high_input.obj");
        File.Copy(highAbs, remeshInputAbs, overwrite: true);

        // 4) Запускаем ремешер
        string remesherExe = FindRemesherExeOrThrow(projectRoot);

        int screenSize = DefaultScreenSize;
        int texSize = DefaultTexSize;
        float cage = DefaultCage;
        int samples = DefaultSamples;

        // (опционально можешь заменить на EditorPrefs/окно настроек)
        int? finalFaceNum = null; // например: 5000

        EditorUtility.DisplayProgressBar("LowPoly Pipeline", "Running SurfaceRemeshingCli...", 0.25f);

        string lowRemeshedAbs = RunRemesherAndGetOutput(
            remesherExe,
            outAbsDir,
            remeshInputAbs,
            screenSize,
            finalFaceNum
        );

        // Переименуем в стабильное имя
        string lowStableAbs = Path.Combine(outAbsDir, "low_remeshed.obj");
        if (!string.Equals(lowRemeshedAbs, lowStableAbs, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(lowStableAbs)) File.Delete(lowStableAbs);
            File.Move(lowRemeshedAbs, lowStableAbs);
        }

        // 5) Запускаем Blender headless: UV + bake high->low + экспорт low_unwrapped.obj
        string blenderExe = FindBlenderExeOrThrow();
        string blenderScriptAbs = FindBlenderScriptOrThrow(projectRoot);

        EditorUtility.DisplayProgressBar("LowPoly Pipeline", "Running Blender headless (unwrap + bake)...", 0.70f);

        string blenderArgs =
            $"-b -P \"{blenderScriptAbs}\" -- " +
            $"--high \"{remeshInputAbs}\" --low \"{lowStableAbs}\" --out \"{outAbsDir}\" " +
            $"--texSize {texSize} --cage {cage} --samples {samples}";

        string blenderLogPath = Path.Combine(outAbsDir, "blender_bake.log");
        RunProcess(blenderExe, blenderArgs, outAbsDir, blenderLogPath);

        EditorUtility.ClearProgressBar();

        // 6) Импорт и настройка ассетов
        AssetDatabase.Refresh();

        string lowUnwrappedPath = $"{outAssetDir}/low_unwrapped.obj";
        string normalPath = $"{outAssetDir}/normal.png";
        string aoPath = $"{outAssetDir}/ao.png";

        ConfigureNormalMap(normalPath);
        ConfigureNonColorTexture(aoPath);
        ConfigureModelTangents(lowUnwrappedPath);

        AssetDatabase.Refresh();

        // 7) Материал
        var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        var aoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
        var mat = CreateMaterial(outAssetDir, normalTex, aoTex);

        UnityEngine.Debug.Log(
            "DONE.\n" +
            $"LOW: {lowUnwrappedPath}\n" +
            $"Normal: {normalPath}\n" +
            $"AO: {aoPath}\n" +
            $"Material: {AssetDatabase.GetAssetPath(mat)}\n\n" +
            "Если нормали выглядят 'вдавленными' — просто включи Flip Green Channel в импортёре normal.png."
        );

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lowUnwrappedPath);
    }

    public static RemeshUnwrapBakeResult RunPipelineWithOptions(string highAssetPath, RemeshUnwrapBakeOptions options)
    {
        if (string.IsNullOrWhiteSpace(highAssetPath))
            throw new ArgumentException("High asset path is empty.", nameof(highAssetPath));

        if (!highAssetPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("High mesh must be a .obj asset.", nameof(highAssetPath));

        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        string highAbs = Path.GetFullPath(Path.Combine(projectRoot, highAssetPath));
        if (!File.Exists(highAbs))
            throw new FileNotFoundException("High mesh file not found.", highAbs);

        string outAssetDir = $"Assets/Generated/LowPolyBakes/{Path.GetFileNameWithoutExtension(highAssetPath)}";
        string outAbsDir = Path.GetFullPath(Path.Combine(projectRoot, outAssetDir));
        Directory.CreateDirectory(outAbsDir);

        string remeshInputAbs = Path.Combine(outAbsDir, "high_input.obj");
        File.Copy(highAbs, remeshInputAbs, overwrite: true);

        string remesherExe = FindRemesherExeOrThrow(projectRoot);

        int screenSize = Math.Max(1, options.ScreenSize);
        int texSize = Math.Max(1, options.TexSize);
        float cage = Mathf.Max(0f, options.Cage);
        int samples = Math.Max(1, options.Samples);
        int? finalFaceNum = options.FinalFaceNum.HasValue ? Math.Max(1, options.FinalFaceNum.Value) : null;

        try
        {
            EditorUtility.DisplayProgressBar("LowPoly Pipeline", "Running SurfaceRemeshingCli...", 0.25f);

            string lowRemeshedAbs = RunRemesherAndGetOutput(
                remesherExe,
                outAbsDir,
                remeshInputAbs,
                screenSize,
                finalFaceNum
            );

            string lowStableAbs = Path.Combine(outAbsDir, "low_remeshed.obj");
            if (!string.Equals(lowRemeshedAbs, lowStableAbs, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(lowStableAbs)) File.Delete(lowStableAbs);
                File.Move(lowRemeshedAbs, lowStableAbs);
            }

            string blenderExe = FindBlenderExeOrThrow();
            string blenderScriptAbs = FindBlenderScriptOrThrow(projectRoot);

            EditorUtility.DisplayProgressBar("LowPoly Pipeline", "Running Blender headless (unwrap + bake)...", 0.70f);

            string blenderArgs =
                $"-b -P \"{blenderScriptAbs}\" -- " +
                $"--high \"{remeshInputAbs}\" --low \"{lowStableAbs}\" --out \"{outAbsDir}\" " +
                $"--texSize {texSize} --cage {cage} --samples {samples}";

        string blenderLogPath = Path.Combine(outAbsDir, "blender_bake.log");
        RunProcess(blenderExe, blenderArgs, outAbsDir, blenderLogPath);

        string lowOutAbs = Path.Combine(outAbsDir, "low_unwrapped.obj");
        string normalAbs = Path.Combine(outAbsDir, "normal.png");
        string aoAbs = Path.Combine(outAbsDir, "ao.png");
        if (!File.Exists(lowOutAbs) || !File.Exists(normalAbs) || !File.Exists(aoAbs))
            throw new Exception($"Blender finished but outputs are missing. Check log: {blenderLogPath}");

            EditorUtility.DisplayProgressBar("LowPoly Pipeline", "Importing assets...", 0.90f);
            AssetDatabase.Refresh();

            string lowUnwrappedPath = $"{outAssetDir}/low_unwrapped.obj";
            string normalPath = $"{outAssetDir}/normal.png";
            string aoPath = $"{outAssetDir}/ao.png";

            ConfigureNormalMap(normalPath);
            ConfigureNonColorTexture(aoPath);
            ConfigureModelTangents(lowUnwrappedPath);

            var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            var aoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
            var mat = CreateMaterial(outAssetDir, normalTex, aoTex);
            string matPath = mat != null ? AssetDatabase.GetAssetPath(mat) : null;

            UnityEngine.Debug.Log(
                "DONE.\n" +
                $"LOW: {lowUnwrappedPath}\n" +
                $"Normal: {normalPath}\n" +
                $"AO: {aoPath}\n" +
                $"Material: {matPath}\n" +
                "Note: if normal looks inverted, flip green channel on normal.png."
            );

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(lowUnwrappedPath);

            return new RemeshUnwrapBakeResult
            {
                OutputAssetDir = outAssetDir,
                LowUnwrappedPath = lowUnwrappedPath,
                NormalPath = normalPath,
                AoPath = aoPath,
                MaterialPath = matPath
            };
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static string RunRemesherAndGetOutput(string exe, string workDir, string inputAbs, int screenSize, int? finalFaceNum)
    {
        // Всё складываем в подпапку, чтобы не путаться
        string outDir = Path.Combine(workDir, "remesh_out");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        string args =
            $"-i \"{inputAbs}\" -n {screenSize} -o \"{outDir}\"" +
            (finalFaceNum.HasValue ? $" -f {finalFaceNum.Value}" : "");

        RunProcess(exe, args, workDir);

        var objs = Directory.GetFiles(outDir, "*.obj", SearchOption.AllDirectories)
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .ToArray();

        if (objs.Length == 0)
            throw new Exception($"Remesher finished but no .obj found in: {outDir}");

        return objs[0].FullName;
    }

    static string FindRemesherExeOrThrow(string projectRoot)
    {
        // 1) env var override
        var env = Environment.GetEnvironmentVariable("SURFACE_REMESHER_EXE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
            return env;

        // 2) рядом с проектом
        string candidate1 = Path.Combine(projectRoot, "Tools", "SurfaceRemesher", "SurfaceRemeshingCli_bin.exe");
        if (File.Exists(candidate1)) return candidate1;

        string packageExe = TryGetPackageFile("com.barkar.cg_tools", Path.Combine("Editor", "BakePipeline", "RoLoPM_EXE", "SurfaceRemeshingCli_bin.exe"));
        if (!string.IsNullOrEmpty(packageExe) && File.Exists(packageExe)) return packageExe;

        // 3) если лежит в пакете (как у тебя по примеру пути)
        string candidate2 = Path.Combine(projectRoot, "Packages", "com.barkar.cg_tools", "Editor", "BakePipeline", "RoLoPM_EXE", "SurfaceRemeshingCli_bin.exe");
        if (File.Exists(candidate2)) return candidate2;

        throw new FileNotFoundException(
            "SurfaceRemeshingCli_bin.exe не найден.\n" +
            "Варианты:\n" +
            "1) Поставь переменную окружения SURFACE_REMESHER_EXE (полный путь к exe)\n" +
            "2) Положи exe в <ProjectRoot>/Tools/SurfaceRemesher/\n" +
            "3) Или держи его в Packages/com.barkar.cg_tools/... как сейчас"
        );
    }

    static string TryGetPackageFile(string packageName, string relativePath)
    {
        string packageRoot = TryGetPackageResolvedPath(packageName);
        if (string.IsNullOrEmpty(packageRoot)) return null;
        return Path.Combine(packageRoot, relativePath);
    }

    static string TryGetPackageResolvedPath(string packageName)
    {
        string packageJsonPath = $"Packages/{packageName}/package.json";
        var info = UPM.PackageInfo.FindForAssetPath(packageJsonPath);
        if (info != null && Directory.Exists(info.resolvedPath))
            return info.resolvedPath;

        return null;
    }

    static string FindBlenderScriptOrThrow(string projectRoot)
    {
        var env = Environment.GetEnvironmentVariable("BLENDER_BAKE_SCRIPT");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
            return env;

        string packageScript = TryGetPackageFile("com.barkar.cg_tools", Path.Combine("Editor", "BakePipeline", "bake_lod.py"));
        if (!string.IsNullOrEmpty(packageScript) && File.Exists(packageScript)) return packageScript;

        string candidate1 = Path.Combine(projectRoot, "Assets", "Editor", "LowPolyBake", "bake_lod.py");
        if (File.Exists(candidate1)) return candidate1;

        string candidate2 = Path.Combine(projectRoot, "Editor", "BakePipeline", "bake_lod.py");
        if (File.Exists(candidate2)) return candidate2;

        throw new FileNotFoundException(
            "Blender script not found. Set BLENDER_BAKE_SCRIPT or place bake_lod.py in the package folder."
        );
    }

    static string FindBlenderExeOrThrow()
    {
        // 1) env var override
        var env = Environment.GetEnvironmentVariable("BLENDER_EXE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env))
            return env;

        // 2) стандартная установка
        string baseDir = @"C:\Program Files\Blender Foundation";
        if (Directory.Exists(baseDir))
        {
            var candidates = Directory.GetDirectories(baseDir)
                .OrderByDescending(d => d)
                .Select(d => Path.Combine(d, "blender.exe"))
                .Where(File.Exists)
                .ToArray();
            if (candidates.Length > 0) return candidates[0];
        }

        throw new FileNotFoundException(
            "Blender не найден. Поставь Blender и/или задай BLENDER_EXE=полный_путь_к_blender.exe"
        );
    }

    static void RunProcess(string exe, string args, string workDir, string logFilePath = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var p = new Process { StartInfo = psi };
        p.Start();

        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (!string.IsNullOrEmpty(logFilePath))
        {
            try
            {
                string header =
                    $"{DateTime.Now:O} {Path.GetFileName(exe)}\n" +
                    $"Args: {args}\n" +
                    $"WorkDir: {workDir}\n";
                string body = header;
                if (!string.IsNullOrWhiteSpace(stdout))
                    body += $"STDOUT:\n{stdout}\n";
                if (!string.IsNullOrWhiteSpace(stderr))
                    body += $"STDERR:\n{stderr}\n";
                body += "----\n";
                File.AppendAllText(logFilePath, body);
            }
            catch
            {
                // ignore log write errors
            }
        }

        if (!string.IsNullOrWhiteSpace(stdout))
            UnityEngine.Debug.Log(stdout);

        if (p.ExitCode != 0)
            throw new Exception($"Process failed (exit {p.ExitCode}).\n{stderr}");

        if (!string.IsNullOrWhiteSpace(stderr))
            UnityEngine.Debug.LogWarning(stderr);
    }

    static void ConfigureNormalMap(string assetPath)
    {
        var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (ti == null) return;
        ti.textureType = TextureImporterType.NormalMap;
        ti.sRGBTexture = false;
        // Если надо — вручную включишь Flip Green Channel в инспекторе
        ti.SaveAndReimport();
    }

    static void ConfigureNonColorTexture(string assetPath)
    {
        var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (ti == null) return;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;
        ti.SaveAndReimport();
    }

    static void ConfigureModelTangents(string modelPath)
    {
        var mi = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (mi == null) return;
        mi.importNormals = ModelImporterNormals.Calculate;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.SaveAndReimport();
    }

    static Material CreateMaterial(string outAssetDir, Texture2D normal, Texture2D ao)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        var mat = new Material(shader);

        if (normal != null)
        {
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normal);
            if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", normal);
            if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1f);
        }

        if (ao != null)
        {
            if (mat.HasProperty("_OcclusionMap")) mat.SetTexture("_OcclusionMap", ao);
            if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", 1f);
        }

        string matPath = $"{outAssetDir}/baked.mat";
        AssetDatabase.CreateAsset(mat, matPath);
        AssetDatabase.ImportAsset(matPath);
        return AssetDatabase.LoadAssetAtPath<Material>(matPath);
    }
}
#endif
