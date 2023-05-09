using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WTFShared.Logging;

namespace WTFShared.Tasks
{
    public static class TaskManager
    {
        private static readonly ConcurrentDictionary<TaskType, List<WTFTask>> _tasks = new ConcurrentDictionary<TaskType, List<WTFTask>>();
        private static readonly Task[] _threadPool = new Task[10];

        private static readonly CancellationTokenSource _updateThreadTokenSource = new CancellationTokenSource();
        private static Task _updateTask;

        public static bool IsStopped => _updateTask == null || _updateTask.Status == TaskStatus.RanToCompletion;

        private static void UpdateThread(CancellationToken token)
        {
            LoggerFactory.Global.Info("===Task manager update thread started===");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    Update(20);
                }
                catch (Exception e)
                {
                    LoggerFactory.Global.Error($"Task manager update loop failed: {e.Info()}");
                }

                Thread.Sleep(200);
            }
        }

        public static void Start()
        {
            _updateTask = Task.Run(() => UpdateThread(_updateThreadTokenSource.Token));
        }

        public static void Stop(bool async = false)
        {
            _updateThreadTokenSource.Cancel();

            if (!async)
                WaitUntilStopped();
        }

        private static void WaitUntilStopped()
        {
            while (!IsStopped)
                Thread.Sleep(50);
        }

        public static WTFTask AddTask(WTFTask task)
        {
            task.Reset();
            if (!_tasks.ContainsKey(task.Type))
                _tasks[task.Type] = new List<WTFTask>();

            LoggerFactory.Global.Info($"TaskManager: Adding task {task.Id} {task.ToString()} of type {task.GetType()}");
            _tasks[task.Type].Add(task);

            return task;
        }

        public static WTFTask AddTask(WTFTask task, DateTime moment)
        {
            task.Schedule(moment);

            return AddTask(task);
        }

        public static WTFTask AddTask(WTFTask task, TimeSpan delay)
        {
            task.Schedule(delay);

            return AddTask(task);
        }

        public static void Update(int maxTasks = 0)
        {
            var taskCounter = 0;

            var removedTasks = new List<WTFTask>();
            foreach (var tasksByType in _tasks)
            {
                if (maxTasks > 0 && taskCounter > maxTasks)
                    break;

                foreach (var task in tasksByType.Value)
                {
                    switch (task.Status)
                    {
                        case WTFTaskStatus.None:
                            if ((maxTasks <= 0 || taskCounter < maxTasks) && task.Moment < DateTime.Now)
                            {
                                ++taskCounter;
                                ExecuteTask(task);
                            }

                            break;
                        case WTFTaskStatus.Retry:
                            LoggerFactory.Global.Error($"TaskManager: WTFTask {task.Id} failed and retrying");
                            break;
                        case WTFTaskStatus.Failed:
                            if (task.TryCount > 0)
                                LoggerFactory.Global.Error($"TaskManager: WTFTask {task.Id} failed after {task.TryCount} retries");
                            else
                                LoggerFactory.Global.Error($"TaskManager: WTFTask {task.Id} failed");
                            removedTasks.Add(task);
                            break;
                        case WTFTaskStatus.Abort:
                            LoggerFactory.Global.Info($"TaskManager: WTFTask {task.Id} aborted");
                            removedTasks.Add(task);
                            break;
                        case WTFTaskStatus.Finished:
                            LoggerFactory.Global.Info($"TaskManager: WTFTask {task.Id} {task.ToString()} finished successfully");
                            removedTasks.Add(task);
                            break;
                    }
                }
            }

            foreach (var task in removedTasks)
            {
                LoggerFactory.Global.Debug($"TaskManager: Removing task {task.Id} of type {task.GetType()}");
                _tasks[task.Type].Remove(task);
            }
        }

        private static bool ExecuteTask(WTFTask task)
        {
            var ind = _threadPool.IndexOf(_ =>
                _ == null ||
                _.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ||
                _.Status == System.Threading.Tasks.TaskStatus.Canceled ||
                _.Status == System.Threading.Tasks.TaskStatus.Faulted);

            if (ind == -1)
                return false;

            task.SetProcessing();

            _threadPool[ind] = Task.Run(() =>
            {
                try
                {
                    task.Execute();
                }
                catch (Exception e)
                {
                    LoggerFactory.Global.Error($"Error executing task from thread pool: {e.Message}");
                }
            });

            return true;
        }
    }
}
