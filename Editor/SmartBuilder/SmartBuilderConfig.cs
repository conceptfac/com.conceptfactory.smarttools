#if UNITY_EDITOR
using Amazon;
using Amazon.S3;
using Concept.UI;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Concept.SmartTools
{
    public enum AmbientType { DEV, TEST, LOCAL };

    [CreateAssetMenu(fileName = "SmartBuilderConfig", menuName = "Tools/Concept Factory/Smart Builder Config Preset")]
    [Serializable]
    public class SmartBuilderConfig : ScriptableObject
    {
        private static SmartBuilderConfig m_instance;
        public static SmartBuilderConfig instance
        {
            get
            {
                if (m_instance == null) m_instance = LoadSmartBuilderConfig();
                return m_instance;
            }
        }
        [SerializeField] private SmartBuilderSettings m_buildSettings = new SmartBuilderSettings();
        public static SmartBuilderSettings buildSettings => instance.m_buildSettings;

        [SerializeField] private bool m_uploadAfterBuild;
        public static bool uploadAfterBuild { get => instance.m_uploadAfterBuild; set { 
            
            instance.m_uploadAfterBuild = value;
            } }

        [SerializeField] private SmartUploaderSettings m_uploadSettings = new SmartUploaderSettings();
        public static SmartUploaderSettings uploadSettings => instance.m_uploadSettings;
        private static SmartBuilderConfig LoadSmartBuilderConfig()
        {
            if (AssetDatabase.IsValidFolder("Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

            string fileName = "SmartBuilderConfig";
            string assetPath = "Assets/Resources/" + fileName + ".asset";
            SmartBuilderConfig preset = AssetDatabase.LoadAssetAtPath<SmartBuilderConfig>(assetPath);

            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<SmartBuilderConfig>();
                AssetDatabase.CreateAsset(preset, assetPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Novo preset 'SmartBuilderConfig' criado e salvo em: " + assetPath);
            }

            return AssetDatabase.LoadAssetAtPath<SmartBuilderConfig>(assetPath);

        }
    }


    [Serializable]
    public class SmartBuilderSettings
    {
        public BuildTarget buildTarget = BuildTarget.NoTarget;
        [PathPicker("Builds")]
        public string buildPath;

        public List<string> scenesToBuild = new List<string>();
        
    }

    [Serializable]
    public class SmartUploaderSettings
    {
        public AmbientType ambientType = AmbientType.DEV;
        public enum UploadTarget { SFTP, AWSS3 }
        public UploadTarget uploadTarget;
        public int awsRemotePort;
        public string awsBucketName;
        public RegionEndpoint bucketRegion = RegionEndpoint.USEast1;
        public IAmazonS3 s3Client;
    }
}
#endif