namespace Segra.Backend
{
    public abstract class Integration
    {
        public abstract Task Start();
        public abstract Task Shutdown();
    }
}
