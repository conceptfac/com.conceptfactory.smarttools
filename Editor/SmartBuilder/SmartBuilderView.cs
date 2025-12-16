#if UNITY_EDITOR
using Concept.UI;
using log4net.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Codice.CM.Common.CmCallContext;

namespace Concept.SmartTools.Editor
{
    [UxmlElement]
    public partial class SmartBuilderView : VisualElement
    {
        private const string UXNLClassName = "SmartBuilderView";
        private const string USSClassName = "smart-build";


        private string m_accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User);
        private string m_secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User);



        private TabNavigation m_tabNavigation;

        //SCENES PANEL
        private VisualElement m_scenesPanel;
        private ScrollView m_scenesScrollView;
        private Button m_buttonNext;
        //BUILDER PANEL
        private VisualElement m_builderPanel;
        private EnumField m_buildTargetEnum;
        private TextField m_builderPathField;
        private CustomToggle m_autoUploadToggle;
        private Button m_buttonSelectPath;

        private Button m_buttonBuild;

        //UPLOADER PANEL
        private VisualElement m_uploaderPanel;
        private EnumField m_ambientTypeEnum;
        private EnumField m_uploadTargetEnum;
        private TextField m_remotePortField;
        private TextField m_bucketNameField;
        private TextField m_accessKeyField;
        private TextField m_secretKeyField;

        private Button m_buttonUpload;


        //PROGRESS OVERLAY
        private VisualElement m_progressOverlay;
        private Label m_progressStatusLabel;
        private VisualElement m_progressPanel;
        private VisualElement m_progressBar;
        private Label m_progressLabel;
        private Button m_buttonCancel;

        public SmartBuilderView()
        {
            AddToClassList(USSClassName);
            var visualTree = Resources.Load<VisualTreeAsset>(UXNLClassName);
            if (visualTree == null)
            {
                Debug.LogError($"[SmartBuilderView] {UXNLClassName} não encontrado em Resources!");
                return;
            }

            visualTree.CloneTree(this);

            dataSource = SmartBuilderConfig.instance;

            m_tabNavigation = this.Q<TabNavigation>();
            m_scenesPanel = this.Q<VisualElement>("ScenesPanel");

            m_tabNavigation.SetTabsContent(new System.Collections.Generic.List<(string, VisualElement)>()
            {
                ("Scenes", m_scenesPanel),
                ("Builder", this.Q<VisualElement>("BuilderPanel")),
                ("Uploader", this.Q<VisualElement>("UploaderPanel"))
            });

            m_scenesScrollView = this.Q<ScrollView>("ScenesScrollView");
            LoadSceneList(m_scenesScrollView);

            m_buttonNext = this.Q<Button>("ButtonNext");
            m_buttonNext.clicked += OnNextClicked;

            m_buttonBuild = this.Q<Button>("ButtonBuild");
            m_buttonBuild.clicked += OnBuildClicked;


            m_progressOverlay = this.Q<VisualElement>("ProgressOverlay");
            m_progressStatusLabel = this.Q<Label>("ProgressStatusLabel");
            m_progressPanel = this.Q<VisualElement>("ProgressPanel");
            m_progressBar = this.Q<VisualElement>("ProgressBar");
            m_progressLabel = this.Q<Label>("ProgressLabel");
            m_buttonCancel = this.Q<Button>("ButtonCancel");
            m_buttonCancel.clicked += CancelCurrentProgress;

      //  string m_accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
      //  string m_secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");


        m_accessKeyField = this.Q<TextField>("AccessKeyTextField");
            m_accessKeyField.value = m_accessKey;
            m_accessKeyField.RegisterValueChangedCallback(evt =>
            {
                m_accessKey = evt.newValue;
                Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", evt.newValue, EnvironmentVariableTarget.User);
            });
            m_secretKeyField = this.Q<TextField>("SecretKeyTextField");
            m_secretKeyField.value = m_secretKey;
            m_secretKeyField.RegisterValueChangedCallback(evt =>
            {
               m_secretKey= evt.newValue;
                Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", evt.newValue, EnvironmentVariableTarget.User);
                PlayerPrefs.Save();
            });


            m_buttonUpload = this.Q<Button>("ButtonUpload");
            m_buttonUpload.clicked += OnUploadClicked;

            m_remotePortField = this.Q<TextField>("RemotePortTextField");
            m_bucketNameField = this.Q<TextField>("BucketNameTextField");


            this.RegisterCallback<AttachToPanelEvent>(evt => {

                m_progressBar.style.scale = new Vector2(0, 1f);

            });

            /*
                        m_tabNavigation.OnTabSelect += UpdatePanels;
            m_scenesPanel = this.Q<VisualElement>("ScenesPanel");
                        
                        m_builderPanel = this.Q<VisualElement>("BuilderPanel");
                        m_buildTargetEnum = this.Q<EnumField>("BuildTargetEnum");
                        m_buildTargetEnum.RegisterValueChangedCallback(evt =>
                        {
                            SmartBuilderConfig.buildSettings.buildTarget = (BuildTarget)evt.newValue;
                        });
                        m_builderPathField = this.Q<TextField>("BuildPathTextField");
                        m_builderPathField.RegisterValueChangedCallback(evt =>
                        {
                            SmartBuilderConfig.buildSettings.buildPath = evt.newValue;
                        });

                        m_buttonSelectPath = this.Q<Button>("ButtonSelectPath");
                        m_buttonSelectPath.clicked += SelectPath;

                        m_autoUploadToggle = this.Q<CustomToggle>("AutoUploadToggle");
                        m_autoUploadToggle.OnToggleChanged += (c) => SmartBuilderConfig.uploadAfterBuild = c;



                        m_uploaderPanel = this.Q<VisualElement>("UploaderPanel");

                        m_ambientTypeEnum = this.Q<EnumField>("AmbientTypeEnum");
                        m_ambientTypeEnum.RegisterValueChangedCallback(evt =>
                        {
                            SmartBuilderConfig.uploadSettings.buildType = (BuildType)evt.newValue;
                        });
                        m_uploadTargetEnum = this.Q<EnumField>("UploadTargetEnum");
                        m_uploadTargetEnum.RegisterValueChangedCallback(evt =>
                        {
                            SmartBuilderConfig.uploadSettings.uploadTarget = (SmartUploaderSettings.UploadTarget)evt.newValue;
                        });
                        m_remotePortField.RegisterValueChangedCallback(evt =>
                        {
                            if (int.TryParse(evt.newValue, out int port))
                            {
                                SmartBuilderConfig.uploadSettings.awsRemotePort = port;
                            }
                            else
                            {
                                m_remotePortField.value = evt.previousValue;
                            }
                        });
                        m_bucketNameField.RegisterValueChangedCallback(evt =>
                        {
                            SmartBuilderConfig.uploadSettings.awsBucketName = evt.newValue;
                        });


                        UpdatePanels(m_tabNavigation.index);
            */
        }


        private void OnNextClicked()
        {
            m_tabNavigation.SelectIndex(1);
        }
        private void OnBuildClicked()
        {
            bool buildSuccess = SmartBuilder.Build();

            if (buildSuccess && SmartBuilderConfig.uploadAfterBuild)
            {
                m_tabNavigation.SelectIndex(2);
                OnUploadClicked();
            }
        }
        private async void OnUploadClicked()
        {
            if (!ValidateUploadSettings()) return;


            SmartUploader uploader = new SmartUploader(m_accessKey, m_secretKey, SmartBuilderConfig.uploadSettings.awsBucketName);

            uploader.OnStatusChanged += (status) =>
            {
                m_progressLabel.text = status;
            };

            uploader.OnProgressChanged += (perc) => {

                m_progressBar.style.scale = new Vector2(perc, 1f);

            };

            m_progressStatusLabel.text = "Uploading";
            m_progressPanel.style.display = DisplayStyle.Flex;
            m_progressLabel.text = "Sending files to remote repository...";
            m_progressOverlay.style.display = DisplayStyle.Flex;
            string rootPath = SmartBuilderConfig.buildSettings.buildPath + "/" + SmartBuilderConfig.buildSettings.buildTarget;
            await uploader.UploadFilesAsync(rootPath, $"{SmartBuilderConfig.uploadSettings.awsRemotePort}/");

            await Task.Delay(1000);
            m_progressOverlay.style.display = DisplayStyle.None;

        }
        void LoadSceneList(ScrollView scrollView)
        {
            scrollView.Clear();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                VisualElement sceneItem = new VisualElement();
                sceneItem.AddToClassList("scene-toggle");

                CustomToggle sceneToggle = new CustomToggle() { label = scene.path };
                sceneToggle.SetValue(SmartBuilderConfig.buildSettings.scenesToBuild.Contains(scene.path));
                sceneItem.Add(sceneToggle);


                sceneToggle.OnToggleChanged += (c) =>
                {

                    bool contains = SmartBuilderConfig.buildSettings.scenesToBuild.Contains(scene.path);

                    if (c && !contains)
                        SmartBuilderConfig.buildSettings.scenesToBuild.Add(scene.path);
                    else
                        if (!c && contains)
                        SmartBuilderConfig.buildSettings.scenesToBuild.Remove(scene.path);

                };

                scrollView.Add(sceneItem);
            }
        }

        void CancelCurrentProgress()
        {
            m_progressOverlay.style.display = DisplayStyle.None;
        }


        bool ValidateUploadSettings()
        {

            Version currentVersion = new Version(PlayerSettings.bundleVersion);
            Version lastVersion = new Version(SmartBuilderConfig.uploadSettings.lastVersion);

            if (currentVersion <= lastVersion)
            {

                Version desiredVersion = new Version(lastVersion.Major, lastVersion.Minor, lastVersion.Build + 1);

                bool incrementAndBuildVersion = EditorUtility.DisplayDialog(
   "Upload Build Version Error",
   $"Current build version '{currentVersion}' must be higher than last build version. You need build a new incremented version! Do you want to increment and build it to '{desiredVersion}'?", "Yes", "No"
);


                if (incrementAndBuildVersion)
                {
                    PlayerSettings.bundleVersion = desiredVersion.ToString();
                    m_tabNavigation.SelectIndex(1);
                    OnBuildClicked();
                }
                return false;
            }


            if (string.IsNullOrEmpty(SmartBuilderConfig.uploadSettings.awsBucketName))
            {
                EditorUtility.DisplayDialog("Smart Uploader Error", "AWS Bucket Name is required!", "OK");
                m_tabNavigation.SelectIndex(2);
                m_bucketNameField.Focus();
                return false;
            }

            if (string.IsNullOrEmpty(m_accessKey))
            {
                EditorUtility.DisplayDialog("Smart Uploader Error", "AWS Access Key is required!", "OK");
                m_tabNavigation.SelectIndex(2);
                m_accessKeyField.Focus();
                return false;
            }

            if (string.IsNullOrEmpty(m_secretKey))
            {
                EditorUtility.DisplayDialog("Smart Uploader Error", "AWS Secret Key is required!", "OK");
                m_tabNavigation.SelectIndex(2);
                m_secretKeyField.Focus();
                return false;
            }



            return true;
        }





        public void SelectTab(int tabIndex)
        {
            m_tabNavigation.SelectIndex(tabIndex);
        }

    }
}
#endif