using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using ZXing;
using Meta.XR;

// REQUIERE en la escena: [BuildingBlock] Passthrough Camera Access (Meta XR SDK)
// REQUIERE en AndroidManifest: horizonos.permission.HEADSET_CAMERA

namespace Inmersus.FiducialMarkers
{
    public class QRDetector : MonoBehaviour
    {
        [Header("Configuración")]
        [Tooltip("Referencia al componente PassthroughCameraAccess del Building Block (cámara izquierda)")]
        public PassthroughCameraAccess passthroughCamera;

        [Tooltip("Cada cuántos segundos escanea. Menor = más rápido pero más CPU")]
        public float segundosEntreEscaneos = 0.2f;

        [Header("Debug")]
        [Tooltip("Muestra mensajes en consola cuando detecta un QR")]
        public bool mostrarMensajesDebug = true;

        // ---------------------------------------------------------------
        // Internos
        // ---------------------------------------------------------------
        private BarcodeReader _barcodeReader;
        private bool          _isScanning = false;

        /// <summary>
        /// Se dispara con (contenido del QR, esquinas normalizadas 0-1).
        /// Las esquinas pueden ser null si ZXing no las reportó.
        /// </summary>
        public event Action<string, Vector2[]> OnQRDetected;

        /// <summary>Se dispara cuando el escaneo comienza (cámara lista).</summary>
        public event Action OnScanningStarted;

        /// <summary>Se dispara si la cámara no se pudo inicializar.</summary>
        public event Action<string> OnCameraError;

        // ---------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------
        private void Start()
        {
            InitializeReader();

            if (passthroughCamera == null)
                passthroughCamera = FindFirstObjectByType<PassthroughCameraAccess>();

            if (passthroughCamera == null)
            {
                string error = "No se encontró PassthroughCameraAccess en la escena. " +
                               "Agrega el Building Block '[BuildingBlock] Passthrough Camera Access'.";
                Debug.LogError($"[QRDetector] {error}");
                OnCameraError?.Invoke(error);
                return;
            }

            StartCoroutine(WaitForCameraAndStart());
        }

        private void OnDestroy()
        {
            _isScanning = false;
        }

        // ---------------------------------------------------------------
        // Inicialización
        // ---------------------------------------------------------------
        private void InitializeReader()
        {
            _barcodeReader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder       = true,
                    PossibleFormats = new BarcodeFormat[] { BarcodeFormat.QR_CODE }
                }
            };

            if (mostrarMensajesDebug)
                Debug.Log("[QRDetector] Lector ZXing inicializado.");
        }

        /// <summary>
        /// Espera a que PassthroughCameraAccess esté reproduciendo frames
        /// antes de iniciar el loop de escaneo.
        /// </summary>
        private IEnumerator WaitForCameraAndStart()
        {
            if (mostrarMensajesDebug)
                Debug.Log("[QRDetector] Esperando que PassthroughCameraAccess esté listo...");

            float timeout = 30f;
            float elapsed = 0f;

            while (!passthroughCamera.IsPlaying && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!passthroughCamera.IsPlaying)
            {
                string error = $"PassthroughCameraAccess no comenzó a reproducir después de {timeout}s. " +
                               "Verifica el permiso HEADSET_CAMERA y que el Building Block esté activo.";
                Debug.LogError($"[QRDetector] {error}");
                OnCameraError?.Invoke(error);
                yield break;
            }

            if (mostrarMensajesDebug)
                Debug.Log($"[QRDetector] Cámara lista ({passthroughCamera.CurrentResolution.x}x{passthroughCamera.CurrentResolution.y}). Iniciando escaneo.");

            StartScanning();
        }

        // ---------------------------------------------------------------
        // Control de escaneo
        // ---------------------------------------------------------------
        public void StartScanning()
        {
            if (!_isScanning && passthroughCamera != null && passthroughCamera.IsPlaying)
            {
                _isScanning = true;
                StartCoroutine(ScanLoop());
                OnScanningStarted?.Invoke();

                if (mostrarMensajesDebug)
                    Debug.Log("[QRDetector] Escaneo iniciado.");
            }
        }

        public void StopScanning()
        {
            _isScanning = false;

            if (mostrarMensajesDebug)
                Debug.Log("[QRDetector] Escaneo detenido.");
        }

        // ---------------------------------------------------------------
        // Loop de escaneo
        // ---------------------------------------------------------------
        private IEnumerator ScanLoop()
        {
            while (_isScanning)
            {
                yield return new WaitForSeconds(segundosEntreEscaneos);
                yield return new WaitForEndOfFrame();

                // Solo escanear si hay un frame nuevo de la cámara
                if (passthroughCamera != null &&
                    passthroughCamera.IsPlaying &&
                    passthroughCamera.IsUpdatedThisFrame)
                {
                    ScanFrame();
                }
            }
        }

        private void ScanFrame()
        {
            try
            {
                // Obtener pixels desde la API nativa de Meta (CPU readback)
                NativeArray<Color32> colors = passthroughCamera.GetColors();

                if (!colors.IsCreated || colors.Length == 0)
                    return;

                int width  = passthroughCamera.CurrentResolution.x;
                int height = passthroughCamera.CurrentResolution.y;

                // Convertir NativeArray a Color32[] que acepta ZXing
                Color32[] pixels = colors.ToArray();

                var result = _barcodeReader.Decode(pixels, width, height);

                if (result != null)
                {
                    if (mostrarMensajesDebug)
                    {
                        // Log muy visible con el texto EXACTO — verificá que coincida con tu arena_config.json
                        Debug.Log($"[QRDetector] *** QR DETECTADO *** Texto exacto: '{result.Text}' " +
                                  $"(longitud: {result.Text.Length} chars) Formato: {result.BarcodeFormat}");
                    }

                    Vector2[] corners = null;
                    if (result.ResultPoints != null && result.ResultPoints.Length >= 3)
                    {
                        corners = new Vector2[result.ResultPoints.Length];
                        for (int i = 0; i < result.ResultPoints.Length; i++)
                        {
                            corners[i] = new Vector2(
                                result.ResultPoints[i].X / width,
                                result.ResultPoints[i].Y / height
                            );
                        }
                    }

                    OnQRDetected?.Invoke(result.Text, corners);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[QRDetector] Error al escanear frame: {e.Message}");
            }
        }
    }
}