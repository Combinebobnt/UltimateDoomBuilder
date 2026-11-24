#region ================== Namespaces

using System;

#endregion

namespace CodeImp.DoomBuilder.Geometry
{
	// Rotations and mirrors about an arbitrary map-space point, shared by tools that use the 2D cursor
	public static class PivotMath
	{
		#region ================== Points

		// Rotates p about pivot by angle (radians, counterclockwise). Whole quarter turns are exact.
		public static Vector2D RotatedAbout(Vector2D p, Vector2D pivot, double angle)
		{
			return pivot + Rotated(p - pivot, angle);
		}

		// Mirrors p across the vertical (x = pivot.x) or horizontal (y = pivot.y) line through pivot
		public static Vector2D MirroredAcross(Vector2D p, Vector2D pivot, bool vertical)
		{
			if(vertical) return new Vector2D(2 * pivot.x - p.x, p.y);
			return new Vector2D(p.x, 2 * pivot.y - p.y);
		}

		#endregion

		#region ================== Edit Selection boxes

		// Box state as Edit Selection mode keeps it: the box center is offset + size / 2 and the
		// contents are rotated by rotation about that center. Rotating about another point keeps the
		// size, adds delta to rotation and swings the center around the pivot.
		public static void RotateBoxAbout(ref Vector2D offset, Vector2D size, ref double rotation, Vector2D pivot, double delta)
		{
			Vector2D center = offset + size * 0.5;
			offset = RotatedAbout(center, pivot, delta) - size * 0.5;
			rotation += delta;
		}

		// A world-axis mirror of the box: the box's own local flip (which keeps its center), the
		// rotation negated, and the center mirrored across the line
		public static void MirrorBoxAcross(ref Vector2D offset, ref Vector2D size, ref double rotation, Vector2D pivot, bool vertical)
		{
			if(vertical)
			{
				offset.x += size.x;
				size.x = -size.x;
			}
			else
			{
				offset.y += size.y;
				size.y = -size.y;
			}

			rotation = -rotation;
			Vector2D center = offset + size * 0.5;
			offset = MirroredAcross(center, pivot, vertical) - size * 0.5;
		}

		#endregion

		#region ================== Private

		private static Vector2D Rotated(Vector2D v, double angle)
		{
			double turns = Math.Round(angle / Angle2D.PIHALF);
			if(Math.Abs(angle - turns * Angle2D.PIHALF) < 1e-12)
			{
				switch(((long)turns % 4 + 4) % 4)
				{
					case 0: return v;
					case 1: return new Vector2D(-v.y, v.x);
					case 2: return new Vector2D(-v.x, -v.y);
					default: return new Vector2D(v.y, -v.x);
				}
			}

			return v.GetRotated(angle);
		}

		#endregion
	}
}
