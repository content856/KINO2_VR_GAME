using UnityEngine;
using UnityEngine.XR;

namespace KinoVR
{
    [DefaultExecutionOrder(-10000)]
    public sealed class KinoPlayerView : MonoBehaviour
    {
        public GameObject vrRig;
        public Transform head;
        public Camera desktopCamera;
        public Camera environmentCamera;
        public Transform View { get; private set; }
        void Awake()
        {
            bool useVR = !Application.isEditor || XRSettings.isDeviceActive;
            if (environmentCamera) environmentCamera.gameObject.SetActive(false);
            if (vrRig) vrRig.SetActive(useVR);
            if (desktopCamera) desktopCamera.gameObject.SetActive(!useVR);
            View = useVR ? head : desktopCamera ? desktopCamera.transform : head;
        }
    }
}
