using System.Collections;
using UnityEngine;

namespace Inmersus.FiducialMarkers
{
    /// <summary>
    /// Oculta los objetos de escena al inicio y los muestra tras calibración.
    /// 
    /// IMPORTANTE: Los objetos interactivos (con Rigidbody/Grabbable) NO deben ser
    /// hijos de ArenaRoot en la Hierarchy. Este script los posiciona manualmente
    /// usando arenaRoot.TransformPoint() después de la calibración.
    /// 
    /// Esto evita que el GrabFreeTransformer de ISDK entre en conflicto con
    /// un padre rotado, eliminando el efecto péndulo/elástico.
    /// 
    /// USO: 
    ///   - "Objetos Estáticos De La Arena": hijos de ArenaRoot (se mueven con él automáticamente)
    ///   - "Objetos Interactivos": NO hijos de ArenaRoot (este script los posiciona)
    /// </summary>
    public class ShowAfterCalibration : MonoBehaviour
    {
        [Header("DOCUMENTACION DEL SCRIPT")]
        [SerializeField] [LockableTextArea] private string descripcionScript =
            "Oculta los objetos de escena al inicio y los muestra tras la calibración.\n" +
            "Posiciona los objetos interactivos (NO hijos de ArenaRoot) usando TransformPoint() " +
            "para evitar el efecto péndulo/elástico causado por un padre rotado.\n\n" +
            "═══ REGLA DE ORO ═══\n" +
            "• ¿Tiene Rigidbody o Grabbable? → Va en 'Objetos Interactivos' (fuera de ArenaRoot)\n" +
            "• ¿Es decoración/escenario estático? → Va como hijo de ArenaRoot en 'Objetos De La Arena'\n\n" +
            "═══ COMPATIBILIDAD CON META XR SDK ═══\n" +
            "✅ FUNCIONA SIN AJUSTES:\n" +
            "  • Rigidbody + Grabbable + GrabFreeTransformer (cubo libre)\n" +
            "  • Rigidbody + gravedad (objetos que caen al soltarlos)\n" +
            "  • OneGrabRotateTransformer (rotar con una mano)\n" +
            "  • TwoGrabFreeTransformer (escalar con dos manos)\n" +
            "  • Objetos estáticos de decoración (hijos de ArenaRoot)\n\n" +
            "⚠️ CASOS QUE NECESITAN AJUSTES:\n" +
            "  • Joints (HingeJoint, SpringJoint): Ambos extremos deben posicionarse juntos\n" +
            "  • Spawn en runtime (Photon Instantiate): Usar arenaRoot.TransformPoint(pos) al instanciar\n" +
            "  • SnapZones: Si es estática va en ArenaRoot, si tiene física va en 'Objetos Interactivos'";

        [Header("Objetos estáticos (hijos de ArenaRoot, se mueven automáticamente)")]
        [Tooltip("Objetos sin Rigidbody/Grab que están dentro de ArenaRoot")]
        public GameObject[] objetosDeLaArena;

        [Header("Objetos interactivos (NO hijos de ArenaRoot)")]
        [Tooltip("Objetos con Rigidbody/Grabbable que están en la raíz de la escena. " +
                 "Se posicionarán manualmente según su posición de diseño.")]
        public GameObject[] objetosInteractivos;

        [Header("Referencia")]
        [Tooltip("El ArenaRoot para calcular posiciones world-space")]
        public Transform arenaRoot;

        [Header("Estabilización")]
        [Tooltip("Segundos de espera antes de activar los objetos")]
        public float delayEstabilizacion = 0.5f;

        [Header("Debug")]
        public bool mostrarMensajesDebug = true;

        private QRDetectionCoordinator _coordinador;

        // Guardamos las posiciones de diseño (antes de la calibración)
        private Vector3[] _posicionesDiseno;
        private Quaternion[] _rotacionesDiseno;

        private void Awake()
        {
            // Ocultar todo inmediatamente
            OcultarArray(objetosDeLaArena);
            OcultarArray(objetosInteractivos);

            // Guardar las posiciones de diseño de los objetos interactivos
            // (estas son las posiciones en coordenadas Unity que coinciden con el JSON)
            if (objetosInteractivos != null && objetosInteractivos.Length > 0)
            {
                _posicionesDiseno = new Vector3[objetosInteractivos.Length];
                _rotacionesDiseno = new Quaternion[objetosInteractivos.Length];
                for (int i = 0; i < objetosInteractivos.Length; i++)
                {
                    if (objetosInteractivos[i] != null)
                    {
                        _posicionesDiseno[i] = objetosInteractivos[i].transform.position;
                        _rotacionesDiseno[i] = objetosInteractivos[i].transform.rotation;
                    }
                }
            }
        }

        private void Start()
        {
            _coordinador = FindFirstObjectByType<QRDetectionCoordinator>();

            if (_coordinador != null)
            {
                _coordinador.OnArenaCalibrated += OnCalibrado;

                if (mostrarMensajesDebug)
                {
                    int total = (objetosDeLaArena?.Length ?? 0) + (objetosInteractivos?.Length ?? 0);
                    Debug.Log($"[ShowAfterCalibration] {total} objeto(s) ocultos. Se mostrarán al calibrar.");
                }
            }
            else
            {
                Debug.LogWarning("[ShowAfterCalibration] No se encontró QRDetectionCoordinator.");
            }
        }

        private void OnDestroy()
        {
            if (_coordinador != null)
                _coordinador.OnArenaCalibrated -= OnCalibrado;
        }

        private void OnCalibrado()
        {
            StartCoroutine(MostrarObjetosEstabilizado());
        }

        private IEnumerator MostrarObjetosEstabilizado()
        {
            if (mostrarMensajesDebug)
                Debug.Log($"[ShowAfterCalibration] Esperando {delayEstabilizacion}s...");

            yield return new WaitForSeconds(delayEstabilizacion);

            // Fase 1: Activar objetos estáticos (hijos de ArenaRoot, se posicionan solos)
            MostrarArray(objetosDeLaArena);

            // Fase 2: Posicionar y activar objetos interactivos
            if (objetosInteractivos != null && arenaRoot != null)
            {
                for (int i = 0; i < objetosInteractivos.Length; i++)
                {
                    var obj = objetosInteractivos[i];
                    if (obj == null) continue;

                    // Calcular posición world-space usando ArenaRoot como referencia
                    Vector3 worldPos = arenaRoot.TransformPoint(_posicionesDiseno[i]);
                    Quaternion worldRot = arenaRoot.rotation * _rotacionesDiseno[i];

                    // Rigidbody: usar teleport limpio
                    Rigidbody rb = obj.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.isKinematic = true;
                        obj.SetActive(true);
                        rb.position = worldPos;
                        rb.rotation = worldRot;
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                    else
                    {
                        obj.transform.position = worldPos;
                        obj.transform.rotation = worldRot;
                        obj.SetActive(true);
                    }

                    if (mostrarMensajesDebug)
                        Debug.Log($"[ShowAfterCalibration] Interactivo '{obj.name}' posicionado en {worldPos:F3}");
                }
            }

            // Esperar frames de física
            for (int i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();

            if (mostrarMensajesDebug)
            {
                int total = (objetosDeLaArena?.Length ?? 0) + (objetosInteractivos?.Length ?? 0);
                Debug.Log($"[ShowAfterCalibration] ¡{total} objeto(s) activados y estabilizados!");
            }
        }

        private void OcultarArray(GameObject[] arr)
        {
            if (arr == null) return;
            foreach (var obj in arr)
                if (obj != null) obj.SetActive(false);
        }

        private void MostrarArray(GameObject[] arr)
        {
            if (arr == null) return;
            foreach (var obj in arr)
                if (obj != null) obj.SetActive(true);
        }

        public void OcultarObjetos()
        {
            OcultarArray(objetosDeLaArena);
            OcultarArray(objetosInteractivos);
        }
    }
}
