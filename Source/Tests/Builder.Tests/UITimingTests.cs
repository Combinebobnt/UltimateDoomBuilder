using System.Globalization;
using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class UITimingTests
	{
		[Fact]
		public void PhaseLineHasJoinableInstanceAndElapsedTimes()
		{
			Assert.Equal("UITIMING SectorEditFormUDMF#12 fieldslist-fixed 123.456 20.125 sessionms=987.654",
				UITiming.FormatLine("SectorEditFormUDMF#12", "fieldslist-fixed", 123.456, 20.125, 987.654));
		}

		[Fact]
		public void RequestAndCtorHaveExplicitZeroOrigin()
		{
			Assert.Equal("UITIMING PreferencesForm#1 request 0.000 0.000 sessionms=10.000",
				UITiming.FormatLine("PreferencesForm#1", "request", 0, 0, 10));
			Assert.Equal("UITIMING PreferencesForm#1 ctor 0.000 2.500 sessionms=12.500",
				UITiming.FormatLine("PreferencesForm#1", "ctor", 0, 2.5, 12.5));
		}

		[Fact]
		public void DecimalFormattingDoesNotFollowTheCurrentCulture()
		{
			CultureInfo previous = CultureInfo.CurrentCulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
				Assert.Equal("UITIMING TextureBrowserForm#7 usable 1234.568 0.125 sessionms=5678.750",
					UITiming.FormatLine("TextureBrowserForm#7", "usable", 1234.5678, 0.125, 5678.75));
			}
			finally { CultureInfo.CurrentCulture = previous; }
		}
	}
}
