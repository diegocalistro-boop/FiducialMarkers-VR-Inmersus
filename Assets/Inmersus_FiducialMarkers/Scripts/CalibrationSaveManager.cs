using UnityEngine;
using System.Globalization;

namespace Inmersus.FiducialMarkers
{
    public class CalibrationSaveManager : MonoBehaviour
    {
        [SerializeField] [LockableTextArea] private string descripcionScript = "DISCO DURO. Al calibrar el cuarto, guarda la posición en memoria. La próxima vez que abras la app, acomoda el mundo en milisegundos sin obligarte a escanear de nuevo la pared.";

        private const string PREF_HAS_SAVE = "Arena_HasSave";
        private const string PREF_POS_X = "Arena_PosX";
        private const string PREF_POS_Y = "Arena_PosY";
        private const string PREF_POS_Z = "Arena_PosZ";
        private const string PREF_ROT_Y = "Arena_RotY";

        [Header("Referencias")]
        public MarkerAnchorManager anchorManager;
        public Transform arenaRoot;

        [Header("Configuración")]
        [Tooltip("Carga la calibración de inmediato al arrancar el juego.")]
        public bool autoCargarAlInicio = true;

        private void Start()
        {
            if (autoCargarAlInicio && TieneCalibracionGuardada())
            {
                Debug.Log("[CalibrationSaveManager] Calibración previa encontrada. Cargando automáticamente...");
                CargarCalibracion();
            }
        }

        public bool TieneCalibracionGuardada()
        {
            return PlayerPrefs.GetInt(PREF_HAS_SAVE, 0) == 1;
        }

        public void GuardarCalibracionLocal(Transform root)
        {
            PlayerPrefs.SetInt(PREF_HAS_SAVE, 1);
            PlayerPrefs.SetFloat(PREF_POS_X, root.position.x);
            PlayerPrefs.SetFloat(PREF_POS_Y, root.position.y);
            PlayerPrefs.SetFloat(PREF_POS_Z, root.position.z);
            PlayerPrefs.SetFloat(PREF_ROT_Y, root.rotation.eulerAngles.y);
            PlayerPrefs.Save();

            Debug.Log($"[CalibrationSaveManager] Calibración Guardada | Pos: {root.position} | RotY: {root.rotation.eulerAngles.y}");
        }

        public void CargarCalibracion()
        {
            if (!TieneCalibracionGuardada()) return;

            float px = PlayerPrefs.GetFloat(PREF_POS_X);
            float py = PlayerPrefs.GetFloat(PREF_POS_Y);
            float pz = PlayerPrefs.GetFloat(PREF_POS_Z);
            float ry = PlayerPrefs.GetFloat(PREF_ROT_Y);

            arenaRoot.position = new Vector3(px, py, pz);
            arenaRoot.rotation = Quaternion.Euler(0f, ry, 0f);

            Debug.Log($"[CalibrationSaveManager] Calibración Cargada | Pos: {arenaRoot.position} | RotY: {ry}");

            // Le avisamos al Anchor Manager que ya estamos listos para que detenga el UI de escanear.
            if (anchorManager != null)
            {
                anchorManager.ForzarCalibracionHecha();
            }
        }

        public void BorrarCalibracionGuardada()
        {
            PlayerPrefs.DeleteKey(PREF_HAS_SAVE);
            PlayerPrefs.Save();
            Debug.Log("[CalibrationSaveManager] Datos de calibración borrados.");
        }
    }
}
