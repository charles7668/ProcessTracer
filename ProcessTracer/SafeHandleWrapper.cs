using System.Runtime.InteropServices;

namespace ProcessTracer
{
    public class SafeHandleWrapper(IntPtr invalidHandleValue) : SafeHandle(invalidHandleValue, true)
    {
        public override bool IsInvalid { get; } = false;

        protected override bool ReleaseHandle()
        {
            SetHandle(IntPtr.Zero);
            return true;
        }
    }
}