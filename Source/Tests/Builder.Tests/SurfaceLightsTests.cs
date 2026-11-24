using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Rendering;

namespace Builder.Tests
{
	public class SurfaceLightsTests
	{
		private static Vector3D[] Box(double x, double y, double z, double half)
		{
			return new[] { new Vector3D(x, y, z), new Vector3D(x - half, y - half, z - half) };
		}

		// The pairwise test the renderer used before the packed one
		private static bool Intersect(Vector3D[] b1, Vector3D[] b2)
		{
			Vector3D dist = b1[0] - b2[0];
			Vector3D h1 = b1[0] - b1[1];
			Vector3D h2 = b2[0] - b2[1];
			return h1.x + h2.x >= Math.Abs(dist.x) && h1.y + h2.y >= Math.Abs(dist.y) && h1.z + h2.z >= Math.Abs(dist.z);
		}

		private static SurfaceLights WithLights(Vector3D[][] boxes)
		{
			SurfaceLights s = new SurfaceLights();
			s.Begin(boxes.Length);
			for(int i = 0; i < boxes.Length; i++)
				s.SetLight(i, boxes[i], new Vector4f(i, 0.5f, 0.25f, 0.5f), new Vector4f(i, 1, 2, 100), new Vector4f(), new Vector2f(), new Vector2f(i, 1));
			return s;
		}

		[Fact]
		public void SelectionMatchesThePairwiseTestInOrderUpToTheCap()
		{
			Random rnd = new Random(7);
			List<string> failures = new List<string>();
			foreach(int lightcount in new[] { 0, 1, 16, 64, 100 })
			{
				Vector3D[][] lights = new Vector3D[lightcount][];
				for(int i = 0; i < lightcount; i++)
					lights[i] = Box(rnd.Next(0, 1024), rnd.Next(0, 1024), rnd.Next(0, 256), rnd.Next(8, 600));
				SurfaceLights s = WithLights(lights);
				for(int g = 0; g < 300; g++)
				{
					// Half the surfaces sit exactly on a light's box edge
					Vector3D[] box = Box(rnd.Next(0, 1024), rnd.Next(0, 1024), rnd.Next(0, 256), rnd.Next(1, 300));
					if(lightcount > 0 && g % 2 == 0)
					{
						Vector3D[] l = lights[rnd.Next(lightcount)];
						double half = box[0].x - box[1].x;
						box = Box(l[0].x + (l[0].x - l[1].x) + half, l[0].y, l[0].z, half);
					}
					List<int> expected = new List<int>();
					for(int i = 0; i < lightcount && expected.Count < SurfaceLights.MAX_PER_SURFACE; i++)
						if(Intersect(box, lights[i])) expected.Add(i);
					s.Select(box);
					List<int> actual = new List<int>();
					for(int i = 0; i < s.SelectedCount; i++) actual.Add(s.Selected(i));
					if(string.Join(",", expected) != string.Join(",", actual))
						failures.Add(lightcount + " lights, surface " + g + ": expected " + string.Join(",", expected) + " got " + string.Join(",", actual));
				}
			}
			Assert.Empty(failures);
		}

		[Fact]
		public void UploadIsNeededOnlyWhenTheSetChangesWithinAPass()
		{
			SurfaceLights s = WithLights(new[] { Box(0, 0, 0, 100), Box(1000, 0, 0, 100) });
			Vector3D[] none = Box(500, 500, 0, 10), first = Box(0, 0, 0, 10), second = Box(1000, 0, 0, 10);

			Assert.True(s.Select(none), "first surface of a pass, even unlit");
			s.Pack();
			Assert.False(s.Select(none), "unlit again");
			Assert.True(s.Select(first), "lit");
			s.Pack();
			Assert.False(s.Select(first), "same light");
			Assert.True(s.Select(second), "other light, same count");
			s.Pack();
			Assert.True(s.Select(none), "back to unlit");
			s.Pack();

			s.Begin(2);
			Assert.True(s.Select(none), "a new pass uploads again");
		}

		[Fact]
		public void PackOrdersSelectedLightsAndTerminatesColours()
		{
			SurfaceLights s = WithLights(new[] { Box(0, 0, 0, 100), Box(5000, 0, 0, 10), Box(50, 0, 0, 100) });
			s.Select(Box(0, 0, 0, 10));
			Assert.Equal(2, s.SelectedCount);
			Assert.Equal(3, s.Pack());
			float[] c = s.PackedColors;
			Assert.Equal(0f, c[0]);
			Assert.Equal(2f, c[4]);
			Assert.Equal(0f, c[11]); // terminator alpha
			Assert.Equal(2f, s.PackedPositions[4]);
			Assert.Equal(2f, s.PackedStrengths[2]);

			s.Select(Box(5000, 5000, 0, 1));
			Assert.Equal(1, s.Pack());
			Assert.Equal(0f, s.PackedColors[3]);
		}

		[Fact]
		public void AFullSurfaceHasNoTerminator()
		{
			Vector3D[][] lights = new Vector3D[70][];
			for(int i = 0; i < lights.Length; i++) lights[i] = Box(i, 0, 0, 500);
			SurfaceLights s = WithLights(lights);
			s.Select(Box(0, 0, 0, 1));
			Assert.Equal(SurfaceLights.MAX_PER_SURFACE, s.SelectedCount);
			Assert.Equal(SurfaceLights.MAX_PER_SURFACE, s.Pack());
			Assert.Equal(63, s.Selected(63));
		}
	}
}
