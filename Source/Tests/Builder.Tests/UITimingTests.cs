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
		public void RequestAndUsableCarryTrailingProcessingState()
		{
			Assert.Equal("proc=0 pc=1 sd=2", UITiming.FormatProcessing(false, 1, 2));
			Assert.Equal("proc=1 pc=3 sd=0", UITiming.FormatProcessing(true, 3, 0));
			Assert.Equal("UITIMING LinedefEditFormUDMF#4 request 0.000 0.000 sessionms=10.000 proc=1 pc=1 sd=0",
				UITiming.FormatLine("LinedefEditFormUDMF#4", "request", 0, 0, 10, UITiming.FormatProcessing(true, 1, 0)));
			Assert.Equal("UITIMING LinedefEditFormUDMF#4 usable 70.825 1.500 sessionms=80.825 proc=0 pc=1 sd=1",
				UITiming.FormatLine("LinedefEditFormUDMF#4", "usable", 70.825, 1.5, 80.825, "proc=0 pc=1 sd=1"));
		}

		[Fact]
		public void NoProcessingStateLeavesTheLineUnchanged()
		{
			Assert.Equal("UITIMING PreferencesForm#1 ctor 0.000 2.500 sessionms=12.500",
				UITiming.FormatLine("PreferencesForm#1", "ctor", 0, 2.5, 12.5, null));
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
