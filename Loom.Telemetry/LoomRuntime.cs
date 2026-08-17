using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Loom.Telemetry
{
    public static class LoomRuntime
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordMethodExecution(string metrickName, TimeSpan elapsed, Exception? exception)
        {
            // TODO: Storage implementation
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RecordPropertyChange<T>(string metricName, T value)
        {
            // TODO: Storage implementation
        }
    }
}
