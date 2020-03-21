using System;
using System.Collections.Generic;
using System.Text;

namespace DebNet
{
    internal static class Ext
    {
        public static int LastIndexOfNone(this string search, char[] chars, int startIndex = 0)
        {
            for(var x = startIndex; x < search.Length;x++)
            {
                var anyMatch = false;
                for(var y = 0; y < chars.Length;y++)
                {
                    if(search[x] == chars[y]) { anyMatch = true; break; }
                }

                if(!anyMatch) { return x; }
            }
            return -1;
        }
    }
}
