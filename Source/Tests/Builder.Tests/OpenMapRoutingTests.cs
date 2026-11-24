using CodeImp.DoomBuilder;
using static Builder.Tests.ZipFixtures;

namespace Builder.Tests
{
	public class OpenMapRoutingTests : IDisposable
	{
		private readonly string dir = Path.Combine(Path.GetTempPath(), "udb-openroute-" + Guid.NewGuid().ToString("N"));

		public OpenMapRoutingTests()
		{
			Directory.CreateDirectory(dir);
		}

		public void Dispose()
		{
			Directory.Delete(dir, true);
		}

		private string Write(string name, byte[] data)
		{
			string path = Path.Combine(dir, name);
			File.WriteAllBytes(path, data);
			return path;
		}

		[Fact]
		public void ArchivesRouteByContentOrExtension()
		{
			byte[] wad = Wad(DoomMap("MAP01"));
			byte[] zip = Zip(E("maps/MAP01.wad", wad));

			Assert.False(General.IsArchiveFile(Write("plain.wad", wad)));
			Assert.False(General.IsArchiveFile(Write("upper.WAD", wad)));
			Assert.True(General.IsArchiveFile(Write("mod.pk3", zip)));
			Assert.True(General.IsArchiveFile(Write("mod.ZIP", zip)));
			Assert.True(General.IsArchiveFile(Write("renamed.wad", zip)));

			// Not a zip, but named like one: routed to the archive path, which reports it as invalid
			Assert.True(General.IsArchiveFile(Write("garbage.pk3", new byte[] { 1, 2, 3, 4, 5 })));
		}

		[Fact]
		public void FilterPatternsMatchEveryLetterCase()
		{
			foreach(string ext in new[] { "wad", "pk3", "zip" })
			{
				string[] patterns = General.FilterPatterns(ext).Split(';');
				Assert.Contains("*." + ext, patterns);
				Assert.Equal(patterns.Length, patterns.Distinct().Count());
				Assert.True(patterns.All(p => string.Equals(p, "*." + ext, StringComparison.OrdinalIgnoreCase)));

				// Case-sensitive platforms list every spelling
				if(patterns.Length > 1)
				{
					int letters = ext.Count(char.IsLetter);
					Assert.Equal(1 << letters, patterns.Length);
				}
			}
		}
	}
}
