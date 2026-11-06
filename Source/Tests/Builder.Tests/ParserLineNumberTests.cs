using System.Reflection;
using System.Text;

namespace Builder.Tests
{
	public class ParserLineNumberTests
	{
		[Theory]
		[InlineData("\n")]
		[InlineData("\r\n")]
		[InlineData("\r")]
		public void ErrorLine_CountsEveryLineBreakStyle(string newline)
		{
			// 40 lines so a per-line byte miscount drifts the result by several lines
			const int badline = 40;
			var sb = new StringBuilder();
			for (int i = 0; i < badline; i++) sb.Append("user int v" + i + ";" + newline);
			sb.Append("user bogus x;" + newline + "user int last;" + newline);

			var parser = new CodeImp.DoomBuilder.ZDoom.CvarInfoParser();
			Assert.False(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString(sb.ToString()), true));

			Assert.Equal(badline, parser.ErrorLine);
		}

		private static readonly string[] Texts =
		{
			"", "a", "ab\ncd", "ab\ncd\n", "ab\r\ncd", "ab\r\ncd\r\n", "ab\rcd", "ab\rcd\r",
			"\n\n", "\r\n\r\n", "\r\r", "a\r\nb\nc\rd\n\re", "a\n\r", "a\r\n\n",
		};

		// The line StreamReader.ReadLine would be on at pos: no line starts after a trailing break
		private static int Oracle(string text, int pos)
		{
			int line = 0, start = 0;
			for (int i = 0; i < text.Length; i++)
			{
				if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
				else if (text[i] != '\r' && text[i] != '\n') continue;
				start = i + 1;
				if (start <= pos && start < text.Length) line++;
			}
			return line;
		}

		[Fact]
		public void GetCurrentLineNumber_MatchesReadLineAtEveryPosition()
		{
			var flags = BindingFlags.NonPublic | BindingFlags.Instance;
			var type = typeof(CodeImp.DoomBuilder.ZDoom.ZDTextParser);
			var failures = new List<string>();

			foreach (string text in Texts)
			{
				for (int pos = 0; pos <= text.Length; pos++)
				{
					var parser = new CodeImp.DoomBuilder.ZDoom.CvarInfoParser();
					var stream = new MemoryStream(Encoding.ASCII.GetBytes(text));
					stream.Position = pos;
					type.GetField("datastream", flags)!.SetValue(parser, stream);
					type.GetField("prevstreamposition", flags)!.SetValue(parser, -1L);

					int actual = (int)type.GetMethod("GetCurrentLineNumber", flags)!.Invoke(parser, null)!;
					int expected = Oracle(text, pos);
					if (actual != expected || stream.Position != pos)
						failures.Add($"\"{text.Replace("\r", "\\r").Replace("\n", "\\n")}\" pos {pos}: expected {expected}, got {actual}, stream at {stream.Position}");
				}
			}

			Assert.True(failures.Count == 0, string.Join("; ", failures));
		}
	}
}
