using CodeImp.DoomBuilder.Map;

namespace Builder.Tests
{
	public class MapFormatConversionTests
	{
		// UDMF -> Hexen used to build this with &= from 0, so it always wrote 0
		[Fact]
		public void FlagsToArgBits_SetsEachFlagsBit()
		{
			Assert.Equal(0, Linedef.FlagsToArgBits(new Dictionary<string, bool>()));
			Assert.Equal(1 | 8 | 128, Linedef.FlagsToArgBits(new Dictionary<string, bool> { { "zoneboundary", true }, { "clipmidtex", true }, { "firstsideonly", true }, { "jumpover", false } }));
			for(int i = 0; i < Linedef.IdentificationArgFlags.Length; i++)
				Assert.Equal(1 << i, Linedef.FlagsToArgBits(new Dictionary<string, bool> { { Linedef.IdentificationArgFlags[i], true } }));
		}
	}
}
