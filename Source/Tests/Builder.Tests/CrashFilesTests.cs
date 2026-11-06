using CodeImp.DoomBuilder;
using System.Runtime.InteropServices;

namespace Builder.Tests
{
	// The files a crash leaves behind: the rotated log and the map backup
	public class CrashFilesTests : IDisposable
	{
		private readonly string dir = Path.Combine(Path.GetTempPath(), "udb-crashfiles-" + Guid.NewGuid().ToString("N"));

		public CrashFilesTests()
		{
			Directory.CreateDirectory(dir);
		}

		public void Dispose()
		{
			Directory.Delete(dir, true);
		}

		[Fact]
		public void RotateLogFileWithNoPreviousLogCreatesNothing()
		{
			string log = Path.Combine(dir, "UDBuilder.log");

			General.RotateLogFile(log);

			Assert.False(File.Exists(log));
			Assert.False(File.Exists(log + ".1"));
		}

		[Fact]
		public void RotateLogFileKeepsThePreviousLogAsGenerationOne()
		{
			string log = Path.Combine(dir, "UDBuilder.log");
			File.WriteAllText(log, "previous run");

			General.RotateLogFile(log);

			Assert.False(File.Exists(log));
			Assert.Equal("previous run", File.ReadAllText(log + ".1"));
		}

		[Fact]
		public void RotateLogFileReplacesAnOlderGeneration()
		{
			string log = Path.Combine(dir, "UDBuilder.log");
			File.WriteAllText(log, "previous run");
			File.WriteAllText(log + ".1", "older run");

			General.RotateLogFile(log);

			Assert.False(File.Exists(log));
			Assert.Equal("previous run", File.ReadAllText(log + ".1"));
		}

		[Fact]
		public void WriteBackupFileCreatesANewBackupWithNoTempLeft()
		{
			string backup = Path.Combine(dir, "map.123.restore");

			MapManager.WriteBackupFile(backup, new byte[] { 1, 2, 3 });

			Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(backup));
			Assert.False(File.Exists(backup + ".tmp"));
		}

		[Fact]
		public void WriteBackupFileReplacesAnExistingBackupWithNoTempLeft()
		{
			string backup = Path.Combine(dir, "map.123.restore");
			File.WriteAllBytes(backup, new byte[] { 9, 9, 9, 9, 9 });

			MapManager.WriteBackupFile(backup, new byte[] { 4, 5 });

			Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(backup));
			Assert.False(File.Exists(backup + ".tmp"));
		}

		// A crash backup that fails halfway must not cost the user the previous one
		[Fact]
		public void WriteBackupFileThatFailsKeepsThePreviousBackup()
		{
			if(Environment.OSVersion.Platform != PlatformID.Unix) return;
			string backup = Path.Combine(dir, "map.123.restore");
			byte[] previous = new byte[100];
			for(int i = 0; i < previous.Length; i++) previous[i] = (byte)i;
			File.WriteAllBytes(backup, previous);

			bool threw = false;
			WithFileSizeLimit(4096, () =>
			{
				try { MapManager.WriteBackupFile(backup, new byte[8192]); }
				catch(IOException) { threw = true; }
			});

			Assert.True(threw);
			Assert.Equal(previous, File.ReadAllBytes(backup));
		}

		private const int RLIMIT_FSIZE = 1;
		private const int SIGXFSZ = 25;
		private static readonly IntPtr SIG_IGN = new IntPtr(1);

		[StructLayout(LayoutKind.Sequential)]
		private struct RLimit
		{
			public ulong Current;
			public ulong Maximum;
		}

		[DllImport("libc", SetLastError = true)] private static extern int getrlimit(int resource, out RLimit limit);
		[DllImport("libc", SetLastError = true)] private static extern int setrlimit(int resource, ref RLimit limit);
		[DllImport("libc")] private static extern IntPtr signal(int signum, IntPtr handler);

		private static void WithFileSizeLimit(long bytes, Action action)
		{
			// Past RLIMIT_FSIZE a write fails with EFBIG once SIGXFSZ is ignored
			RLimit saved;
			Assert.Equal(0, getrlimit(RLIMIT_FSIZE, out saved));
			IntPtr oldhandler = signal(SIGXFSZ, SIG_IGN);
			RLimit limited = new RLimit { Current = (ulong)bytes, Maximum = saved.Maximum };
			Assert.Equal(0, setrlimit(RLIMIT_FSIZE, ref limited));
			try { action(); }
			finally
			{
				setrlimit(RLIMIT_FSIZE, ref saved);
				signal(SIGXFSZ, oldhandler);
			}
		}
	}
}
