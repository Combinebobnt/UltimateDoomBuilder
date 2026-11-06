using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	// Who handles a worker crash: the UI delegate or the worker's timeout path, never both
	public class CrashPickupTests
	{
		private const long WINDOW = 10000;
		private const long CAP = 300000;

		[Fact]
		public void DelegateFirstMakesTheWorkersAbandonClaimLose()
		{
			CrashPickup pickup = new CrashPickup(0, WINDOW, CAP);

			Assert.True(pickup.TryPickUp());
			Assert.False(pickup.TryAbandon());
			Assert.True(pickup.IsPickedUp);
		}

		[Fact]
		public void ASecondPostSeesThePickupAlreadyOwned()
		{
			CrashPickup pickup = new CrashPickup(0, WINDOW, CAP);

			// A retried BeginInvoke can queue the delegate twice; the second must return, not park
			Assert.True(pickup.TryPickUp());
			Assert.False(pickup.TryPickUp());
			Assert.True(pickup.IsPickedUp);
		}

		[Fact]
		public void WorkerFirstMakesTheLateDelegateLose()
		{
			CrashPickup pickup = new CrashPickup(0, WINDOW, CAP);

			Assert.True(pickup.TryAbandon());
			Assert.False(pickup.TryPickUp());
			Assert.False(pickup.IsPickedUp);
		}

		[Fact]
		public void WindowExpiresWithNoSave()
		{
			CrashPickup pickup = new CrashPickup(1000, WINDOW, CAP);

			Assert.False(pickup.WindowExpired(1000 + WINDOW - 1, false));
			Assert.True(pickup.WindowExpired(1000 + WINDOW, false));
		}

		[Fact]
		public void ASaveReArmsTheWindowFromTheLastPollThatSawIt()
		{
			CrashPickup pickup = new CrashPickup(0, WINDOW, CAP);

			Assert.False(pickup.WindowExpired(9000, true));
			Assert.False(pickup.WindowExpired(15000, true));
			// The save ended: the window runs from the last poll that saw it
			Assert.False(pickup.WindowExpired(24999, false));
			Assert.True(pickup.WindowExpired(25000, false));
		}

		[Fact]
		public void TheCapEndsTheWaitEvenWhileSaving()
		{
			CrashPickup pickup = new CrashPickup(0, WINDOW, CAP);

			for(long t = 0; t < CAP; t += 5000) Assert.False(pickup.WindowExpired(t, true));
			Assert.True(pickup.WindowExpired(CAP, true));
			Assert.True(pickup.CapReached(CAP));
		}
	}
}
