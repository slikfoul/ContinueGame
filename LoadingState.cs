namespace ContinueGame
{
    public enum LoadingStage { FindingServer, LoadingWorld }

    public sealed class LoadingState
    {
        public LoadingStage Stage { get; private set; }
        public void Advance(LoadingStage stage) { if (stage > Stage) Stage = stage; }
    }
}
