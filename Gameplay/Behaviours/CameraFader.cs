using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using Valve.VR;

namespace HPVR.Gameplay.Behaviours
{
    [RegisterTypeInIl2Cpp()]
    public class CameraFader : MonoBehaviour
    {
        private readonly List<Collider> colliders = new();
        private readonly List<Collision> collisions = new();
        private bool inFade = false;

        public CameraFader(IntPtr value) : base(value) { }

        public CameraFader() : base(ClassInjector.DerivedConstructorPointer<CameraFader>()) => ClassInjector.DerivedConstructorBody(this);

        protected void OnCollisionEnter(Collision other)
        {
            MelonLogger.Msg(other.transform.name + " " + other.transform.gameObject.layer);
            collisions.Add(other);
        }

        protected void OnCollisionExit(Collision other)
        {
            collisions.Remove(other);
        }

        protected void OnUpdate()
        {
            if (colliders.Count > 0 || collisions.Count > 0)
            {
                if (!inFade)
                {
                    inFade = true;
                    SteamVRFade.View(Color.black, 0.2f);
                }
            }
            else if (colliders.Count == 0 && collisions.Count == 0)
            {
                if (inFade)
                {
                    inFade = false;
                    SteamVRFade.View(Color.clear, 0.2f);
                }
            }
        }
    }
}
