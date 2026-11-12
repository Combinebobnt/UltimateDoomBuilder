using System;
using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class ProcessingGateTests
	{
		private static ProcessingGate GateWithCount(int count)
		{
			ProcessingGate gate = new ProcessingGate(null);
			for(int i = 0; i < count; i++) gate.Enable();
			return gate;
		}

		private static void AssertState(ProcessingGate gate, int count, int depth, bool running)
		{
			Assert.Equal(count, gate.EnableCount);
			Assert.Equal(depth, gate.SuspendDepth);
			Assert.Equal(running, gate.Running);
		}

		[Fact]
		public void NestedBrowserInsideEditDialogKeepsProcessingOffAndRestoresCount()
		{
			ProcessingGate gate = GateWithCount(1);
			IDisposable opener = gate.Suspend();
			Assert.False(gate.Running, "opener open");
			IDisposable browser = gate.Suspend();
			Assert.False(gate.Running, "browser open");
			browser.Dispose();
			Assert.False(gate.Running, "browser closed, opener still open");
			opener.Dispose();
			AssertState(gate, 1, 0, true);
		}

		[Fact]
		public void StandaloneBrowseWrapperAroundBrowserScopeRestoresCount()
		{
			ProcessingGate gate = GateWithCount(1);
			IDisposable wrapper = gate.Suspend();
			IDisposable browser = gate.Suspend();
			Assert.False(gate.Running, "browser open");
			browser.Dispose();
			Assert.False(gate.Running, "wrapper still open");
			wrapper.Dispose();
			AssertState(gate, 1, 0, true);
		}

		[Fact]
		public void BrowserReactivationCannotResumeProcessingUnderTheModal()
		{
			ProcessingGate gate = GateWithCount(1);
			IDisposable opener = gate.Suspend();
			IDisposable browser = gate.Suspend();
			for(int i = 0; i < 3; i++)
			{
				browser.Dispose();
				Assert.False(gate.Running, "browser re-activated " + i);
			}
			opener.Dispose();
			AssertState(gate, 1, 0, true);
		}

		[Fact]
		public void RepeatedNestedVisitsDoNotLeakIntoTheNextDialog()
		{
			ProcessingGate gate = GateWithCount(1);
			IDisposable opener = gate.Suspend();
			for(int i = 0; i < 5; i++)
			{
				IDisposable browser = gate.Suspend();
				browser.Dispose();
				Assert.False(gate.Running, "after nested visit " + i);
			}
			opener.Dispose();
			AssertState(gate, 1, 0, true);

			IDisposable next = gate.Suspend();
			Assert.False(gate.Running, "next opener open");
			next.Dispose();
			AssertState(gate, 1, 0, true);
		}

		[Fact]
		public void DoubleDisposeDoesNotChangeDepth()
		{
			ProcessingGate gate = GateWithCount(1);
			IDisposable outer = gate.Suspend();
			IDisposable inner = gate.Suspend();
			inner.Dispose();
			inner.Dispose();
			AssertState(gate, 1, 1, false);
			outer.Dispose();
			outer.Dispose();
			AssertState(gate, 1, 0, true);
		}

		[Fact]
		public void DisposeAfterResetIsIgnored()
		{
			ProcessingGate gate = GateWithCount(1);
			IDisposable stale = gate.Suspend();
			gate.Reset();
			AssertState(gate, 0, 0, false);
			gate.Enable();
			IDisposable current = gate.Suspend();
			stale.Dispose();
			AssertState(gate, 1, 1, false);
			current.Dispose();
			AssertState(gate, 1, 0, true);
		}

		[Fact]
		public void ClassicModeWithNoEnableReasonStaysOffAfterAnEditDialog()
		{
			ProcessingGate gate = GateWithCount(0);
			IDisposable opener = gate.Suspend();
			Assert.False(gate.Running, "opener open");
			opener.Dispose();
			AssertState(gate, 0, 0, false);
		}

		[Fact]
		public void ModeDisengagingUnderAModalLeavesProcessingOff()
		{
			ProcessingGate gate = GateWithCount(1);
			IDisposable opener = gate.Suspend();
			gate.Disable();
			Assert.False(gate.Running, "mode gone, opener open");
			opener.Dispose();
			AssertState(gate, 0, 0, false);
		}

		[Fact]
		public void OffToOnEdgeIsReportedOncePerTransition()
		{
			int calls = 0;
			int edges = 0;
			ProcessingGate gate = new ProcessingGate(turnedon => { calls++; if(turnedon) edges++; });

			gate.Enable();
			gate.Enable();
			Assert.Equal(1, edges);

			IDisposable outer = gate.Suspend();
			IDisposable inner = gate.Suspend();
			inner.Dispose();
			Assert.Equal(1, edges);
			outer.Dispose();
			Assert.Equal(2, edges);

			gate.Disable();
			gate.Disable();
			gate.Enable();
			Assert.Equal(3, edges);
			Assert.Equal(9, calls);
		}
	}
}
