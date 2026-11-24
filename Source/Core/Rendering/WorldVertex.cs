
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

using CodeImp.DoomBuilder.Geometry;
using System.Runtime.InteropServices;

#endregion

namespace CodeImp.DoomBuilder.Rendering
{
    // WorldVertex
    [StructLayout(LayoutKind.Sequential)]
    public struct WorldVertex
	{
		// Vertex format
		public const int Stride = 40; //mxd: 9 * 4, was 6 * 4; plus the 4-byte light

		// Members
		public float x;
		public float y;
		public float z;
		public int c;
		public float u;
		public float v;

		//mxd
		public float nx;
		public float ny;
		public float nz;

		// Sector light for the shader: level, fake contrast + 128, black-fog level, 255 when set (0: c is final)
		public byte light;
		public byte rel;
		public byte foglight;
		public byte lit;

		// Constructor
		public WorldVertex(float x, float y, float z, int c, float u, float v)
		{
			this.x = x;
			this.y = y;
			this.z = z;
			this.c = c;
			this.u = u;
			this.v = v;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;

			this.light = this.rel = this.foglight = this.lit = 0;
		}

		// Constructor
		/*public WorldVertex(float x, float y, float z, int c, Vector2D t)
		{
			this.x = x;
			this.y = y;
			this.z = z;
			this.c = c;
			this.u = t.x;
			this.v = t.y;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;
		}*/

		// Constructor
		/*public WorldVertex(Vector3D p, int c, float u, float v)
		{
			this.x = p.x;
			this.y = p.y;
			this.z = p.z;
			this.c = c;
			this.u = u;
			this.v = v;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;
		}*/

		// Constructor
		public WorldVertex(Vector3D p, int c, Vector2D t)
		{
			this.x = (float)p.x;
			this.y = (float)p.y;
			this.z = (float)p.z;
			this.c = c;
			this.u = (float)t.x;
			this.v = (float)t.y;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;

			this.light = this.rel = this.foglight = this.lit = 0;
		}

		// Constructor with a packed light (LightModel.PackLight)
		public WorldVertex(Vector3D p, int c, Vector2D t, int packedlight) : this(p, c, t)
		{
			SetLight(packedlight);
		}

		// Sets the light attribute from a packed light (LightModel.PackLight), 0 for "c is final"
		public void SetLight(int packed)
		{
			light = (byte)packed;
			rel = (byte)(packed >> 8);
			foglight = (byte)(packed >> 16);
			lit = (byte)(packed >> 24);
		}

		// The packed light, as SetLight takes it
		public int PackedLight { get { return light | (rel << 8) | (foglight << 16) | (lit << 24); } }

		// Constructor
		/*public WorldVertex(float x, float y, float z, float u, float v)
		{
			this.x = x;
			this.y = y;
			this.z = z;
			this.c = -1;
			this.u = u;
			this.v = v;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;
		}*/

		// Constructor
		/*public WorldVertex(float x, float y, float z, Vector2D t)
		{
			this.x = x;
			this.y = y;
			this.z = z;
			this.c = -1;
			this.u = t.x;
			this.v = t.y;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;
		}*/

		// Constructor
		/*public WorldVertex(Vector3D p, float u, float v)
		{
			this.x = p.x;
			this.y = p.y;
			this.z = p.z;
			this.c = -1;
			this.u = u;
			this.v = v;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;
		}*/

		// Constructor
		/*public WorldVertex(Vector3D p, Vector2D t)
		{
			this.x = p.x;
			this.y = p.y;
			this.z = p.z;
			this.c = -1;
			this.u = t.x;
			this.v = t.y;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;
		}*/

		// Constructor
		/*public WorldVertex(float x, float y, float z, int c)
		{
			this.x = x;
			this.y = y;
			this.z = z;
			this.c = c;
			this.u = 0.0f;
			this.v = 0.0f;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;
		}*/

		// Constructor
		/*public WorldVertex(Vector3D p, int c)
		{
			this.x = p.x;
			this.y = p.y;
			this.z = p.z;
			this.c = c;
			this.u = 0.0f;
			this.v = 0.0f;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;
		}*/

		// Constructor
		public WorldVertex(float x, float y, float z)
		{
			this.x = x;
			this.y = y;
			this.z = z;
			this.c = -1;
			this.u = 0.0f;
			this.v = 0.0f;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;

			this.light = this.rel = this.foglight = this.lit = 0;
		}

		// Constructor
		public WorldVertex(Vector3D p)
		{
			this.x = (float)p.x;
			this.y = (float)p.y;
			this.z = (float)p.z;
			this.c = -1;
			this.u = 0.0f;
			this.v = 0.0f;

			//mxd
			this.nx = 0.0f;
			this.ny = 0.0f;
			this.nz = 0.0f;

			this.light = this.rel = this.foglight = this.lit = 0;
		}
	}
}
