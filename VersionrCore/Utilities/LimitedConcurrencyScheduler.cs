using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    public class LimitedConcurrencyScheduler : TaskScheduler
    {
        [ThreadStatic]
        private static bool m_CurrentThreadIsProcessingItems;
        private readonly int m_MaxDegreeOfParallelism;

        private int m_DelegatesQueuedOrRunning = 0;
        
        public LimitedConcurrencyScheduler(int maxDegreeOfParallelism)
        {
            if (maxDegreeOfParallelism < 1)
                throw new ArgumentOutOfRangeException("maxDegreeOfParallelism");
            m_MaxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        protected sealed override void QueueTask(Task task)
        {
            InternalList.Enqueue(task);
            if (m_DelegatesQueuedOrRunning < m_MaxDegreeOfParallelism)
            {
                System.Threading.Interlocked.Increment(ref m_DelegatesQueuedOrRunning);
                NotifyThreadPoolOfPendingWork();
            }
        }

        ConcurrentQueue<Task> InternalList = new ConcurrentQueue<Task>();

        // Inform the ThreadPool that there's work to be executed for this scheduler.  
        private void NotifyThreadPoolOfPendingWork()
        {
            System.Threading.ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                m_CurrentThreadIsProcessingItems = true;
                try
                {
                    while (true)
                    {
                        if (!InternalList.TryDequeue(out Task item))
                        {
                            System.Threading.Interlocked.Decrement(ref m_DelegatesQueuedOrRunning);
                            break;
                        }
                        base.TryExecuteTask(item);
                    }
                }
                finally { m_CurrentThreadIsProcessingItems = false; }
            }, null);
        }
        
        protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (!m_CurrentThreadIsProcessingItems)
                return false;
            
            if (taskWasPreviouslyQueued)
                if (TryDequeue(task))
                    return base.TryExecuteTask(task);
                else
                    return false;
            else
                return base.TryExecuteTask(task);
        }

        protected sealed override bool TryDequeue(Task task)
        {
            return false;
        }

        public sealed override int MaximumConcurrencyLevel { get { return m_MaxDegreeOfParallelism; } }

        protected sealed override IEnumerable<Task> GetScheduledTasks()
        {
            bool lockTaken = false;
            try
            {
                System.Threading.Monitor.TryEnter(InternalList, ref lockTaken);
                if (lockTaken)
                    return InternalList;
                else throw new NotSupportedException();
            }
            finally
            {
                if (lockTaken)
                    System.Threading.Monitor.Exit(InternalList);
            }
        }
    }
}
