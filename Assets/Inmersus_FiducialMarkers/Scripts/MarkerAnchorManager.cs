using System.Collections.Generic;
using UnityEngine;
using Meta.XR.BuildingBlocks;

namespace Inmersus.FiducialMarkers
{
    public class MarkerAnchorManager : MonoBehaviour
    {
        [Header("Referencias")]
        public QRDetector detectorQR;
        public SharedSpatialAnchorCore anchorCore;

        [Header("Debug")]
        public bool mostrarMensajesDebug = true;

        private Dictionary<string, bool> _marcadoresProcesados = new Dictionary<string, bool>();

        private void Start()
        {
            if (detectorQR == null)
            {
                Debug.LogError("[MarkerAnchorManager] Falta asignar el QRDetector.");
                return;
            }

            detectorQR.OnQRDetected += OnQRDetected;
            detectorQR.StartScanning();

            if (mostrarMensajesDebug)
                Debug.Log("[MarkerAnchorManager] Sistema iniciado, escaneando...");
        }

        private void OnQRDetected(string qrContent, Vector2[] corners)
        {
            if (_marcadoresProcesados.ContainsKey(qrContent))
                return;

            MarkerConfig config = ArenaConfig.Instance?.GetMarkerById(qrContent);
            if (config == null)
            {
                if (mostrarMensajesDebug)
                    Debug.LogWarning($"[MarkerAnchorManager] QR detectado pero no está en la config: {qrContent}");
                return;
            }

            if (mostrarMensajesDebug)
                Debug.Log($"[MarkerAnchorManager] Marcador reconocido: {qrContent} | Tamaño: {config.size}m | Posición: ({config.position.x}, {config.position.y})");

            _marcadoresProcesados[qrContent] = true;

            Vector3 posicionMundo = new Vector3(config.position.x, 0f, config.position.y);

            CrearAnchor(qrContent, posicionMundo);
        }

        private void CrearAnchor(string markerId, Vector3 posicion)
        {
            if (anchorCore == null)
            {
                Debug.LogError("[MarkerAnchorManager] Falta asignar el SharedSpatialAnchorCore.");
                return;
            }

            if (mostrarMensajesDebug)
                Debug.Log($"[MarkerAnchorManager] Creando anchor para {markerId} en {posicion}");

            anchorCore.InstantiateSpatialAnchor(null, posicion, Quaternion.identity);
        }

        public void ReiniciarMarcadores()
        {
            _marcadoresProcesados.Clear();
            if (mostrarMensajesDebug)
                Debug.Log("[MarkerAnchorManager] Marcadores reiniciados.");
        }

        private void OnDestroy()
        {
            if (detectorQR != null)
                detectorQR.OnQRDetected -= OnQRDetected;
        }
    }
}