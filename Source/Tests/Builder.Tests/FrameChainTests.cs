using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class FrameChainTests
	{
		private sealed class Rig
		{
			internal bool Wanted = true;
			internal int Paints;
			internal bool PostWorks = true;
			internal long Time = 1000;
			internal int Ticks;
			internal readonly List<int> Sleeps = new List<int>();
			internal readonly Queue<Action> Posted = new Queue<Action>();
			internal readonly FrameChain Chain;

			internal Rig()
			{
				Chain = new FrameChain(() => Wanted, () => Paints, Post, () => Time, ms => { Sleeps.Add(ms); Time += ms; }, () => Ticks++);
			}

			private bool Post(Action frame)
			{
				if(!PostWorks) return false;
				Posted.Enqueue(frame);
				return true;
			}

			internal void Dispatch()
			{
				while(Posted.Count > 0) Posted.Dequeue()();
			}
		}

		[Fact]
		public void OnePostPerFrameAndNoneWhilePaintsWaitOrTheChainIsUnwanted()
		{
			Rig r = new Rig();
			Assert.Equal(ChainIdle.Posted, r.Chain.OnIdle());
			Assert.Equal(ChainIdle.Pending, r.Chain.OnIdle());
			Assert.Equal(1, r.Posted.Count);
			r.Dispatch();
			Assert.Equal(1, r.Ticks);

			r.Paints = 1;
			Assert.Equal(ChainIdle.Paints, r.Chain.OnIdle());
			r.Paints = 0;
			r.Wanted = false;
			Assert.Equal(ChainIdle.Unwanted, r.Chain.OnIdle());
			Assert.Equal(0, r.Posted.Count);

			r.Wanted = true;
			r.Time += 50;
			r.Chain.OnIdle();
			Assert.Equal(1, r.Posted.Count);
		}

		[Fact]
		public void FrameUnwantedByDispatchTimeDoesNotTickAndTheChainRearms()
		{
			Rig r = new Rig();
			r.Chain.OnIdle();
			r.Wanted = false;
			r.Dispatch();
			Assert.Equal(0, r.Ticks);
			Assert.False(r.Chain.Pending, "pending after the dropped frame");
			r.Wanted = true;
			r.Chain.OnIdle();
			Assert.Equal(1, r.Posted.Count);
		}

		[Fact]
		public void FailedPostLeavesTheChainFreeToRetry()
		{
			Rig r = new Rig();
			r.PostWorks = false;
			r.Chain.OnIdle();
			Assert.False(r.Chain.Pending, "pending after a failed post");
			r.PostWorks = true;
			r.Chain.OnIdle();
			Assert.Equal(1, r.Posted.Count);
		}

		[Fact]
		public void TimerTicksOnlyWhenNoChainedFrameRanSinceItsLastTick()
		{
			Rig r = new Rig();
			Assert.True(r.Chain.TimerShouldTick(true), "no chained frames yet");
			r.Chain.OnIdle(); r.Dispatch();
			r.Time += 10;
			r.Chain.OnIdle(); r.Dispatch();
			Assert.False(r.Chain.TimerShouldTick(true), "chained frames ran since the last timer tick");
			Assert.True(r.Chain.TimerShouldTick(true), "chain stalled for a whole timer period");

			r.Time += 10;
			r.Chain.OnIdle();
			Assert.False(r.Chain.TimerShouldTick(true), "a frame posted in this pump pass is about to run");
			r.Dispatch();
			Assert.False(r.Chain.TimerShouldTick(true), "that frame ran");
		}

		[Fact]
		public void LeavingTheChainResyncsTheTimerAndAFrameBlocksTimerAndIdleUnderIt()
		{
			Rig r = new Rig();
			r.Chain.OnIdle(); r.Dispatch();
			Assert.True(r.Chain.TimerShouldTick(false), "chain unwanted: the timer ticks");
			Assert.True(r.Chain.TimerShouldTick(true), "no frame since the unwanted tick");

			bool timerinside = true;
			ChainIdle idleinside = ChainIdle.Posted;
			FrameChain chain = null;
			chain = new FrameChain(() => true, () => 0, f => { f(); return true; }, () => 0, ms => { },
				() => { timerinside = chain.TimerShouldTick(false); idleinside = chain.OnIdle(); });
			chain.OnIdle();
			Assert.False(timerinside, "timer ticked inside a chained frame");
			Assert.Equal(ChainIdle.Pending, idleinside);
		}

		[Fact]
		public void MinimumFrameTimeSleepsOnlyTheRemainder()
		{
			Rig r = new Rig();
			r.Chain.MinFrameMs = 4;
			r.Chain.OnIdle(); r.Dispatch();
			Assert.Equal(0, r.Sleeps.Count);

			r.Time += 1;
			r.Chain.OnIdle(); r.Dispatch();
			Assert.Equal(3, r.Sleeps[0]);

			r.Time += 4;
			r.Chain.OnIdle(); r.Dispatch();
			r.Time -= 100000;
			r.Chain.OnIdle(); r.Dispatch();
			Assert.Equal(1, r.Sleeps.Count);
			Assert.Equal(4, r.Ticks);
		}
	}
}
