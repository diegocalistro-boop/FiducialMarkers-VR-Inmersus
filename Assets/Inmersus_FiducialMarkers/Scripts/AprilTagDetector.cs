using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using AprilTag;
using Meta.XR;

namespace Inmersus.FiducialMarkers
{
    public class AprilTagDetector : MonoBehaviour
    {
        [Header("Configuración")]
        [Tooltip("Referencia al componente PassthroughCameraAccess")]
        public PassthroughCameraAccess passthroughCamera;

        [Tooltip("Tamaño físico del lado del AprilTag impreso (en centímetros). Ej: 20 para 20cm")]
        public float tagSizeCm = 20f;

        /// <summary>Tamaño en metros (para uso interno del engine).</summary>
        public float tagSize => tagSizeCm / 100f;

        [Tooltip("Cada cuántos segundos escanea.")]
        public float segundosEntreEscaneos = 0.1f;

        [Header("Debug")]
        public bool mostrarMensajesDebug = true;

        // ---------------------------------------------------------------
        // Internos
        // ---------------------------------------------------------------
        private TagDetector _detector;
        private bool        _isScanning = false;
        private int         _cameraWidth = 0;
        private int         _cameraHeight = 0;

        /// <summary>
        /// Se dispara con: ID numérico del AprilTag, Posición Local, Rotación Local (respecto a la cámara)
        /// </summary>
        public event Action<int, Vector3, Quaternion> OnTagDetected;

        /// <summary>Se dispara cuando el escaneo comienza.</summary>
        public event Action OnScanningStarted;

        /// <summary>Se dispara si la cámara no se pudo inicializar.</summary>
        public event Action<string> OnCameraError;

        // ---------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------
        private void Start()
        {
            if (passthroughCamera == null)
                passthroughCamera = FindFirstObjectByType<PassthroughCameraAccess>();

            if (passthroughCamera == null)
            {
                string error = "No se encontró PassthroughCameraAccess en la escena.";
                Debug.LogError($"[AprilTagDetector] {error}");
                OnCameraError?.Invoke(error);
                return;
            }

            StartCoroutine(WaitForCameraAndStart());
        }

        private void OnDestroy()
        {
            _isScanning = false;
            _detector?.Dispose();
        }

        // ---------------------------------------------------------------
        // Inicialización
        // ---------------------------------------------------------------
        private IEnumerator WaitForCameraAndStart()
        {
            if (mostrarMensajesDebug)
                Debug.Log("[AprilTagDetector] Esperando que PassthroughCameraAccess esté listo...");

            float timeout = 30f;
            float elapsed = 0f;

            while (!passthroughCamera.IsPlaying && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!passthroughCamera.IsPlaying)
            {
                string error = $"PassthroughCameraAccess no comenzó a reproducir después de {timeout}s.";
                Debug.LogError($"[AprilTagDetector] {error}");
                OnCameraError?.Invoke(error);
                yield break;
            }

            _cameraWidth = passthroughCamera.CurrentResolution.x;
            _cameraHeight = passthroughCamera.CurrentResolution.y;

            // decimation: 2 reduce la resolución a la mitad para mayor rendimiento (Quest)
            _detector = new TagDetector(_cameraWidth, _cameraHeight, decimation: 2);

            if (mostrarMensajesDebug)
                Debug.Log($"[AprilTagDetector] Cámara lista ({_cameraWidth}x{_cameraHeight}). Iniciando escaneo AprilTag.");

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
            }
        }

        public void StopScanning()
        {
            _isScanning = false;
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
            if (_detector == null) return;

            try
            {
                NativeArray<Color32> colors = passthroughCamera.GetColors();

                if (!colors.IsCreated || colors.Length == 0)
                    return;

                // Calcular FOV horizontal real desde los intrínsecos de la cámara passthrough.
                // FOV = 2 * atan(sensorWidth / (2 * focalLength))
                var intrinsics = passthroughCamera.Intrinsics;
                float fov;
                if (intrinsics.FocalLength.x > 0 && intrinsics.SensorResolution.x > 0)
                {
                    fov = 2f * Mathf.Atan2(intrinsics.SensorResolution.x * 0.5f, intrinsics.FocalLength.x) * Mathf.Rad2Deg;
                    if (mostrarMensajesDebug && Time.frameCount % 300 == 0)
                        Debug.Log($"[AprilTagDetector] FOV horizontal real de la cámara passthrough: {fov:F1}°");
                }
                else
                {
                    // Fallback si los intrínsecos no están disponibles
                    fov = 60f;
                    if (mostrarMensajesDebug && Time.frameCount % 300 == 0)
                        Debug.LogWarning("[AprilTagDetector] Intrínsecos no disponibles, usando FOV fallback de 60°");
                }

                var pixels = colors.ToArray();
                // Procesa y detecta AprilTags con la resolución real de la cámara física.
                // Ajusta pose según tamaño físico del tag.
                _detector.ProcessImage(pixels, fov, tagSize);

                foreach (var tag in _detector.DetectedTags)
                {
                    if (mostrarMensajesDebug)
                    {
                        Debug.Log($"[AprilTagDetector] *** AprilTag ID {tag.ID} DETECTADO *** " +
                                  $"Pos: {tag.Position} | Rot: {tag.Rotation.eulerAngles}");
                    }

                    OnTagDetected?.Invoke(tag.ID, tag.Position, tag.Rotation);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AprilTagDetector] Error al escanear frame: {e.Message}");
            }
        }
    }
}