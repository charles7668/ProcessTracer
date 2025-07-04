using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcessTracer
{
    public class Win32
    {
        [Flags]
        public enum CreationFlag
        {
            NORMAL = 0x0,
            CREATE_SUSPENDED = 0x00000004,
            CREATE_DEFAULT_ERROR_MODE = 0x04000000
        }
    }
}