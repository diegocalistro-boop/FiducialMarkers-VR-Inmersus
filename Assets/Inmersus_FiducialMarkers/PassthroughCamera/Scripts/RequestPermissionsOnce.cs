// Copyright (c) Meta Platforms, Inc. and affiliates.

using UnityEngine;
using UnityEngine.SceneManagement;

namespace PassthroughCameraSamples
{
    internal static class RequestPermissionsOnce
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad()
        {
            // Pedir permisos inmediatamente al cargar (para la escena inicial)
            OVRPermissionsRequester.Request(new[]
            {
                OVRPermissionsRequester.Permission.Scene,
                OVRPermissionsRequester.Permission.PassthroughCameraAccess
            });

            // También pedir en futuras escenas como respaldo
            bool permissionsRequestedOnce = false;
            SceneManager.sceneLoaded += (scene, _) =>
            {
                if (!permissionsRequestedOnce)
                {
                    permissionsRequestedOnce = true;
                    OVRPermissionsRequester.Request(new[]
                    {
                        OVRPermissionsRequester.Permission.Scene,
                        OVRPermissionsRequester.Permission.PassthroughCameraAccess
                    });
                }
            };
        }
    }
}
