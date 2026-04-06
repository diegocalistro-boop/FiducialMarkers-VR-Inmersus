using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

namespace Inmersus.FiducialMarkers
{
    /// <summary>
    /// ALINEACIÓN DE 2 PUNTOS (ArenaRoot):
    /// 
    /// 1. Escanea QR_01 → guarda su posición FÍSICA (anchor en el piso)
    /// 2. Escanea QR_02 → guarda su posición FÍSICA (anchor en el piso)
    /// 3. Con las 2 posiciones FÍSICAS + las 2 posiciones del JSON (que son las
    ///    coordenadas en UNITY), calcula la transformación rígida (traslación + rotación yaw)
    ///    que convierte el espacio Unity al espacio físico.
    /// 4. Mueve el ArenaRoot (contenedor de objetos del escenario) para alinear
    ///    el contenido virtual con el espacio físico. NO se toca el OVRCameraRig,
    ///    lo que preserva las físicas, el grab y la sincronización de red.
    /// 5. Todos los jugadores que escaneen los mismos QR ven lo mismo.
    ///
    /// Los QR del piso definen un sistema de coordenadas XZ:
    ///   - MarkerPosition.x → Unity X
    ///   - MarkerPosition.y → Unity Z (porque el piso es XZ)
    /// </summary>
    public class MarkerAnchorManager : MonoBehaviour
    {
        [Header("Referencias")]
        public AprilTagDetector detectorTag;

        [Tooltip("Prefab que contiene OVRSpatialAnchor")]
        public GameObject anchorPrefab;

        [Header("Arena")]
        [Tooltip("GameObject raíz que contiene todos los objetos del escenario (Cube, Cylinder, luces, etc.). Se mueve/rota para alinear el contenido virtual con el espacio físico.")]
        public Transform arenaRoot;

        [Header("Debug")]
        public bool mostrarMensajesDebug = true;

        // ---------------------------------------------------------------
        // Eventos
        // ---------------------------------------------------------------
        public event System.Action<string, OVRSpatialAnchor> OnMarkerAnchorCreated;
        
        /// <summary>
        /// Se dispara para actualizar la interfaz interactiva.
        /// Args: Titulo, Instrucciones, TipoDePaso (0=buscando, 1=apuntando, 2=confirmando, 3=exito)
        /// </summary>
        public event System.Action<string, string, int> OnInstruccionInteractiva;

        // ---------------------------------------------------------------
        // Estado
        // ---------------------------------------------------------------
        private readonly Dictionary<string, bool> _confirmados = new();
        private readonly Dictionary<string, Vector3> _posicionesFisicas = new();
        private readonly Dictionary<string, OVRSpatialAnchor> _anchorsPorMarcador = new();

        public bool AlineacionHecha => _alineacionHecha;
        private bool _alineacionHecha = false;

        // Estado Máquina Manual (Point & Shoot)
        private enum EstadoPosicionamiento { Inactivo, Apuntando, ListoParaConfirmar }
        private EstadoPosicionamiento _estadoActual = EstadoPosicionamiento.Inactivo;
        private string _tagActivo = "";
        private GameObject _anchorGhost;
        private LineRenderer _laserRenderer;
        private OVRCameraRig _cameraRigCache;
        private Vector3 _ultimoRayoHit;

        // ---------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------
        private void Start()
        {
            if (detectorTag == null)
                Debug.LogError("[MarkerAnchorManager] Falta asignar el AprilTagDetector.");
            if (anchorPrefab == null)
                Debug.LogError("[MarkerAnchorManager] Falta asignar el anchorPrefab.");
            if (arenaRoot == null)
                Debug.LogError("[MarkerAnchorManager] Falta asignar el ArenaRoot. Los objetos del escenario no se alinearán.");

            detectorTag.OnTagDetected += OnTagDetected;

            _cameraRigCache = FindFirstObjectByType<OVRCameraRig>();

            // Crear el láser dinámico
            GameObject laserR = new GameObject("LaserAnchorPlacement");
            laserR.transform.SetParent(transform);
            _laserRenderer = laserR.AddComponent<LineRenderer>();
            _laserRenderer.startWidth = 0.005f;
            _laserRenderer.endWidth = 0.005f;
            _laserRenderer.material = new Material(Shader.Find("Sprites/Default"));
            _laserRenderer.startColor = Color.red;
            _laserRenderer.endColor = Color.red;
            _laserRenderer.enabled = false;

            if (mostrarMensajesDebug)
                Debug.Log("[MarkerAnchorManager] Sistema Point&Shoot iniciado. Escaneá los QR del piso.");
        }

        private void OnDestroy()
        {
            if (detectorTag != null)
                detectorTag.OnTagDetected -= OnTagDetected;
        }

        private void Update()
        {
            if (_estadoActual == EstadoPosicionamiento.Inactivo || _cameraRigCache == null)
                return;

            Transform controlDerecho = _cameraRigCache.rightControllerAnchor;
            if (controlDerecho == null) return;

            // Calcular intersección del láser con Y=0 del tracking space
            Vector3 origin = controlDerecho.position;
            Vector3 direction = controlDerecho.forward;
            
            // Queremos y=0 en tracking space. Para simplicidad, si Guardian está activo, origin.y es relativo al piso.
            // t = (targetY - origin.y) / direction.y
            float t = (0f - origin.y) / direction.y;
            
            if (t > 0f && t < 10f) 
            {
                _ultimoRayoHit = origin + direction * t;
                _laserRenderer.enabled = true;
                _laserRenderer.SetPosition(0, origin);
                _laserRenderer.SetPosition(1, _ultimoRayoHit);
            }
            else
            {
                _laserRenderer.enabled = false;
            }

            // GATILLO: Colocar / Mover Ancla
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
            {
                if (_anchorGhost != null)
                {
                    Destroy(_anchorGhost);
                }
                
                _anchorGhost = Instantiate(anchorPrefab, _ultimoRayoHit, Quaternion.identity);
                
                _estadoActual = EstadoPosicionamiento.ListoParaConfirmar;
                OnInstruccionInteractiva?.Invoke("Confirma la posición", "¿Quedó bien alineado? Pulsa el botón 'A' para confirmar\no vuelve a apuntar y presiona Gatillo para moverlo.", 2);
            }


            // BOTÓN A: Confirmar y Guardar
            if (_estadoActual == EstadoPosicionamiento.ListoParaConfirmar && OVRInput.GetDown(OVRInput.Button.One))
            {
                ConfirmarPosicionManual();
            }
        }

        private async void ConfirmarPosicionManual()
        {
            _estadoActual = EstadoPosicionamiento.Inactivo;
            _laserRenderer.enabled = false;

            Vector3 posFinal = _anchorGhost.transform.position;
            string idConfirmado = _tagActivo;

            _posicionesFisicas[idConfirmado] = posFinal;
            _confirmados[idConfirmado] = true;

            Destroy(_anchorGhost);
            _anchorGhost = null;
            _tagActivo = "";

            OnInstruccionInteractiva?.Invoke($"¡Tag {idConfirmado} anclado!", "Procesando posición...", 3);

            await CrearAnchor(idConfirmado, posFinal);
            TryAlinear();
        }

        // ---------------------------------------------------------------
        // ---------------------------------------------------------------
        // Callback del detector — AprilTag lee el ID
        // ---------------------------------------------------------------
        private void OnTagDetected(int tagID, Vector3 localPos, Quaternion localRot)
        {
            // Solo actura si estamos libres
            if (_estadoActual != EstadoPosicionamiento.Inactivo)
                return;

            string qrContent = tagID.ToString();

            // Verificar si este Tag ya fue confirmado exitosamente
            if (_confirmados.ContainsKey(qrContent))
                return;

            // Verificar que esté en la configuración
            MarkerConfig config = ArenaConfig.Instance?.GetMarkerById(qrContent);
            if (config == null)
                return; // Ignorar silencio

            // Iniciar modo Point & Shoot
            _tagActivo = qrContent;
            _estadoActual = EstadoPosicionamiento.Apuntando;

            if (mostrarMensajesDebug)
                Debug.Log($"[MarkerAnchorManager] Tag {qrContent} visualizado. Activando Láser.");

            OnInstruccionInteractiva?.Invoke($"¡Tag {qrContent} detectado!", "Apunta con tu láser (control derecho) al centro de la hoja impresa y presiona el GATILLO.", 1);
        }



        // ---------------------------------------------------------------
        // ALINEACIÓN DE 2 PUNTOS — el corazón del sistema
        // ---------------------------------------------------------------
        /// <summary>
        /// Con dos posiciones FÍSICAS (del headset) y dos posiciones del JSON (Unity),
        /// calcula la transformación rígida (traslación + rotación yaw) que alinea
        /// el espacio Unity con el espacio físico.
        ///
        /// Ejemplo con el JSON actual:
        ///   QR_01: Unity (0, 0)   → Físico P1
        ///   QR_02: Unity (1, 1)   → Físico P2
        ///
        /// El vector Unity QR_01→QR_02 es (1, 0, 1) en XZ
        /// El vector Físico P1→P2 define la orientación real
        /// La diferencia angular en Y entre ambos vectores = yaw de corrección
        /// La posición de P1 = dónde queda el origen de Unity en el mundo real
        /// </summary>
        private void TryAlinear()
        {
            if (_alineacionHecha) return;

            // Necesitamos al menos 2 marcadores confirmados
            var config = ArenaConfig.Instance?.Config;
            if (config == null || config.markers.Count < 2) return;

            var m0 = config.markers[0];  // QR_01 (origen)
            var m1 = config.markers[1];  // QR_02

            if (!_posicionesFisicas.ContainsKey(m0.id) || !_posicionesFisicas.ContainsKey(m1.id))
                return;  // aún falta uno

            _alineacionHecha = true;

            Vector3 fisP0 = _posicionesFisicas[m0.id];  // posición física del QR_01
            Vector3 fisP1 = _posicionesFisicas[m1.id];  // posición física del QR_02

            // Posiciones Unity de los markers (MarkerPosition.x → X, MarkerPosition.y → Z)
            Vector3 uniP0 = new Vector3(m0.position.x, 0f, m0.position.y);
            Vector3 uniP1 = new Vector3(m1.position.x, 0f, m1.position.y);

            // --- Vector entre markers en cada espacio (solo XZ) ---
            Vector3 vecFis = new Vector3(fisP1.x - fisP0.x, 0f, fisP1.z - fisP0.z);
            Vector3 vecUni = new Vector3(uniP1.x - uniP0.x, 0f, uniP1.z - uniP0.z);

            // --- Ángulo de rotación (yaw) necesario ---
            // Queremos rotar el mundo físico (vecFis) para que se alinee con el virtual (vecUni).
            // Rotación delta = anguloUni - anguloFis
            float anguloFis = Mathf.Atan2(vecFis.x, vecFis.z) * Mathf.Rad2Deg;
            float anguloUni = Mathf.Atan2(vecUni.x, vecUni.z) * Mathf.Rad2Deg;
            float yawCorreccion = Mathf.DeltaAngle(anguloFis, anguloUni);

            // --- Escala (por si la arena física no es exactamente 1:1) ---
            float distFis = vecFis.magnitude;
            float distUni = vecUni.magnitude;
            float escala  = (distUni > 0.01f) ? distFis / distUni : 1f;

            if (mostrarMensajesDebug)
            {
                Debug.Log($"[MarkerAnchorManager] === ALINEACIÓN DE 2 PUNTOS ===");
                Debug.Log($"  QR_01 físico: {fisP0:F3}  |  Unity: {uniP0:F3}");
                Debug.Log($"  QR_02 físico: {fisP1:F3}  |  Unity: {uniP1:F3}");
                Debug.Log($"  Distancia física: {distFis:F3}m  |  Distancia Unity: {distUni:F3}m");
                Debug.Log($"  Escala: {escala:F3}  |  Yaw: {yawCorreccion:F1}°");
            }

            // --- Aplicar transformación al ArenaRoot ---
            AplicarAlineacion(fisP0, uniP0, yawCorreccion);

            // --- Publicar alineación por Photon ---
            if (PhotonNetwork.InRoom && _anchorsPorMarcador.ContainsKey(m0.id) && _anchorsPorMarcador[m0.id] != null)
            {
                var anchor = _anchorsPorMarcador[m0.id];
                var anchorPose = new Pose(anchor.transform.position, anchor.transform.rotation);
                PhotonAnchorManager.PublishAlignmentAnchor(anchor.Uuid, anchorPose);
            }

            if (mostrarMensajesDebug)
                Debug.Log("[MarkerAnchorManager] ¡Arena alineada! El escenario virtual debería coincidir con los QR físicos.");
                
            // Guardar configuración automáticamente para la próxima vez
            var saveManager = GetComponent<CalibrationSaveManager>();
            if (saveManager != null) saveManager.GuardarCalibracionLocal(arenaRoot);

        }

        public void ForzarCalibracionHecha()
        {
            _alineacionHecha = true;
            if (_laserRenderer != null) _laserRenderer.enabled = false;
            OnInstruccionInteractiva?.Invoke("Calibración Restaurada", "La arena ha sido cargada desde el último guardado automático.", 3);
            
            var coord = FindFirstObjectByType<QRDetectionCoordinator>();
            if (coord != null)
            {
                coord.CompletarCalibracion();
            }
        }

        /// <summary>
        /// Mueve el ArenaRoot (contenedor de objetos del escenario) para que:
        ///  - Los objetos en coordenadas Unity se posicionen en las coordenadas físicas correctas
        ///  - NO se toca el trackingSpace → las manos, controladores, físicas y grab funcionan normal
        ///
        /// Matemática:
        ///  - yawGrados es el ángulo Físico→Unity (calculado en TryAlinear)
        ///  - Para ArenaRoot necesitamos el ángulo Unity→Físico = -yawGrados
        ///  - Posicionamos ArenaRoot de modo que: ArenaRoot.TransformPoint(uniP0) == fisP0
        /// </summary>
        private void AplicarAlineacion(Vector3 fisP0, Vector3 uniP0, float yawGrados)
        {
            if (arenaRoot == null)
            {
                Debug.LogError("[MarkerAnchorManager] No se asignó ArenaRoot. Los objetos no se alinearán.");
                return;
            }

            // Invertir el yaw: antes movíamos el observador (trackingSpace) hacia Unity,
            // ahora movemos el contenido (ArenaRoot) hacia el espacio físico.
            float yawArena = -yawGrados;
            Quaternion rotArena = Quaternion.Euler(0f, yawArena, 0f);

            // 1. Aplicar rotación al ArenaRoot
            arenaRoot.rotation = rotArena;

            // 2. Posicionar ArenaRoot para que su punto local uniP0 quede en fisP0 (world)
            //    ArenaRoot.TransformPoint(uniP0) == fisP0
            //    => arenaRoot.position + rotArena * uniP0 == fisP0
            //    => arenaRoot.position = fisP0 - rotArena * uniP0
            arenaRoot.position = fisP0 - rotArena * uniP0;

            if (mostrarMensajesDebug)
            {
                Debug.Log($"[MarkerAnchorManager] ArenaRoot alineado:");
                Debug.Log($"  Posición: {arenaRoot.position:F3}  |  Rotación Y: {yawArena:F1}°");
                Debug.Log($"  Verificación — Tag1 en world: {arenaRoot.TransformPoint(uniP0):F3} debería ser ≈ {fisP0:F3}");
            }
        }

        // ---------------------------------------------------------------
        // Creación de anchor (sin alineación — la alineación es separada)
        // ---------------------------------------------------------------
        private async Task CrearAnchor(string markerId, Vector3 posicionPiso)
        {
            // Crear anchor en la posición del piso, mirando hacia arriba
            Quaternion rotPiso = Quaternion.identity;
            GameObject anchorGO = Instantiate(anchorPrefab, posicionPiso, rotPiso);
            anchorGO.name = $"Anchor_{markerId}";

            OVRSpatialAnchor spatialAnchor = anchorGO.GetComponent<OVRSpatialAnchor>();
            if (spatialAnchor == null)
                spatialAnchor = anchorGO.AddComponent<OVRSpatialAnchor>();

            bool creado = await spatialAnchor.WhenCreatedAsync();
            if (!creado)
            {
                Debug.LogError($"[MarkerAnchorManager] No se pudo crear anchor para '{markerId}'.");
                _confirmados.Remove(markerId);
                Destroy(anchorGO);
                return;
            }

            bool localizado = await spatialAnchor.WhenLocalizedAsync();
            if (!localizado)
            {
                Debug.LogError($"[MarkerAnchorManager] Anchor '{markerId}' no se localizó.");
                _confirmados.Remove(markerId);
                Destroy(anchorGO);
                return;
            }

            _anchorsPorMarcador[markerId] = spatialAnchor;

            if (mostrarMensajesDebug)
                Debug.Log($"[MarkerAnchorManager] Anchor '{markerId}' creado y localizado | UUID: {spatialAnchor.Uuid}");

            // Guardar anchor
            var saveResult = await spatialAnchor.SaveAnchorAsync();
            if (!saveResult.Success)
                Debug.LogWarning($"[MarkerAnchorManager] No se pudo guardar anchor '{markerId}': {saveResult.Status}");

            // Compartir por Photon
            if (PhotonNetwork.InRoom)
                CompartirConSala(markerId, spatialAnchor);

            OnMarkerAnchorCreated?.Invoke(markerId, spatialAnchor);
        }

        private async void CompartirConSala(string markerId, OVRSpatialAnchor anchor)
        {
            var userIds = PhotonAnchorManager.RoomUserIds;
            if (userIds == null || userIds.Count == 0) return;

            var spaceUsers = new List<OVRSpaceUser>();
            foreach (ulong uid in userIds)
                if (OVRSpaceUser.TryCreate(uid, out var su))
                    spaceUsers.Add(su);

            if (spaceUsers.Count == 0) return;

            var result = await anchor.ShareAsync(spaceUsers);
            if (result.IsSuccess())
            {
                if (mostrarMensajesDebug)
                    Debug.Log($"[MarkerAnchorManager] Anchor '{markerId}' compartido con {spaceUsers.Count} usuario(s).");
                PhotonAnchorManager.PublishAnchorToUsers(anchor.Uuid, userIds);
            }
            else
            {
                Debug.LogError($"[MarkerAnchorManager] Error al compartir anchor '{markerId}': {result}");
            }
        }



        // ---------------------------------------------------------------
        // API pública
        // ---------------------------------------------------------------
        public void ReiniciarMarcadores()
        {
            _tagActivo = "";
            _estadoActual = EstadoPosicionamiento.Inactivo;
            if (_anchorGhost != null) Destroy(_anchorGhost);
            if (_laserRenderer != null) _laserRenderer.enabled = false;

            _anchorsPorMarcador.Clear();
            _confirmados.Clear();
            _posicionesFisicas.Clear();
            _alineacionHecha = false;
            if (mostrarMensajesDebug)
                Debug.Log("[MarkerAnchorManager] Marcadores reiniciados.");
        }

        public int CantidadAnchorsCreados => _anchorsPorMarcador.Count;
    }
}