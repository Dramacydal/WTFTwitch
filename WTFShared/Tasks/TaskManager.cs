using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WTFShared.Logging;

namespace WTFShared.Tasks
{
    public class TaskManager
    {
        private readonly Dictionary<TaskType, List<WTFTask>> _tasks = new Dictionary<TaskType, List<WTFTask>>();
        private readonly Task[] _threadPool = new Task[10];

        public TaskManager()
        {
        }

        public void AddTask(WTFTask wtfTask)
        {
            wtfTask.Reset();
            if (!_tasks.ContainsKey(wtfTask.Type))
                _tasks[wtfTask.Type] = new List<WTFTask>();

            _tasks[wtfTask.Type].Add(wtfTask);
        }

        public void AddTask(WTFTask wtfTask, DateTime moment)
        {
            wtfTask.Schedule(moment);

            AddTask(wtfTask);
        }

        public void AddTask(WTFTask wtfTask, TimeSpan delay)
        {
            wtfTask.Schedule(delay);

            AddTask(wtfTask);
        }

        public void Update(int maxTasks = 0)
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
                        case TaskStatus.None:
                            if ((maxTasks <= 0 || taskCounter > maxTasks) && task.Moment < DateTime.Now)
                            {
                                ++taskCounter;
                                ExecuteTask(task);
                            }

                            break;
                        case TaskStatus.Retry:
                            Logger.Instance.Error($"WTFTask {task.Id} failed and retrying");
                            break;
                        case TaskStatus.Failed:
                            if (task.TryCount > 0)
                                Logger.Instance.Error($"WTFTask {task.Id} failed after {task.TryCount} retries");
                            else
                                Logger.Instance.Error($"WTFTask {task.Id} failed");
                            removedTasks.Add(task);
                            break;
                        case TaskStatus.Abort:
                            Logger.Instance.Info($"WTFTask {task.Id} aborted");
                            removedTasks.Add(task);
                            break;
                        case TaskStatus.Finished:
                            Logger.Instance.Info($"WTFTask {task.Id} finished successfully");
                            removedTasks.Add(task);
                            break;
                    }
                }
            }

            foreach (var task in removedTasks)
                _tasks[task.Type].Remove(task);
        }

        private bool ExecuteTask(WTFTask task)
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
                    Logger.Instance.Error($"Error executing task from thread pool: {e.Message}");
                }
            });

            return true;
        }
    }
}
