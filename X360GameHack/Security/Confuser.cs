using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace X360GameHack
{
    public static class Confuser
    {
        [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
        public class ObfuscateAttribute : Attribute
        {
            public ObfuscateAttribute()
            {
            }
        }
    }
}
