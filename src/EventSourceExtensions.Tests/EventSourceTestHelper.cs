using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace EventSourceExtensions.Tests
{
    public static class EventSourceTestHelper
    {
        public static IEnumerable<object> AllValues(this TraceEvent traceEvent)
        {
            for (var i = 0; i < traceEvent.PayloadNames.Length; i++)
            {
                yield return traceEvent.PayloadValue(i);
            }
        }

        public static Task ListenAndAssertAsync(this EventSource eventSource, Action action, Action<TraceEvent> assertCallback, int timeoutMs = 5000)
        {
            return eventSource.ListenAndAssertAsync(action, e => { assertCallback(e); return true; }, timeoutMs);
        }

        public static async Task ListenAndAssertAsync(this EventSource eventSource, Action action, Func<TraceEvent, bool> assertCallback, int timeoutMs = 500)
        {
            Task<bool> processTask;
            var tcs = new TaskCompletionSource<TraceEvent>();
            using (var session = new TraceEventSession("TestSession") { StopOnDispose = true })
            {
                session.Source.Dynamic.All += delegate (TraceEvent d)
                {
                    if (d.TaskName == "ManifestData") return;
                    try
                    {
                        if (assertCallback(d)) tcs.SetResult(d);
                    }
                    catch (Exception e)
                    {
                        tcs.SetException(e);
                    }
                };

                session.EnableProvider(eventSource.Name);

                // ReSharper disable once AccessToDisposedClosure
                processTask = Task.Run(() => session.Source.Process());

                await Task.Delay(1000).ConfigureAwait(false);

                action();

                var delayTask = Task.Delay(timeoutMs);
                var task = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);
                await task.ConfigureAwait(false);
                if (task == delayTask)
                {
                    throw new TimeoutException();
                }
            }
            await processTask.ConfigureAwait(false);
        }
    }
}