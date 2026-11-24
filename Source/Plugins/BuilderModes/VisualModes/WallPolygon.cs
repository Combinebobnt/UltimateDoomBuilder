#region === Copyright (c) 2010 Pascal van der Heiden ===

using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	internal class WallPolygon : List<Vector3D>
	{
		// The light color that the wall should have
		public int color;

		// The wall's packed light (LightModel.PackLight), 0 when color is final
		public int light;
		
		// Constructors
		public WallPolygon() { }
		public WallPolygon(int capacity) : base(capacity) { }
		
		// This copies all the wall properties
		public void CopyProperties(WallPolygon target)
		{
			target.color = this.color;
			target.light = this.light;
		}
	}
}
