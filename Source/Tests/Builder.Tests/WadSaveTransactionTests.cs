using CodeImp.DoomBuilder.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Builder.Tests
{
	public class WadSaveTransactionTests : IDisposable
	{
		private const int RLIMIT_FSIZE = 1;
		private const int SIGXFSZ = 25;
		private static readonly IntPtr SIG_IGN = new IntPtr(1);

		[StructLayout(LayoutKind.Sequential)]
		private struct RLimit
		{
			public ulong Current;
			public ulong Maximum;
		}

		[DllImport("libc", SetLastError = true)] private static extern int chmod(string path, uint mode);
		[DllImport("libc")] private static extern uint umask(uint mask);
		[DllImport("libc", SetLastError = true)] private static extern int symlink(string target, string linkpath);
		[DllImport("libc", SetLastError = true)] private static extern int getrlimit(int resource, out RLimit limit);
		[DllImport("libc", SetLastError = true)] private static extern int setrlimit(int resource, ref RLimit limit);
		[DllImport("libc")] private static extern IntPtr signal(int signum, IntPtr handler);

		private readonly string dir = Path.Combine(Path.GetTempPath(), "udb-wadsave-" + Guid.NewGuid().ToString("N"));

		public WadSaveTransactionTests()
		{
			Directory.CreateDirectory(dir);
		}

		public void Dispose()
		{
			if(IsUnix) chmod(dir, Convert.ToUInt32("755", 8));
			Directory.Delete(dir, true);
		}

		private static bool IsUnix { get { return Environment.OSVersion.Platform == PlatformID.Unix; } }

		private static byte[] Data(string text, int length)
		{
			byte[] data = new byte[length];
			byte[] pattern = Encoding.ASCII.GetBytes(text);
			for(int i = 0; i < length; i++) data[i] = pattern[i % pattern.Length];
			return data;
		}

		private static void WriteWad(string path, bool iwad, bool directoryfirst, string[] names, byte[][] datas)
		{
			int datalength = datas.Sum(d => d.Length);
			int dataoffset = directoryfirst ? 12 + 16 * names.Length : 12;
			int directoryoffset = directoryfirst ? 12 : 12 + datalength;

			using(FileStream file = new FileStream(path, FileMode.Create))
			using(BinaryWriter writer = new BinaryWriter(file))
			{
				writer.Write(Encoding.ASCII.GetBytes(iwad ? "IWAD" : "PWAD"));
				writer.Write(names.Length);
				writer.Write(directoryoffset);

				byte[] directory = new byte[16 * names.Length];
				int offset = dataoffset;
				for(int i = 0; i < names.Length; i++)
				{
					BitConverter.GetBytes(offset).CopyTo(directory, i * 16);
					BitConverter.GetBytes(datas[i].Length).CopyTo(directory, i * 16 + 4);
					Encoding.ASCII.GetBytes(names[i]).CopyTo(directory, i * 16 + 8);
					offset += datas[i].Length;
				}

				if(directoryfirst) writer.Write(directory);
				foreach(byte[] data in datas) writer.Write(data);
				if(!directoryfirst) writer.Write(directory);
			}
		}

		private string NewTarget(string name = "map.wad")
		{
			string path = Path.Combine(dir, name);
			WriteWad(path, false, false, new[] { "MAP01", "THINGS", "TEXTURE1" }, new[] { new byte[0], Data("things", 300), Data("tex", 5000) });
			return path;
		}

		private static string Kind(string path)
		{
			byte[] header = new byte[4];
			using(FileStream file = File.OpenRead(path)) file.Read(header, 0, 4);
			return Encoding.ASCII.GetString(header);
		}

		private static Dictionary<string, byte[]> ReadLumps(string path, List<string> order = null)
		{
			byte[] wad = File.ReadAllBytes(path);
			int count = BitConverter.ToInt32(wad, 4);
			int directory = BitConverter.ToInt32(wad, 8);
			Dictionary<string, byte[]> lumps = new Dictionary<string, byte[]>();
			for(int i = 0; i < count; i++)
			{
				int offset = BitConverter.ToInt32(wad, directory + i * 16);
				int length = BitConverter.ToInt32(wad, directory + i * 16 + 4);
				string name = Encoding.ASCII.GetString(wad, directory + i * 16 + 8, 8).TrimEnd('\0');
				byte[] data = new byte[length];
				Array.Copy(wad, offset, data, 0, length);
				lumps[name] = data;
				if(order != null) order.Add(name);
			}
			return lumps;
		}

		private static void AddLump(WadSaveTransaction transaction, string name, byte[] data)
		{
			Lump lump = transaction.Wad.Insert(name, transaction.Wad.Lumps.Count, data.Length, false);
			lump.Stream.Write(data, 0, data.Length);
			transaction.Wad.WriteHeaders();
		}

		private static void Save(string target, string basepath, string name, byte[] data)
		{
			using(WadSaveTransaction transaction = new WadSaveTransaction(target, basepath))
			{
				AddLump(transaction, name, data);
				transaction.Seal();
				transaction.Swap();
			}
		}

		private static string Stat(string format, string path)
		{
			ProcessStartInfo info = new ProcessStartInfo("stat", "-c " + format + " \"" + path + "\"") { RedirectStandardOutput = true, UseShellExecute = false };
			using(Process process = Process.Start(info))
			{
				string output = process.StandardOutput.ReadToEnd().Trim();
				process.WaitForExit();
				return output;
			}
		}

		private static string[] Leftovers(string directory)
		{
			return Directory.GetFiles(directory, "*" + WadSaveTransaction.SIBLING_SUFFIX);
		}

		private static void WithFileSizeLimit(long bytes, Action action)
		{
			// Past RLIMIT_FSIZE a write fails with EFBIG once SIGXFSZ is ignored, a real failure in-process
			RLimit saved;
			Assert.Equal(0, getrlimit(RLIMIT_FSIZE, out saved));
			IntPtr oldhandler = signal(SIGXFSZ, SIG_IGN);
			RLimit limited = new RLimit { Current = (ulong)bytes, Maximum = saved.Maximum };
			Assert.Equal(0, setrlimit(RLIMIT_FSIZE, ref limited));
			try
			{
				action();
			}
			finally
			{
				setrlimit(RLIMIT_FSIZE, ref saved);
				signal(SIGXFSZ, oldhandler);
			}
		}

		[Fact]
		public void SealAndSwapReplacesTheTarget()
		{
			if(!IsUnix) return;
			string target = NewTarget();

			Save(target, target, "NEWLUMP", Data("new", 1234));

			List<string> order = new List<string>();
			Dictionary<string, byte[]> lumps = ReadLumps(target, order);
			Assert.Equal(new[] { "MAP01", "THINGS", "TEXTURE1", "NEWLUMP" }, order.ToArray());
			Assert.Equal(Data("tex", 5000), lumps["TEXTURE1"]);
			Assert.Equal(Data("new", 1234), lumps["NEWLUMP"]);
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void DisposeWithoutSealLeavesTheTargetAlone()
		{
			if(!IsUnix) return;
			string target = NewTarget();
			byte[] before = File.ReadAllBytes(target);

			using(WadSaveTransaction transaction = new WadSaveTransaction(target, target))
				AddLump(transaction, "NEWLUMP", Data("new", 1234));

			Assert.Equal(before, File.ReadAllBytes(target));
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void StaleSiblingIsReplaced()
		{
			if(!IsUnix) return;
			string target = NewTarget();
			// A negative lump count, so parsing it fails fast instead of allocating a huge directory
			byte[] junk = Data("JUNK", 64);
			BitConverter.GetBytes(-1).CopyTo(junk, 4);
			File.WriteAllBytes(target + WadSaveTransaction.SIBLING_SUFFIX, junk);

			Save(target, target, "NEWLUMP", Data("new", 10));

			Assert.Equal(4, ReadLumps(target).Count);
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void StaleTempFileIsLeftForTheUser()
		{
			if(!IsUnix) return;
			string target = NewTarget();
			string temp = target + ".temp";
			WriteWad(temp, false, false, new[] { "OLDMAP" }, new[] { Data("old", 777) });
			byte[] tempbefore = File.ReadAllBytes(temp);

			Save(target, target, "NEWLUMP", Data("new", 10));

			Assert.Equal(Data("new", 10), ReadLumps(target)["NEWLUMP"]);
			Assert.Equal(tempbefore, File.ReadAllBytes(temp));
		}

		[Fact]
		public void WriteFailureDuringTheBaseCopyChangesNothing()
		{
			if(!IsUnix) return;
			string target = Path.Combine(dir, "big.wad");
			WriteWad(target, false, false, new[] { "A", "B", "C" }, new[] { Data("a", 65536), Data("b", 65536), Data("c", 65536) });
			byte[] before = File.ReadAllBytes(target);

			WithFileSizeLimit(32768, () => Assert.ThrowsAny<IOException>(() => new WadSaveTransaction(target, target)));

			Assert.Equal(before, File.ReadAllBytes(target));
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void WriteFailureDuringALumpInsertChangesNothing()
		{
			if(!IsUnix) return;
			string target = NewTarget();
			byte[] before = File.ReadAllBytes(target);

			using(WadSaveTransaction transaction = new WadSaveTransaction(target, target))
			{
				long length = new FileInfo(transaction.SiblingPath).Length;
				WithFileSizeLimit(length, () => Assert.ThrowsAny<IOException>(() => AddLump(transaction, "NEWLUMP", Data("new", 65536))));
			}

			Assert.Equal(before, File.ReadAllBytes(target));
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void WriteFailureAtSealClosesTheSibling()
		{
			if(!IsUnix) return;
			string target = NewTarget();
			byte[] before = File.ReadAllBytes(target);

			WadSaveTransaction transaction = new WadSaveTransaction(target, null);
			WAD wad = transaction.Wad;
			Lump lump = wad.Insert("NEWLUMP", 0, 8192, false);
			long lumpoffset = lump.Offset;

			// Small enough to stay in the FileStream buffer until Seal flushes it
			lump.Stream.Write(Data("new", 100), 0, 100);

			// Dispose inside the limit too: a retried flush that succeeds would hide a leaked handle
			WithFileSizeLimit(lumpoffset, () =>
			{
				Assert.ThrowsAny<IOException>(() => transaction.Seal());
				transaction.Dispose();
				Assert.True(wad.IsDisposed, "the sibling WAD was left open");
				transaction.Dispose();
				wad.Dispose();
			});

			Assert.Equal(before, File.ReadAllBytes(target));
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void FailedMoveLeavesTheTargetAndNoSibling()
		{
			if(!IsUnix) return;
			string target = Path.Combine(dir, "taken.wad");
			Directory.CreateDirectory(target);
			File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");

			using(WadSaveTransaction transaction = new WadSaveTransaction(target, null))
			{
				AddLump(transaction, "NEWLUMP", Data("new", 10));
				transaction.Seal();
				Exception e = Assert.ThrowsAny<Exception>(() => transaction.Swap());
				Assert.True(e is IOException || e is UnauthorizedAccessException, e.ToString());
			}

			Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void FailedReplaceLeavesTheTargetAndNoSibling()
		{
			if(!IsUnix) return;
			string target = NewTarget();
			byte[] before = File.ReadAllBytes(target);

			using(WadSaveTransaction transaction = new WadSaveTransaction(target, target))
			{
				AddLump(transaction, "NEWLUMP", Data("new", 10));
				transaction.Seal();
				Assert.Equal(0, chmod(dir, Convert.ToUInt32("555", 8)));
				try
				{
					Exception e = Assert.ThrowsAny<Exception>(() => transaction.Swap());
					Assert.True(e is IOException || e is UnauthorizedAccessException, e.ToString());
				}
				finally
				{
					chmod(dir, Convert.ToUInt32("755", 8));
				}
			}

			Assert.Equal(before, File.ReadAllBytes(target));
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void TargetModeIsKept()
		{
			if(!IsUnix) return;
			uint oldmask = umask(Convert.ToUInt32("022", 8));
			try
			{
				string target = NewTarget();
				Assert.Equal(0, chmod(target, Convert.ToUInt32("640", 8)));

				Save(target, target, "NEWLUMP", Data("new", 10));

				Assert.Equal("640", Stat("%a", target));
			}
			finally
			{
				umask(oldmask);
			}
		}

		[Fact]
		public void MissingTargetIsCreated()
		{
			if(!IsUnix) return;
			string target = Path.Combine(dir, "fresh.wad");

			Save(target, null, "NEWLUMP", Data("new", 10));

			Assert.Equal("PWAD", Kind(target));
			Assert.Equal(Data("new", 10), ReadLumps(target)["NEWLUMP"]);
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void SymlinkedTargetStaysALink()
		{
			if(!IsUnix) return;
			string real = NewTarget("real.wad");
			string link = Path.Combine(dir, "link.wad");
			Assert.Equal(0, symlink(real, link));

			using(WadSaveTransaction transaction = new WadSaveTransaction(link, link))
			{
				AddLump(transaction, "NEWLUMP", Data("new", 10));
				transaction.Seal();
				Assert.Equal(real + WadSaveTransaction.SIBLING_SUFFIX, transaction.SiblingPath);
				Assert.True(File.Exists(real + WadSaveTransaction.SIBLING_SUFFIX), "no sibling beside the real file");
				Assert.False(File.Exists(link + WadSaveTransaction.SIBLING_SUFFIX), "sibling beside the link");
				transaction.Swap();
			}

			Assert.Equal("symbolic link", Stat("%F", link));
			Assert.Equal(Data("new", 10), ReadLumps(real)["NEWLUMP"]);
			Assert.Empty(Leftovers(dir));
		}

		[Fact]
		public void IwadFlagIsKept()
		{
			if(!IsUnix) return;
			string target = Path.Combine(dir, "game.wad");
			WriteWad(target, true, false, new[] { "PLAYPAL" }, new[] { Data("pal", 768) });

			Save(target, target, "NEWLUMP", Data("new", 10));

			Assert.Equal("IWAD", Kind(target));
		}

		[Fact]
		public void BaseWithDirectoryFirstKeepsAllLumps()
		{
			if(!IsUnix) return;
			string target = Path.Combine(dir, "dirfirst.wad");
			WriteWad(target, false, true, new[] { "A", "B", "C" }, new[] { Data("a", 100), Data("b", 2000), Data("c", 30) });

			Save(target, target, "NEWLUMP", Data("new", 5000));

			Dictionary<string, byte[]> lumps = ReadLumps(target);
			Assert.Equal(Data("a", 100), lumps["A"]);
			Assert.Equal(Data("b", 2000), lumps["B"]);
			Assert.Equal(Data("c", 30), lumps["C"]);
			Assert.Equal(Data("new", 5000), lumps["NEWLUMP"]);
		}

		[Fact]
		public void TargetMovedAwayBeforeSwapIsRecreated()
		{
			if(!IsUnix) return;
			string target = NewTarget();
			string moved = target + ".autosave2";

			using(WadSaveTransaction transaction = new WadSaveTransaction(target, target))
			{
				AddLump(transaction, "NEWLUMP", Data("new", 10));
				transaction.Seal();
				File.Move(target, moved);
				transaction.Swap();
			}

			Assert.Equal(Data("new", 10), ReadLumps(target)["NEWLUMP"]);
			Assert.Equal(3, ReadLumps(moved).Count);
			Assert.Empty(Leftovers(dir));
		}
	}
}
