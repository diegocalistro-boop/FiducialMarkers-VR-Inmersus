using UnityEngine;
using UnityEditor;

namespace Inmersus.FiducialMarkers
{
    [CustomPropertyDrawer(typeof(MarkerConfig))]
    public class MarkerConfigDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Encontrar la propiedad "id" dentro de este MarkerConfig
            SerializedProperty idProp = property.FindPropertyRelative("id");
            string idStr = idProp != null ? idProp.stringValue : "?";
            
            // Extraer el índice del array desde el propertyPath (ej: "marcadores.Array.data[3]")
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
            if (index >= 0)
                label.text = $"Tag {index + 1} (ID: {idStr})";
            else
                label.text = $"Tag (ID: {idStr})";

            // Comenzar el bloque de propiedad para que funcione bien con deshacer/rehacer y prefabs
            EditorGUI.BeginProperty(position, label, property);
            
            // Dibujar la propiedad con todos sus hijos usando nuestra nueva etiqueta
            EditorGUI.PropertyField(position, property, label, true);
            
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Devolver la altura total necesaria para dibujar todos los campos que estén desplegados
            return EditorGUI.GetPropertyHeight(property, true);
        }
    }
}
