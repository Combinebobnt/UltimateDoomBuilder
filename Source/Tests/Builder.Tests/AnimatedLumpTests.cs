using System.Text;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	public class AnimatedLumpTests
	{
		[Fact]
		public void BinaryOffsetsTerminatorAndSwirlSpeedArePreserved()
		{
			var data = new byte[47];
			data[0] = 1;
			Encoding.ASCII.GetBytes("LAST").CopyTo(data, 1);
			Encoding.ASCII.GetBytes("FIRST").CopyTo(data, 10);
			data[19] = 1; data[21] = 1;
			data[23] = 255;
			var entries = AnimatedLump.Parse(data);
			Assert.Equal(1, entries.Count);
			Assert.True(entries[0].IsTexture);
			Assert.Equal("FIRST", entries[0].First);
			Assert.Equal("LAST", entries[0].Last);
			Assert.Equal(65537u, entries[0].Tics);
		}

		[Fact]
		public void TruncatedEntryWarnsWithoutThrowing()
		{
			int warnings = 0;
			Assert.Equal(0, AnimatedLump.Parse(new byte[22], message => warnings++).Count);
			Assert.Equal(1, warnings);
			Assert.Equal(0, AnimatedLump.Parse(new byte[] { 255 }).Count);
		}

		[Theory]
		[InlineData("zandronum", 63)]
		[InlineData("doom", 22)]
		[InlineData("heretic", 8)]
		[InlineData("strife", 33)]
		public void DefaultsAreEmbeddedInTheBuiltAssembly(string table, int count)
		{
			Assert.Equal(count, AnimatedLump.LoadDefaults(table).Count);
		}
	}
}
