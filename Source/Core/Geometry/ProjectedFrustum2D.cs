
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;

#endregion

namespace CodeImp.DoomBuilder.Geometry
{
	public class ProjectedFrustum2D
	{
		#region ================== Variables
		
		// Frustum settings
		private readonly float near;
		private readonly float far;
		private readonly float fov;
		private readonly float fovy;
		private readonly Vector2D pos;
		private readonly float xyangle;
		private readonly float zangle;
		
		// Frustum lines
		private readonly Line2D[] lines;

		// Circle
		private readonly Vector2D center;
		private readonly float radius;
		
		#endregion

		#region ================== Properties

		public float Near { get { return near; } }
		public float Far { get { return far; } }
		public float Fov { get { return fov; } }
		public float FovY { get { return fovy; } }
		public Vector2D Position { get { return pos; } }
		public float XYAngle { get { return xyangle; } }
		public float ZAngle { get { return zangle; } }
		public Line2D[] Lines { get { return lines; } }
		public Vector2D Center { get { return center; } }
		public float Radius { get { return radius; } }

		#endregion

		#region ================== Constructor / Destructor

		// Constructor. fov and fovy are the full horizontal and vertical angles the screen shows, near and
		// far the depth range drawn; zangle is Vector3D.GetAngleZ of the view direction.
		public ProjectedFrustum2D(Vector2D pos, float xyangle, float zangle, float near, float far, float fov, float fovy)
		{
			// Initialize
			this.pos = pos;
			this.xyangle = xyangle;
			this.zangle = zangle;
			this.near = near;
			this.far = far;
			this.fov = fov;
			this.fovy = fovy;

			// The 2D shadow of the 3D view frustum is the convex hull of its 8 corners projected onto the map
			double pitchcos = -Math.Cos(zangle); // pitch = zangle - PI, positive when looking down
			double pitchsin = -Math.Sin(zangle);
			double tanx = Math.Tan(fov * 0.5);
			double tany = Math.Tan(fovy * 0.5);
			Vector2D forward = Vector2D.FromAngle(xyangle);
			Vector2D right = forward.GetPerpendicular();
			Vector2D[] corners = new Vector2D[8];
			int c = 0;
			foreach(double depth in new double[] { near, far })
				for(int sy = -1; sy <= 1; sy += 2)
					for(int sx = -1; sx <= 1; sx += 2)
						corners[c++] = pos + forward * (depth * (pitchcos + sy * tany * pitchsin)) + right * (depth * sx * tanx);

			// Make the frustum lines, which all have the inside on their positive GetSideOfLine side
			Vector2D[] hull = ConvexHull(corners);
			lines = new Line2D[hull.Length];
			for(int i = 0; i < hull.Length; i++)
				lines[i] = new Line2D(hull[i], hull[(i + 1) % hull.Length]);

			// Calculate the circle center
			center = new Vector2D();
			foreach(Vector2D v in hull) center += v;
			center = center * (1.0 / hull.Length);

			// Calculate the radius from the center to the farthest corner
			float radius2 = 0.0f;
			for(int i = 0; i < hull.Length; i++)
			{
				float distance2 = (float)Vector2D.DistanceSq(center, hull[i]);
				if(distance2 > radius2) radius2 = distance2;
			}
			radius = (float)Math.Sqrt(radius2);
		}

		// Andrew's monotone chain, counterclockwise, collinear points dropped
		private static Vector2D[] ConvexHull(Vector2D[] points)
		{
			Vector2D[] sorted = (Vector2D[])points.Clone();
			Array.Sort(sorted, (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
			Vector2D[] hull = new Vector2D[sorted.Length * 2];
			int k = 0;
			for(int i = 0; i < sorted.Length; i++)
			{
				while(k >= 2 && Line2D.GetSideOfLine(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
				hull[k++] = sorted[i];
			}
			for(int i = sorted.Length - 2, lower = k + 1; i >= 0; i--)
			{
				while(k >= lower && Line2D.GetSideOfLine(hull[k - 2], hull[k - 1], sorted[i]) <= 0) k--;
				hull[k++] = sorted[i];
			}
			Array.Resize(ref hull, k - 1);
			return hull;
		}

		#endregion

		#region ================== Methods

		// This checks if a specified circle is intersecting the frustum
		// NOTE: This checks only against the actual frustum and does not use the frustum circle!
		public bool IntersectCircle(Vector2D circlecenter, float circleradius)
		{
			// Go for all frustum lines
			for(int i = 0; i < lines.Length; i++)
			{
				// Check on which side the circle center lies
				if(lines[i].GetSideOfLine(circlecenter) < 0)
				{
					// Center is outside the frustum
					// If the circle is not overlapping, it is not intersecting.
					if(lines[i].GetDistanceToLineSq(circlecenter, false) > (circleradius * circleradius)) return false;
				}
			}

			// Intersecting!
			return true;
		}

		#endregion
	}
}
