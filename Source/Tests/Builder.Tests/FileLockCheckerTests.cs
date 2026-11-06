using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class FileLockCheckerTests
	{
		[Fact]
		public void UnwritableFileIsReportedNotThrown()
		{
			// A read-only file opens with the same UnauthorizedAccessException as a file on a read-only mount
			string path = Path.GetTempFileName();
			try
			{
				File.SetAttributes(path, FileAttributes.ReadOnly);

				FileLockChecker.FileLockCheckResult result = FileLockChecker.CheckFile(path);

				Assert.False(string.IsNullOrEmpty(result.Error), "no error reported");
				Assert.Empty(result.Processes);
			}
			finally
			{
				File.SetAttributes(path, FileAttributes.Normal);
				File.Delete(path);
			}
		}
	}
}
