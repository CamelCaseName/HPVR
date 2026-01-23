using MelonLoader;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace HPVR.FSR3
{
    [RegisterTypeInIl2Cpp(true)]
    //public class FsrHDRP : CustomPass
    public class FsrPrePostProcess : FullScreenCustomPass
    {
        //public FsrHDRP(IntPtr value) : base(value) { }

        //public FsrHDRP() : base(ClassInjector.DerivedConstructorPointer<FsrHDRP>()) => ClassInjector.DerivedConstructorBody(this);

        public static event Action? OnExecute;
        int i = 0;

        public static CustomPassContext? Context { get; private set; }

        public override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd) { }

        public override void Execute(CustomPassContext ctx)
        {
            targetDepthBuffer = TargetBuffer.Camera;
            targetColorBuffer = TargetBuffer.Camera;
            Context = ctx;

            //GetCameraBuffers(out var color, out var depth); //color is fine, depth is again empty as it is everywhere else...
            //Extensions.SaveRT(depth, "rg" + (i++) + ".png");

            OnExecute?.Invoke();
        }

        public override void Cleanup() { }
    }
}
