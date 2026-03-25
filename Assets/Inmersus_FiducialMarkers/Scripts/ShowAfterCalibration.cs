using UnityEngine;

namespace Inmersus.FiducialMarkers
{
    /// <summary>
    /// Oculta los objetos de escena asignados al inicio de la app,
    /// y los muestra solamente cuando la arena está completamente calibrada
    /// (todos los QR escaneados y anchors creados).
    /// 
    /// USO: Agregar este componente a cualquier GameObject de la escena.
    ///      Arrastrar Cylinder, Cube y cualquier otro objeto virtual al array
    ///      "Objetos A Mostrar".
    /// </summary>
    public class ShowAfterCalibration : MonoBehaviour
    {
        [Header("Objetos a ocultar hasta que la arena esté calibrada")]
        [Tooltip("Arrastrá aquí el Cylinder, Cube y cualquier otro objeto virtual del escenario")]
        public GameObject[] objetosDeLaArena;

        [Header("Debug")]
        public bool mostrarMensajesDebug = true;

        private QRDetectionCoordinator _coordinador;

        // ---------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------
        private void Awake()
        {
            // Ocultar inmediatamente, antes de que el usuario vea cualquier frame
            foreach (var obj in objetosDeLaArena)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }

        private void Start()
        {
            _coordinador = FindFirstObjectByType<QRDetectionCoordinator>();

            if (_coordinador != null)
            {
                _coordinador.OnArenaCalibrated += MostrarObjetos;

                if (mostrarMensajesDebug)
                    Debug.Log($"[ShowAfterCalibration] {objetosDeLaArena.Length} objeto(s) ocultos. " +
                              "Se mostrarán al calibrar la arena.");
            }
            else
            {
                Debug.LogWarning("[ShowAfterCalibration] No se encontró QRDetectionCoordinator. " +
                                 "Los objetos permanecerán ocultos indefinidamente.");
            }
        }

        private void OnDestroy()
        {
            if (_coordinador != null)
                _coordinador.OnArenaCalibrated -= MostrarObjetos;
        }

        // ---------------------------------------------------------------
        // Callback
        // ---------------------------------------------------------------
        private void MostrarObjetos()
        {
            foreach (var obj in objetosDeLaArena)
            {
                if (obj != null)
                    obj.SetActive(true);
            }

            if (mostrarMensajesDebug)
                Debug.Log($"[ShowAfterCalibration] ¡Arena calibrada! Mostrando {objetosDeLaArena.Length} objeto(s).");
        }

        // ---------------------------------------------------------------
        // API pública (por si necesitás ocultarlos de nuevo)
        // ---------------------------------------------------------------
        public void OcultarObjetos()
        {
            foreach (var obj in objetosDeLaArena)
                if (obj != null) obj.SetActive(false);
        }
    }
}
