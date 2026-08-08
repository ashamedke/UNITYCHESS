using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// CI build script invoked by game-ci/unity-builder via -buildMethod.
/// Builds the Android APK using PlayerSettings already configured in
/// ProjectSettings.asset (landscape, IL2CPP, ARM64, API 24–33).
///
/// The CI calls:
///   Unity.exe -batchmode -nographics -quit
///     -buildTarget Android
///     -buildMethod UnityBuilderAction.BuildScript.Build
///     -customBuildPath build/Android
/// </summary>
namespace UnityBuilderAction
{
    public static class BuildScript
    {
        public static void Build()
        {
            // Collect environment / custom parameters passed by game-ci
            string buildPath = GetArg("-customBuildPath", "build/Android");
            string buildName = GetArg("-buildName", "ChessGodWAP");

            Debug.Log($"[Build] Starting Android build → {buildPath}/{buildName}.apk");

            // Ensure output directory exists
            System.IO.Directory.CreateDirectory(buildPath);

            // Scenes to include (matches EditorBuildSettings.asset)
            string[] scenes = System.Array.ConvertAll(
                EditorBuildSettings.scenes, s => s.path);

            var options = new BuildPlayerOptions
            {
                scenes           = scenes,
                locationPathName = $"{buildPath}/{buildName}.apk",
                target           = BuildTarget.Android,
                options          = BuildOptions.None
            };

            // Apply scripting backend & architecture
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Android, ScriptingImplementation.Mono2x);
            
            // Mono only supports ARMv7 on Android
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion       = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion    = (AndroidSdkVersions)33;

            // Orientation: landscape only
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft  = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Keystore from environment secrets (set by game-ci action)
            string keystoreBase64 = System.Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_BASE64");
            if (!string.IsNullOrEmpty(keystoreBase64))
            {
                string keystorePath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), "chessgod.jks");
                System.IO.File.WriteAllBytes(
                    keystorePath,
                    System.Convert.FromBase64String(keystoreBase64));

                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keystorePass =
                    System.Environment.GetEnvironmentVariable("ANDROID_STORE_PASS") ?? "";
                PlayerSettings.Android.keyaliasName =
                    System.Environment.GetEnvironmentVariable("ANDROID_KEY_ALIAS") ?? "";
                PlayerSettings.Android.keyaliasPass =
                    System.Environment.GetEnvironmentVariable("ANDROID_KEY_PASS") ?? "";

                Debug.Log("[Build] Signing APK with keystore: " + keystorePath);
            }
            else
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[Build] No keystore provided — building unsigned/debug APK");
            }

            // Build
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Build] ✅ SUCCESS — {summary.totalSize / 1024 / 1024}MB " +
                          $"in {summary.totalTime.TotalSeconds:F0}s");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[Build] ❌ FAILED: {summary.result}");
                EditorApplication.Exit(1);
            }
        }

        private static string GetArg(string name, string fallback = "")
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == name && i + 1 < args.Length)
                    return args[i + 1];
            return fallback;
        }
    }
}
