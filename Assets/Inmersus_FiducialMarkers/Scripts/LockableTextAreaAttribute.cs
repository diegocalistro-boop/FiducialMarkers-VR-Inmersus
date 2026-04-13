using UnityEngine;
using System;

namespace Inmersus.FiducialMarkers
{
    /// <summary>
    /// Crea un área de texto con un botón candado para evitar ediciones por accidente.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class LockableTextAreaAttribute : PropertyAttribute { }
}
