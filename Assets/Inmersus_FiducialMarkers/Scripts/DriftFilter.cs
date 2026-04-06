using System.Collections.Generic;
using UnityEngine;

namespace Inmersus.FiducialMarkers
{
    /// <summary>
    /// Filtro pasa bajas / Promediador de datos espaciales.
    /// Recolecta lecturas ópticas, descarta ruido y genera un punto estable.
    /// </summary>
    public class DriftFilter
    {
        private readonly int _requeridos;
        private readonly List<Vector3> _posiciones;
        private readonly List<Quaternion> _rotaciones;

        public DriftFilter(int lecturasRequeridas = 30)
        {
            _requeridos = lecturasRequeridas;
            _posiciones = new List<Vector3>(_requeridos);
            _rotaciones = new List<Quaternion>(_requeridos);
        }

        public void AgregarLectura(Vector3 pos, Quaternion rot)
        {
            if (_posiciones.Count >= _requeridos)
            {
                _posiciones.RemoveAt(0);
                _rotaciones.RemoveAt(0);
            }

            _posiciones.Add(pos);
            _rotaciones.Add(rot);
        }

        public bool TieneDatosCompletos => _posiciones.Count >= _requeridos;

        public Vector3 PromedioPosicion()
        {
            if (_posiciones.Count == 0) return Vector3.zero;
            Vector3 suma = Vector3.zero;
            foreach (var p in _posiciones) suma += p;
            return suma / _posiciones.Count;
        }

        /// <summary>
        /// Calcula la varianza para ver si las lecturas están saltando mucho.
        /// Si la varianza es muy alta, hubo mala lectura de cámara (jitter óptimo severo).
        /// </summary>
        public float CalcularVarianza()
        {
            if (_posiciones.Count < 2) return 0f;
            Vector3 promedio = PromedioPosicion();
            float sumSq = 0f;
            foreach (var p in _posiciones)
            {
                sumSq += (p - promedio).sqrMagnitude;
            }
            return sumSq / (_posiciones.Count - 1);
        }

        public void Limpiar()
        {
            _posiciones.Clear();
            _rotaciones.Clear();
        }
    }
}
