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
        [SerializeField] [LockableTextArea] private string descripcionScript = "OJO ÓPTICO. Prende y controla la cámara Passthrough de Quest, buscando cuadros AprilTag y avisando continuamente a los demás scripts sus datos fotográficos en el espacio.";

        [Header("Configuración")]
        [Tooltip("Referencia al componente PassthroughCameraAccess")]
        public PassthroughCameraAccess passthroughCamera;

        [Tooltip("Tamaño físico del lado del AprilTag impreso (en centímetros). Ej: 20 para 20cm")]
        public float tagSizeCm = 20f;

        /// <summary>Tamaño en metros (para uso interno del engine).</summary>
        public float tagSize => tagSizeCm / 100f;

        [Tooltip("Cada cuántos segundos escanea (modo calibración = rápido).")]
        public float segundosEntreEscaneos = 0.1f;

        [Tooltip("Cada cuántos segundos escanea en modo bajo consumo (post-calibración).")]
        public float segundosEntreEscaneosLowPower = 10.0f;

        private bool _lowPowerMode = false;

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
            _lowPowerMode = false;
        }

        /// <summary>
        /// Activa el escaneo en modo bajo consumo (cada 2s en vez de 0.1s).
        /// Ideal para corrección de drift post-calibración sin causar vibración.
        /// </summary>
        public void StartScanningLowPower()
        {
            _lowPowerMode = true;
            if (!_isScanning && passthroughCamera != null && passthroughCamera.IsPlaying)
            {
                _isScanning = true;
                StartCoroutine(ScanLoop());
            }
        }

        // ---------------------------------------------------------------
        // Loop de escaneo
        // ---------------------------------------------------------------
        private IEnumerator ScanLoop()
        {
            while (_isScanning)
            {
                float intervalo = _lowPowerMode ? segundosEntreEscaneosLowPower : segundosEntreEscaneos;
                yield return new WaitForSeconds(intervalo);
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

                // Calcular FOV para el paquete de Keijiro.
                // IMPORTANTE: jp.keijiro.apriltag calcula internamente la focal así:
                // focalLength = height / 2 / math.tan(fov / 2)
                // Esto significa que 'fov' debe ser el FOV VERTICAL en RADIANES.
                var intrinsics = passthroughCamera.Intrinsics;
                float fov;
                if (intrinsics.FocalLength.y > 0 && intrinsics.SensorResolution.y > 0)
                {
                    // Despejamos el fov para que Keijiro obtenga exactamente nuestro FocalLength.y real.
                    fov = 2f * Mathf.Atan(intrinsics.SensorResolution.y / (2f * intrinsics.FocalLength.y));
                    
                    if (mostrarMensajesDebug && Time.frameCount % 300 == 0)
                        Debug.Log($"[AprilTagDetector] FOV vertical en radianes inyectado a Keijiro: {fov:F3}");
                }
                else
                {
                    // Fallback usando Camera.main.fieldOfView (que ya es vertical, lo pasamos a radianes)
                    fov = Camera.main != null ? Camera.main.fieldOfView * Mathf.Deg2Rad : 1.0f;
                    if (mostrarMensajesDebug && Time.frameCount % 300 == 0)
                        Debug.LogWarning("[AprilTagDetector] Intrínsecos no disponibles, usando FOV vertical de fallback");
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