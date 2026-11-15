using System.Runtime.InteropServices;

namespace ShibuyaRush
{
    /// <summary>ブラウザの環境(スマホ・タブレットか、PCか)。</summary>
    public static class WebEnv
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int WebEnv_IsTouch();
        [DllImport("__Internal")] static extern void WebEnv_GoPortal();
        public static bool IsTouch() { try { return WebEnv_IsTouch() == 1; } catch { return false; } }
        public static bool HasPortal => true;
        public static void GoPortal() { try { WebEnv_GoPortal(); } catch { } }
#else
        public static bool IsTouch() => false;
        public static bool HasPortal => false;
        public static void GoPortal() { }
#endif
    }
}
