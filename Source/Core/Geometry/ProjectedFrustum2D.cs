
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
using System.Collections.Generic;

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
		// far the depth range drawn; zangle is Vector3D.GetAngleZ of the view direction. The part of the view
		// below minz is left out, since nothing is rendered there.
		public ProjectedFrustum2D(Vector3D pos, float xyangle, float zangle, float near, float far, float fov, float fovy, double minz)
		{
			// Initialize
			this.pos = new Vector2D(pos.x, pos.y);
			this.xyangle = xyangle;
			this.zangle = zangle;
			this.near = near;
			this.far = far;
			this.fov = fov;
			this.fovy = fovy;

			// The 3D view frustum's 8 corners, indexed depth * 4 + y * 2 + x
			double pitchcos = -Math.Cos(zangle); // pitch = zangle - PI, positive when looking down
			double pitchsin = -Math.Sin(zangle);
			double tanx = Math.Tan(fov * 0.5);
			double tany = Math.Tan(fovy * 0.5);
			Vector2D forward = Vector2D.FromAngle(xyangle);
			Vector2D right = forward.GetPerpendicular();
			Vector3D[] corners = new Vector3D[8];
			int c = 0;
			foreach(double depth in new double[] { near, far })
				for(int sy = -1; sy <= 1; sy += 2)
					for(int sx = -1; sx <= 1; sx += 2)
					{
						Vector2D xy = this.pos + forward * (depth * (pitchcos + sy * tany * pitchsin)) + right * (depth * sx * tanx);
						corners[c++] = new Vector3D(xy, pos.z + depth * (sy * tany * pitchcos - pitchsin));
					}

			// Its 2D shadow above minz is the convex hull of the corners above minz and the points where edges cross it
			int[,] edges = { { 0, 1 }, { 2, 3 }, { 0, 2 }, { 1, 3 }, { 4, 5 }, { 6, 7 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
			List<Vector2D> points = new List<Vector2D>(12);
			foreach(Vector3D v in corners)
				if(v.z >= minz) points.Add(new Vector2D(v.x, v.y));
			for(int i = 0; i < edges.GetLength(0); i++)
			{
				Vector3D a = corners[edges[i, 0]], b = corners[edges[i, 1]];
				if((a.z < minz) != (b.z < minz))
				{
					double t = (minz - a.z) / (b.z - a.z);
					points.Add(new Vector2D(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t));
				}
			}

			// Make the frustum lines, which all have the inside on their positive GetSideOfLine side.
			// A view entirely below minz, or only touching it, shows nothing; it keeps the whole shadow so culling still has an area.
			Vector2D[] hull = ConvexHull(points.ToArray());
			if(hull.Length < 3)
			{
				points.Clear();
				foreach(Vector3D v in corners) points.Add(new Vector2D(v.x, v.y));
				hull = ConvexHull(points.ToArray());
			}
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
			if(points.Length == 0) return points;
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
