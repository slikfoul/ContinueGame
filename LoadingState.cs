namespace ContinueGame
{
    public enum LoadingStage
    {
        SelectingCharacter, RestoringPassword, FindingServer, LoadingScene, Connecting,
        SendingPassword, Authenticating, ReceivingWorld, LoadingArea
    }

    public sealed class LoadingState
    {
        public LoadingStage Stage { get; private set; }
        public bool Completed { get; private set; }
        public void Advance(LoadingStage stage) { if (!Completed && stage > Stage) Stage = stage; }
        public void ObserveNativeLoading(bool active, float alpha, bool worldLoadingActive, bool artworkVisible)
        {
            if (active && alpha > 0f && worldLoadingActive && artworkVisible) Completed = true;
        }
    }
}
