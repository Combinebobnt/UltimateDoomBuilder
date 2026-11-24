using System;
using CodeImp.DoomBuilder.BuilderModes;
using Xunit;

namespace Builder.Tests
{
	public class CameraFallTests
	{
		private static double FallFor(long totalms, long tickms)
		{
			double velocity = 0.0, height = 0.0;
			for(long t = 0; t < totalms; t += tickms)
			{
				velocity = CameraFall.Accelerate(velocity, 1.0, tickms);
				height += CameraFall.Distance(velocity, tickms);
			}
			return height;
		}

		[Fact]
		public void FallDistanceDoesNotDependOnTheTickRate()
		{
			double slow = FallFor(960, 32), reference = FallFor(960, 16), fast = FallFor(960, 8);
			Assert.True(slow < 0 && reference < 0 && fast < 0, "falls go down");
			// Euler steps differ by under one tick's share: 3.3% between 8 and 32 ms over 960 ms
			Assert.True(Math.Abs(slow - fast) / Math.Abs(fast) < 0.05, slow + " vs " + fast);
			Assert.True(Math.Abs(reference - fast) / Math.Abs(fast) < 0.05, reference + " vs " + fast);
		}

		[Fact]
		public void ReferenceTickMovesByTheVelocity()
		{
			long tick = (long)CameraFall.REFERENCE_TICK_MS;
			Assert.True(CameraFall.REFERENCE_TICK_MS == tick, "whole-ms reference tick");
			Assert.True(Math.Abs(CameraFall.Distance(-2.5, tick) - -2.5) < 1e-12, "one reference tick moves by the velocity");
		}
	}
}
