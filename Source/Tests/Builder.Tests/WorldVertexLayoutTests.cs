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
			Assert.Equal(44, (int)Marshal.OffsetOf(typeof(WorldVertex), "lv"));
			Assert.Equal(48, (int)Marshal.OffsetOf(typeof(WorldVertex), "lw"));
		}
	}
}
