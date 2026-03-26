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
        private const double HoldOpenDelaySeconds = 0.8d;
        private static double s_buildPressStartTime = -1d;
        private static bool s_buildHoldTriggered;
        private static bool s_buildButtonPressed;

        static SmartbuildToolbar()
        {
            ToolbarExtender.RightToolbarGUI.Add(OnToolbarGUI);
        }

        static void OnToolbarGUI()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Space(8);
            DrawToolbarSeparator();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            {
                GUIContent content = new GUIContent("Smart Build");
                Vector2 size = EditorStyles.linkLabel.CalcSize(content);
                Rect rect = GUILayoutUtility.GetRect(size.x, size.y);

                if (EditorGUI.LinkButton(rect, content))
                {
                    SmartBuilderWindow.OpenBound(1);
                }

                GUILayout.Space(8);

                // Usando um Popup (dropdown) do IMGUI
                EditorGUI.BeginChangeCheck();

                SmartUploader.Settings.buildType = (BuildType)EditorGUILayout.EnumPopup(SmartUploader.Settings.buildType, EditorStyles.toolbarPopup, GUILayout.Width(120));

                if (EditorGUI.EndChangeCheck())
                {
                    //GameManager.Config.buildType = buildType;
                }

                GUIContent buildContent = new GUIContent("BUILD", "Hold for Build Config");
                bool buildClicked = GUILayout.Button(buildContent, EditorStyles.toolbarButton);
                Rect buildButtonRect = GUILayoutUtility.GetLastRect();
                HandleBuildButtonHold(buildButtonRect);

                if (buildClicked && !s_buildHoldTriggered)
                    SmartBuilder.Build();

                if (Event.current != null && Event.current.rawType == EventType.MouseUp)
                {
                    s_buildButtonPressed = false;
                    s_buildPressStartTime = -1d;
                    s_buildHoldTriggered = false;
                }

                if (GUILayout.Button(new GUIContent("PUBLISH", "Hold for Publish Config"), EditorStyles.toolbarButton))
                {
                    SmartBuilderWindow.OpenBound(2);
                }
            } GUILayout.EndHorizontal();

        }

        static void DrawToolbarSeparator()
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 16f, GUILayout.Width(1f), GUILayout.Height(16f));
            rect.y += 1f;
            rect.height -= 2f;
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.14f)
                : new Color(0f, 0f, 0f, 0.18f));
        }

        static void HandleBuildButtonHold(Rect buildButtonRect)
        {
            Event currentEvent = Event.current;
            if (currentEvent == null)
                return;

            if (currentEvent.rawType == EventType.MouseDown && buildButtonRect.Contains(currentEvent.mousePosition))
            {
                s_buildButtonPressed = true;
                s_buildPressStartTime = EditorApplication.timeSinceStartup;
                s_buildHoldTriggered = false;
                return;
            }

            if (currentEvent.rawType == EventType.MouseUp)
            {
                s_buildButtonPressed = false;
                s_buildPressStartTime = -1d;
                s_buildHoldTriggered = false;
                return;
            }

            if (!s_buildButtonPressed || s_buildHoldTriggered)
                return;

            if (!buildButtonRect.Contains(currentEvent.mousePosition))
            {
                s_buildButtonPressed = false;
                s_buildPressStartTime = -1d;
                s_buildHoldTriggered = false;
                return;
            }

            if (EditorApplication.timeSinceStartup - s_buildPressStartTime < HoldOpenDelaySeconds)
                return;

            s_buildHoldTriggered = true;
            s_buildButtonPressed = false;
            SmartBuilderWindow.OpenBound(1);
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
