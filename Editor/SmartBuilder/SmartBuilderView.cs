#if UNITY_EDITOR
using Concept.UI;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Concept.SmartTools.Editor
{
    [UxmlElement]
    public partial class SmartBuilderView : VisualElement
    {
        private const string UXNLClassName = "SmartBuilderView";
        private const string USSClassName = "smart-build";

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
            /*
                        m_tabNavigation.OnTabSelect += UpdatePanels;
            m_scenesPanel = this.Q<VisualElement>("ScenesPanel");
                        m_scenesScrollView = this.Q<ScrollView>("ScenesScrollView");
                        m_buttonNext = this.Q<Button>("ButtonNext");
                        m_buttonNext.clicked += OnNextClicked;
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



                        m_buttonBuild = this.Q<Button>("ButtonBuild");
                        m_buttonBuild.clicked += OnBuildClicked;
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
                        m_remotePortField = this.Q<TextField>("RemotePortTextField");
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
                        m_bucketNameField = this.Q<TextField>("BucketNameTextField");
                        m_bucketNameField.RegisterValueChangedCallback(evt =>
                        {
                            SmartBuilderConfig.uploadSettings.awsBucketName = evt.newValue;
                        });
                        m_accessKeyField = this.Q<TextField>("AccessKeyTextField");
                        m_accessKeyField.RegisterValueChangedCallback(evt =>
                        {
                        });
                        m_secretKeyField = this.Q<TextField>("SecretKeyTextField");
                        m_secretKeyField.RegisterValueChangedCallback(evt =>
                        {
                        });

                        m_buttonUpload = this.Q<Button>("ButtonUpload");
                        m_buttonUpload.clicked += OnUploadClicked;
                        m_progressOverlay = this.Q<VisualElement>("ProgressOverlay");
                        m_progressStatusLabel = this.Q<Label>("ProgressStatusLabel");
                        m_progressPanel = this.Q<VisualElement>("ProgressPanel");
                        m_progressBar = this.Q<VisualElement>("ProgressBar");
                        m_progressLabel = this.Q<Label>("ProgressLabel");
                        m_buttonCancel = this.Q<Button>("ButtonCancel");
                        m_buttonCancel.clicked += CancelCurrentProgress;

                        UpdatePanels(m_tabNavigation.index);
            */
        }


        private void OnNextClicked()
        {
            m_tabNavigation.SelectIndex(1);
        }
        private void OnBuildClicked()
        {
            if (!ValidateBuildSettings()) return;

            m_progressStatusLabel.text = "Building Project";
            m_progressPanel.style.display = DisplayStyle.None;
            m_progressLabel.text = "See building dialog status...";
            m_progressOverlay.style.display = DisplayStyle.Flex;

            SmartBuilder.Build();

            m_progressOverlay.style.display = DisplayStyle.None;

            if (SmartBuilderConfig.uploadAfterBuild)
            {
                m_tabNavigation.SelectIndex(2);
                OnUploadClicked();
            }

        }
        private void OnUploadClicked()
        {
            m_progressStatusLabel.text = "Uploading";
            m_progressPanel.style.display = DisplayStyle.Flex;
            m_progressLabel.text = "Sending files to remote repository...";
            m_progressOverlay.style.display = DisplayStyle.Flex;
        }
        void UpdatePanels(int index)
        {
            m_scenesPanel.style.display = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            m_builderPanel.style.display = index == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            m_uploaderPanel.style.display = index == 2 ? DisplayStyle.Flex : DisplayStyle.None;

            if (index == 0)
            {
                m_scenesScrollView.Clear();
                foreach (var scene in EditorBuildSettings.scenes)
                {
                    CustomToggle sceneToggle = new CustomToggle() { text = scene.path, IsChecked = SmartBuilderConfig.buildSettings.scenesToBuild.Contains(scene.path), IsLabelLeft = false };

                    sceneToggle.AddToClassList("scene-toggle");

                    sceneToggle.OnToggleChanged += (c) =>
                    {

                        bool contains = SmartBuilderConfig.buildSettings.scenesToBuild.Contains(scene.path);

                        if (c && !contains)
                            SmartBuilderConfig.buildSettings.scenesToBuild.Add(scene.path);
                        else
                            if (!c && contains)
                            SmartBuilderConfig.buildSettings.scenesToBuild.Remove(scene.path);

                    };

                    m_scenesScrollView.Add(sceneToggle);
                }
            }
            else

            if (index == 1)
            {
                m_buildTargetEnum.value = SmartBuilderConfig.buildSettings.buildTarget;
                m_builderPathField.value = SmartBuilderConfig.buildSettings.buildPath;
                m_autoUploadToggle.IsChecked = SmartBuilderConfig.uploadAfterBuild;
            }
            else if (index == 2)
            {
                m_ambientTypeEnum.value = SmartBuilderConfig.uploadSettings.buildType;
                m_uploadTargetEnum.value = SmartBuilderConfig.uploadSettings.uploadTarget;
                m_remotePortField.value = SmartBuilderConfig.uploadSettings.awsRemotePort.ToString();
                m_bucketNameField.value = SmartBuilderConfig.uploadSettings.awsBucketName;
                //m_accessKeyField.value = SmartBuilderConfig.uploadSettings.awsAccessKey;
                //m_secretKeyField.value = SmartBuilderConfig.uploadSettings.awsSecretKey;
            }

        }

        void CancelCurrentProgress()
        {
            m_progressOverlay.style.display = DisplayStyle.None;
        }

        bool ValidateBuildSettings()
        {

            if (SmartBuilderConfig.buildSettings.buildTarget == BuildTarget.NoTarget)
            {
                EditorUtility.DisplayDialog("Smart Builder Error", "Select a Build Target!", "OK");
                m_tabNavigation.SelectIndex(1);
                return false;
            }

            if (SmartBuilderConfig.buildSettings.scenesToBuild.Count == 0)
            {
                EditorUtility.DisplayDialog("Smart Builder Error", "None scenes to build selected!", "OK");
                m_tabNavigation.SelectIndex(0);
                return false;
            }

            string buildPath = SmartBuilderConfig.buildSettings.buildPath;

            // resolve o caminho absoluto
            string fullPath = Path.IsPathRooted(buildPath)
                ? Path.GetFullPath(buildPath)
                : Path.GetFullPath(Path.Combine(Application.dataPath, buildPath));

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

        private void SelectPath()
        {
            string newPath = LoadPath();
            if (!string.IsNullOrEmpty(newPath))
            {
                m_builderPathField.value = newPath;
            }
        }

        private string LoadPath()
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