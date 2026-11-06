using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class UIActionQueueTests
	{
		// A failing action must not cost the others, and BeginInvoke would hide the loss without a trace
		[Fact]
		public void FailingActionLeavesTheRestOfTheBatchToRun()
		{
			var queue = new UIActionQueue();
			var ran = new List<string>();
			var logged = new List<Exception>();

			queue.Add(() => ran.Add("first"), () => { });
			queue.Add(() => throw new InvalidOperationException("broken"), () => { });
			queue.Add(() => ran.Add("last"), () => { });

			var rethrown = Assert.Throws<InvalidOperationException>(() => queue.Process(logged.Add));

			Assert.Equal(new List<string> { "first", "last" }, ran);
			Assert.Equal("broken", Assert.Single(logged).Message);
			Assert.Equal("broken", rethrown.Message);
		}

		[Fact]
		public void FailingLogLeavesTheRestOfTheBatchToRun()
		{
			var queue = new UIActionQueue();
			var ran = false;

			queue.Add(() => throw new InvalidOperationException("broken"), () => { });
			queue.Add(() => ran = true, () => { });

			Assert.Throws<InvalidOperationException>(() => queue.Process(_ => throw new IOException("log gone")));

			Assert.True(ran);
		}

		// An unsent notify must not stall the queue: nothing else would ever reach Process
		[Fact]
		public void FailedNotifyIsSentAgainByTheNextAdd()
		{
			var queue = new UIActionQueue();
			var ran = new List<string>();
			var notifies = 0;

			Assert.Throws<ThreadInterruptedException>(() =>
				queue.Add(() => ran.Add("stranded"), () => throw new ThreadInterruptedException()));

			queue.Add(() => ran.Add("next"), () => notifies++);
			Assert.Equal(1, notifies);

			queue.Add(() => ran.Add("after"), () => notifies++);
			Assert.Equal(1, notifies);

			queue.Process(_ => { });
			Assert.Equal(new List<string> { "stranded", "next", "after" }, ran);
		}

		// One notify per batch keeps the UI's message queue short, and the first Add after a swap must send one
		[Fact]
		public void NotifiesOncePerBatch()
		{
			var queue = new UIActionQueue();
			var notifies = 0;

			queue.Add(() => { }, () => notifies++);
			queue.Add(() => { }, () => notifies++);
			Assert.Equal(1, notifies);

			queue.Process(_ => { });

			queue.Add(() => { }, () => notifies++);
			Assert.Equal(2, notifies);
		}
	}
}
