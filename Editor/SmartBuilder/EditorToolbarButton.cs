#if UNITY_EDITOR && TOOLBAR_EXTENDER
using Concept.UI;
using UnityEditor;
using UnityEngine;
using UnityToolbarExtender;

namespace Concept.SmartTools.Editor
{


    [InitializeOnLoad]
    public static class SmartbuildToolbar
    {

        static SmartbuildToolbar()
        {
            ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI);
        }

        static void OnToolbarGUI()
        {
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            {
                GUIContent content = new GUIContent("Smart Build");
                Vector2 size = EditorStyles.linkLabel.CalcSize(content);
                Rect rect = GUILayoutUtility.GetRect(size.x, size.y);

                if (EditorGUI.LinkButton(rect, content))
                {
                    SmartBuilderWindow.Open(1);
                }

                GUILayout.Space(8);

                // Usando um Popup (dropdown) do IMGUI
                EditorGUI.BeginChangeCheck();

                SmartBuilderConfig.uploadSettings.buildType = (BuildType)EditorGUILayout.EnumPopup(SmartBuilderConfig.uploadSettings.buildType, EditorStyles.toolbarPopup, GUILayout.Width(120));

                if (EditorGUI.EndChangeCheck())
                {
                    //GameManager.Config.buildType = buildType;
                }

                if (GUILayout.Button(new GUIContent("BUILD", "Hold for Build Config"), EditorStyles.toolbarButton)) {
                    SmartBuilder.Build();
                
                }
                if (GUILayout.Button(new GUIContent("PUBLISH", "Hold for Publish Config"), EditorStyles.toolbarButton))
                {

                    SmartBuilderWindow.Open(2);

                    return;
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Opção 1"), false, () => Debug.Log("Opção 1 selecionada"));
                    menu.AddItem(new GUIContent("Opção 2"), false, () => Debug.Log("Opção 2 selecionada"));
                    menu.ShowAsContext();
                }
            } GUILayout.EndHorizontal();

            Rect fullRect = GUILayoutUtility.GetLastRect();

            GUILayout.Space(2);
            GUILayout.Box("", GUI.skin.verticalSlider, GUILayout.Width(.5f), GUILayout.Height(16));
            GUILayout.Space(8);

        }

        static void DrawBorder(Rect rect, float thickness, Color color)
        {
            // superior
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            // inferior
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), color);
            // esquerda
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            // direita
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), color);
        }

    }

}
#endif