using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace Inmersus.FiducialMarkers
{
    public class AutoAlignmentCorrector : MonoBehaviour
    {
        [Header("Referencias")]
        public MarkerAnchorManager anchorManager;
        public AprilTagDetector aprilTagDetector;
        public Transform arenaRoot;
        public OVRCameraRig cameraRig;

        [Header("Configuración de Auto-Corrección")]
        [Tooltip("Habilita la corrección automática de drift en background.")]
        public bool autoCorrectionEnabled = true;

        [Tooltip("Cuadros de lectura necesarios para confiar en un Tag.")]
        public int lecturasRequeridas = 30;

        [Tooltip("Distancia máxima a un tag para considerarlo legible (en metros).")]
        public float distanciaMaximaEscaneo = 2.0f;

        [Tooltip("Tiempo que tarda la transición suave en aplicarse.")]
        public float tiempoInterpolacion = 2.0f;

        [Tooltip("Solo corrige rotación si lee al menos 2 tags en este lapso de segundos.")]
        public float ventanaTiempoMultiTag = 10.0f;

        private Dictionary<string, DriftFilter> _filtrosTag = new Dictionary<string, DriftFilter>();
        private Dictionary<string, float> _ultimoTiempoValido = new Dictionary<string, float>();
        
        private bool _isCorrecting = false;

        private void Start()
        {
            if (aprilTagDetector != null)
                aprilTagDetector.OnTagDetected += OnTagDetected;
            
            if (cameraRig == null)
                cameraRig = FindFirstObjectByType<OVRCameraRig>();
        }

        private void OnDestroy()
        {
            if (aprilTagDetector != null)
                aprilTagDetector.OnTagDetected -= OnTagDetected;
        }

        private void OnTagDetected(int tagID, Vector3 localPos, Quaternion localRot)
        {
            if (!autoCorrectionEnabled || _isCorrecting || anchorManager == null || !anchorManager.AlineacionHecha)
                return;

            string idStr = tagID.ToString();
            var config = ArenaConfig.Instance?.GetMarkerById(idStr);
            if (config == null) return; // No es parte de la arena

            // Transformar la posición óptica (Camera Space) a World Space
            Vector3 worldPos = cameraRig.centerEyeAnchor.TransformPoint(localPos);

            // Validar distancia para asegurar que es una lectura de alta calidad
            float distancia = Vector3.Distance(cameraRig.centerEyeAnchor.position, worldPos);
            if (distancia > distanciaMaximaEscaneo) return;

            // Registrar lectura en el filtro
            if (!_filtrosTag.ContainsKey(idStr))
                _filtrosTag[idStr] = new DriftFilter(lecturasRequeridas);

            // Guardamos ignorando la rotación por ahora para simplificar el filtro anti-jitter posicional
            _filtrosTag[idStr].AgregarLectura(worldPos, Quaternion.identity);

            // Chequear si el filtro está lleno y sólido
            if (_filtrosTag[idStr].TieneDatosCompletos)
            {
                // Validación de ruido/varianza
                float varianza = _filtrosTag[idStr].CalcularVarianza();
                if (varianza < 0.005f) // Si la varianza es muy baja, es un dato excelente
                {
                    _ultimoTiempoValido[idStr] = Time.time;
                    EvaluarCorreccion();
                }
            }
        }

        private void EvaluarCorreccion()
        {
            // Limpiar datos viejos
            var tagsRecientes = _ultimoTiempoValido.Where(kvp => Time.time - kvp.Value <= ventanaTiempoMultiTag).ToList();
            
            if (tagsRecientes.Count == 0) return;

            if (tagsRecientes.Count == 1)
            {
                // Un solo tag reciente: Corregir solo posición (X, Z)
                string tagId = tagsRecientes[0].Key;
                Vector3 opticalAvg = _filtrosTag[tagId].PromedioPosicion();
                
                var config = ArenaConfig.Instance.GetMarkerById(tagId);
                Vector3 uniPos = new Vector3(config.position.x, 0, config.position.y);

                StartCoroutine(CorrectPositionOnly(uniPos, opticalAvg));
            }
            else
            {
                // Dos o más tags recientes: Corregir Posición Y Rotación
                string tag0 = tagsRecientes[0].Key;
                string tag1 = tagsRecientes[1].Key;

                Vector3 fis0 = _filtrosTag[tag0].PromedioPosicion();
                Vector3 fis1 = _filtrosTag[tag1].PromedioPosicion();

                var c0 = ArenaConfig.Instance.GetMarkerById(tag0);
                var c1 = ArenaConfig.Instance.GetMarkerById(tag1);

                Vector3 uni0 = new Vector3(c0.position.x, 0, c0.position.y);
                Vector3 uni1 = new Vector3(c1.position.x, 0, c1.position.y);

                StartCoroutine(CorrectPositionAndRotation(fis0, fis1, uni0, uni1));
            }

            // Limpiamos filtros para evitar re-gatillar constantemente
            foreach (var tag in tagsRecientes)
            {
                _filtrosTag[tag.Key].Limpiar();
            }
        }

        private IEnumerator CorrectPositionOnly(Vector3 uniPos, Vector3 opticalAvg)
        {
            _isCorrecting = true;
            Debug.Log("[AutoAlignment] Iniciando corrección de Posición (1 Tag)");

            // ¿Dónde está ArenaRoot ahora?
            Vector3 startPos = arenaRoot.position;

            // Queremos que arenaRoot.TransformPoint(uniPos) coincida con opticalAvg en X y Z.
            // O sea: arenaRoot.position + (rotación actual * uniPos) = opticalAvg
            Vector3 targetRootPos = opticalAvg - (arenaRoot.rotation * uniPos);
            
            // Forzar a que la altura (Y) se mantenga intacta
            targetRootPos.y = startPos.y; 

            float elapsed = 0f;
            while (elapsed < tiempoInterpolacion)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / tiempoInterpolacion;
                t = t * t * (3f - 2f * t); // SmoothStep

                arenaRoot.position = Vector3.Lerp(startPos, targetRootPos, t);
                yield return null;
            }

            arenaRoot.position = targetRootPos;
            _isCorrecting = false;
        }

        private IEnumerator CorrectPositionAndRotation(Vector3 fis0, Vector3 fis1, Vector3 uni0, Vector3 uni1)
        {
            _isCorrecting = true;
            Debug.Log("[AutoAlignment] Iniciando corrección de Posición y Rotación (Multi-Tag)");

            Vector3 vecFis = new Vector3(fis1.x - fis0.x, 0f, fis1.z - fis0.z);
            Vector3 vecUni = new Vector3(uni1.x - uni0.x, 0f, uni1.z - uni0.z);

            float anguloFis = Mathf.Atan2(vecFis.x, vecFis.z) * Mathf.Rad2Deg;
            float anguloUni = Mathf.Atan2(vecUni.x, vecUni.z) * Mathf.Rad2Deg;
            float yawCorreccion = Mathf.DeltaAngle(anguloFis, anguloUni);

            float yawArena = -yawCorreccion;
            Quaternion targetRot = Quaternion.Euler(0f, yawArena, 0f);

            // Mantener altura Y original para evitar saltos en el piso
            Vector3 targetPos = fis0 - targetRot * uni0;
            targetPos.y = arenaRoot.position.y;

            Vector3 startPos = arenaRoot.position;
            Quaternion startRot = arenaRoot.rotation;

            float elapsed = 0f;
            while (elapsed < tiempoInterpolacion)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / tiempoInterpolacion;
                t = t * t * (3f - 2f * t); // SmoothStep

                arenaRoot.position = Vector3.Lerp(startPos, targetPos, t);
                arenaRoot.rotation = Quaternion.Slerp(startRot, targetRot, t);
                yield return null;
            }

            arenaRoot.position = targetPos;
            arenaRoot.rotation = targetRot;
            _isCorrecting = false;
        }
    }
}
