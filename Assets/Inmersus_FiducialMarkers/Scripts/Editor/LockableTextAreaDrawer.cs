#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Inmersus.FiducialMarkers
{
    [CustomPropertyDrawer(typeof(LockableTextAreaAttribute))]
    public class LockableTextAreaDrawer : PropertyDrawer
    {
        private bool IsLocked(string id) => EditorPrefs.GetBool(id + "_locked", true);
        private void SetLocked(string id, bool val) => EditorPrefs.SetBool(id + "_locked", val);
        
        private bool IsExpanded(string id) => EditorPrefs.GetBool(id + "_expanded", false);
        private void SetExpanded(string id, bool val) => EditorPrefs.SetBool(id + "_expanded", val);

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            string id = property.propertyPath + "_" + property.serializedObject.targetObject.GetInstanceID();
            if (!IsExpanded(id)) return 22f; // Altura mínima cuando está contraído

            GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea);
            textAreaStyle.wordWrap = true;
            
            // Calculamos el largo dinámico de la caja para que todo el texto encaje de una vez
            float width = EditorGUIUtility.currentViewWidth - 50f;
            float textHeight = textAreaStyle.CalcHeight(new GUIContent(property.stringValue), width);
            
            // Foldout (22) + MargenCaja (5) + Botón (20) + MargenInterior (5) + Texto + MargenInferior (5)
            return 22f + 5f + 20f + 5f + textHeight + 5f; 
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            string id = property.propertyPath + "_" + property.serializedObject.targetObject.GetInstanceID();
            bool expanded = IsExpanded(id);
            bool locked = IsLocked(id);

            // 1. Dibuja Pestaña Contraíble (Foldout)
            Rect foldoutRect = new Rect(position.x, position.y, position.width, 20f);
            GUIStyle foldoutStyle = new GUIStyle(EditorStyles.foldoutHeader);
            foldoutStyle.fontStyle = FontStyle.Bold;
            foldoutStyle.normal.textColor = new Color(0.2f, 0.7f, 1f); // Tono amigable y discreto
            
            EditorGUI.BeginChangeCheck();
            expanded = EditorGUI.Foldout(foldoutRect, expanded, "ℹ️ DOCUMENTACIÓN DEL SCRIPT", true, foldoutStyle);
            if (EditorGUI.EndChangeCheck())
            {
                SetExpanded(id, expanded);
            }

            // 2. Si está abierto, dibujamos lo de adentro
            if (expanded)
            {
                // Caja Gris de fondo
                Rect boxRect = new Rect(position.x, position.y + 22f, position.width, position.height - 24f);
                GUI.Box(boxRect, "", EditorStyles.helpBox);

                // Botón interactivo dentro de la caja
                Rect buttonRect = new Rect(position.x + 5f, position.y + 27f, position.width - 10f, 20f);
                string btnText = locked ? "🔒 (Fijo) Clic para Modificar Info" : "🔓 (Editando) Clic para Bloquear Info";
                GUIStyle btnStyle = new GUIStyle(GUI.skin.button);
                btnStyle.normal.textColor = locked ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.9f, 0.3f, 0.3f);

                if (GUI.Button(buttonRect, btnText, btnStyle))
                {
                    SetLocked(id, !locked);
                }

                // Área de Texto Dinámica
                Rect textRect = new Rect(position.x + 5f, position.y + 50f, position.width - 10f, position.height - 50f - 5f);
                
                GUI.enabled = !locked; // Bloquea si está en modo Fijo
                GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea);
                textAreaStyle.wordWrap = true;
                
                EditorGUI.BeginChangeCheck();
                string newText = EditorGUI.TextArea(textRect, property.stringValue, textAreaStyle);
                if (EditorGUI.EndChangeCheck())
                {
                    property.stringValue = newText;
                }
                GUI.enabled = true; // Restaurar GUI
            }
        }
    }
}
#endif
