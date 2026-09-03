using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if UNITY_IOS
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

/// <summary>
/// CI'nin komut satirindan cagirdigi build giris noktasi.
/// Bu dosya "Assets/Editor/" altinda durmak zorundadir.
/// </summary>
public static class CIBuild
{
    public static void BuildIOS()
    {
        string outputDir = GetArgument("-outputDir");
        if (string.IsNullOrEmpty(outputDir))
        {
            Fail("-outputDir argumani verilmedi.");
            return;
        }

        ApplyVersionFromCI();

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Fail("Build Settings icinde etkin sahne yok.");
            return;
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputDir,
            target = BuildTarget.iOS,
            targetGroup = BuildTargetGroup.iOS,
            options = BuildOptions.None
        });

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[CIBuild] Basarili. Sure: {report.summary.totalTime}");
            EditorApplication.Exit(0);
            return;
        }

        foreach (var step in report.steps)
        {
            foreach (var message in step.messages)
            {
                if (message.type == LogType.Error || message.type == LogType.Exception)
                {
                    Debug.LogError($"[CIBuild] {step.name}: {message.content}");
                }
            }
        }

        Fail($"Build basarisiz: {report.summary.result}, {report.summary.totalErrors} hata");
    }

    /// <summary>
    /// Surum tag'den, build numarasi pipeline sayacindan gelir.
    /// TestFlight ayni build numarasini iki kez kabul etmez.
    /// </summary>
    private static void ApplyVersionFromCI()
    {
        string tag = Environment.GetEnvironmentVariable("CI_COMMIT_TAG");
        if (!string.IsNullOrEmpty(tag) && tag.StartsWith("ios_"))
        {
            PlayerSettings.bundleVersion = tag.Substring("ios_".Length);
        }

        string pipelineId = Environment.GetEnvironmentVariable("CI_PIPELINE_IID");
        if (!string.IsNullOrEmpty(pipelineId))
        {
            PlayerSettings.iOS.buildNumber = pipelineId;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[CIBuild] Surum {PlayerSettings.bundleVersion} ({PlayerSettings.iOS.buildNumber})");
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    // Exit(1) olmadan Unity basarisiz build'de bile 0 donebilir ve bozuk bir
    // proje TestFlight'a gidebilir.
    private static void Fail(string message)
    {
        Debug.LogError($"[CIBuild] {message}");
        EditorApplication.Exit(1);
    }

#if UNITY_IOS
    /// <summary>
    /// Ihracat uyumlulugu beyanini Info.plist'e yazar. Bu anahtar olmadan her
    /// build App Store Connect'te "Missing Compliance" durumunda bekler ve elle
    /// onaylanana kadar test cihazlarina dagitilamaz.
    ///
    /// false = uygulama yalnizca muaf sifreleme kullaniyor (HTTPS, iOS'un kendi
    /// kripto API'leri). Kendi ozel sifreleme algoritmanizi eklerseniz bu deger
    /// artik dogru olmaz.
    /// </summary>
    [PostProcessBuild(999)]
    public static void SetExportCompliance(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS)
        {
            return;
        }

        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        if (!File.Exists(plistPath))
        {
            Debug.LogWarning($"[CIBuild] Info.plist bulunamadi: {plistPath}");
            return;
        }

        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
        plist.WriteToFile(plistPath);

        Debug.Log("[CIBuild] ITSAppUsesNonExemptEncryption = false yazildi");
    }
#endif
}
