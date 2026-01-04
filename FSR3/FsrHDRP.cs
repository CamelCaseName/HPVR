using MelonLoader;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace HPVR.FSR3
{
    [RegisterTypeInIl2Cpp(true)]
    //public class FsrHDRP : CustomPass
    public class FsrHDRP : FullScreenCustomPass
    {
        //public FsrHDRP(IntPtr value) : base(value) { }

        //public FsrHDRP() : base(ClassInjector.DerivedConstructorPointer<FsrHDRP>()) => ClassInjector.DerivedConstructorBody(this);

        public static event Action? onRender;

        public static CustomPassContext? context;

        public override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd) { }

        public override void Execute(CustomPassContext ctx)
        {
            context = ctx;
            onRender?.Invoke();
        }

        public override void Cleanup() { }
    }
}
