using System;

namespace CodeImp.DoomBuilder
{
	// Whether the main processor timer runs: enable reasons held by modes, minus modal suspensions.
	internal sealed class ProcessingGate
	{
		private readonly Action<bool> applied;
		private int enablecount;
		private int suspenddepth;
		private int epoch;

		internal int EnableCount { get { return enablecount; } }
		internal int SuspendDepth { get { return suspenddepth; } }
		internal bool Running { get { return enablecount > 0 && suspenddepth == 0; } }

		internal ProcessingGate(Action<bool> applied)
		{
			this.applied = applied;
		}

		internal void Enable()
		{
			bool wasrunning = Running;
			enablecount++;
			Apply(wasrunning);
		}

		internal void Disable()
		{
			bool wasrunning = Running;
			enablecount--;
			if(enablecount < 0) enablecount = 0;
			Apply(wasrunning);
		}

		internal IDisposable Suspend()
		{
			bool wasrunning = Running;
			suspenddepth++;
			Apply(wasrunning);
			return new Token(this, epoch);
		}

		internal void Reset()
		{
			bool wasrunning = Running;
			enablecount = 0;
			suspenddepth = 0;
			epoch++;
			Apply(wasrunning);
		}

		private void Resume(int tokenepoch)
		{
			if(tokenepoch != epoch || suspenddepth == 0) return;
			bool wasrunning = Running;
			suspenddepth--;
			Apply(wasrunning);
		}

		private void Apply(bool wasrunning)
		{
			if(applied != null) applied(!wasrunning && Running);
		}

		private sealed class Token : IDisposable
		{
			private readonly ProcessingGate gate;
			private readonly int epoch;
			private bool disposed;

			internal Token(ProcessingGate gate, int epoch)
			{
				this.gate = gate;
				this.epoch = epoch;
			}

			public void Dispose()
			{
				if(disposed) return;
				disposed = true;
				gate.Resume(epoch);
			}
		}
	}
}
