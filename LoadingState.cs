using System.Collections.Generic;

namespace ContinueGame
{
    public enum LoadingStage
    {
        SelectingCharacter, RestoringPassword, FindingServer, LoadingScene, Connecting,
        SendingPassword, Authenticating, ReceivingWorld, LoadingArea, PreparingCharacter, Ready
    }

    public sealed class LoadingState
    {
        private readonly List<LoadingStage> _visited = new List<LoadingStage> { LoadingStage.SelectingCharacter };
        public LoadingState() { Visited = _visited.AsReadOnly(); }
        public IReadOnlyList<LoadingStage> Visited { get; }
        public LoadingStage Stage { get; private set; }
        public bool Completed { get; private set; }
        public void Advance(LoadingStage stage)
        {
            if (Completed || stage <= Stage) return;
            Stage = stage;
            _visited.Add(stage);
        }
        public void Complete() { Advance(LoadingStage.Ready); Completed = true; }
    }
}
