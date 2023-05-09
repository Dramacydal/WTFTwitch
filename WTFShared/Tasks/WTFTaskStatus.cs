namespace WTFShared.Tasks
{
    public enum WTFTaskStatus
    {
        None,
        Processing,
        Executing,
        Failed,
        Retry,
        Abort,
        Finished,
    }
}
