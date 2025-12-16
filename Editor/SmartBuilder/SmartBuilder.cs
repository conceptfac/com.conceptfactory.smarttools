#if UNITY_EDITOR
using Concept.UI;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Concept.SmartTools.Editor
{
    public static class SmartBuilder
    {

        /// <summary>
        /// Fired when the progress of the build changes (0..1).
        /// </summary>
        public static event Action<string> OnStatusChanged;
        public static bool Build()
        {
            if (!ValidateBuildSettings())
            {
                SmartBuilderWindow.Open(1);
                return false;

            }

            Version currentVersion = new Version(PlayerSettings.bundleVersion);
            Version lastVersion = new Version(SmartBuilderConfig.buildSettings.lastVersion);

            if (currentVersion <= lastVersion)
            {

                Version desiredVersion = new Version(lastVersion.Major, lastVersion.Minor, lastVersion.Build + 1);

                bool incrementBuildVersion = EditorUtility.DisplayDialog(
   "Build Version Error",
   $"Current build version '{currentVersion}' must be higher than last build version. Do you want to increment it to '{desiredVersion}'?", "Yes", "No"
);


                if (!incrementBuildVersion) return false;
                PlayerSettings.bundleVersion = desiredVersion.ToString();
            }


            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = SmartBuilderConfig.buildSettings.scenesToBuild.ToArray();
            buildPlayerOptions.options = BuildOptions.None;
            buildPlayerOptions.locationPathName = SmartBuilderConfig.buildSettings.buildPath + "/" + SmartBuilderConfig.buildSettings.buildTarget;
            try
            {
                Debug.Log("[SmartBuilder] Start building.");
                OnStatusChanged?.Invoke("[SmartBuilder] Start building.");

                switch (SmartBuilderConfig.buildSettings.buildTarget)
                {
                    case BuildTarget.LinuxHeadlessSimulation:
                    case BuildTarget.StandaloneLinux64:
                        buildPlayerOptions.target = BuildTarget.StandaloneLinux64;
                        buildPlayerOptions.subtarget = (int)StandaloneBuildSubtarget.Server;
                        buildPlayerOptions.locationPathName += "/run.x86_64";
                        break;
                    default:
                        buildPlayerOptions.target = SmartBuilderConfig.buildSettings.buildTarget;
                        break;
                }

                if (Directory.Exists(buildPlayerOptions.locationPathName))
                    Directory.Delete(buildPlayerOptions.locationPathName, true);

                Directory.CreateDirectory(buildPlayerOptions.locationPathName);

                BuildPipeline.BuildPlayer(buildPlayerOptions);

                Debug.Log("[SmartBuilder] Building complete!");
                OnStatusChanged?.Invoke("[SmartBuilder] Building complete!");
                SmartBuilderConfig.buildSettings.lastVersion = PlayerSettings.bundleVersion;
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


            //Check current build target

            if (SmartBuilderConfig.buildSettings.buildTarget != EditorUserBuildSettings.activeBuildTarget)
            {
                bool changeBuildTarget = EditorUtility.DisplayDialog(
                   "Incompatible Build Platforms",
                   $"Current build target is {EditorUserBuildSettings.activeBuildTarget}. Do you want to switch to {SmartBuilderConfig.buildSettings.buildTarget}?", "Yes", "No"
               );

                if (changeBuildTarget)
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(SmartBuilderConfig.buildSettings.buildTarget);

                }
                else
                    SmartBuilderConfig.buildSettings.buildTarget = EditorUserBuildSettings.activeBuildTarget;
                return false;
            }



            if (SmartBuilderConfig.buildSettings.buildTarget == BuildTarget.NoTarget)
            {
                EditorUtility.DisplayDialog("Smart Builder Error", "Select a Build Target!", "OK");
                SmartBuilderWindow.Open(1);
                return false;
            }

            if (SmartBuilderConfig.buildSettings.scenesToBuild.Count == 0)
            {
                EditorUtility.DisplayDialog("Smart Builder Error", "None scenes to build selected!", "OK");
                SmartBuilderWindow.Open(0);
                return false;
            }

            string buildPath = SmartBuilderConfig.buildSettings.buildPath;

            // resolve o caminho absoluto
            string fullPath = Path.IsPathRooted(buildPath)
                ? Path.GetFullPath(buildPath)
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", buildPath));

            // se não existir ou estiver vazio, abre janela
            if (string.IsNullOrEmpty(buildPath) || !Directory.Exists(fullPath))
            {

                string newPath = LoadPath();

                if (string.IsNullOrEmpty(newPath))
                {
                    Debug.LogWarning("[SmartBuilder] Build canceled: no folder selected.");
                    return false;
                }
                SmartBuilderConfig.buildSettings.buildPath = newPath;
            }


            return true;
        }


        public static void SelectPath()
        {
            string newPath = LoadPath();
            if (!string.IsNullOrEmpty(newPath))
            {
                SmartBuilderConfig.buildSettings.buildPath = newPath;
            }
        }

        public static string LoadPath()
        {

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            string buildDir = Path.Combine(projectRoot, "Build");
            if (!Directory.Exists(buildDir))
                Directory.CreateDirectory(buildDir);

            string newPath = EditorUtility.SaveFolderPanel("Select Build Folder", buildDir, "");


            var fullPath = Path.GetFullPath(newPath);

            // se estiver dentro do projeto, salva relativo
            if (fullPath.StartsWith(projectRoot))
            {
                string relative = Path.GetRelativePath(projectRoot, fullPath).Replace("\\", "/");
                if (!relative.StartsWith("../")) relative = "../" + relative;
                return relative;
            }
            else
            {
                return fullPath;
            }
        }

    }
}
#endif