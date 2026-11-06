using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class ExceptionReportTests
	{
		// Both constructors call this, so a throw here turns any unhandled exception into a hard exit
		[Fact]
		public void GetSystemInfoReportsOsAndBuildWithoutStartup()
		{
			string info = ExceptionReport.GetSystemInfo();

			Assert.Contains("OS: ", info);
			Assert.Contains("UDB: R", info);
			Assert.Contains("Platform: ", info);
		}

		// No startup means no error logger or main window, as in a crash before MainForm exists
		[Fact]
		public void GetExceptionDescriptionWorksWithoutStartup()
		{
			string description = ExceptionReport.GetExceptionDescription(new InvalidOperationException("t012 probe"));

			Assert.Contains("t012 probe", description);
		}

		// The settings path doesn't exist early in startup, and a throw here also loses the dialog
		[Fact]
		public void WriteCrashDumpReportsFailureInsteadOfThrowing()
		{
			Assert.Null(ExceptionReport.WriteCrashDump("t012 probe"));
		}
	}
}
