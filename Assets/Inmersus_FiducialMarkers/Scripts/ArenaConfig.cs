using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Inmersus.FiducialMarkers
{
    [Serializable]
    public class MarkerPosition
    {
        public float x;
        public float y;
    }

    [Serializable]
    public class MarkerConfig
    {
        public string id;

        [Tooltip("Tamaño del lado del marcador en centímetros. Ej: 20 para 20cm")]
        public float size; // en centímetros

        public MarkerPosition position;

        /// <summary>Tamaño en metros (para uso interno del engine).</summary>
        public float SizeInMeters => size / 100f;
    }

    [Serializable]
    public class ArenaData
    {
        public string name;
        public float width;
        public float height;
    }

    [Serializable]
    public class ArenaConfigData
    {
        public ArenaData arena;
        public List<MarkerConfig> markers;
    }

    public class ArenaConfig : MonoBehaviour
    {
        [SerializeField] [LockableTextArea] private string descripcionScript = "DICCIONARIO DE LA ARENA. Guarda las medidas y coordenadas de los AprilTags del mundo real en un JSON. Es el 'mapa' que los demás leen para saber dónde deberian estar los objetos virtuales.";

        public static ArenaConfig Instance { get; private set; }

        [Header("Configuración de la Arena")]
        public string nombreArena = "Arena Principal";
        public float anchoArena = 6f;
        public float altoArena = 6f;

        [Header("Marcadores")]
        public List<MarkerConfig> marcadores = new List<MarkerConfig>();

        [Header("Debug")]
        public bool mostrarMensajesDebug = true;

        public ArenaConfigData Config { get; private set; }

        private string _rutaJson => Path.Combine(Application.streamingAssetsPath, "arena_config.json");

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CargarDesdeJSON();
        }

        public void CargarDesdeJSON()
        {
            if (File.Exists(_rutaJson))
            {
                string json = File.ReadAllText(_rutaJson);
                Config = JsonUtility.FromJson<ArenaConfigData>(json);

                // Sincroniza el Inspector con el JSON
                nombreArena = Config.arena.name;
                anchoArena = Config.arena.width;
                altoArena = Config.arena.height;
                marcadores = Config.markers;

                if (mostrarMensajesDebug)
                    Debug.Log($"[ArenaConfig] Cargado: {nombreArena} - {marcadores.Count} marcadores");
            }
            else
            {
                Debug.LogWarning($"[ArenaConfig] No se encontró el JSON, usando valores del Inspector.");
                SincronizarAConfig();
            }
        }

        public void GuardarAJSON()
        {
            SincronizarAConfig();
            string json = JsonUtility.ToJson(Config, true);
            File.WriteAllText(_rutaJson, json);

            if (mostrarMensajesDebug)
                Debug.Log($"[ArenaConfig] Guardado en: {_rutaJson}");
        }

        private void SincronizarAConfig()
        {
            Config = new ArenaConfigData
            {
                arena = new ArenaData
                {
                    name = nombreArena,
                    width = anchoArena,
                    height = altoArena
                },
                markers = marcadores
            };
        }

        public MarkerConfig GetMarkerById(string id)
        {
            return Config?.markers.Find(m => m.id == id);
        }
    }
}