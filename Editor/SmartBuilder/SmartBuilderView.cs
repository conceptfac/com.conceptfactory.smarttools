#if UNITY_EDITOR
using Concept.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static Codice.CM.Common.CmCallContext;

namespace Concept.SmartTools.Editor
{
    [UxmlElement]
    public partial class SmartBuilderView : VisualElement
    {
        private enum MessageOverlayTone
        {
            Neutral,
            Error,
            Warning,
            Success
        }

        private sealed class TextFieldHistoryState
        {
            public readonly List<string> undoStack = new List<string>();
            public readonly List<string> redoStack = new List<string>();
            public bool isApplyingHistory;
        }

        private const string UXNLClassName = "SmartBuilderView";
        private const string OverlayStylesheetResourceName = "SmartBuilderWindowStyles";
        private const string USSClassName = "smart-build";
        private const string SftpPasswordPrefsKey = "Concept.SmartTools.SftpPassword";
        private const string SftpPrivateKeyPathPrefsKey = "Concept.SmartTools.SftpPrivateKeyPath";
        private const string SftpKeyPassphrasePrefsKey = "Concept.SmartTools.SftpKeyPassphrase";
        private const string CustomPrivateKeyDropdownChoice = "Choose custom file...";
        private const string MessageOverlayErrorClass = "message-overlay--error";
        private const string MessageOverlayWarningClass = "message-overlay--warning";
        private const string MessageOverlaySuccessClass = "message-overlay--success";
        private const string HiddenClass = "is-hidden";


        private string m_accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID", EnvironmentVariableTarget.User);
        private string m_secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", EnvironmentVariableTarget.User);
        private readonly Dictionary<TextField, TextFieldHistoryState> m_textFieldHistory = new Dictionary<TextField, TextFieldHistoryState>();



        private TabNavigation m_tabNavigation;
        private Label m_headerTitleLabel;
        private Label m_headerVersionLabel;

        //SCENES PANEL
        private VisualElement m_scenesPanel;
        private ScrollView m_scenesScrollView;
        private Button m_buttonAddOpenScenes;
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
        private TextField m_remoteDirectoryField;
        private TextField m_remoteSubDirectoryField;
        private CustomToggle m_cleanUpDirectoryToggle;
        private VisualElement m_rootUploadHint;
        private Label m_rootUploadHintLabel;
        private VisualElement m_localBuildUploadHint;
        private Label m_localBuildUploadHintLabel;
        private VisualElement m_nonReleaseUploadHint;
        private Label m_nonReleaseUploadHintLabel;
        private TextField m_bucketNameField;
        private TextField m_accessKeyField;
        private TextField m_secretKeyField;
        private TextField m_sftpHostField;
        private IntegerField m_sftpPortField;
        private TextField m_sftpUserField;
        private EnumField m_sftpAuthModeEnum;
        private TextField m_sftpPasswordField;
        private TextField m_sftpPasswordRevealField;
        private Button m_sftpPasswordRevealButton;
        private DropdownField m_sftpPrivateKeyDropdown;
        private TextField m_sftpPrivateKeyPassphraseField;
        private TextField m_sftpPrivateKeyPassphraseRevealField;
        private Button m_sftpPrivateKeyPassphraseRevealButton;
        private VisualElement m_sftpFieldsGroup;
        private VisualElement m_sftpPasswordFieldsGroup;
        private VisualElement m_sftpKeyFieldsGroup;
        private VisualElement m_awsFieldsGroup;
        private bool m_isSftpPasswordVisible;
        private readonly Dictionary<string, string> m_sftpPrivateKeyChoices = new Dictionary<string, string>();
        private string m_lastResolvedPrivateKeyChoice;
        private bool m_blocksHostClose;
        private string m_hostCloseBlockedMessage;

        private Button m_buttonUpload;
        private SmartUploader m_activeUploader;
        private bool m_isCancellingUpload;


        //PROGRESS OVERLAY
        private VisualElement m_progressOverlay;
        private Label m_progressStatusLabel;
        private VisualElement m_progressPanel;
        private VisualElement m_progressBar;
        private Label m_progressLabel;
        private VisualElement m_overallProgressPanel;
        private VisualElement m_overallProgressBar;
        private Label m_progressOverallLabel;
        private Button m_buttonCancel;
        private VisualElement m_messageOverlay;
        private Label m_messageTitleLabel;
        private Label m_messageBodyLabel;
        private Button m_messageCloseButton;
        private VisualElement m_overlayHost;
        private StyleSheet m_overlayStyleSheet;

        public bool BlocksHostClose => m_blocksHostClose;
        public string HostCloseBlockedMessage => m_hostCloseBlockedMessage;
        public event Action<SmartBuilderView> HostCloseGuardChanged;

        public SmartBuilderView()
        {
            AddToClassList(USSClassName);
            var visualTree = Resources.Load<VisualTreeAsset>(UXNLClassName);
            m_overlayStyleSheet = Resources.Load<StyleSheet>(OverlayStylesheetResourceName);
            if (visualTree == null)
            {
                Debug.LogError($"[SmartBuilderView] {UXNLClassName} não encontrado em Resources!");
                return;
            }

            visualTree.CloneTree(this);

            dataSource = SmartBuilderConfig.instance;

            m_tabNavigation = this.Q<TabNavigation>("MainTabNavigation");
            m_headerTitleLabel = this.Q<Label>("HeaderTitleLabel");
            m_headerVersionLabel = this.Q<Label>("HeaderVersionLabel");
            m_scenesPanel = this.Q<VisualElement>("ScenesPanel");
            if (m_tabNavigation == null)
            {
                Debug.LogError("[SmartBuilderView] TabNavigation was not found in SmartBuilderView.uxml.");
                return;
            }

            m_tabNavigation.SetTabsContent(new System.Collections.Generic.List<(string, VisualElement)>()
            {
                ("Scenes", m_scenesPanel),
                ("Builder", this.Q<VisualElement>("BuilderPanel")),
                ("Uploader", this.Q<VisualElement>("UploaderPanel"))
            });

            m_scenesScrollView = this.Q<ScrollView>("ScenesScrollView");
            LoadSceneList(m_scenesScrollView);
            m_buttonAddOpenScenes = this.Q<Button>("ButtonAddOpenScenes");
            m_buildTargetEnum = this.Q<EnumField>("BuildTargetEnum");
            m_builderPathField = this.Q<TextField>("BuildPathTextField");
            m_buttonSelectPath = this.Q<Button>("ButtonSelectPath");
            ConfigureBuildSettingsFields();

            if (m_buttonAddOpenScenes != null)
                m_buttonAddOpenScenes.clicked += OnAddOpenScenesClicked;

            m_buttonNext = this.Q<Button>("ButtonNext");
            m_buttonNext.clicked += OnNextClicked;

            m_buttonBuild = this.Q<Button>("ButtonBuild");
            m_buttonBuild.clicked += OnBuildClicked;


            m_progressOverlay = this.Q<VisualElement>("ProgressOverlay");
            m_progressStatusLabel = this.Q<Label>("ProgressStatusLabel");
            m_progressPanel = this.Q<VisualElement>("ProgressPanel");
            m_progressBar = this.Q<VisualElement>("ProgressBar");
            m_progressLabel = this.Q<Label>("ProgressLabel");
            m_overallProgressPanel = this.Q<VisualElement>("OverallProgressPanel");
            m_overallProgressBar = this.Q<VisualElement>("OverallProgressBar");
            m_progressOverallLabel = this.Q<Label>("ProgressOverallLabel");
            m_buttonCancel = this.Q<Button>("ButtonCancel");
            m_buttonCancel.clicked += CancelCurrentProgress;
            m_messageOverlay = this.Q<VisualElement>("MessageOverlay");
            m_messageTitleLabel = this.Q<Label>("MessageTitleLabel");
            m_messageBodyLabel = this.Q<Label>("MessageBodyLabel");
            m_messageCloseButton = this.Q<Button>("MessageCloseButton");
            if (m_messageCloseButton != null)
                m_messageCloseButton.clicked += HideMessageOverlay;
            m_overlayHost = this;
            EnsureOverlayStyles(m_progressOverlay);
            EnsureOverlayStyles(m_messageOverlay);

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

            m_ambientTypeEnum = this.Q<EnumField>("AmbientTypeEnum");
            m_uploadTargetEnum = this.Q<EnumField>("UploadTargetEnum");
            m_remoteDirectoryField = this.Q<TextField>("RemoteDirectoryTextField");
            m_remoteSubDirectoryField = this.Q<TextField>("RemoteSubDirectoryTextField");
            m_cleanUpDirectoryToggle = this.Q<CustomToggle>("CleanUpDirectoryToggle");
            m_rootUploadHint = this.Q<VisualElement>("RootUploadHint");
            m_rootUploadHintLabel = this.Q<Label>("RootUploadHintLabel");
            m_localBuildUploadHint = this.Q<VisualElement>("LocalBuildUploadHint");
            m_localBuildUploadHintLabel = this.Q<Label>("LocalBuildUploadHintLabel");
            m_nonReleaseUploadHint = this.Q<VisualElement>("NonReleaseUploadHint");
            m_nonReleaseUploadHintLabel = this.Q<Label>("NonReleaseUploadHintLabel");
            ConfigureRemoteSubDirectoryField();
            ConfigureCleanUpDirectoryToggle();
            m_bucketNameField = this.Q<TextField>("BucketNameTextField");
            m_sftpHostField = this.Q<TextField>("SftpHostTextField");
            m_sftpPortField = this.Q<IntegerField>("SftpPortIntegerField");
            m_sftpUserField = this.Q<TextField>("SftpUserTextField");
            m_sftpAuthModeEnum = this.Q<EnumField>("SftpAuthModeEnum");
            m_sftpPasswordField = this.Q<TextField>("SftpPasswordTextField");
            m_sftpPasswordRevealField = this.Q<TextField>("SftpPasswordRevealTextField");
            m_sftpPasswordRevealButton = this.Q<Button>("SftpPasswordRevealButton");
            m_sftpPrivateKeyDropdown = this.Q<DropdownField>("SftpPrivateKeyDropdown");
            m_sftpPrivateKeyPassphraseField = this.Q<TextField>("SftpPrivateKeyPassphraseTextField");
            m_sftpPrivateKeyPassphraseRevealField = this.Q<TextField>("SftpPrivateKeyPassphraseRevealTextField");
            m_sftpPrivateKeyPassphraseRevealButton = this.Q<Button>("SftpPrivateKeyPassphraseRevealButton");
            m_sftpFieldsGroup = this.Q<VisualElement>("SftpFieldsGroup");
            m_sftpPasswordFieldsGroup = this.Q<VisualElement>("SftpPasswordFieldsGroup");
            m_sftpKeyFieldsGroup = this.Q<VisualElement>("SftpKeyFieldsGroup");
            m_awsFieldsGroup = this.Q<VisualElement>("AwsFieldsGroup");
            SetupRevealPasswordField(m_sftpPasswordField, m_sftpPasswordRevealField, m_sftpPasswordRevealButton);
            SetupRevealPasswordField(m_sftpPrivateKeyPassphraseField, m_sftpPrivateKeyPassphraseRevealField, m_sftpPrivateKeyPassphraseRevealButton);
            ConfigurePrivateKeyField();
            LoadLocalSensitiveFields();
            RegisterSensitiveFieldPersistence();
            ConfigureTextFieldUndoRedo();
            ConfigureBuildTypeField();
            ConfigureUploadTargetFields();
            RefreshVersionLabels();
            m_tabNavigation.OnTabSelect += RefreshHeaderForTab;


            this.RegisterCallback<AttachToPanelEvent>(evt => {

                m_progressBar.style.scale = new Vector2(0, 1f);
                if (m_overallProgressBar != null)
                    m_overallProgressBar.style.scale = new Vector2(0, 1f);
                if (m_progressOverallLabel != null)
                    m_progressOverallLabel.text = "0%";
                RefreshBuildSettingsFields();
                RefreshVersionLabels();
                RefreshHeaderForTab(m_tabNavigation.index);

            });

            /*
                        m_tabNavigation.OnTabSelect += UpdatePanels;
            m_scenesPanel = this.Q<VisualElement>("ScenesPanel");
                        
                        m_builderPanel = this.Q<VisualElement>("BuilderPanel");
                        m_buildTargetEnum = this.Q<EnumField>("BuildTargetEnum");
                        m_buildTargetEnum.RegisterValueChangedCallback(evt =>
                        {
                            SmartBuilder.Settings.buildTarget = (BuildTarget)evt.newValue;
                        });
                        m_builderPathField = this.Q<TextField>("BuildPathTextField");
                        m_builderPathField.RegisterValueChangedCallback(evt =>
                        {
                            SmartBuilder.Settings.buildPath = evt.newValue;
                        });

                        m_buttonSelectPath = this.Q<Button>("ButtonSelectPath");
                        m_buttonSelectPath.clicked += SelectPath;

                        m_autoUploadToggle = this.Q<CustomToggle>("AutoUploadToggle");
                        m_autoUploadToggle.OnToggleChanged += (c) => SmartBuilderConfig.uploadAfterBuild = c;



                        m_uploaderPanel = this.Q<VisualElement>("UploaderPanel");

                        m_ambientTypeEnum = this.Q<EnumField>("AmbientTypeEnum");
                        m_ambientTypeEnum.RegisterValueChangedCallback(evt =>
                        {
                            SmartUploader.Settings.buildType = (BuildType)evt.newValue;
                        });
                        m_uploadTargetEnum = this.Q<EnumField>("UploadTargetEnum");
                        m_uploadTargetEnum.RegisterValueChangedCallback(evt =>
                        {
                            SmartUploader.Settings.uploadTarget = (SmartUploaderSettings.UploadTarget)evt.newValue;
                        });
                        m_remotePortField.RegisterValueChangedCallback(evt =>
                        {
                            if (int.TryParse(evt.newValue, out int port))
                            {
                                SmartUploader.Settings.awsRemotePort = port;
                            }
                            else
                            {
                                m_remotePortField.value = evt.previousValue;
                            }
                        });
                        m_bucketNameField.RegisterValueChangedCallback(evt =>
                        {
                            SmartUploader.Settings.awsBucketName = evt.newValue;
                        });


                        UpdatePanels(m_tabNavigation.index);
            */
        }


        private void OnNextClicked()
        {
            m_tabNavigation.SelectIndex(1);
        }

        private void OnAddOpenScenesClicked()
        {
            int addedCount = AddOpenScenesToBuildSettings();
            LoadSceneList(m_scenesScrollView);

            if (addedCount > 0)
                ShowMessageOverlay("Scenes Added", $"{addedCount} open scene(s) were added to Build Settings.", MessageOverlayTone.Success);
            else
                ShowMessageOverlay("No Scenes Added", "All open scenes are already present in Build Settings.", MessageOverlayTone.Warning);
        }

        private void ConfigureBuildSettingsFields()
        {
            if (m_buildTargetEnum != null)
            {
                m_buildTargetEnum.Init(EditorUserBuildSettings.activeBuildTarget);
                m_buildTargetEnum.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is not BuildTarget buildTarget)
                        return;

                    if (buildTarget != EditorUserBuildSettings.activeBuildTarget)
                    {
                        bool confirmSwitch = EditorUtility.DisplayDialog(
                            "Change Project Platform",
                            $"Do you want to switch the active project platform from {EditorUserBuildSettings.activeBuildTarget} to {buildTarget}?",
                            "Switch Platform",
                            "Cancel");

                        if (confirmSwitch)
                        {
                            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
                            EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);
                        }
                    }

                    RefreshBuildSettingsFields();
                });
            }

            if (m_builderPathField != null)
                m_builderPathField.isReadOnly = true;

            if (m_buttonSelectPath != null)
            {
                m_buttonSelectPath.clicked += () =>
                {
                    SmartBuilder.SelectBuildLocation();
                    RefreshBuildSettingsFields();
                };
            }

            RefreshBuildSettingsFields();
        }

        private void RefreshBuildSettingsFields()
        {
            if (m_buildTargetEnum != null)
                m_buildTargetEnum.SetValueWithoutNotify(EditorUserBuildSettings.activeBuildTarget);

            if (m_builderPathField != null)
                m_builderPathField.SetValueWithoutNotify(SmartBuilder.GetCurrentBuildLocation());
        }

        private void RefreshVersionLabels()
        {
            RefreshHeaderForTab(m_tabNavigation?.index ?? 0);
        }

        private void RefreshHeaderForTab(int tabIndex)
        {
            if (m_headerTitleLabel == null || m_headerVersionLabel == null)
                return;

            switch (tabIndex)
            {
                case 1:
                    m_headerTitleLabel.text = "SMART BUILDER - Build Settings";
                    m_headerVersionLabel.text = $"Last Build Version: {SmartBuilderConfig.instance.m_buildSettings.lastVersion}";
                    break;
                case 2:
                    m_headerTitleLabel.text = "SMART BUILDER - Upload Settings";
                    m_headerVersionLabel.text = $"Last Upload Version: {SmartUploader.Settings.lastVersion}";
                    break;
                default:
                    m_headerTitleLabel.text = "SMART BUILDER - Scenes to Publish";
                    m_headerVersionLabel.text = string.Empty;
                    break;
            }
        }

        private void ConfigureBuildTypeField()
        {
            if (m_ambientTypeEnum == null)
                return;

            m_ambientTypeEnum.Init(SmartUploader.Settings.buildType);
            m_ambientTypeEnum.SetValueWithoutNotify(SmartUploader.Settings.buildType);
            m_ambientTypeEnum.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is not BuildType buildType)
                    return;

                SmartUploader.Settings.buildType = buildType;
                EditorUtility.SetDirty(SmartBuilderConfig.instance);
                AssetDatabase.SaveAssets();
                RefreshNonReleaseUploadHint();
            });

            RefreshNonReleaseUploadHint();
        }

        private void ConfigureUploadTargetFields()
        {
            if (m_sftpAuthModeEnum != null)
            {
                m_sftpAuthModeEnum.Init(SmartUploader.Settings.sftpAuthMode);
                m_sftpAuthModeEnum.SetValueWithoutNotify(SmartUploader.Settings.sftpAuthMode);
                m_sftpAuthModeEnum.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is not SmartUploaderSettings.SftpAuthMode authMode)
                        return;

                    SmartUploader.Settings.sftpAuthMode = authMode;
                    EditorUtility.SetDirty(SmartBuilderConfig.instance);
                    AssetDatabase.SaveAssets();
                    RefreshUploadTargetFields();
                });
            }

            if (m_uploadTargetEnum != null)
            {
                m_uploadTargetEnum.Init(SmartUploader.Settings.uploadTarget);
                m_uploadTargetEnum.SetValueWithoutNotify(SmartUploader.Settings.uploadTarget);
                m_uploadTargetEnum.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is not SmartUploaderSettings.UploadTarget uploadTarget)
                        return;

                    SmartUploader.Settings.uploadTarget = uploadTarget;
                    EditorUtility.SetDirty(SmartBuilderConfig.instance);
                    AssetDatabase.SaveAssets();
                    RefreshUploadTargetFields();
                });
            }

            RefreshUploadTargetFields();
        }

        private void RefreshUploadTargetFields()
        {
            bool isSftp = SmartUploader.Settings.uploadTarget == SmartUploaderSettings.UploadTarget.SFTP;
            bool usePassword = SmartUploader.Settings.sftpAuthMode == SmartUploaderSettings.SftpAuthMode.Password;

            if (m_sftpFieldsGroup != null)
                SetElementVisible(m_sftpFieldsGroup, isSftp);

            if (m_sftpPasswordFieldsGroup != null)
                SetElementVisible(m_sftpPasswordFieldsGroup, isSftp && usePassword);

            if (m_sftpKeyFieldsGroup != null)
                SetElementVisible(m_sftpKeyFieldsGroup, isSftp && !usePassword);

            if (m_awsFieldsGroup != null)
                SetElementVisible(m_awsFieldsGroup, !isSftp);

            RefreshNonReleaseUploadHint();
        }

        private void OnBuildClicked()
        {
            bool buildSuccess = SmartBuilder.Build();

            RefreshBuildSettingsFields();
            RefreshVersionLabels();

            if (buildSuccess && SmartBuilderConfig.uploadAfterBuild)
            {
                m_tabNavigation.SelectIndex(2);
                OnUploadClicked();
            }
        }
        private async void OnUploadClicked()
        {
            bool uploadSucceeded = false;
            bool uploadCancelled = false;
            string uploadErrorMessage = null;

            if (SmartUploader.Settings.buildType == BuildType.LOCAL)
            {
                EditorUtility.DisplayDialog("Smart Uploader", "LOCAL builds cannot be uploaded to a remote host. Change Build Type to DEVELOPMENT, TEST, PREVIEW or RELEASE.", "OK");
                return;
            }

            if (!EnsureUploadVersionIsReady())
                return;

            if (!ValidateUploadSettings()) return;
            if (!TryGetRemoteUploadDirectory(out string remoteUploadDirectory)) return;
            if (SmartUploader.Settings.cleanUpDirectory && IsRootRemoteSubdirectorySelected())
            {
                SmartUploader.Settings.cleanUpDirectory = false;
                PersistSmartBuilderConfig();
                RefreshCleanUpDirectoryToggle();
                ShowMessageOverlay("Unsafe Clean Up Blocked", "Clean Up Directory was turned off because the destination is [root]. Root cleanup is not allowed.", MessageOverlayTone.Warning);
                return;
            }
            if (!ConfirmUpload(remoteUploadDirectory)) return;

            string rootPath = SmartBuilder.GetCurrentBuildRootPath();
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                EditorUtility.DisplayDialog("Smart Uploader Error", "Build output folder was not found. Build the project first or check the BuildPath.", "OK");
                m_tabNavigation.SelectIndex(1);
                return;
            }


            SmartUploader uploader = SmartUploader.Settings.uploadTarget == SmartUploaderSettings.UploadTarget.SFTP
                ? CreateSftpUploader()
                : new SmartUploader(
                    m_accessKey,
                    m_secretKey,
                    SmartUploader.Settings.awsBucketName,
                    SmartUploader.Settings.bucketRegion);
            m_activeUploader = uploader;
            m_isCancellingUpload = false;

            uploader.OnStatusChanged += (status) =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (m_progressLabel != null)
                        m_progressLabel.text = status;
                };
            };

            uploader.OnStepProgressChanged += (perc) =>
            {
                EditorApplication.delayCall += () =>
                {
                    if (m_progressBar != null)
                        m_progressBar.style.scale = new Vector2(perc, 1f);
                };
            };

            uploader.OnProgressChanged += (perc) => {
                EditorApplication.delayCall += () =>
                {
                    if (m_overallProgressBar != null)
                        m_overallProgressBar.style.scale = new Vector2(perc, 1f);

                    if (m_progressOverallLabel != null)
                        m_progressOverallLabel.text = $"{Mathf.RoundToInt(Mathf.Clamp01(perc) * 100f)}%";
                };

            };

            m_progressStatusLabel.text = "Uploading";
            SetElementVisible(m_progressPanel, true);
            if (m_overallProgressPanel != null)
                SetElementVisible(m_overallProgressPanel, true);
            m_progressLabel.text = "Sending files to remote repository...";
            if (m_progressBar != null)
                m_progressBar.style.scale = new Vector2(0f, 1f);
            if (m_overallProgressBar != null)
                m_overallProgressBar.style.scale = new Vector2(0f, 1f);
            if (m_progressOverallLabel != null)
                m_progressOverallLabel.text = "0%";
            SetElementVisible(m_progressOverlay, true);
            UpdateOverlayHostVisibility();
            SetElementVisible(m_buttonCancel, true);
            HideMessageOverlay();
            SetHostCloseBlocked(true, "An upload is in progress. Wait until it finishes before closing this window.");
            try
            {
                await uploader.UploadFilesAsync(rootPath, remoteUploadDirectory, SmartUploader.Settings.cleanUpDirectory);
                SmartUploader.Settings.lastVersion = SmartBuilderConfig.instance.m_buildSettings.lastVersion;
                PersistSmartBuilderConfig();
                RefreshVersionLabels();
                uploadSucceeded = true;
                Debug.Log($"[SmartBuilderView] Upload finished successfully. Version: {SmartUploader.Settings.lastVersion}");
                await Task.Delay(1000);
            }
            catch (OperationCanceledException)
            {
                uploadCancelled = true;
                Debug.LogWarning("[SmartBuilderView] Upload cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SmartUploader] Upload failed: {ex}");
                uploadErrorMessage = ex.Message;
            }
            finally
            {
                SetElementVisible(m_progressOverlay, false);
                UpdateOverlayHostVisibility();
                SetElementVisible(m_buttonCancel, false);
                SetHostCloseBlocked(false, null);
                m_activeUploader = null;
                m_isCancellingUpload = false;
            }

            if (uploadSucceeded)
            {
                ShowMessageOverlay(
                    "Upload Complete",
                    $"Version '{SmartUploader.Settings.lastVersion}' was uploaded successfully.",
                    MessageOverlayTone.Success);
            }
            else if (uploadCancelled)
            {
                ShowMessageOverlay(
                    "Upload Cancelled",
                    "The upload operation was cancelled.",
                    MessageOverlayTone.Warning);
            }
            else if (!string.IsNullOrWhiteSpace(uploadErrorMessage))
            {
                ShowMessageOverlay("Upload Error", uploadErrorMessage, MessageOverlayTone.Error);
            }

        }

        private void SetHostCloseBlocked(bool blocked, string message)
        {
            string normalizedMessage = blocked
                ? (string.IsNullOrWhiteSpace(message) ? "An operation is currently in progress." : message)
                : null;

            if (m_blocksHostClose == blocked && string.Equals(m_hostCloseBlockedMessage, normalizedMessage, StringComparison.Ordinal))
                return;

            m_blocksHostClose = blocked;
            m_hostCloseBlockedMessage = normalizedMessage;
            HostCloseGuardChanged?.Invoke(this);
        }
        void LoadSceneList(ScrollView scrollView)
        {
            scrollView.Clear();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                VisualElement sceneItem = new VisualElement();
                sceneItem.AddToClassList("scene-toggle");

                VisualElement sceneRow = new VisualElement();
                sceneRow.AddToClassList("scene-toggle__row");
                sceneItem.Add(sceneRow);

                VisualElement sceneLeft = new VisualElement();
                sceneLeft.AddToClassList("scene-toggle__left");
                sceneRow.Add(sceneLeft);

                CustomToggle sceneToggle = new CustomToggle() { label = scene.path };
                sceneToggle.SetValue(scene.enabled);
                sceneToggle.AddToClassList("scene-toggle__field");
                sceneLeft.Add(sceneToggle);

                Button removeButton = new Button();
                removeButton.AddToClassList("scene-remove-button");
                removeButton.tooltip = "Remove scene from Build Settings";
                Texture removeIcon = EditorGUIUtility.IconContent("TreeEditor.Trash").image;
                if (removeIcon is Texture2D removeTexture)
                    removeButton.iconImage = removeTexture;
                else
                    removeButton.text = "X";
                sceneRow.Add(removeButton);


                sceneToggle.OnToggleChanged += (c) =>
                {
                    SetSceneEnabledInBuildSettings(scene.path, c);
                };

                removeButton.clicked += () =>
                {
                    if (!RemoveSceneFromBuildSettings(scene.path))
                        return;

                    LoadSceneList(scrollView);
                };

                scrollView.Add(sceneItem);
            }
        }

        private static void SetSceneEnabledInBuildSettings(string scenePath, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
                return;

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            bool changed = false;

            for (int i = 0; i < scenes.Length; i++)
            {
                EditorBuildSettingsScene scene = scenes[i];
                if (scene == null || !string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (scene.enabled != enabled)
                {
                    scene.enabled = enabled;
                    changed = true;
                }

                break;
            }

            if (changed)
                EditorBuildSettings.scenes = scenes;
        }

        private static bool RemoveSceneFromBuildSettings(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath))
                return false;

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            List<EditorBuildSettingsScene> updatedScenes = scenes
                .Where(scene => scene != null && !string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (updatedScenes.Count == scenes.Length)
                return false;

            EditorBuildSettings.scenes = updatedScenes.ToArray();
            return true;
        }

        private static int AddOpenScenesToBuildSettings()
        {
            EditorBuildSettingsScene[] currentScenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            HashSet<string> existingScenePaths = currentScenes
                .Where(scene => scene != null && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<EditorBuildSettingsScene> updatedScenes = new List<EditorBuildSettingsScene>(currentScenes);
            int addedCount = 0;

            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                Scene openScene = EditorSceneManager.GetSceneAt(i);
                if (!openScene.IsValid() || string.IsNullOrWhiteSpace(openScene.path))
                    continue;

                if (existingScenePaths.Contains(openScene.path))
                    continue;

                updatedScenes.Add(new EditorBuildSettingsScene(openScene.path, true));
                existingScenePaths.Add(openScene.path);
                addedCount++;
            }

            if (addedCount > 0)
                EditorBuildSettings.scenes = updatedScenes.ToArray();

            return addedCount;
        }

        void CancelCurrentProgress()
        {
            if (m_activeUploader == null || m_isCancellingUpload)
                return;

           // Debug.Log("[SmartBuilderView] Cancel upload requested.");
            m_isCancellingUpload = true;
            m_progressLabel.text = "Cancelling upload...";
            m_activeUploader.Cancel();
        }

        public void CancelActiveUpload()
        {
            CancelCurrentProgress();
        }

        private void ShowMessageOverlay(string title, string message, MessageOverlayTone tone = MessageOverlayTone.Neutral)
        {
            if (m_messageTitleLabel != null)
                m_messageTitleLabel.text = string.IsNullOrWhiteSpace(title) ? "Message" : title;

            if (m_messageBodyLabel != null)
                m_messageBodyLabel.text = string.IsNullOrWhiteSpace(message) ? "An unexpected error occurred." : message;

            if (m_messageOverlay != null)
            {
                m_messageOverlay.RemoveFromClassList(MessageOverlayErrorClass);
                m_messageOverlay.RemoveFromClassList(MessageOverlayWarningClass);
                m_messageOverlay.RemoveFromClassList(MessageOverlaySuccessClass);

                switch (tone)
                {
                    case MessageOverlayTone.Error:
                        m_messageOverlay.AddToClassList(MessageOverlayErrorClass);
                        break;
                    case MessageOverlayTone.Warning:
                        m_messageOverlay.AddToClassList(MessageOverlayWarningClass);
                        break;
                    case MessageOverlayTone.Success:
                        m_messageOverlay.AddToClassList(MessageOverlaySuccessClass);
                        break;
                }

                SetElementVisible(m_messageOverlay, true);
                UpdateOverlayHostVisibility();
            }
        }

        private void HideMessageOverlay()
        {
            if (m_messageOverlay != null)
            {
                m_messageOverlay.RemoveFromClassList(MessageOverlayErrorClass);
                m_messageOverlay.RemoveFromClassList(MessageOverlayWarningClass);
                m_messageOverlay.RemoveFromClassList(MessageOverlaySuccessClass);
                SetElementVisible(m_messageOverlay, false);
                UpdateOverlayHostVisibility();
            }
        }


        private bool EnsureUploadVersionIsReady()
        {
            Version productVersion = new Version(PlayerSettings.bundleVersion);
            Version lastBuildVersion = new Version(SmartBuilderConfig.instance.m_buildSettings.lastVersion);
            Version lastUploadVersion = new Version(SmartUploader.Settings.lastVersion);

            if (productVersion > lastBuildVersion)
            {
                bool buildBeforeUpload = EditorUtility.DisplayDialog(
                    "Build Required Before Upload",
                    $"Product version '{productVersion}' is newer than the last build version '{lastBuildVersion}'. Do you want to build a newer version first and then continue with the upload?",
                    "Build First",
                    "Cancel");

                if (!buildBeforeUpload)
                    return false;

                bool buildSuccess = SmartBuilder.Build();
                RefreshBuildSettingsFields();
                RefreshVersionLabels();

                if (!buildSuccess)
                    return false;
            }

            productVersion = new Version(PlayerSettings.bundleVersion);
            lastBuildVersion = new Version(SmartBuilderConfig.instance.m_buildSettings.lastVersion);
            lastUploadVersion = new Version(SmartUploader.Settings.lastVersion);

            if (productVersion == lastBuildVersion && lastBuildVersion == lastUploadVersion)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Upload Already Sent",
                    $"Version '{productVersion}' has already been uploaded. If there are changes, choose whether to build again, upload anyway, or cancel.",
                    "Build Again",
                    "Upload Anyway",
                    "Cancel");

                if (choice == 0)
                {
                    bool buildSuccess = SmartBuilder.Build();
                    RefreshBuildSettingsFields();
                    RefreshVersionLabels();
                    return buildSuccess;
                }

                if (choice == 1)
                    return true;

                return false;
            }

            return true;
        }

        bool ValidateUploadSettings()
        {

            if (SmartUploader.Settings.uploadTarget == SmartUploaderSettings.UploadTarget.SFTP)
            {
                if (string.IsNullOrWhiteSpace(SmartUploader.Settings.remoteDirectory))
                {
                    EditorUtility.DisplayDialog("Smart Uploader Error", "Remote Directory is required!", "OK");
                    m_tabNavigation.SelectIndex(2);
                    m_remoteDirectoryField?.Focus();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(SmartUploader.Settings.sftpHost))
                {
                    EditorUtility.DisplayDialog("Smart Uploader Error", "SFTP Host is required!", "OK");
                    m_tabNavigation.SelectIndex(2);
                    m_sftpHostField?.Focus();
                    return false;
                }

                if (SmartUploader.Settings.sftpPort <= 0)
                {
                    EditorUtility.DisplayDialog("Smart Uploader Error", "SFTP Port must be greater than zero!", "OK");
                    m_tabNavigation.SelectIndex(2);
                    m_sftpPortField?.Focus();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(SmartUploader.Settings.sftpUser))
                {
                    EditorUtility.DisplayDialog("Smart Uploader Error", "SFTP User is required!", "OK");
                    m_tabNavigation.SelectIndex(2);
                    m_sftpUserField?.Focus();
                    return false;
                }

                if (SmartUploader.Settings.sftpAuthMode == SmartUploaderSettings.SftpAuthMode.Password
                    && string.IsNullOrWhiteSpace(GetLocalPref(SftpPasswordPrefsKey)))
                {
                    EditorUtility.DisplayDialog("Smart Uploader Error", "SFTP Password is required!", "OK");
                    m_tabNavigation.SelectIndex(2);
                    m_sftpPasswordField?.Focus();
                    return false;
                }

                if (SmartUploader.Settings.sftpAuthMode == SmartUploaderSettings.SftpAuthMode.SshKey)
                {
                    string privateKeyPath = GetLocalPref(SftpPrivateKeyPathPrefsKey);
                    if (string.IsNullOrWhiteSpace(privateKeyPath))
                    {
                        EditorUtility.DisplayDialog("Smart Uploader Error", "SFTP Private Key path is required!", "OK");
                        m_tabNavigation.SelectIndex(2);
                        FocusPrivateKeyField();
                        return false;
                    }

                    if (!File.Exists(privateKeyPath))
                    {
                        EditorUtility.DisplayDialog("Smart Uploader Error", "SFTP Private Key file was not found!", "OK");
                        m_tabNavigation.SelectIndex(2);
                        FocusPrivateKeyField();
                        return false;
                    }
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(SmartUploader.Settings.awsBucketName))
                {
                    EditorUtility.DisplayDialog("Smart Uploader Error", "AWS Bucket Name is required!", "OK");
                    m_tabNavigation.SelectIndex(2);
                    m_bucketNameField?.Focus();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(m_accessKey))
                {
                    EditorUtility.DisplayDialog("Smart Uploader Error", "AWS Access Key is required!", "OK");
                    m_tabNavigation.SelectIndex(2);
                    m_accessKeyField?.Focus();
                    return false;
                }

                if (string.IsNullOrWhiteSpace(m_secretKey))
                {
                    EditorUtility.DisplayDialog("Smart Uploader Error", "AWS Secret Key is required!", "OK");
                    m_tabNavigation.SelectIndex(2);
                    m_secretKeyField?.Focus();
                    return false;
                }
            }

            return true;
        }

        private static string GetRemoteDirectory()
        {
            string remoteDirectory = SmartUploader.Settings.remoteDirectory;
            if (string.IsNullOrWhiteSpace(remoteDirectory))
                return "/";

            return SmartUploader.NormalizeRemotePath(remoteDirectory, treatEmptyAsRoot: true);
        }

        private bool TryGetRemoteUploadDirectory(out string remoteUploadDirectory)
        {
            remoteUploadDirectory = string.Empty;

            if (!SmartUploader.TryNormalizeRemoteSubdirectory(
                SmartUploader.Settings.remoteSubDirectory,
                out string normalizedSubDirectory,
                out string errorMessage))
            {
                EditorUtility.DisplayDialog("Smart Uploader Error", errorMessage, "OK");
                m_tabNavigation.SelectIndex(2);
                m_remoteSubDirectoryField?.Focus();
                return false;
            }

            string effectiveSubDirectory = GetEffectiveRemoteSubDirectory(normalizedSubDirectory);
            remoteUploadDirectory = string.IsNullOrEmpty(effectiveSubDirectory)
                ? GetRemoteDirectory()
                : SmartUploader.CombineRemotePath(GetRemoteDirectory(), effectiveSubDirectory);

            return true;
        }

        private static bool IsRootRemoteSubdirectorySelected()
        {
            return string.Equals(
                SmartUploader.Settings.remoteSubDirectory?.Trim(),
                SmartUploaderSettings.RootSubdirectoryToken,
                StringComparison.OrdinalIgnoreCase);
        }

        private bool ConfirmUpload(string remoteUploadDirectory)
        {
            string destination = string.IsNullOrWhiteSpace(remoteUploadDirectory) ? "/" : remoteUploadDirectory;

            if (SmartUploader.Settings.uploadTarget == SmartUploaderSettings.UploadTarget.SFTP)
            {
                string host = string.IsNullOrWhiteSpace(SmartUploader.Settings.sftpHost) ? "<unknown host>" : SmartUploader.Settings.sftpHost;
                return EditorUtility.DisplayDialog(
                    "Confirm Upload",
                    $"Do you really want to continue with the upload?\n\nDestination: {host}:{destination}\n\nExisting files in that location may be overwritten or lost.",
                    "Continue",
                    "Cancel");
            }

            string bucket = string.IsNullOrWhiteSpace(SmartUploader.Settings.awsBucketName) ? "<unknown bucket>" : SmartUploader.Settings.awsBucketName;
            return EditorUtility.DisplayDialog(
                "Confirm Upload",
                $"Do you really want to continue with the upload?\n\nDestination: {bucket}:{destination}\n\nExisting files in that location may be overwritten or lost.",
                "Continue",
                "Cancel");
        }

        private void ConfigureRemoteSubDirectoryField()
        {
            if (m_remoteSubDirectoryField == null)
                return;

            string suggestedSubDirectory = GetFallbackNonReleaseSubDirectoryName();
            if (!string.IsNullOrWhiteSpace(suggestedSubDirectory))
                m_remoteSubDirectoryField.textEdition.placeholder = suggestedSubDirectory;

            if (string.IsNullOrWhiteSpace(SmartUploader.Settings.remoteSubDirectory))
            {
                SmartUploader.Settings.remoteSubDirectory = suggestedSubDirectory;
                m_remoteSubDirectoryField.SetValueWithoutNotify(suggestedSubDirectory);
                EditorUtility.SetDirty(SmartBuilderConfig.instance);
                AssetDatabase.SaveAssets();
            }

            m_remoteSubDirectoryField.RegisterValueChangedCallback(evt =>
            {
                if (!string.IsNullOrWhiteSpace(evt.newValue))
                {
                    RefreshNonReleaseUploadHint();
                    return;
                }

                SmartUploader.Settings.remoteSubDirectory = SmartUploaderSettings.RootSubdirectoryToken;
                m_remoteSubDirectoryField.SetValueWithoutNotify(SmartUploaderSettings.RootSubdirectoryToken);
                EditorUtility.SetDirty(SmartBuilderConfig.instance);
                AssetDatabase.SaveAssets();
                RefreshCleanUpDirectoryToggle();
                RefreshNonReleaseUploadHint();
            });

            m_remoteSubDirectoryField.RegisterCallback<FocusOutEvent>(_ =>
            {
                RefreshCleanUpDirectoryToggle();
                RefreshNonReleaseUploadHint();
            });
        }

        private void ConfigureCleanUpDirectoryToggle()
        {
            if (m_cleanUpDirectoryToggle == null)
                return;

            m_cleanUpDirectoryToggle.SetValue(SmartUploader.Settings.cleanUpDirectory);
            m_cleanUpDirectoryToggle.OnToggleChanged += value =>
            {
                SmartUploader.Settings.cleanUpDirectory = value;
                PersistSmartBuilderConfig();
            };

            RefreshCleanUpDirectoryToggle();
        }

        private void RefreshCleanUpDirectoryToggle()
        {
            if (m_cleanUpDirectoryToggle == null)
                return;

            bool isRootSubdirectory = string.Equals(
                SmartUploader.Settings.remoteSubDirectory?.Trim(),
                SmartUploaderSettings.RootSubdirectoryToken,
                StringComparison.OrdinalIgnoreCase);

            if (isRootSubdirectory)
            {
                if (SmartUploader.Settings.cleanUpDirectory)
                {
                    SmartUploader.Settings.cleanUpDirectory = false;
                    m_cleanUpDirectoryToggle.SetValue(false);
                    PersistSmartBuilderConfig();
                }

                m_cleanUpDirectoryToggle.SetEnabled(false);
                return;
            }

            m_cleanUpDirectoryToggle.SetEnabled(true);
            m_cleanUpDirectoryToggle.SetValue(SmartUploader.Settings.cleanUpDirectory);
        }

        private void RefreshNonReleaseUploadHint()
        {
            if (m_buttonUpload != null)
                m_buttonUpload.SetEnabled(SmartUploader.Settings.buildType != BuildType.LOCAL);

            bool isRootSubdirectory = string.Equals(
                SmartUploader.Settings.remoteSubDirectory?.Trim(),
                SmartUploaderSettings.RootSubdirectoryToken,
                StringComparison.OrdinalIgnoreCase);

            if (m_rootUploadHintLabel != null)
            {
                m_rootUploadHintLabel.text = SmartUploader.Settings.buildType == BuildType.RELEASE
                    ? "[root] will upload the project directly into the remote root directory. Use it with caution."
                    : $"[root] will target the remote root path. For {SmartUploader.Settings.buildType}, the upload will still be redirected to the prefixed destination automatically.";
            }

            if (m_rootUploadHint != null)
                SetElementVisible(m_rootUploadHint, isRootSubdirectory);

            RefreshCleanUpDirectoryToggle();

            if (m_localBuildUploadHint != null)
                SetElementVisible(m_localBuildUploadHint, SmartUploader.Settings.buildType == BuildType.LOCAL);

            if (m_nonReleaseUploadHint == null)
                return;

            if (SmartUploader.Settings.buildType == BuildType.RELEASE || SmartUploader.Settings.buildType == BuildType.LOCAL || isRootSubdirectory)
            {
                SetElementVisible(m_nonReleaseUploadHint, false);
                return;
            }

            string prefix = GetBuildTypePrefix();
            string normalizedSubDirectory = string.Empty;
            SmartUploader.TryNormalizeRemoteSubdirectory(
                SmartUploader.Settings.remoteSubDirectory,
                out normalizedSubDirectory,
                out _);

            string effectiveSubDirectory = GetEffectiveRemoteSubDirectory(normalizedSubDirectory);
            string label = string.IsNullOrWhiteSpace(effectiveSubDirectory) ? SmartUploaderSettings.RootSubdirectoryToken : effectiveSubDirectory;
            string buildTypeLabel = SmartUploader.Settings.buildType.ToString();

            if (m_nonReleaseUploadHintLabel != null)
                m_nonReleaseUploadHintLabel.text = $"This build type is {buildTypeLabel}. The upload subdirectory will be prefixed with '{prefix}' and the effective destination will be '{label}'.";
            SetElementVisible(m_nonReleaseUploadHint, true);
        }

        private static string GetEffectiveRemoteSubDirectory(string normalizedSubDirectory)
        {
            if (SmartUploader.Settings.buildType == BuildType.RELEASE)
                return normalizedSubDirectory;

            string prefix = GetBuildTypePrefix();
            string baseSubDirectory = string.IsNullOrEmpty(normalizedSubDirectory)
                ? GetFallbackNonReleaseSubDirectoryName()
                : normalizedSubDirectory;

            string[] segments = baseSubDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return prefix + GetFallbackNonReleaseSubDirectoryName();

            segments[segments.Length - 1] = prefix + segments[segments.Length - 1];
            return string.Join("/", segments);
        }

        private static string GetBuildTypePrefix()
        {
            return SmartUploader.Settings.buildType switch
            {
                BuildType.DEVELOPMENT => "dev_",
                BuildType.LOCAL => "local_",
                BuildType.TEST => "test_",
                BuildType.PREVIEW => "preview_",
                _ => string.Empty
            };
        }

        private static string GetFallbackNonReleaseSubDirectoryName()
        {
            string sanitizedProductName = SanitizeRemoteDirectorySegment(Application.productName);
            return string.IsNullOrWhiteSpace(sanitizedProductName) ? "project" : sanitizedProductName;
        }

        private static string SanitizeRemoteDirectorySegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            StringBuilder builder = new StringBuilder(value.Length);
            bool previousWasSeparator = false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = char.ToLowerInvariant(value[i]);
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

            return builder.ToString().Trim('-');
        }

        private void ConfigureTextFieldUndoRedo()
        {
            RegisterTextFieldUndoRedo(m_remoteDirectoryField);
            RegisterTextFieldUndoRedo(m_remoteSubDirectoryField);
            RegisterTextFieldUndoRedo(m_bucketNameField);
            RegisterTextFieldUndoRedo(m_accessKeyField);
            RegisterTextFieldUndoRedo(m_secretKeyField);
            RegisterTextFieldUndoRedo(m_sftpHostField);
            RegisterTextFieldUndoRedo(m_sftpUserField);
            RegisterTextFieldUndoRedo(m_sftpPasswordField);
            RegisterTextFieldUndoRedo(m_sftpPasswordRevealField);
            RegisterTextFieldUndoRedo(m_sftpPrivateKeyPassphraseField);
            RegisterTextFieldUndoRedo(m_sftpPrivateKeyPassphraseRevealField);
        }

        private void ConfigurePrivateKeyField()
        {
            if (m_sftpPrivateKeyDropdown == null)
                return;

            RefreshPrivateKeyChoices();

            m_sftpPrivateKeyDropdown.RegisterValueChangedCallback(evt =>
            {
                if (string.Equals(evt.newValue, CustomPrivateKeyDropdownChoice, StringComparison.Ordinal))
                {
                    string selectedFilePath = EditorUtility.OpenFilePanel("Select SSH Private Key", GetDefaultPrivateKeyDirectory(), string.Empty);
                    if (string.IsNullOrWhiteSpace(selectedFilePath))
                    {
                        RestorePreviousPrivateKeyChoice();
                        return;
                    }

                    string customLabel = GetCustomPrivateKeyChoiceLabel(selectedFilePath);
                    m_sftpPrivateKeyChoices[customLabel] = selectedFilePath;
                    RefreshPrivateKeyDropdownChoices(customLabel);
                    m_sftpPrivateKeyDropdown.SetValueWithoutNotify(customLabel);
                    m_lastResolvedPrivateKeyChoice = customLabel;
                    SetLocalPref(SftpPrivateKeyPathPrefsKey, selectedFilePath);
                }
                else if (m_sftpPrivateKeyChoices.TryGetValue(evt.newValue, out string detectedPath))
                {
                    SetLocalPref(SftpPrivateKeyPathPrefsKey, detectedPath);
                    m_lastResolvedPrivateKeyChoice = evt.newValue;
                }
            });
        }

        private void RefreshPrivateKeyChoices()
        {
            if (m_sftpPrivateKeyDropdown == null)
                return;

            m_sftpPrivateKeyChoices.Clear();

            List<string> detectedKeys = DetectSshPrivateKeys();
            List<string> choiceLabels = new List<string>(detectedKeys.Count + 1);
            foreach (string keyPath in detectedKeys)
            {
                string label = GetPrivateKeyChoiceLabel(keyPath);
                m_sftpPrivateKeyChoices[label] = keyPath;
            }

            string configuredPath = GetLocalPref(SftpPrivateKeyPathPrefsKey);
            string detectedLabel = m_sftpPrivateKeyChoices.FirstOrDefault(pair =>
                string.Equals(pair.Value, configuredPath, StringComparison.OrdinalIgnoreCase)).Key;

            if (!string.IsNullOrWhiteSpace(detectedLabel))
            {
                RefreshPrivateKeyDropdownChoices(detectedLabel);
                m_sftpPrivateKeyDropdown.SetValueWithoutNotify(detectedLabel);
                m_lastResolvedPrivateKeyChoice = detectedLabel;
                return;
            }

            if (string.IsNullOrWhiteSpace(configuredPath) && detectedKeys.Count == 1)
            {
                string autoDetectedLabel = GetPrivateKeyChoiceLabel(detectedKeys[0]);
                RefreshPrivateKeyDropdownChoices(autoDetectedLabel);
                m_sftpPrivateKeyDropdown.SetValueWithoutNotify(autoDetectedLabel);
                m_lastResolvedPrivateKeyChoice = autoDetectedLabel;
                SetLocalPref(SftpPrivateKeyPathPrefsKey, detectedKeys[0]);
                return;
            }

            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string customLabel = GetCustomPrivateKeyChoiceLabel(configuredPath);
                m_sftpPrivateKeyChoices[customLabel] = configuredPath;
                RefreshPrivateKeyDropdownChoices(customLabel);
                m_sftpPrivateKeyDropdown.SetValueWithoutNotify(customLabel);
                m_lastResolvedPrivateKeyChoice = customLabel;
                return;
            }

            RefreshPrivateKeyDropdownChoices(null);
            m_sftpPrivateKeyDropdown.index = -1;
            m_lastResolvedPrivateKeyChoice = null;
        }

        private void RefreshPrivateKeyDropdownChoices(string selectedLabel)
        {
            if (m_sftpPrivateKeyDropdown == null)
                return;

            List<string> labels = m_sftpPrivateKeyChoices.Keys
                .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(selectedLabel) && !labels.Contains(selectedLabel))
                labels.Add(selectedLabel);

            labels.Add(CustomPrivateKeyDropdownChoice);
            m_sftpPrivateKeyDropdown.choices = labels;
        }

        private static List<string> DetectSshPrivateKeys()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
                return new List<string>();

            string sshDirectory = Path.Combine(userProfile, ".ssh");
            if (!Directory.Exists(sshDirectory))
                return new List<string>();

            string[] preferredFileNames =
            {
                "id_ed25519",
                "id_rsa",
                "id_ecdsa",
                "id_dsa"
            };

            List<string> detected = new List<string>();
            foreach (string fileName in preferredFileNames)
            {
                string fullPath = Path.Combine(sshDirectory, fileName);
                if (File.Exists(fullPath))
                    detected.Add(fullPath);
            }

            foreach (string filePath in Directory.GetFiles(sshDirectory))
            {
                if (detected.Any(existingPath => string.Equals(existingPath, filePath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string fileName = Path.GetFileName(filePath);
                if (string.IsNullOrWhiteSpace(fileName) ||
                    fileName.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) ||
                    fileName.EndsWith(".ppk", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("known_hosts", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("config", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("authorized_keys", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (fileName.StartsWith("id_", StringComparison.OrdinalIgnoreCase))
                    detected.Add(filePath);
            }

            return detected;
        }

        private static string GetPrivateKeyChoiceLabel(string keyPath)
        {
            string fileName = Path.GetFileName(keyPath);
            return string.IsNullOrWhiteSpace(fileName) ? keyPath : $"{fileName} ({keyPath})";
        }

        private static string GetCustomPrivateKeyChoiceLabel(string keyPath)
        {
            string fileName = Path.GetFileName(keyPath);
            return string.IsNullOrWhiteSpace(fileName) ? $"Custom ({keyPath})" : $"Custom: {fileName} ({keyPath})";
        }

        private static string GetDefaultPrivateKeyDirectory()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(userProfile))
                return string.Empty;

            string sshDirectory = Path.Combine(userProfile, ".ssh");
            return Directory.Exists(sshDirectory) ? sshDirectory : userProfile;
        }

        private void RestorePreviousPrivateKeyChoice()
        {
            if (m_sftpPrivateKeyDropdown == null)
                return;

            if (!string.IsNullOrWhiteSpace(m_lastResolvedPrivateKeyChoice) &&
                m_sftpPrivateKeyChoices.TryGetValue(m_lastResolvedPrivateKeyChoice, out string previousPath))
            {
                m_sftpPrivateKeyDropdown.SetValueWithoutNotify(m_lastResolvedPrivateKeyChoice);
                SetLocalPref(SftpPrivateKeyPathPrefsKey, previousPath);
                return;
            }

            m_sftpPrivateKeyDropdown.index = -1;
            SetLocalPref(SftpPrivateKeyPathPrefsKey, string.Empty);
        }

        private void FocusPrivateKeyField()
        {
            m_sftpPrivateKeyDropdown?.Focus();
        }

        private static void PersistSmartBuilderConfig(bool saveAssets = true)
        {
            EditorUtility.SetDirty(SmartBuilderConfig.instance);
            if (saveAssets)
                AssetDatabase.SaveAssets();
        }

        private void RegisterTextFieldUndoRedo(TextField textField)
        {
            if (textField == null || m_textFieldHistory.ContainsKey(textField))
                return;

            var history = new TextFieldHistoryState();
            history.undoStack.Add(textField.value ?? string.Empty);
            m_textFieldHistory[textField] = history;

            textField.RegisterValueChangedCallback(evt =>
            {
                if (!m_textFieldHistory.TryGetValue(textField, out TextFieldHistoryState state))
                    return;

                if (state.isApplyingHistory)
                    return;

                string newValue = evt.newValue ?? string.Empty;
                string currentValue = state.undoStack.Count > 0 ? state.undoStack[state.undoStack.Count - 1] : string.Empty;
                if (string.Equals(currentValue, newValue, StringComparison.Ordinal))
                    return;

                state.undoStack.Add(newValue);
                state.redoStack.Clear();
            });

            textField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!IsUndoRedoShortcut(evt))
                    return;

                bool handled = evt.shiftKey ? TryRedoTextField(textField) : TryUndoTextField(textField);
                if (!handled && IsRedoShortcut(evt))
                    handled = TryRedoTextField(textField);

                if (!handled)
                    return;

                evt.StopImmediatePropagation();
                evt.StopPropagation();
                textField.focusController?.IgnoreEvent(evt);
            }, TrickleDown.TrickleDown);
        }

        private static bool IsUndoRedoShortcut(KeyDownEvent evt)
        {
            if (evt == null)
                return false;

            bool actionKey = evt.ctrlKey || evt.commandKey;
            if (!actionKey)
                return false;

            return evt.keyCode == KeyCode.Z || evt.keyCode == KeyCode.Y;
        }

        private static bool IsRedoShortcut(KeyDownEvent evt)
        {
            if (evt == null)
                return false;

            bool actionKey = evt.ctrlKey || evt.commandKey;
            return actionKey && (evt.keyCode == KeyCode.Y || (evt.keyCode == KeyCode.Z && evt.shiftKey));
        }

        private bool TryUndoTextField(TextField textField)
        {
            if (textField == null || !m_textFieldHistory.TryGetValue(textField, out TextFieldHistoryState state))
                return false;

            if (state.undoStack.Count <= 1)
                return false;

            string currentValue = state.undoStack[state.undoStack.Count - 1];
            state.undoStack.RemoveAt(state.undoStack.Count - 1);
            state.redoStack.Add(currentValue);

            ApplyTextFieldHistoryValue(textField, state, state.undoStack[state.undoStack.Count - 1]);
            return true;
        }

        private bool TryRedoTextField(TextField textField)
        {
            if (textField == null || !m_textFieldHistory.TryGetValue(textField, out TextFieldHistoryState state))
                return false;

            if (state.redoStack.Count == 0)
                return false;

            string nextValue = state.redoStack[state.redoStack.Count - 1];
            state.redoStack.RemoveAt(state.redoStack.Count - 1);
            state.undoStack.Add(nextValue);

            ApplyTextFieldHistoryValue(textField, state, nextValue);
            return true;
        }

        private static void ApplyTextFieldHistoryValue(TextField textField, TextFieldHistoryState state, string value)
        {
            state.isApplyingHistory = true;
            try
            {
                textField.value = value ?? string.Empty;
            }
            finally
            {
                state.isApplyingHistory = false;
            }
        }

        private void SetupRevealPasswordField(TextField passwordField, TextField revealField, Button revealButton)
        {
            if (passwordField == null || revealField == null || revealButton == null)
                return;

            revealButton.focusable = false;
            revealButton.tooltip = "Show password";

            Texture revealIcon = EditorGUIUtility.IconContent("animationvisibilitytoggleon").image;
            if (revealIcon is Texture2D revealTexture)
                revealButton.iconImage = revealTexture;
            else
                revealButton.text = "O";

            revealButton.clicked += () =>
            {
                m_isSftpPasswordVisible = !m_isSftpPasswordVisible;
                SetPasswordFieldVisible(passwordField, revealField, revealButton, m_isSftpPasswordVisible);
            };

            SetPasswordFieldVisible(passwordField, revealField, revealButton, false);
        }

        private static void SetPasswordFieldVisible(TextField passwordField, TextField revealField, Button revealButton, bool isVisible)
        {
            if (passwordField == null || revealField == null || revealButton == null)
                return;

            string currentValue = passwordField.value;
            revealField.SetValueWithoutNotify(currentValue);
            SetElementVisible(passwordField, !isVisible);
            SetElementVisible(revealField, isVisible);
            revealButton.tooltip = isVisible ? "Hide password" : "Show password";
        }

        private static void SetElementVisible(VisualElement element, bool visible)
        {
            if (element == null)
                return;

            element.EnableInClassList(HiddenClass, !visible);
        }

        private void UpdateOverlayHostVisibility()
        {
            if (m_overlayHost == null || ReferenceEquals(m_overlayHost, this))
                return;

            bool isProgressVisible = m_progressOverlay != null && !m_progressOverlay.ClassListContains(HiddenClass);
            bool isMessageVisible = m_messageOverlay != null && !m_messageOverlay.ClassListContains(HiddenClass);
            bool isVisible = isProgressVisible || isMessageVisible;
            m_overlayHost.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
            m_overlayHost.style.position = Position.Absolute;
            m_overlayHost.style.left = 0;
            m_overlayHost.style.top = 0;
            m_overlayHost.style.right = 0;
            m_overlayHost.style.bottom = 0;

            if (!isVisible)
                return;

            m_overlayHost.BringToFront();
            m_progressOverlay?.BringToFront();
            m_messageOverlay?.BringToFront();
        }

        private void LoadLocalSensitiveFields()
        {
            if (m_sftpPasswordField != null)
            {
                string password = GetLocalPref(SftpPasswordPrefsKey);
                m_sftpPasswordField.SetValueWithoutNotify(password);
                m_sftpPasswordRevealField?.SetValueWithoutNotify(password);
                if (m_sftpPasswordRevealButton != null)
                    SetPasswordFieldVisible(m_sftpPasswordField, m_sftpPasswordRevealField, m_sftpPasswordRevealButton, false);
            }

            if (m_sftpPrivateKeyPassphraseField != null)
            {
                string passphrase = GetLocalPref(SftpKeyPassphrasePrefsKey);
                m_sftpPrivateKeyPassphraseField.SetValueWithoutNotify(passphrase);
                m_sftpPrivateKeyPassphraseRevealField?.SetValueWithoutNotify(passphrase);
                if (m_sftpPrivateKeyPassphraseRevealButton != null)
                    SetPasswordFieldVisible(m_sftpPrivateKeyPassphraseField, m_sftpPrivateKeyPassphraseRevealField, m_sftpPrivateKeyPassphraseRevealButton, false);
            }
        }

        private void RegisterSensitiveFieldPersistence()
        {
            if (m_sftpPasswordField != null)
            {
                m_sftpPasswordField.RegisterValueChangedCallback(evt =>
                {
                    SetLocalPref(SftpPasswordPrefsKey, evt.newValue);
                    m_sftpPasswordRevealField?.SetValueWithoutNotify(evt.newValue);
                });
            }

            if (m_sftpPasswordRevealField != null)
            {
                m_sftpPasswordRevealField.RegisterValueChangedCallback(evt =>
                {
                    m_sftpPasswordField?.SetValueWithoutNotify(evt.newValue);
                    SetLocalPref(SftpPasswordPrefsKey, evt.newValue);
                });
            }

            if (m_sftpPrivateKeyPassphraseField != null)
            {
                m_sftpPrivateKeyPassphraseField.RegisterValueChangedCallback(evt =>
                {
                    SetLocalPref(SftpKeyPassphrasePrefsKey, evt.newValue);
                    m_sftpPrivateKeyPassphraseRevealField?.SetValueWithoutNotify(evt.newValue);
                });
            }

            if (m_sftpPrivateKeyPassphraseRevealField != null)
            {
                m_sftpPrivateKeyPassphraseRevealField.RegisterValueChangedCallback(evt =>
                {
                    m_sftpPrivateKeyPassphraseField?.SetValueWithoutNotify(evt.newValue);
                    SetLocalPref(SftpKeyPassphrasePrefsKey, evt.newValue);
                });
            }
        }

        private static string GetLocalPref(string key)
        {
            return PlayerPrefs.GetString(GetScopedPrefKey(key), string.Empty);
        }

        private static void SetLocalPref(string key, string value)
        {
            PlayerPrefs.SetString(GetScopedPrefKey(key), value ?? string.Empty);
            PlayerPrefs.Save();
        }

        private static string GetScopedPrefKey(string key)
        {
            return $"{Application.companyName}.{Application.productName}.{key}";
        }

        private static SmartUploader CreateSftpUploader()
        {
            return SmartUploader.Settings.sftpAuthMode == SmartUploaderSettings.SftpAuthMode.SshKey
                ? new SmartUploader(
                    SmartUploader.Settings.sftpHost,
                    SmartUploader.Settings.sftpPort,
                    SmartUploader.Settings.sftpUser,
                    GetLocalPref(SftpPrivateKeyPathPrefsKey),
                    GetLocalPref(SftpKeyPassphrasePrefsKey),
                    true)
                : new SmartUploader(
                    SmartUploader.Settings.sftpHost,
                    SmartUploader.Settings.sftpPort,
                    SmartUploader.Settings.sftpUser,
                    GetLocalPref(SftpPasswordPrefsKey));
        }





        public void SelectTab(int tabIndex)
        {
            m_tabNavigation.SelectIndex(tabIndex);
        }

        public void SetOverlayHost(VisualElement overlayHost)
        {
            VisualElement resolvedHost = overlayHost ?? this;
            if (ReferenceEquals(m_overlayHost, resolvedHost))
                return;

            m_overlayHost = resolvedHost;
            MoveOverlayToHost(m_progressOverlay, resolvedHost);
            MoveOverlayToHost(m_messageOverlay, resolvedHost);
            UpdateOverlayHostVisibility();
        }

        private static void MoveOverlayToHost(VisualElement overlay, VisualElement host)
        {
            if (overlay == null || host == null)
                return;

            overlay.RemoveFromHierarchy();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.top = 0;
            overlay.style.right = 0;
            overlay.style.bottom = 0;
            host.Add(overlay);
        }

        private void EnsureOverlayStyles(VisualElement overlay)
        {
            if (overlay == null || m_overlayStyleSheet == null)
                return;

            if (!overlay.styleSheets.Contains(m_overlayStyleSheet))
                overlay.styleSheets.Add(m_overlayStyleSheet);
        }

    }
}
#endif
