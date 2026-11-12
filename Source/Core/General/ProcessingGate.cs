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

		// The callback runs after every call except an ignored resume; its argument is true only on an off-to-on edge.
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

		// Clamped at 0 so a stray extra call from a mode or plugin cannot go negative.
		internal void Disable()
		{
			bool wasrunning = Running;
			enablecount--;
			if(enablecount < 0) enablecount = 0;
			Apply(wasrunning);
		}

		// Holds processing off until the returned token is disposed; disposing it again is a no-op.
		internal IDisposable Suspend()
		{
			bool wasrunning = Running;
			suspenddepth++;
			Apply(wasrunning);
			return new Token(this, epoch);
		}

		// Drops every enable reason and suspension; tokens taken before this are ignored.
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
