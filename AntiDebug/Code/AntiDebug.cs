using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AntiDebug.Code
{
    class AntiDebug
    {
        [DllImport("kernel32.dll")]
        static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);

        static Thread thread;

        public static void Start()
        {
            thread = new Thread(Worker);
            thread.IsBackground = true;
            thread.Start(null);
        }

        static void Worker(object arg)
        {
            var th = arg as Thread;

            if (Environment.GetEnvironmentVariable("COR_PROFILER") != null ||
                Environment.GetEnvironmentVariable("COR_ENABLE_PROFILING") != null)
            {
                Environment.FailFast(null);
            }

            while (true)
            {
                bool isDebuggerPresent = false;
                CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref isDebuggerPresent);

                if (isDebuggerPresent || Debugger.IsAttached || Debugger.IsLogging())
                {
                    Environment.FailFast(null);
                }
                
                if (th == null)
                {
                    th = new Thread(Worker);
                    th.IsBackground = true;
                    th.Start(Thread.CurrentThread);
                }

                if (th.Join(1000))
                    th = null;
            }
        }
    }
}
