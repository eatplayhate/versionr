using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Versionr.Utilities
{
    public static class LimitedTaskDispatcher
    {
        public static TaskFactory Factory = new TaskFactory(new LimitedConcurrencyScheduler(16));
    }
    public class LimitedConcurrencyScheduler : TaskScheduler
    {
        [ThreadStatic]
        private static bool m_CurrentThreadIsProcessingItems;

        private readonly LinkedList<Task> m_Tasks = new LinkedList<Task>();
        private readonly int m_MaxDegreeOfParallelism;

        private long m_DelegatesQueuedOrRunning = 0;
        
        public LimitedConcurrencyScheduler(int maxDegreeOfParallelism)
        {
            if (maxDegreeOfParallelism < 1)
                throw new ArgumentOutOfRangeException("maxDegreeOfParallelism");
            m_MaxDegreeOfParallelism = maxDegreeOfParallelism;
        }
  
        protected sealed override void QueueTask(Task task)
        {
            lock (m_Tasks)
            {
                m_Tasks.AddLast(task);
                if (m_DelegatesQueuedOrRunning < m_MaxDegreeOfParallelism)
                {
                    ++m_DelegatesQueuedOrRunning;
                    NotifyThreadPoolOfPendingWork();
                }
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
                        Task item;
                        if (!InternalList.TryDequeue(out item))
                        {
                            lock (m_Tasks)
                            {
                                while (m_Tasks.Count > 0)
                                {
                                    InternalList.Enqueue(m_Tasks.First.Value);
                                    m_Tasks.RemoveFirst();
                                }
                            }
                            if (!InternalList.TryDequeue(out item))
                            {
                                --m_DelegatesQueuedOrRunning;
                                break;
                            }
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
            lock (m_Tasks)
                return m_Tasks.Remove(task);
        }

        public sealed override int MaximumConcurrencyLevel { get { return m_MaxDegreeOfParallelism; } }

        protected sealed override IEnumerable<Task> GetScheduledTasks()
        {
            bool lockTaken = false;
            try
            {
                System.Threading.Monitor.TryEnter(m_Tasks, ref lockTaken);
                if (lockTaken)
                    return m_Tasks;
                else throw new NotSupportedException();
            }
            finally
            {
                if (lockTaken)
                    System.Threading.Monitor.Exit(m_Tasks);
            }
        }
    }
}
