using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Rendering;
using Xunit;

namespace Builder.Tests
{
	public class ProjectedFrustum2DTests
	{
		private const float FAR = 3000f;

		// Built as Renderer3D.CreateProjection does, from the FOV setting and the view's aspect
		private static Matrix Projection(double visualfovdeg, double aspect)
		{
			double fovsetting = visualfovdeg * Math.PI / 180.0;
			float fovy = (float)Math.Atan(Math.Tan(fovsetting / 2.0) / aspect);
			return Matrix.PerspectiveFov(fovy, (float)aspect, 1f, FAR);
		}

		// What the shaders do with an eye-space point (camera looking down -z): row vector times matrix, then the GL clip box
		private static bool Visible(Matrix p, double x, double y, double z)
		{
			double cx = x * p.M11 + y * p.M21 + z * p.M31 + p.M41;
			double cy = x * p.M12 + y * p.M22 + z * p.M32 + p.M42;
			double cz = x * p.M13 + y * p.M23 + z * p.M33 + p.M43;
			double cw = x * p.M14 + y * p.M24 + z * p.M34 + p.M44;
			return cw > 0 && Math.Abs(cx) <= cw && Math.Abs(cy) <= cw && Math.Abs(cz) <= cw;
		}

		// Largest t in [lo, hi] with inside(t), for an inside region that is an interval starting at lo
		private static double Bisect(Func<double, bool> inside, double lo, double hi)
		{
			for(int i = 0; i < 100; i++)
			{
				double mid = (lo + hi) / 2;
				if(inside(mid)) lo = mid; else hi = mid;
			}
			return lo;
		}

		private static Vector3D Direction(double yaw, double pitchdown)
		{
			return new Vector3D(Math.Cos(yaw) * Math.Cos(pitchdown), Math.Sin(yaw) * Math.Cos(pitchdown), -Math.Sin(pitchdown));
		}

		// Built the way Renderer3D.PositionAndLookAt does, from a look direction
		private static ProjectedFrustum2D Make(Matrix p, Vector3D pos, Vector3D dir)
		{
			Renderer3D.GetRenderedFrustum(p, out float fov, out float fovy, out float near);
			return new ProjectedFrustum2D(new Vector2D(pos.x, pos.y), (float)dir.GetAngleXY(), (float)dir.GetAngleZ(), near, FAR, fov, fovy);
		}

		private static double WorstOutside(ProjectedFrustum2D f, Vector2D p)
		{
			double worst = 0;
			foreach(Line2D l in f.Lines)
			{
				double s = l.GetSideOfLine(p);
				if(s < 0) worst = Math.Max(worst, -s / l.GetLength());
			}
			return worst;
		}

		[Fact]
		public void ContainsEverythingTheProjectionRenders()
		{
			List<string> failures = new List<string>();
			Vector3D pos = new Vector3D(123.5, -456.25, 40);
			foreach(double visualfov in new[] { 60.0, 90.0, 105.0, 140.0 })
			foreach(double aspect in new[] { 0.6, 1.6, 2.39 })
			{
				Matrix p = Projection(visualfov, aspect);
				double tx = Bisect(t => Visible(p, t, 0, -1), 0, 100) * (1 - 1e-9);
				double ty = Bisect(t => Visible(p, 0, t, -1), 0, 100) * (1 - 1e-9);
				double near = 1.0 / Bisect(t => Visible(p, 0, 0, -1.0 / t), 0, 100) * (1 + 1e-9);
				double[] depths = { near, near * 2, 1, 10, 100, FAR / 2, FAR };

				for(int yawdeg = -180; yawdeg < 180; yawdeg += 37)
				for(int pitchdeg = -88; pitchdeg <= 88; pitchdeg += 4)
				{
					Vector3D dir = Direction(yawdeg * Math.PI / 180.0, pitchdeg * Math.PI / 180.0);
					ProjectedFrustum2D f = Make(p, pos, dir);

					// Camera basis as Matrix.LookAt builds it, with world up +z
					Vector3D right = Vector3D.CrossProduct(dir, new Vector3D(0, 0, 1)).GetNormal();
					Vector3D up = Vector3D.CrossProduct(right, dir);
					double worst = 0;
					foreach(double d in depths)
					for(int ix = -2; ix <= 2; ix++)
					for(int iy = -2; iy <= 2; iy++)
					{
						double x = tx * d * ix / 2.0, y = ty * d * iy / 2.0;
						if(!Visible(p, x, y, -d)) { failures.Add("sample not visible: " + x + ", " + y + ", " + d); continue; }
						Vector3D w = pos + dir * d + right * x + up * y;
						worst = Math.Max(worst, WorstOutside(f, new Vector2D(w.x, w.y)));
					}

					// The angles go in as floats, worth about 0.001 units at the frustum's far corners
					if(worst > 0.01)
						failures.Add("fov " + visualfov + " aspect " + aspect + " yaw " + yawdeg + " pitch " + pitchdeg + ": " + worst.ToString("0.###") + " units outside");
				}
			}
			Assert.True(failures.Count == 0, failures.Count + " views cull on-screen points:\n" + string.Join("\n", failures));
		}

		[Fact]
		public void LevelViewIsNoWiderThanRendered()
		{
			Matrix p = Projection(90, 1.6);
			double edge = Bisect(t => Visible(p, t, 0, -1), 0, 100) * 1000;
			Vector3D dir = Direction(0.7, 0);
			ProjectedFrustum2D f = Make(p, new Vector3D(0, 0, 0), dir);
			Vector2D forward = new Vector2D(dir.x, dir.y);
			Vector2D side = new Vector2D(-dir.y, dir.x);
			foreach(int s in new[] { -1, 1 })
			{
				Assert.True(WorstOutside(f, forward * 1000 + side * (s * edge * 0.99)) == 0, "just inside the edge is culled");
				Assert.True(WorstOutside(f, forward * 1000 + side * (s * edge * 1.01)) > 0, "just outside the edge is kept");
			}
			Assert.True(WorstOutside(f, forward * -10) > 0, "behind the camera is kept");
		}
	}
}
