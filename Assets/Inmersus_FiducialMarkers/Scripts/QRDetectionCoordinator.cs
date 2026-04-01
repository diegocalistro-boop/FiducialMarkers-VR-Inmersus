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
        [Header("Referencias")]
        public MarkerAnchorManager anchorManager;
        public AprilTagDetector    detectorTag;
        public QRScanningUI        scanningUI;   // opcional

        [Header("Configuración")]
        [Tooltip("Número mínimo de QR escaneados para considerar la arena calibrada")]
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

        private void CompletarCalibracion()
        {
            _calibrado = true;

            if (mostrarMensajesDebug)
                Debug.Log("[QRDetectionCoordinator] ¡Arena calibrada! Todos los marcadores escaneados.");

            // Detener el escaneo para ahorrar CPU/batería
            if (detectorTag != null)
                detectorTag.StopScanning();

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
