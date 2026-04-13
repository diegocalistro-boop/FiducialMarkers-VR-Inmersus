using UnityEngine;
using UnityEditor;

namespace Inmersus.FiducialMarkers
{
    [CustomPropertyDrawer(typeof(MarkerConfig))]
    public class MarkerConfigDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.isExpanded)
            {
                var posProp = property.FindPropertyRelative("position");
                float height = EditorGUIUtility.singleLineHeight + 2f; // foldout rect
                height += EditorGUIUtility.singleLineHeight + 2f; // id rect
                height += EditorGUIUtility.singleLineHeight + 2f; // size rect
                height += EditorGUI.GetPropertyHeight(posProp, true) + 2f; // position rect
                return height;
            }
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Encontrar la propiedad "id" dentro de este MarkerConfig
            SerializedProperty idProp = property.FindPropertyRelative("id");
            string idStr = idProp != null && !string.IsNullOrEmpty(idProp.stringValue) ? idProp.stringValue : "?";
            
            // Extraer el índice del array desde el propertyPath
            int index = -1;
            int startBracket = property.propertyPath.LastIndexOf('[');
            if (startBracket >= 0)
            {
                int endBracket = property.propertyPath.IndexOf(']', startBracket);
                if (endBracket > startBracket)
                {
                    string indexStr = property.propertyPath.Substring(startBracket + 1, endBracket - startBracket - 1);
                    int.TryParse(indexStr, out index);
                }
            }

            // Cambiar la etiqueta para que muestre "Tag N (ID: X)"
            string customLabel = (index >= 0) ? $"Tag {index + 1} (ID: {idStr})" : $"Tag (ID: {idStr})";

            EditorGUI.BeginProperty(position, label, property);
            
            Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, customLabel, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                
                var sizeProp = property.FindPropertyRelative("size");
                var posProp = property.FindPropertyRelative("position");

                float y = position.y + EditorGUIUtility.singleLineHeight + 2f;
                Rect idRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.PropertyField(idRect, idProp);

                y += EditorGUIUtility.singleLineHeight + 2f;
                Rect sizeRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
                // Aquí renombramos gráficamente el Size a "Tamaño (cm)"
                EditorGUI.PropertyField(sizeRect, sizeProp, new GUIContent("Tamaño (cm)", "Tamaño del lado del marcador en centímetros. Ej: 20"));

                y += EditorGUIUtility.singleLineHeight + 2f;
                float posHeight = EditorGUI.GetPropertyHeight(posProp, true);
                Rect posRect = new Rect(position.x, y, position.width, posHeight);
                EditorGUI.PropertyField(posRect, posProp, true);

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }
}
