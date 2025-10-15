#if UNITY_EDITOR
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
        public static void Build()
        {

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

                if(Directory.Exists(buildPlayerOptions.locationPathName))
                    Directory.Delete(buildPlayerOptions.locationPathName, true);

                Directory.CreateDirectory(buildPlayerOptions.locationPathName);

                BuildPipeline.BuildPlayer(buildPlayerOptions);

                Debug.Log("[SmartBuilder] Building complete!");
                OnStatusChanged?.Invoke("[SmartBuilder] Building complete!");


            }
            catch (Exception e)
            {
                Debug.LogError("[SmartBuilder] Building error: " + e.Message);
                return;
            }
        }

    }
}
#endif