using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

namespace Inmersus.FiducialMarkers
{
    /// <summary>
    /// ALINEACIÓN DE 2 PUNTOS:
    /// 
    /// 1. Escanea QR_01 → guarda su posición FÍSICA (anchor en el piso)
    /// 2. Escanea QR_02 → guarda su posición FÍSICA (anchor en el piso)
    /// 3. Con las 2 posiciones FÍSICAS + las 2 posiciones del JSON (que son las
    ///    coordenadas en UNITY), calcula la transformación rígida (traslación + rotación yaw)
    ///    que convierte el espacio Unity al espacio físico.
    /// 4. Mueve el OVRCameraRig para que ambos mundos coincidan.
    /// 5. Todos los jugadores que escaneen los mismos QR ven lo mismo.
    ///
    /// Los QR del piso definen un sistema de coordenadas XZ:
    ///   - MarkerPosition.x → Unity X
    ///   - MarkerPosition.y → Unity Z (porque el piso es XZ)
    /// </summary>
    public class MarkerAnchorManager : MonoBehaviour
    {
        [Header("Referencias")]
        public QRDetector detectorQR;

        [Tooltip("Prefab que contiene OVRSpatialAnchor")]
        public GameObject anchorPrefab;

        [Header("Debug")]
        public bool mostrarMensajesDebug = true;

        // ---------------------------------------------------------------
        // Eventos
        // ---------------------------------------------------------------
        public event System.Action<string, OVRSpatialAnchor> OnMarkerAnchorCreated;
        
        /// <summary>
        /// Se dispara cuando el sistema detectó un QR y está esperando
        /// que el usuario presione el gatillo para confirmar su posición.
        /// </summary>
        public event System.Action<string> OnEsperandoConfirmacionUsuario;

        // ---------------------------------------------------------------
        // Estado
        // ---------------------------------------------------------------
        private string _qrPendienteDeConfirmacion;
        private readonly Dictionary<string, bool> _confirmados = new();

        // Posiciones FÍSICAS confirmadas (en espacio del headset/mundo Meta)
        private readonly Dictionary<string, Vector3> _posicionesFisicas = new();

        // Anchors creados
        private readonly Dictionary<string, OVRSpatialAnchor> _anchorsPorMarcador = new();

        private bool   _alineacionHecha = false;
        private int    _anchorsPendientes = 0;

        // ---------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------
        private void Start()
        {
            if (detectorQR == null)
            {
                Debug.LogError("[MarkerAnchorManager] Falta asignar el QRDetector.");
                return;
            }
            if (anchorPrefab == null)
            {
                Debug.LogError("[MarkerAnchorManager] Falta asignar el anchorPrefab.");
                return;
            }

            detectorQR.OnQRDetected += OnQRDetected;

            if (mostrarMensajesDebug)
                Debug.Log("[MarkerAnchorManager] Sistema de alineación de 2 puntos iniciado. Escaneá los QR del piso.");
        }

        private void OnDestroy()
        {
            if (detectorQR != null)
                detectorQR.OnQRDetected -= OnQRDetected;
        }

        // ---------------------------------------------------------------
        // Callback del detector — ZXing lee el QR identificador
        // ---------------------------------------------------------------
        private void OnQRDetected(string qrContent, Vector2[] corners)
        {
            // Si ya estamos esperando que el usuario confirme un QR, ignoramos nuevas lecturas
            if (!string.IsNullOrEmpty(_qrPendienteDeConfirmacion))
                return;

            // Verificar si este QR ya fue confirmado exitosamente
            if (_confirmados.ContainsKey(qrContent))
                return;

            // Verificar que esté en la configuración
            MarkerConfig config = ArenaConfig.Instance?.GetMarkerById(qrContent);
            if (config == null)
            {
                if (mostrarMensajesDebug)
                    Debug.LogWarning($"[MarkerAnchorManager] QR '{qrContent}' no está en arena_config.json. Ignorado.");
                return;
            }

            // Pasamos al estado de esperar confirmación manual
            _qrPendienteDeConfirmacion = qrContent;

            if (mostrarMensajesDebug)
                Debug.Log($"[MarkerAnchorManager] QR detectado '{qrContent}'. Esperando confirmación con el gatillo...");

            // Notificamos a la UI
            OnEsperandoConfirmacionUsuario?.Invoke(qrContent);
        }

        // ---------------------------------------------------------------
        // Input del usuario — Confirmación de posición
        // ---------------------------------------------------------------
        private void Update()
        {
            if (string.IsNullOrEmpty(_qrPendienteDeConfirmacion)) 
                return;

            bool triggerPressed = false;

            // Soporte Input System o viejo Input (Simulación para el Editor)
            #if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.Space))
                triggerPressed = true;
            #endif

            // Gatillo del visor (Quest)
            if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger) || 
                OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger))
            {
                triggerPressed = true;
            }

            if (triggerPressed)
            {
                ConfirmarPosicionQR(_qrPendienteDeConfirmacion);
            }
        }

        private void ConfirmarPosicionQR(string qrContent)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // Capturamos la posición del visor con Y=0
            Vector3 posFinal = new Vector3(cam.transform.position.x, 0f, cam.transform.position.z);
            
            // Limpiamos el estado transitorio
            _qrPendienteDeConfirmacion = null;

            // Marcamos como confirmado y guardamos
            _confirmados[qrContent] = true;
            _posicionesFisicas[qrContent] = posFinal;

            if (mostrarMensajesDebug)
                Debug.Log($"[MarkerAnchorManager] *** '{qrContent}' CONFIRMADO MANUALMENTE *** Pos física: {posFinal:F3}");

            // Crear el anchor en esa posición
            _ = CrearAnchor(qrContent, posFinal);

            // Si ya tenemos 2+ posiciones, intentar alinear
            TryAlinear();
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
            // Ángulo del vector Unity al vector Físico (en grados, alrededor de Y)
            float anguloFis = Mathf.Atan2(vecFis.x, vecFis.z) * Mathf.Rad2Deg;
            float anguloUni = Mathf.Atan2(vecUni.x, vecUni.z) * Mathf.Rad2Deg;
            float yawCorreccion = anguloFis - anguloUni;

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

            // --- Aplicar transformación al OVRCameraRig ---
            AplicarAlineacion(fisP0, uniP0, yawCorreccion, escala);

            // --- Publicar alineación por Photon ---
            if (PhotonNetwork.InRoom && _anchorsPorMarcador.ContainsKey(m0.id) && _anchorsPorMarcador[m0.id] != null)
            {
                var anchor = _anchorsPorMarcador[m0.id];
                var anchorPose = new Pose(anchor.transform.position, anchor.transform.rotation);
                PhotonAnchorManager.PublishAlignmentAnchor(anchor.Uuid, anchorPose);
            }

            if (mostrarMensajesDebug)
                Debug.Log("[MarkerAnchorManager] ¡Arena alineada! El escenario virtual debería coincidir con los QR físicos.");
        }

        /// <summary>
        /// Mueve el OVRCameraRig (tracking space) para que:
        ///  - El punto Unity uniP0 quede exactamente en la posición física fisP0
        ///  - La orientación esté rotada por yawCorreccion grados en Y
        /// </summary>
        private void AplicarAlineacion(Vector3 fisP0, Vector3 uniP0, float yawGrados, float escala)
        {
            // Buscar el tracking space (OVRCameraRig)
            OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig == null)
            {
                Debug.LogError("[MarkerAnchorManager] No se encontró OVRCameraRig para alinear.");
                return;
            }

            Transform trackingSpace = cameraRig.trackingSpace;
            if (trackingSpace == null)
            {
                Debug.LogError("[MarkerAnchorManager] OVRCameraRig no tiene trackingSpace.");
                return;
            }

            // 1. Crear la rotación de corrección (solo yaw)
            Quaternion rotCorreccion = Quaternion.Euler(0f, yawGrados, 0f);

            // 2. El punto Unity uniP0 en el mundo (después de escalar y rotar) debe quedar en fisP0
            //    Fórmula: fisP0 = trackingSpace.position + rotCorreccion * (uniP0 * escala)
            //    →  trackingSpace.position = fisP0 - rotCorreccion * (uniP0 * escala)
            //
            //    Pero como OVRCameraRig ya está en el espacio de tracking, necesitamos
            //    transformar el tracking space para que el mapeo sea correcto.

            // Posición original del tracking space
            Vector3 origPos = trackingSpace.position;
            Quaternion origRot = trackingSpace.rotation;

            // Donde caería uniP0 en el espacio mundo actual del rig
            Vector3 uniP0EnMundo = trackingSpace.TransformPoint(uniP0);

            // Aplicar rotación primero (rotar tracking space alrededor de fisP0)
            trackingSpace.rotation = rotCorreccion * origRot;

            // Recalcular dónde queda uniP0 después de rotar
            uniP0EnMundo = trackingSpace.TransformPoint(uniP0);

            // Traslación: mover tracking space para que uniP0EnMundo = fisP0
            Vector3 offset = fisP0 - uniP0EnMundo;
            trackingSpace.position += offset;

            if (mostrarMensajesDebug)
            {
                // Verificación: dónde queda uniP0 ahora
                Vector3 verificacion = trackingSpace.TransformPoint(uniP0);
                Debug.Log($"[MarkerAnchorManager] TrackingSpace movido. Verificación — uniP0 debería estar en {fisP0:F3}, está en {verificacion:F3}");
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
            _qrPendienteDeConfirmacion = null;
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