using CodeImp.DoomBuilder.IO;
using System.Text;

namespace Builder.Tests
{
	public class UdmfRoundTripTests
	{
		private static readonly string[] KNOWN = { "namespace", "vertex", "sector", "linedef", "sidedef", "thing" };

		private const string TEXTMAP =
			"namespace = \"zdoom\";\n" +
			"customglobal = 7;\n" +
			"vertex { x = 0.0; y = 0.0; }\n" +
			"vertex { x = 0.0; y = 64.0; }\n" +
			"linedef { v1 = 0; v2 = 1; sidefront = 0; }\n" +
			"sidedef { sector = 0; }\n" +
			"sector { texturefloor = \"FLOOR\"; textureceiling = \"CEIL\"; }\n" +
			"customblock\n{\nfoo = 1;\nbar = \"caf\u00e9\";\n}\n";

		private static string[] Lines(string text)
		{
			return UniversalStreamReader.ReadLines(new MemoryStream(UniversalStreamReader.ENCODING.GetBytes(text)));
		}

		private static UniversalParser Parse(string[] lines)
		{
			UniversalParser parser = new UniversalParser();
			Assert.True(parser.InputConfiguration(lines), parser.ErrorDescription);
			return parser;
		}

		private static List<UniversalEntry> Unknown(UniversalParser parser)
		{
			return parser.Root.Where(e => !KNOWN.Contains(e.Key)).ToList();
		}

		[Fact]
		public void EveryByteSurvivesReadAndWrite()
		{
			List<byte> bytes = new List<byte>();
			for(int b = 1; b < 256; b++) if(b != '\r' && b != '\n') bytes.Add((byte)b);
			byte[] original = bytes.ToArray();

			string[] lines = UniversalStreamReader.ReadLines(new MemoryStream(original));
			MemoryStream written = new MemoryStream();
			UniversalStreamWriter.WriteText(written, string.Join("\n", lines));

			Assert.Equal(original, written.ToArray());
		}

		[Fact]
		public void NonAsciiStringValueSurvivesParseAndOutput()
		{
			byte[] input = Encoding.ASCII.GetBytes("thing { comment = \"caf_ na_ve\"; }\n");
			input[Array.IndexOf(input, (byte)'_')] = 0xE9;
			input[Array.LastIndexOf(input, (byte)'_')] = 0xEF;

			UniversalParser parsed = Parse(UniversalStreamReader.ReadLines(new MemoryStream(input)));
			MemoryStream written = new MemoryStream();
			UniversalStreamWriter.WriteText(written, parsed.OutputConfiguration());
			byte[] output = written.ToArray();

			Assert.Contains((byte)0xE9, output);
			Assert.Contains((byte)0xEF, output);
			Assert.DoesNotContain((byte)'?', output);
		}

		[Fact]
		public void BlockTheNodebuilderDroppedIsRestoredOnce()
		{
			List<UniversalEntry> unknown = Unknown(Parse(Lines(TEXTMAP)));

			// What zdbsp writes: the global keys before the first block survive, unknown blocks don't
			string nodebuilt = TEXTMAP.Substring(0, TEXTMAP.IndexOf("customblock", StringComparison.Ordinal));
			string missing = UniversalStreamWriter.MissingUnknownData(Lines(nodebuilt), unknown);

			Assert.NotNull(missing);
			List<UniversalEntry> restored = Unknown(Parse(Lines(nodebuilt + "\r\n" + missing)));
			Assert.Equal(new[] { "customglobal", "customblock" }, restored.Select(e => e.Key).ToArray());
			Assert.Equal("caf\u00e9", ((UniversalCollection)restored[1].Value).First(e => e.Key == "bar").Value);
		}

		[Fact]
		public void EntriesTheNodebuilderKeptAreNotAddedAgain()
		{
			List<UniversalEntry> unknown = Unknown(Parse(Lines(TEXTMAP)));

			Assert.Null(UniversalStreamWriter.MissingUnknownData(Lines(TEXTMAP), unknown));
		}
	}
}
