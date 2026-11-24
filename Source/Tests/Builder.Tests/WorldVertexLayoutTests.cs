using System.Runtime.InteropServices;
using CodeImp.DoomBuilder.Rendering;

namespace Builder.Tests
{
	public class WorldVertexLayoutTests
	{
		// The native vertex array (GLVertexBuffer.cpp SetupWorldVAO) reads these offsets with this stride
		[Fact]
		public void LayoutMatchesTheNativeAttributeOffsets()
		{
			Assert.Equal(WorldVertex.Stride, Marshal.SizeOf(typeof(WorldVertex)));
			Assert.Equal(16, (int)Marshal.OffsetOf(typeof(WorldVertex), "u"));
			Assert.Equal(24, (int)Marshal.OffsetOf(typeof(WorldVertex), "nx"));
			Assert.Equal(36, (int)Marshal.OffsetOf(typeof(WorldVertex), "light"));
			Assert.Equal(40, (int)Marshal.OffsetOf(typeof(WorldVertex), "lu"));
		}

		[Fact]
		public void ConstructedVerticesCarryNoLightmap()
		{
			var vertices = new[]
			{
				new WorldVertex(1f, 2f, 3f, -1, 0.5f, 0.5f),
				new WorldVertex(new CodeImp.DoomBuilder.Geometry.Vector3D(1, 2, 3), -1, new CodeImp.DoomBuilder.Geometry.Vector2D(0.5, 0.5), 0x01020304),
				new WorldVertex(1f, 2f, 3f),
				new WorldVertex(new CodeImp.DoomBuilder.Geometry.Vector3D(1, 2, 3)),
			};
			foreach(WorldVertex v in vertices) Assert.Equal(0f, v.lw);

			WorldVertex lit = vertices[0];
			lit.SetLightmap(0.25f, 0.75f, 2);
			Assert.Equal(3f, lit.lw);
		}
	}
}
