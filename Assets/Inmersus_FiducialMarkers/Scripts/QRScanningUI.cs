using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Inmersus.FiducialMarkers
{
    /// <summary>
    /// Muestra feedback visual en VR durante el escaneo de QR.
    /// Muestra progreso (1/N, 2/N...) y confirma cuando la arena está calibrada.
    /// Se conecta a QRDetectionCoordinator para saber cuántos QR son necesarios.
    /// </summary>
    public class QRScanningUI : MonoBehaviour
    {
        [Header("Referencias")]
        [Tooltip("Referencia al AprilTagDetector de la escena")]
        public AprilTagDetector detectorTag;

        [Tooltip("Referencia al MarkerAnchorManager de la escena")]
        public MarkerAnchorManager anchorManager;

        [Tooltip("Referencia al QRDetectionCoordinator (se busca automáticamente si no se asigna)")]
        public QRDetectionCoordinator coordinador;

        [Header("Configuración visual")]
        [Tooltip("Distancia del panel frente a la cámara (metros)")]
        public float distanciaAlFrente = 2.0f;

        [Tooltip("Mensaje cuando se detectó exitosamente el último QR")]
        public string mensajeArenaCalibrada = "¡Arena calibrada!";

        [Tooltip("Seconds before hiding the success screen")]
        public float tiempoMensajeExito = 3f;

        [Header("Colores")]
        public Color colorFondo = new Color(0.05f, 0.05f, 0.15f, 0.85f);
        public Color colorTexto = Color.white;
        public Color colorExito = new Color(0.2f, 0.9f, 0.4f, 1f);
        public Color colorIcono = new Color(0.4f, 0.7f, 1f, 1f);
        public Color colorParcial = new Color(1f, 0.8f, 0.2f, 1f);

        // ---------------------------------------------------------------
        // Internos
        // ---------------------------------------------------------------
        private Canvas          _canvas;
        private GameObject      _panelRoot;
        private TextMeshProUGUI _textoTitulo;
        private TextMeshProUGUI _textoInstrucciones;
        private TextMeshProUGUI _textoIcono;
        private TextMeshProUGUI _textoContador;
        private Image           _fondoPanel;
        private Coroutine       _animacionPuntos;
        private bool            _calibrado = false;
        private int             _escaneados = 0;

        // ---------------------------------------------------------------
        // Unity lifecycle
        // ---------------------------------------------------------------
        private void Start()
        {
            if (detectorTag == null)
            {
                detectorTag = FindFirstObjectByType<AprilTagDetector>();
                if (detectorTag == null)
                {
                    Debug.LogError("[QRScanningUI] No se encontró AprilTagDetector en la escena.");
                    return;
                }
            }

            if (coordinador == null)
                coordinador = FindFirstObjectByType<QRDetectionCoordinator>();

            if (anchorManager == null)
                anchorManager = FindFirstObjectByType<MarkerAnchorManager>();

            CrearUI();
            OcultarPanel();

            detectorTag.OnScanningStarted += OnEscaneoIniciado;
            detectorTag.OnCameraError    += OnErrorCamara;

            if (coordinador != null)
                coordinador.OnArenaCalibrated += OnArenaCalibrada;

            if (anchorManager != null)
            {
                anchorManager.OnEsperandoConfirmacionUsuario += OnEsperandoConfirmacion;
                anchorManager.OnMarkerAnchorCreated += OnMarkerConfirmado;
            }
        }

        private void LateUpdate()
        {
            if (_panelRoot != null && _panelRoot.activeSelf)
                SeguirCamara();
        }

        private void OnDestroy()
        {
            if (detectorTag != null)
            {
                detectorTag.OnScanningStarted -= OnEscaneoIniciado;
                detectorTag.OnCameraError     -= OnErrorCamara;
            }
            if (coordinador != null)
                coordinador.OnArenaCalibrated -= OnArenaCalibrada;
            if (anchorManager != null)
            {
                anchorManager.OnEsperandoConfirmacionUsuario -= OnEsperandoConfirmacion;
                anchorManager.OnMarkerAnchorCreated -= OnMarkerConfirmado;
            }
        }

        // ---------------------------------------------------------------
        // Callbacks
        // ---------------------------------------------------------------
        private void OnEscaneoIniciado()
        {
            if (_calibrado) return;
            _escaneados = 0;
            MostrarPanel();
            ActualizarPanelEscaneo();

            if (_animacionPuntos != null) StopCoroutine(_animacionPuntos);
            _animacionPuntos = StartCoroutine(AnimarPuntosEscaneo());
        }

        // Se llama cuando MarkerAnchorManager detecta un Tag y lo alinea automáticamente
        private void OnEsperandoConfirmacion(string markerId)
        {
            if (_calibrado) return;
            if (_animacionPuntos != null) StopCoroutine(_animacionPuntos);

            _textoIcono.text = "✨"; 
            _textoIcono.color = new Color(0.2f, 0.8f, 1f, 1f); 
            _textoIcono.fontSize = 54;
            
            _textoTitulo.text = $"¡{markerId} detectado!";
            _textoTitulo.color = _textoIcono.color;
            
            _textoInstrucciones.text = "El tag fue registrado y alineado exitosamente.";
            _fondoPanel.color = new Color(0.05f, 0.1f, 0.15f, 0.9f);
        }

        // Se llama tras presionar el gatillo y crearse el anchor
        private void OnMarkerConfirmado(string markerId, OVRSpatialAnchor anchor)
        {
            if (_calibrado) return;

            _escaneados++;
            int total = coordinador != null ? coordinador.anchorsNecesarios : 2;

            if (_animacionPuntos != null) StopCoroutine(_animacionPuntos);

            if (_escaneados < total)
            {
                // QR parcial — mostrar progreso y seguir escaneando
                ActualizarPanelParcial(_escaneados, total);
                _animacionPuntos = StartCoroutine(AnimarPuntosEscaneo());
            }
        }

        private void OnArenaCalibrada()
        {
            if (_calibrado) return;
            _calibrado = true;

            if (_animacionPuntos != null) StopCoroutine(_animacionPuntos);
            StartCoroutine(MostrarExitoYOcultar());
        }

        private void OnErrorCamara(string error)
        {
            _textoTitulo.text = "Error de cámara";
            _textoTitulo.color = new Color(1f, 0.4f, 0.4f);
            _textoInstrucciones.text = error;
            _textoIcono.text = "⚠";
            _textoIcono.color = new Color(1f, 0.4f, 0.4f);
            _textoContador.text = "";
            MostrarPanel();
        }

        // ---------------------------------------------------------------
        // Actualización de contenido
        // ---------------------------------------------------------------
        private void ActualizarPanelEscaneo()
        {
            int total = coordinador != null ? coordinador.anchorsNecesarios : 2;
            _textoIcono.text = "⬜";
            _textoIcono.color = colorIcono;
            _textoIcono.fontSize = 48;
            _textoTitulo.text = "Busca y escanea los AprilTags";
            _textoTitulo.color = colorTexto;
            _textoInstrucciones.text = "Apuntá el visor hacia los marcadores del suelo";
            _textoContador.text = $"0 / {total} escaneados";
            _textoContador.color = colorTexto;
            _fondoPanel.color = colorFondo;
        }

        private void ActualizarPanelParcial(int escaneados, int total)
        {
            _textoIcono.text = "✓";
            _textoIcono.color = colorParcial;
            _textoIcono.fontSize = 48;
            _textoTitulo.text = $"Tag {escaneados} encontrado — ¡busca el siguiente!";
            _textoTitulo.color = colorParcial;
            _textoInstrucciones.text = "Apuntá hacia el otro marcador AprilTag";
            _textoContador.text = $"{escaneados} / {total} escaneados";
            _textoContador.color = colorParcial;
            _fondoPanel.color = new Color(0.08f, 0.12f, 0.05f, 0.85f);
        }

        // ---------------------------------------------------------------
        // Animaciones
        // ---------------------------------------------------------------
        private IEnumerator AnimarPuntosEscaneo()
        {
            string[] frames = { "◻  ◻  ◻", "◼  ◻  ◻", "◻  ◼  ◻", "◻  ◻  ◼" };
            int index = 0;

            while (!_calibrado)
            {
                // Solo animar si todavía estamos esperando (no parcial)
                if (_escaneados == 0)
                    _textoIcono.text = frames[index % frames.Length];

                index++;
                yield return new WaitForSeconds(0.4f);
            }
        }

        private IEnumerator MostrarExitoYOcultar()
        {
            int total = coordinador != null ? coordinador.anchorsNecesarios : 2;

            _textoIcono.text = "✓";
            _textoIcono.color = colorExito;
            _textoIcono.fontSize = 64;
            _textoTitulo.text = mensajeArenaCalibrada;
            _textoTitulo.color = colorExito;
            _textoInstrucciones.text = "El espacio virtual está alineado al espacio físico";
            _textoContador.text = $"{total} / {total} escaneados";
            _textoContador.color = colorExito;
            _fondoPanel.color = new Color(0.05f, 0.15f, 0.08f, 0.85f);

            yield return new WaitForSeconds(tiempoMensajeExito);

            // Fade out
            float fadeTime = 0.5f;
            float elapsed = 0f;
            CanvasGroup cg = _panelRoot.GetComponent<CanvasGroup>();
            if (cg == null) cg = _panelRoot.AddComponent<CanvasGroup>();

            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                cg.alpha = 1f - (elapsed / fadeTime);
                yield return null;
            }

            OcultarPanel();
            cg.alpha = 1f;
            _textoIcono.fontSize = 48;
        }

        // ---------------------------------------------------------------
        // Crear UI programáticamente (World Space Canvas para VR)
        // ---------------------------------------------------------------
        private void CrearUI()
        {
            // --- Canvas World Space ---
            GameObject canvasGO = new GameObject("QRScanningUI_Canvas");
            canvasGO.transform.SetParent(transform);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;
            canvasGO.AddComponent<GraphicRaycaster>();

            RectTransform canvasRT = _canvas.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(650, 340);
            canvasRT.localScale = Vector3.one * 0.002f;

            // --- Panel de fondo ---
            _panelRoot = new GameObject("Panel");
            _panelRoot.transform.SetParent(canvasGO.transform, false);

            RectTransform panelRT = _panelRoot.AddComponent<RectTransform>();
            panelRT.anchorMin = Vector2.zero;
            panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            _fondoPanel = _panelRoot.AddComponent<Image>();
            _fondoPanel.color = colorFondo;
            _fondoPanel.type = Image.Type.Sliced;

            VerticalLayoutGroup layout = _panelRoot.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 8f;
            layout.padding = new RectOffset(40, 40, 25, 25);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // --- Ícono animado ---
            _textoIcono = CrearTexto("Icono", _panelRoot, 48, colorIcono, 60);
            _textoIcono.text = "⬜";

            // --- Texto título ---
            _textoTitulo = CrearTexto("Titulo", _panelRoot, 30, colorTexto, 44);
            _textoTitulo.fontStyle = FontStyles.Bold;
            _textoTitulo.text = "Busca y escanea los AprilTags";

            // --- Texto instrucciones ---
            _textoInstrucciones = CrearTexto("Instrucciones", _panelRoot, 22,
                new Color(colorTexto.r, colorTexto.g, colorTexto.b, 0.7f), 30);
            _textoInstrucciones.text = "Apuntá el visor hacia los marcadores";

            // --- Contador de progreso (NUEVO) ---
            _textoContador = CrearTexto("Contador", _panelRoot, 26, colorIcono, 36);
            _textoContador.fontStyle = FontStyles.Bold;
            _textoContador.text = "0 / 2 escaneados";
        }

        private TextMeshProUGUI CrearTexto(string nombre, GameObject padre, float fontSize, Color color, float alturaPreferida)
        {
            GameObject go = new GameObject(nombre);
            go.transform.SetParent(padre.transform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = alturaPreferida;
            return tmp;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------
        private void MostrarPanel()
        {
            if (_panelRoot != null)
                _panelRoot.SetActive(true);
        }

        private void OcultarPanel()
        {
            if (_panelRoot != null)
                _panelRoot.SetActive(false);
        }

        private void SeguirCamara()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 posicion = cam.transform.position
                             + cam.transform.forward * distanciaAlFrente
                             + cam.transform.up * (-0.3f);

            _canvas.transform.position = Vector3.Lerp(
                _canvas.transform.position, posicion, Time.deltaTime * 5f);

            _canvas.transform.rotation = Quaternion.Lerp(
                _canvas.transform.rotation,
                Quaternion.LookRotation(_canvas.transform.position - cam.transform.position),
                Time.deltaTime * 5f);
        }
    }
}
