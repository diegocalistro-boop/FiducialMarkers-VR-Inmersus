using UnityEngine;

namespace Inmersus.FiducialMarkers
{
    /// <summary>
    /// Coordina el proceso de calibración de la arena.
    /// Escucha a MarkerAnchorManager y determina cuándo el espacio
    /// está listo (≥ anchorsNecesarios escaneados y compartidos).
    /// 
    /// Cuando la calibración está completa:
    ///  - Dispara el evento OnArenaCalibrated
    ///  - Desactiva el QRDetector para ahorrar CPU
    ///  - Puede mostrar un mensaje al usuario
    /// </summary>
    public class QRDetectionCoordinator : MonoBehaviour
    {
        [SerializeField] [LockableTextArea] private string descripcionScript =
            "EL ÁRBITRO DE LA PARTIDA. Lee automáticamente cuántos tags hay en el ArenaConfig (JSON) " +
            "y espera que todos sean escaneados antes de pitar 'Calibración Exitosa'.\n\n" +
            "═══ VINCULACIÓN CON ARENACONFIG ═══\n" +
            "• En Start() lee ArenaConfig.Instance.Config.markers.Count\n" +
            "• Si ArenaConfig no existe, usa el valor manual de 'anchorsNecesarios' como fallback\n" +
            "• Si agregas/quitas tags en el JSON, este script se actualiza solo";

        [Header("Referencias")]
        public MarkerAnchorManager anchorManager;
        public AprilTagDetector    detectorTag;
        public QRScanningUI        scanningUI;   // opcional

        [Header("Configuración")]
        [Tooltip("Fallback: Se usa solo si ArenaConfig no está disponible. " +
                 "Si ArenaConfig existe, se lee automáticamente del JSON.")]
        [Min(1)]
        public int anchorsNecesarios = 2;

        [Header("Debug")]
        public bool mostrarMensajesDebug = true;

        // ---------------------------------------------------------------
        // Eventos
        // ---------------------------------------------------------------

        /// <summary>
        /// Se dispara cuando la arena está completamente calibrada
        /// (todos los anchors necesarios fueron creados y compartidos).
        /// </summary>
        public event System.Action OnArenaCalibrated;

        // ---------------------------------------------------------------
        // Estado interno
        // ---------------------------------------------------------------
        private int  _anchorsCompletados = 0;
        private bool _calibrado          = false;

        // ---------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------
        private void Start()
        {
            if (anchorManager == null)
                anchorManager = FindFirstObjectByType<MarkerAnchorManager>();

            if (detectorTag == null)
                detectorTag = FindFirstObjectByType<AprilTagDetector>();

            if (anchorManager == null)
            {
                Debug.LogError("[QRDetectionCoordinator] No se encontró MarkerAnchorManager.");
                return;
            }

            // Leer automáticamente cuántos marcadores hay en el ArenaConfig
            if (ArenaConfig.Instance != null && ArenaConfig.Instance.Config != null
                && ArenaConfig.Instance.Config.markers != null
                && ArenaConfig.Instance.Config.markers.Count > 0)
            {
                anchorsNecesarios = ArenaConfig.Instance.Config.markers.Count;
                if (mostrarMensajesDebug)
                    Debug.Log($"[QRDetectionCoordinator] ArenaConfig detectado: {anchorsNecesarios} marcador(es) en el JSON.");
            }
            else
            {
                if (mostrarMensajesDebug)
                    Debug.LogWarning($"[QRDetectionCoordinator] ArenaConfig no disponible, usando fallback manual: {anchorsNecesarios}");
            }

            anchorManager.OnMarkerAnchorCreated += OnAnchorCreado;

            if (mostrarMensajesDebug)
                Debug.Log($"[QRDetectionCoordinator] Esperando {anchorsNecesarios} marcador(es)...");
        }

        private void OnDestroy()
        {
            if (anchorManager != null)
                anchorManager.OnMarkerAnchorCreated -= OnAnchorCreado;
        }

        // ---------------------------------------------------------------
        // Callbacks
        // ---------------------------------------------------------------
        private void OnAnchorCreado(string markerId, OVRSpatialAnchor anchor)
        {
            if (_calibrado) return;

            _anchorsCompletados++;

            if (mostrarMensajesDebug)
                Debug.Log($"[QRDetectionCoordinator] Anchor {_anchorsCompletados}/{anchorsNecesarios}: {markerId}");

            if (_anchorsCompletados >= anchorsNecesarios)
            {
                CompletarCalibracion();
            }
        }

        public void CompletarCalibracion()
        {
            _calibrado = true;

            if (mostrarMensajesDebug)
                Debug.Log("[QRDetectionCoordinator] ¡Arena calibrada! Todos los marcadores escaneados.");

            // Detener escaneo tras calibración para eliminar vibración al caminar.
            // El AutoAlignmentCorrector lo reactivará cuando necesite re-escanear.
            if (detectorTag != null)
            {
                detectorTag.StopScanning();
                if (mostrarMensajesDebug)
                    Debug.Log("[QRDetectionCoordinator] Escaneo detenido post-calibración.");
            }

            OnArenaCalibrated?.Invoke();
        }

        // ---------------------------------------------------------------
        // API pública
        // ---------------------------------------------------------------

        /// <summary>True si la arena ya está calibrada.</summary>
        public bool EstaCalibrara => _calibrado;

        /// <summary>Reinicia la calibración (por ejemplo si cambia de sala).</summary>
        public void ReiniciarCalibracion()
        {
            _calibrado          = false;
            _anchorsCompletados = 0;

            if (anchorManager != null)
                anchorManager.ReiniciarMarcadores();

            if (detectorTag != null)
                detectorTag.StartScanning();

            if (mostrarMensajesDebug)
                Debug.Log("[QRDetectionCoordinator] Calibración reiniciada.");
        }
    }
}
