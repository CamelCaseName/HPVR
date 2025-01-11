using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;

namespace HPVR.Components
{
    [RegisterTypeInIl2Cpp]
    internal class Laser : MonoBehaviour
    {
        public Laser(IntPtr value) : base(value) { }

        public Laser() : base(ClassInjector.DerivedConstructorPointer<Laser>()) => ClassInjector.DerivedConstructorBody(this);
    }
}
