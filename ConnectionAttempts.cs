namespace ContinueGame
{
    internal enum ResumeFailure { Network, Password, Other }

    // A bounded connection sequence. Waiting for a human password confirmation is not an automatic retry.
    internal sealed class ConnectionAttempts
    {
        internal const int Maximum = 3;
        internal const double Delay = 5;
        internal bool Active { get; private set; }
        internal int Attempt { get; private set; }
        internal bool WaitingForRetry { get; private set; }
        internal bool WaitingForPassword { get; private set; }
        internal bool NeedsPassword { get; private set; }
        internal double NextAt { get; private set; }

        internal void Start()
        {
            Cancel();
            Active = true;
            Attempt = 1;
        }

        internal bool Fail(ResumeFailure failure, double now)
        {
            if (!Active) return false;
            if (WaitingForPassword || WaitingForRetry) return true;
            if (failure == ResumeFailure.Password)
            {
                NeedsPassword = true;
                WaitingForPassword = true;
                return true;
            }
            if (failure == ResumeFailure.Network && Attempt < Maximum)
            {
                WaitingForRetry = true;
                NextAt = now + Delay;
                return true;
            }
            Cancel();
            return false;
        }

        internal void ConfirmPassword(double now)
        {
            if (!Active || !WaitingForPassword) return;
            WaitingForPassword = false;
            // The user explicitly starts a new sequence with corrected credentials.
            Attempt = 0;
            WaitingForRetry = true;
            NextAt = now;
        }

        internal bool TryBegin(double now, bool menuReady)
        {
            if (!Active || !WaitingForRetry || !menuReady || now < NextAt) return false;
            WaitingForRetry = false;
            Attempt++;
            return true;
        }

        internal void PasswordEntered() { NeedsPassword = false; }

        internal void Cancel()
        {
            Active = false;
            Attempt = 0;
            WaitingForRetry = false;
            WaitingForPassword = false;
            NeedsPassword = false;
            NextAt = 0;
        }
    }
}
