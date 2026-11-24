using System;
using CodeImp.DoomBuilder.Geometry;
using Xunit;

namespace Builder.Tests
{
	public class PivotMathTests
	{
		private const double Eps = 1e-9;

		private static void Near(Vector2D expected, Vector2D actual, string what)
		{
			Assert.True(Math.Abs(expected.x - actual.x) < Eps && Math.Abs(expected.y - actual.y) < Eps,
				what + ": expected " + expected + ", got " + actual);
		}

		// Copy of EditSelectionMode.TransformedPoint: resize, rotate about the box center, translate
		private static Vector2D Transformed(Vector2D p, Vector2D baseoffset, Vector2D basesize, Vector2D offset, Vector2D size, double rotation)
		{
			p = (p - baseoffset) * (size / basesize) + baseoffset;
			Vector2D center = baseoffset + size * 0.5;
			p = (p - center).GetRotated(rotation) + center;
			return p + offset - baseoffset;
		}

		[Fact]
		public void RotatesPointsAboutAPivot()
		{
			Near(new Vector2D(0, 10), PivotMath.RotatedAbout(new Vector2D(10, 0), new Vector2D(0, 0), Angle2D.PIHALF), "origin pivot");
			Near(new Vector2D(5, 13), PivotMath.RotatedAbout(new Vector2D(13, 5), new Vector2D(5, 5), Angle2D.PIHALF), "offset pivot");
			Near(new Vector2D(-3, 5), PivotMath.RotatedAbout(new Vector2D(13, 5), new Vector2D(5, 5), Angle2D.PI), "half turn");

			Vector2D start = new Vector2D(17.25, -3.5);
			Assert.Equal(start, PivotMath.RotatedAbout(start, new Vector2D(4, 9), 0.0));

			Vector2D p = start;
			for(int i = 0; i < 4; i++) p = PivotMath.RotatedAbout(p, new Vector2D(4.5, 9), Angle2D.PIHALF);
			Near(start, p, "four quarter turns");

			p = start;
			for(int i = 0; i < 8; i++) p = PivotMath.RotatedAbout(p, new Vector2D(-7, 2), Angle2D.PI / 4);
			Near(start, p, "eight eighth turns");
		}

		[Fact]
		public void MirrorsPointsAcrossAxesThroughThePivot()
		{
			Vector2D pivot = new Vector2D(8, -2);
			Near(new Vector2D(6, 5), PivotMath.MirroredAcross(new Vector2D(10, 5), pivot, true), "vertical axis");
			Near(new Vector2D(10, -9), PivotMath.MirroredAcross(new Vector2D(10, 5), pivot, false), "horizontal axis");
			Near(new Vector2D(8, 40), PivotMath.MirroredAcross(new Vector2D(8, 40), pivot, true), "on vertical axis");
			Near(new Vector2D(-30, -2), PivotMath.MirroredAcross(new Vector2D(-30, -2), pivot, false), "on horizontal axis");
		}

		[Fact]
		public void BoxStepsMatchThePointTransforms()
		{
			Random rnd = new Random(1234);
			Vector2D baseoffset = new Vector2D(-40, 16);
			Vector2D basesize = new Vector2D(96, 64);
			Vector2D[] sizes =
			{
				basesize,                                   // unscaled
				new Vector2D(144, 48),                      // resized
				new Vector2D(-96, 64),                      // flipped in x
				new Vector2D(96, -64),                      // flipped in y
				new Vector2D(-120, -80),                    // flipped both, resized
			};
			string failures = "";

			foreach(Vector2D size in sizes)
			{
				for(int trial = 0; trial < 8; trial++)
				{
					Vector2D offset = new Vector2D(rnd.NextDouble() * 200 - 100, rnd.NextDouble() * 200 - 100);
					double rotation = rnd.NextDouble() * 6 - 3;
					if(rotation == 0) rotation = 0.5;
					Vector2D pivot = new Vector2D(rnd.NextDouble() * 400 - 200, rnd.NextDouble() * 400 - 200);
					double delta = rnd.NextDouble() * 8 - 4;
					bool vertical = (trial % 2) == 0;

					Vector2D roff = offset;
					double rrot = rotation;
					PivotMath.RotateBoxAbout(ref roff, size, ref rrot, pivot, delta);

					Vector2D moff = offset;
					Vector2D msize = size;
					double mrot = rotation;
					PivotMath.MirrorBoxAcross(ref moff, ref msize, ref mrot, pivot, vertical);

					for(int i = 0; i < 6; i++)
					{
						Vector2D p = baseoffset + new Vector2D(rnd.NextDouble() * basesize.x, rnd.NextDouble() * basesize.y);
						Vector2D before = Transformed(p, baseoffset, basesize, offset, size, rotation);

						Vector2D want = PivotMath.RotatedAbout(before, pivot, delta);
						Vector2D got = Transformed(p, baseoffset, basesize, roff, size, rrot);
						if(Vector2D.Distance(want, got) > Eps)
							failures += "rotate size " + size + " trial " + trial + ": " + want + " vs " + got + "\n";

						want = PivotMath.MirroredAcross(before, pivot, vertical);
						got = Transformed(p, baseoffset, basesize, moff, msize, mrot);
						if(Vector2D.Distance(want, got) > Eps)
							failures += "mirror size " + size + " trial " + trial + ": " + want + " vs " + got + "\n";
					}
				}
			}

			Assert.True(failures.Length == 0, failures);
		}
	}
}
