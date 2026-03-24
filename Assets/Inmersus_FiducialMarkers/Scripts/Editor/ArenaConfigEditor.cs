using UnityEngine;
using UnityEditor;

namespace Inmersus.FiducialMarkers
{
    [CustomEditor(typeof(ArenaConfig))]
    public class ArenaConfigEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ArenaConfig arenaConfig = (ArenaConfig)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Acciones", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            if (GUILayout.Button("💾 Guardar cambios en JSON", GUILayout.Height(35)))
            {
                arenaConfig.GuardarAJSON();
                Debug.Log("[ArenaConfig] JSON guardado desde el Inspector.");
            }

            GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
            if (GUILayout.Button("🔄 Cargar desde JSON", GUILayout.Height(35)))
            {
                arenaConfig.CargarDesdeJSON();
                Debug.Log("[ArenaConfig] JSON cargado desde el Inspector.");
            }

            GUI.backgroundColor = Color.white;
        }

        private void OnSceneGUI()
        {
            ArenaConfig arenaConfig = (ArenaConfig)target;

            if (arenaConfig.marcadores == null || arenaConfig.marcadores.Count == 0)
                return;

            float ancho = arenaConfig.anchoArena;
            float alto = arenaConfig.altoArena;

            // Dibuja el perímetro de la arena
            Handles.color = new Color(0f, 1f, 0.5f, 0.8f);
            Vector3 p1 = new Vector3(0, 0, 0);
            Vector3 p2 = new Vector3(ancho, 0, 0);
            Vector3 p3 = new Vector3(ancho, 0, alto);
            Vector3 p4 = new Vector3(0, 0, alto);

            Handles.DrawLine(p1, p2);
            Handles.DrawLine(p2, p3);
            Handles.DrawLine(p3, p4);
            Handles.DrawLine(p4, p1);

            // Etiqueta del tamaño
            Handles.Label(new Vector3(ancho / 2, 0, -0.3f),
                $"{ancho}m x {alto}m",
                new GUIStyle { normal = { textColor = Color.green }, fontStyle = FontStyle.Bold });

            // Dibuja cada marcador
            foreach (var marker in arenaConfig.marcadores)
            {
                if (marker == null || marker.position == null) continue;

                Vector3 pos = new Vector3(marker.position.x, 0, marker.position.y);
                float size = marker.size;

                // Calcula distancia diagonal desde el origen (0,0)
                float distDesdeOrigen = Mathf.Sqrt(
                    marker.position.x * marker.position.x +
                    marker.position.y * marker.position.y
                );

                // Cuadrado del marcador
                Handles.color = new Color(1f, 0.5f, 0f, 0.9f);
                Vector3 m1 = pos + new Vector3(-size / 2, 0, -size / 2);
                Vector3 m2 = pos + new Vector3(size / 2, 0, -size / 2);
                Vector3 m3 = pos + new Vector3(size / 2, 0, size / 2);
                Vector3 m4 = pos + new Vector3(-size / 2, 0, size / 2);

                Handles.DrawLine(m1, m2);
                Handles.DrawLine(m2, m3);
                Handles.DrawLine(m3, m4);
                Handles.DrawLine(m4, m1);

                // Línea diagonal desde el origen al marcador
                Handles.color = new Color(1f, 1f, 0f, 0.4f);
                Handles.DrawDottedLine(Vector3.zero, pos, 3f);

                // Punto central
                Handles.color = Color.red;
                Handles.DrawSolidDisc(pos, Vector3.up, 0.05f);

                // Etiqueta — origen o distancia diagonal
                string etiqueta;
                if (distDesdeOrigen < 0.01f)
                    etiqueta = $"{marker.id}\n← origen (0m, 0m)";
                else
                    etiqueta = $"{marker.id}\n({marker.position.x}m, {marker.position.y}m)\nDesde origen: ~{distDesdeOrigen:F1}m";

                Handles.Label(pos + new Vector3(0, 0, size / 2 + 0.1f),
                    etiqueta,
                    new GUIStyle { normal = { textColor = Color.yellow }, fontStyle = FontStyle.Bold });
            }
        }
    }
}