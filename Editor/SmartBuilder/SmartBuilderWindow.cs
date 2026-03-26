#if UNITY_EDITOR
using Concept.SmartTools.Editor;
using System;
using UnityEditor;
using UnityEngine;

namespace Concept.UI
{
    public class SmartBuilderWindow : EditorWindow
    {
        private SmartBuilderView m_view;
        private static int s_pendingTabIndex = -1;

        public static Action<int> OpenSmartWindow;

        public static void OpenOriginal(int tabIndex)
        {
            s_pendingTabIndex = tabIndex;
            OpenWindow(tabIndex);
        }

        public static void OpenBound(int tabIndex)
        {
            s_pendingTabIndex = tabIndex;
            if (OpenSmartWindow != null)
            {
                OpenSmartWindow.Invoke(tabIndex);
                return;
            }
            Open();
        }

        [MenuItem("Tools/Smart Tools/Smart Builder")]
        public static void Open()
        {
            OpenWindow(1);
        }

        [MenuItem("Tools/Smart Tools/Smart Uploader")]
        public static void OpenUploader()
        {
            OpenWindow(2);
        }

        private static void OpenWindow(int tabIndex)
        {
            s_pendingTabIndex = tabIndex;
            var wnd = GetWindow<SmartBuilderWindow>();
            wnd.titleContent = new GUIContent("Smart Builder");
            wnd.minSize = new Vector2(1024, 512);
            wnd.Show();
            wnd.Focus();

            if (wnd.m_view != null)
            {
                wnd.m_view.SelectTab(tabIndex);
                s_pendingTabIndex = -1;
            }
        }

        // Unity chama CreateGUI quando a janela precisa montar sua UI com UI Toolkit. :contentReference[oaicite:0]{index=0}
        private void CreateGUI()
        {
            // Limpa conteúdo anterior (caso esteja sendo reconstruída)
            rootVisualElement.Clear();

            // Cria ou reutiliza a view
            if (m_view == null)
            {
                m_view = new SmartBuilderView();
                m_view.AddToClassList("smart-build-wnd");
                m_view.HostCloseGuardChanged += OnHostCloseGuardChanged;
            }

            // (Opcional) ajuste para fazer a view se expandir para preencher espaço
            m_view.style.flexGrow = 1;

            // Adiciona ao root da janela
            rootVisualElement.Add(m_view);

            if (s_pendingTabIndex >= 0)
            {
                m_view.SelectTab(s_pendingTabIndex);
                s_pendingTabIndex = -1;
            }

            UpdateCloseGuardState();
        }

        private void OnDisable()
        {
            if (m_view != null && m_view.BlocksHostClose)
            {
                Debug.Log("[SmartBuilderWindow] Window is closing during upload. Cancelling active upload.");
                m_view.CancelActiveUpload();
            }

            UpdateCloseGuardState(forceClear: true);
        }

        private void OnHostCloseGuardChanged(SmartBuilderView _)
        {
            UpdateCloseGuardState();
        }

        private void UpdateCloseGuardState(bool forceClear = false)
        {
            bool blocked = !forceClear && m_view != null && m_view.BlocksHostClose;
            hasUnsavedChanges = blocked;
            saveChangesMessage = blocked
                ? m_view.HostCloseBlockedMessage
                : string.Empty;
        }
    }
}
#endif


