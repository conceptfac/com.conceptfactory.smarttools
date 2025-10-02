#if UNITY_EDITOR
using Concept.SmartTools.Editor;
using UnityEditor;
using UnityEngine;

namespace Concept.UI
{
    public class SmartBuilderWindow : EditorWindow
    {
        private SmartBuilderView m_view;

        [MenuItem("Window/Concept Factory/Smart Builder")]
        public static void Open()
        {
            var wnd = GetWindow<SmartBuilderWindow>();
            wnd.titleContent = new GUIContent("Smart Builder");
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
            }

            // (Opcional) ajuste para fazer a view se expandir para preencher espaço
            m_view.style.flexGrow = 1;

            // Adiciona ao root da janela
            rootVisualElement.Add(m_view);
        }
    }
}
#endif