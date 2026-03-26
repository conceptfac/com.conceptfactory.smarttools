#if UNITY_EDITOR
using Amazon;
using Amazon.S3;
using Concept.UI;
using System;
using UnityEditor;
using UnityEngine;

namespace Concept.SmartTools
{

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
        [SerializeField] internal SmartBuilderSettings m_buildSettings = new SmartBuilderSettings();

        [SerializeField] private bool m_uploadAfterBuild;
        public static bool uploadAfterBuild { get => instance.m_uploadAfterBuild; set { 
            
            instance.m_uploadAfterBuild = value;
            } }

        [SerializeField] internal SmartUploaderSettings m_uploadSettings = new SmartUploaderSettings();
        private static SmartBuilderConfig LoadSmartBuilderConfig()
        {
            string resourcesPath = "Assets/Resources/";
            string fileName = "SmartBuilderConfig";
            string assetPath = resourcesPath + fileName + ".asset";

            if (!AssetDatabase.IsValidFolder(resourcesPath))
                AssetDatabase.CreateFolder("Assets", "Resources");

            SmartBuilderConfig preset = AssetDatabase.LoadAssetAtPath<SmartBuilderConfig>(assetPath);

            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<SmartBuilderConfig>();
                preset.m_uploadSettings.remoteSubDirectory = GetDefaultRemoteSubdirectory();
                AssetDatabase.CreateAsset(preset, assetPath);
                AssetDatabase.SaveAssets();
                Debug.Log("Novo preset 'SmartBuilderConfig' criado e salvo em: " + assetPath);
            }
            else if (string.IsNullOrWhiteSpace(preset.m_uploadSettings.remoteSubDirectory))
            {
                preset.m_uploadSettings.remoteSubDirectory = GetDefaultRemoteSubdirectory();
                EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssets();
            }

            return AssetDatabase.LoadAssetAtPath<SmartBuilderConfig>(assetPath);
        }

        private static string GetDefaultRemoteSubdirectory()
        {
            string productName = Application.productName;
            if (string.IsNullOrWhiteSpace(productName))
                return "project";

            System.Text.StringBuilder builder = new System.Text.StringBuilder(productName.Length);
            bool previousWasSeparator = false;

            for (int i = 0; i < productName.Length; i++)
            {
                char c = char.ToLowerInvariant(productName[i]);
                bool isAllowed = char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-';
                if (isAllowed)
                {
                    builder.Append(c);
                    previousWasSeparator = false;
                    continue;
                }

                if (previousWasSeparator)
                    continue;

                builder.Append('-');
                previousWasSeparator = true;
            }

            string sanitized = builder.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(sanitized) ? "project" : sanitized;
        }
    }


    [Serializable]
    public class SmartBuilderSettings
    {
        //[HideInInspector]
        public string lastVersion = "0.0.0";
    }

    [Serializable]
    public class SmartUploaderSettings
    {
        public const string RootSubdirectoryToken = "[root]";
       // [HideInInspector]
        public string lastVersion = "0.0.0";
        public BuildType buildType = BuildType.DEVELOPMENT;
        public enum UploadTarget { SFTP, AWSS3 }
        public enum SftpAuthMode { Password, SshKey }
        public UploadTarget uploadTarget;
        public string remoteDirectory;
        public string remoteSubDirectory;
        public bool cleanUpDirectory;
        public string sftpHost;
        public int sftpPort = 22;
        public string sftpUser;
        public SftpAuthMode sftpAuthMode;
        public int awsRemotePort;
        public string awsBucketName;
        public RegionEndpoint bucketRegion = RegionEndpoint.USEast1;
        public IAmazonS3 s3Client;
    }
}
#endif
