using System;
using CodeImp.DoomBuilder.Geometry;
using Xunit;

namespace Builder.Tests
{
	public class AngleQuarterTurnTests
	{
		private static void Near(double expected, double actual)
		{
			Assert.True(Math.Abs(expected - actual) < 1e-12, "expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
		}

		[Fact]
		public void OneTurnFromZeroIsExact()
		{
			Assert.Equal(Angle2D.PIHALF, Angle2D.AddQuarterTurns(0.0, 1));
			Assert.Equal(-Angle2D.PIHALF, Angle2D.AddQuarterTurns(0.0, -1));
		}

		[Fact]
		public void FourTurnsReturnToExactZero()
		{
			double cw = 0.0;
			double ccw = 0.0;
			for(int i = 0; i < 4; i++)
			{
				cw = Angle2D.AddQuarterTurns(cw, -1);
				ccw = Angle2D.AddQuarterTurns(ccw, 1);
			}

			Assert.True(cw == 0.0, "clockwise ended at " + cw.ToString("R"));
			Assert.True(ccw == 0.0, "counterclockwise ended at " + ccw.ToString("R"));
		}

		[Fact]
		public void HalfTurnIsPositivePi()
		{
			Assert.Equal(Angle2D.PI, Angle2D.AddQuarterTurns(0.0, 2));
			Assert.Equal(Angle2D.PI, Angle2D.AddQuarterTurns(0.0, -2));
			Assert.Equal(Angle2D.PI, Angle2D.AddQuarterTurns(-Angle2D.PI, 0));
		}

		[Fact]
		public void SumJustPastPiSnapsToPi()
		{
			Assert.Equal(Angle2D.PI, Angle2D.AddQuarterTurns(18 * Angle2D.DegToRad(5), 1));

			// The fine rotate action accumulates its steps, so test the summed value too
			double accumulated = 0.0;
			for(int i = 0; i < 18; i++) accumulated += Angle2D.DegToRad(5);
			Assert.Equal(Angle2D.PI, Angle2D.AddQuarterTurns(accumulated, 1));
		}

		[Fact]
		public void OffQuarterAngleKeepsItsOffsetAndWraps()
		{
			Near(Angle2D.DegToRad(100), Angle2D.AddQuarterTurns(Angle2D.DegToRad(10), 1));
			Near(Angle2D.DegToRad(-100), Angle2D.AddQuarterTurns(Angle2D.DegToRad(170), 1));
		}

		[Fact]
		public void LargeTurnCountsReduce()
		{
			Assert.Equal(Angle2D.PIHALF, Angle2D.AddQuarterTurns(0.0, 9));
			Assert.Equal(-Angle2D.PIHALF, Angle2D.AddQuarterTurns(0.0, -9));
		}
	}
}
