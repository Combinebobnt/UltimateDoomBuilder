using System.Collections.Generic;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Actions;
using Xunit;

namespace Builder.Tests
{
	public class ModifierHintTrackerTests
	{
		private class Host
		{
			public int Restarts;
			public bool TimerRunning;
			public bool CanShow = true;
			public readonly List<int> Overlays = new List<int>();
			public readonly ModifierHintTracker Tracker;

			public Host()
			{
				Tracker = new ModifierHintTracker(
					() => { Restarts++; TimerRunning = true; },
					() => TimerRunning = false,
					Overlays.Add,
					() => CanShow);
			}

			public int Shown { get { return Overlays.Count == 0 ? 0 : Overlays[Overlays.Count - 1]; } }

			public void Fire()
			{
				if(TimerRunning) Tracker.TimerFired();
			}
		}

		private const Keys CtrlDown = Keys.ControlKey | Keys.Control;

		[Fact]
		public void HoldingAModifierShowsTheOverlayWhenTheTimerFires()
		{
			Host h = new Host();
			h.Tracker.KeyDown(CtrlDown);
			Assert.True(h.TimerRunning);
			Assert.Empty(h.Overlays);
			h.Fire();
			Assert.Equal((int)Keys.Control, h.Shown);
			h.Tracker.KeyUp(Keys.ControlKey);
			Assert.Equal(0, h.Shown);
		}

		[Fact]
		public void AChordKeyCancelsUntilTheModifiersChange()
		{
			Host h = new Host();
			h.Tracker.KeyDown(CtrlDown);
			h.Tracker.KeyDown(Keys.S | Keys.Control);
			Assert.False(h.TimerRunning);
			h.Tracker.TimerFired();
			Assert.Empty(h.Overlays);

			// Releasing S and repeating Ctrl leaves it cancelled; adding Shift starts over
			h.Tracker.KeyUp(Keys.S | Keys.Control);
			h.Tracker.KeyDown(CtrlDown);
			Assert.False(h.TimerRunning);
			h.Tracker.KeyDown(Keys.ShiftKey | Keys.Shift | Keys.Control);
			h.Fire();
			Assert.Equal((int)(Keys.Control | Keys.Shift), h.Shown);

			// A chord while the overlay is up clears it
			h.Tracker.KeyDown(Keys.Z | Keys.Control | Keys.Shift);
			Assert.Equal(0, h.Shown);
		}

		[Fact]
		public void ReleasingAModifierClearsEvenIfItsBitIsStillReported()
		{
			Host h = new Host();
			h.Tracker.KeyDown(CtrlDown);
			h.Tracker.KeyUp(CtrlDown);
			Assert.False(h.TimerRunning);
			Assert.Equal(0, h.Tracker.HeldMask);

			h.Tracker.KeyDown(Keys.Menu | Keys.Alt);
			h.Tracker.KeyDown(Keys.ShiftKey | Keys.Shift | Keys.Alt);
			h.Fire();
			h.Tracker.KeyUp(Keys.Menu | Keys.Alt | Keys.Shift);
			Assert.Equal((int)Keys.Shift, h.Tracker.HeldMask);
		}

		[Fact]
		public void AutoRepeatDoesNotRestartTheTimer()
		{
			Host h = new Host();
			h.Tracker.KeyDown(CtrlDown);
			h.Tracker.KeyDown(CtrlDown);
			h.Tracker.KeyDown(CtrlDown);
			Assert.Equal(1, h.Restarts);
		}

		[Fact]
		public void SidedAndMenuKeysCountAsModifiers()
		{
			foreach(Keys k in new[] { Keys.LControlKey | Keys.Control, Keys.RShiftKey | Keys.Shift, Keys.Menu | Keys.Alt, Keys.LMenu | Keys.Alt })
			{
				Host h = new Host();
				h.Tracker.KeyDown(k);
				Assert.True(h.TimerRunning, k.ToString());
				h.Fire();
				Assert.Equal((int)(k & Keys.Modifiers), h.Shown);
			}
		}

		[Fact]
		public void ShowConditionsGateShowingButNeverClearing()
		{
			Host h = new Host();
			h.CanShow = false;
			h.Tracker.KeyDown(CtrlDown);
			h.Fire();
			Assert.Empty(h.Overlays);

			h.CanShow = true;
			h.Tracker.KeyUp(Keys.ControlKey);
			h.Tracker.KeyDown(CtrlDown);
			h.Fire();
			Assert.Equal((int)Keys.Control, h.Shown);

			// Clearing ignores the show conditions
			h.CanShow = false;
			h.Tracker.KeyUp(Keys.ControlKey);
			Assert.Equal(0, h.Shown);
			Assert.False(h.TimerRunning);
		}

		[Fact]
		public void AddingAModifierWhileShowingFollowsImmediately()
		{
			Host h = new Host();
			h.Tracker.KeyDown(CtrlDown);
			h.Fire();
			h.Tracker.KeyDown(Keys.ShiftKey | Keys.Shift | Keys.Control);
			Assert.Equal((int)(Keys.Control | Keys.Shift), h.Shown);
			Assert.False(h.TimerRunning);
			h.Tracker.KeyUp(Keys.ShiftKey | Keys.Control);
			Assert.Equal((int)Keys.Control, h.Shown);

			// Without the show conditions a chord change clears and waits again
			h.CanShow = false;
			h.Tracker.KeyDown(Keys.Menu | Keys.Alt | Keys.Control);
			Assert.Equal(0, h.Shown);
			Assert.True(h.TimerRunning);
		}

		[Fact]
		public void PollCatchesAReleaseMissedWhileTheTimerWaits()
		{
			Host h = new Host();
			h.Tracker.KeyDown(CtrlDown);
			h.Tracker.Poll(Keys.None);
			Assert.Equal(0, h.Tracker.HeldMask);
			Assert.False(h.TimerRunning);

			// The next Ctrl press is a fresh hold, not auto-repeat
			h.Tracker.KeyDown(CtrlDown);
			Assert.True(h.TimerRunning);
		}

		[Fact]
		public void ReleaseAndPollClearAStuckOverlay()
		{
			Host h = new Host();
			h.Tracker.KeyDown(CtrlDown);
			h.Fire();
			h.Tracker.Release();
			Assert.Equal(0, h.Shown);
			Assert.Equal(0, h.Tracker.HeldMask);

			h.Tracker.KeyDown(CtrlDown);
			h.Fire();
			h.Tracker.Poll(Keys.Control);
			Assert.Equal((int)Keys.Control, h.Shown);
			h.Tracker.Poll(Keys.None);
			Assert.Equal(0, h.Shown);
			Assert.False(h.TimerRunning);

			// A plain key after a missed release clears too
			h.Tracker.KeyDown(CtrlDown);
			h.Tracker.KeyDown(Keys.A);
			Assert.False(h.TimerRunning);
			Assert.Equal(0, h.Tracker.HeldMask);
		}
	}
}
