using System;
using System.Collections;
using UnityEngine;
using ZXing;

namespace Inmersus.FiducialMarkers
{
    public class QRDetector : MonoBehaviour
    {
        [Header("Configuración")]
        [Tooltip("Cada cuántos segundos escanea. Menor = más rápido pero más CPU")]
        public float segundosEntreEscaneos = 0.2f;

        [Header("Debug")]
        [Tooltip("Muestra mensajes en consola cuando detecta un QR")]
        public bool mostrarMensajesDebug = true;

        private WebCamTexture _webCamTexture;
        private BarcodeReader _barcodeReader;
        private bool _isScanning = false;

        public event Action<string, Vector2[]> OnQRDetected;

        private void Start()
        {
            InitializeCamera();
            InitializeReader();
        }

        private void InitializeCamera()
        {
            WebCamDevice[] devices = WebCamTexture.devices;

            if (devices.Length == 0)
            {
                Debug.LogError("[QRDetector] No se encontró ninguna cámara.");
                return;
            }

            _webCamTexture = new WebCamTexture(devices[0].name, 1280, 960, 30);
            _webCamTexture.Play();

            if (mostrarMensajesDebug)
                Debug.Log($"[QRDetector] Cámara iniciada: {devices[0].name}");
        }

        private void InitializeReader()
        {
            _barcodeReader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new BarcodeFormat[] { BarcodeFormat.QR_CODE }
                }
            };
        }

        public void StartScanning()
        {
            if (!_isScanning)
            {
                _isScanning = true;
                StartCoroutine(ScanLoop());
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

        private IEnumerator ScanLoop()
        {
            while (_isScanning)
            {
                yield return new WaitForSeconds(segundosEntreEscaneos);

                if (_webCamTexture != null && _webCamTexture.isPlaying)
                    ScanFrame();
            }
        }

        private void ScanFrame()
        {
            try
            {
                Color32[] pixels = _webCamTexture.GetPixels32();
                int width = _webCamTexture.width;
                int height = _webCamTexture.height;

                var result = _barcodeReader.Decode(pixels, width, height);

                if (result != null)
                {
                    if (mostrarMensajesDebug)
                        Debug.Log($"[QRDetector] QR detectado: {result.Text}");

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
                Debug.LogWarning($"[QRDetector] Error al escanear: {e.Message}");
            }
        }

        private void OnDestroy()
        {
            if (_webCamTexture != null)
            {
                _webCamTexture.Stop();
                _webCamTexture = null;
            }
        }
    }
}