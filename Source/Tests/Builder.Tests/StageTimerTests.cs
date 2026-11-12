using System.Globalization;
using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class StageTimerTests
	{
		[Fact]
		public void StageLineIncludesPrefixStageAndMilliseconds()
		{
			Assert.Equal("Reload timing: GC 123.457 ms", StageTimer.FormatLine("Reload", "GC", 123.4567));
		}

		[Fact]
		public void ReaderLinePreservesResourceNameAndType()
		{
			Assert.Equal("Load timing: reader \"pack.pk3\" (PK3Reader) 0.000 ms",
				StageTimer.FormatLine("Load", "reader \"pack.pk3\" (PK3Reader)", 0));
		}

		[Fact]
		public void DecimalFormattingDoesNotFollowTheCurrentCulture()
		{
			CultureInfo previous = CultureInfo.CurrentCulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
				Assert.Equal("Load timing: total 1234.568 ms", StageTimer.FormatLine("Load", "total", 1234.5678));
			}
			finally { CultureInfo.CurrentCulture = previous; }
		}
	}
}
