using UnityEngine;

namespace BlocksEscape
{
    // App-wide settings applied once at startup, before the first scene loads.
    static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // Android defaults to 30 fps; vsync off so targetFrameRate also applies on PC.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
        }
    }
}
