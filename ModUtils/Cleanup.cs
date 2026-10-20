using System;
using System.Collections.Generic;

namespace ADModUtils
{
    public static class Cleanup
    {
        // Releasing one resource must not prevent independent resources being released.
        public static void Run(params Action[] actions)
        {
            var failures = new List<Exception>();
            foreach (var action in actions)
            {
                try { action(); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0)
                throw new AggregateException("One or more cleanup operations failed.", failures);
        }
    }
}
