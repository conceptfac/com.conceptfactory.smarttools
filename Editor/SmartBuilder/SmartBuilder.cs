#if UNITY_EDITOR
using Concept.SmartTools;
using Concept.UI;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Concept.SmartTools.Editor
{
    public static class SmartBuilder
    {
        public static event Action<string> OnStatusChanged;

        public static bool Build()
        {
            if (!ValidateBuildSettings())
            {
                SmartBuilderWindow.OpenOriginal(1);
                return false;
            }

            Version currentVersion = new Version(PlayerSettings.bundleVersion);
            Version lastVersion = new Version(SmartBuilderConfig.instance.m_buildSettings.lastVersion);

            if (lastVersion < currentVersion)
            {
                Version desiredVersion = IncrementPatchVersion(currentVersion);

                bool incrementProjectVersion = EditorUtility.DisplayDialog(
                    "Build Version Update",
                    $"Build Settings last version '{lastVersion}' is lower than project version '{currentVersion}'. Do you want to increment the project version to '{desiredVersion}' before building?",
                    "Yes",
                    "No");

                if (!incrementProjectVersion)
                    return false;

                currentVersion = ApplyProjectVersion(desiredVersion);
            }
            else if (currentVersion <= lastVersion)
            {
                Version desiredVersion = IncrementPatchVersion(lastVersion);

                bool incrementBuildVersion = EditorUtility.DisplayDialog(
                    "Build Version Error",
                    $"Current build version '{currentVersion}' must be higher than last build version. Do you want to increment it to '{desiredVersion}'?",
                    "Yes",
                    "No");

                if (!incrementBuildVersion)
                    return false;

                currentVersion = ApplyProjectVersion(desiredVersion);
            }

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledBuildScenes(),
                options = BuildOptions.None,
                locationPathName = GetCurrentBuildLocation()
            };

            try
            {
                Debug.Log("[SmartBuilder] Start building.");
                OnStatusChanged?.Invoke("[SmartBuilder] Start building.");

                switch (EditorUserBuildSettings.activeBuildTarget)
                {
                    case BuildTarget.LinuxHeadlessSimulation:
                    case BuildTarget.StandaloneLinux64:
                        buildPlayerOptions.target = BuildTarget.StandaloneLinux64;
                        buildPlayerOptions.subtarget = (int)StandaloneBuildSubtarget.Server;
                        break;
                    default:
                        buildPlayerOptions.target = EditorUserBuildSettings.activeBuildTarget;
                        break;
                }

                BuildPipeline.BuildPlayer(buildPlayerOptions);

                Debug.Log("[SmartBuilder] Building complete!");
                OnStatusChanged?.Invoke("[SmartBuilder] Building complete!");
                SmartBuilderConfig.instance.m_buildSettings.lastVersion = currentVersion.ToString();
                EditorUtility.SetDirty(SmartBuilderConfig.instance);
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[SmartBuilder] Building error: " + e.Message);
                return false;
            }
        }

        static bool ValidateBuildSettings()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.NoTarget)
            {
                EditorUtility.DisplayDialog("Smart Builder Error", "Select a Build Target!", "OK");
                SmartBuilderWindow.OpenOriginal(1);
                return false;
            }

            if (GetEnabledBuildScenes().Length == 0)
            {
                EditorUtility.DisplayDialog("Smart Builder Error", "None scenes to build selected!", "OK");
                SmartBuilderWindow.OpenOriginal(0);
                return false;
            }

            if (string.IsNullOrWhiteSpace(GetCurrentBuildLocation()))
            {
                string newPath = SelectBuildLocation();

                if (string.IsNullOrEmpty(newPath))
                {
                    Debug.LogWarning("[SmartBuilder] Build canceled: no build location selected.");
                    return false;
                }
            }

            return true;
        }

        public static string GetCurrentBuildLocation()
        {
            return EditorUserBuildSettings.GetBuildLocation(EditorUserBuildSettings.activeBuildTarget);
        }

        public static string GetCurrentBuildRootPath()
        {
            string buildLocation = GetCurrentBuildLocation();
            if (string.IsNullOrWhiteSpace(buildLocation))
                return string.Empty;

            return Path.HasExtension(buildLocation)
                ? Path.GetDirectoryName(buildLocation)
                : buildLocation;
        }

        public static string SelectBuildLocation()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string lastLocation = EditorUserBuildSettings.GetBuildLocation(target);
            string initialDirectory = string.IsNullOrEmpty(lastLocation)
                ? Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Build")
                : Path.GetDirectoryName(lastLocation);
            string defaultName = GetDefaultBuildName(lastLocation, target);

            if (!Directory.Exists(initialDirectory))
                Directory.CreateDirectory(initialDirectory);

            string location = IsFolderBuildTarget(target)
                ? EditorUtility.SaveFolderPanel($"Build {target}", initialDirectory, defaultName)
                : EditorUtility.SaveFilePanel($"Build {target}", initialDirectory, defaultName, GetBuildExtension(target));

            if (string.IsNullOrWhiteSpace(location))
                return string.Empty;

            EditorUserBuildSettings.SetBuildLocation(target, location);
            return location;
        }

        public static void SelectPath()
        {
            SelectBuildLocation();
        }

        public static string LoadPath()
        {
            return SelectBuildLocation();
        }

        private static bool IsFolderBuildTarget(BuildTarget target)
        {
            return target == BuildTarget.WebGL
                || target == BuildTarget.iOS
                || target == BuildTarget.WSAPlayer;
        }

        private static string GetBuildExtension(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return EditorUserBuildSettings.buildAppBundle ? "aab" : "apk";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "exe";
                case BuildTarget.StandaloneOSX:
                    return "app";
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.LinuxHeadlessSimulation:
                    return "x86_64";
                default:
                    return string.Empty;
            }
        }

        private static string GetDefaultBuildName(string lastLocation, BuildTarget target)
        {
            if (!string.IsNullOrWhiteSpace(lastLocation))
            {
                if (IsFolderBuildTarget(target))
                    return Path.GetFileName(lastLocation);

                return Path.GetFileNameWithoutExtension(lastLocation);
            }

            return Application.productName;
        }

        private static Version IncrementPatchVersion(Version version)
        {
            int build = version.Build < 0 ? 0 : version.Build;
            return new Version(version.Major, version.Minor, build + 1);
        }

        private static Version ApplyProjectVersion(Version version)
        {
            PlayerSettings.bundleVersion = version.ToString();
            return new Version(PlayerSettings.bundleVersion);
        }

        internal static string[] GetEnabledBuildScenes()
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            List<string> enabledScenes = new List<string>(buildScenes.Length);

            for (int i = 0; i < buildScenes.Length; i++)
            {
                EditorBuildSettingsScene scene = buildScenes[i];
                if (scene == null || !scene.enabled || string.IsNullOrWhiteSpace(scene.path))
                    continue;

                enabledScenes.Add(scene.path);
            }

            return enabledScenes.ToArray();
        }
    }
}
#endif
