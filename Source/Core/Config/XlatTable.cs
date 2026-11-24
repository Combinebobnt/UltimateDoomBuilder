#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

#endregion

namespace CodeImp.DoomBuilder.Config
{
	// A parse error in a translator file, with the file and line it came from
	public sealed class XlatParseException : Exception
	{
		public string File { get; }
		public int Line { get; }

		public XlatParseException(string file, int line, string message) : base(file + ", line " + line + ": " + message)
		{
			File = file;
			Line = line;
		}
	}

	// Something the engine's parser only prints and skips or truncates
	public struct XlatWarning
	{
		public string File;
		public int Line;
		public string Message;

		public override string ToString() { return File + ", line " + Line + ": " + Message; }
	}

	public enum XlatArgKind { Const, Tag, Expr }

	// One special argument of a simple line type: a constant, the line's tag, or an expression using the tag
	public sealed class XlatArg
	{
		public static readonly XlatArg Zero = new XlatArg(XlatArgKind.Const, 0, null);

		public XlatArgKind Kind { get; }
		public int Value { get; }
		internal XlatExpr Expr { get; }

		internal XlatArg(XlatArgKind kind, int value, XlatExpr expr)
		{
			Kind = kind;
			Value = value;
			Expr = expr;
		}

		public int Evaluate(int tag)
		{
			switch(Kind)
			{
				case XlatArgKind.Tag: return tag;
				case XlatArgKind.Expr: return Expr.Evaluate(tag);
				default: return Value;
			}
		}
	}

	// Simple line type: "type = flags, Special(args)"
	public sealed class XlatLineEntry
	{
		public const int FIRSTSIDE = 32; // defines.i; the low 5 flag bits are the Hexen REP, SPAC and MONST bits

		public int Type { get; internal set; }
		public int Special { get; internal set; }
		public int Flags { get; internal set; }
		public XlatArg[] Args { get; internal set; }
		public string File { get; internal set; }
		public int Line { get; internal set; }
	}

	// One store of a Boom generalized range: arg2..arg5 (ArgNum 0-3) or the flags (ArgNum 4)
	public sealed class XlatBoomArg
	{
		public int ArgNum { get; internal set; }
		public bool OrExisting { get; internal set; }
		public int ConstantValue { get; internal set; }
		public int AndValue { get; internal set; }
		public int[] Filters { get; internal set; } // null for a constant store
		public int[] Values { get; internal set; }
	}

	// Boom generalized range: "[Special] (first, last) { stores }"
	public sealed class XlatBoomRange
	{
		public int Special { get; internal set; }
		public int FirstType { get; internal set; }
		public int LastType { get; internal set; }
		public List<XlatBoomArg> Args { get; } = new List<XlatBoomArg>();
	}

	public struct XlatSectorEntry
	{
		public int NewType;
		public bool BitmaskAllowed;
	}

	// "sector bitmask M [clear | <<= n | >>= n]": Op 1 shifts left, -1 right, 0 keeps (Shift 1 = clear)
	public struct XlatSectorMask
	{
		public int Mask;
		public int Op;
		public int Shift;
	}

	public struct XlatLineFlag
	{
		public bool Defined;
		public int NewValue;
		public bool IsMask;
	}

	// A ZDoom-family xlat translator (wadsrc/static/xlat), parsed the way p_xlat.cpp and xlat_parser.y read it
	public sealed class XlatTable
	{
		#region ================== Data

		public Dictionary<int, XlatLineEntry> SimpleLines { get; } = new Dictionary<int, XlatLineEntry>();
		public List<XlatBoomRange> BoomRanges { get; } = new List<XlatBoomRange>();
		public Dictionary<int, XlatSectorEntry> Sectors { get; } = new Dictionary<int, XlatSectorEntry>();
		public List<XlatSectorMask> SectorMasks { get; } = new List<XlatSectorMask>();
		public XlatLineFlag[] LineFlags { get; } = new XlatLineFlag[16];
		public List<XlatWarning> Warnings { get; } = new List<XlatWarning>();

		// Special names the tables may use as numbers, as the engine's lexer resolves them
		public IReadOnlyDictionary<string, int> SpecialNames { get; private set; }

		#endregion

		#region ================== Loading

		// Opener returns a file's text by its include name ("xlat/base.txt"), or null when missing
		public static XlatTable Load(string filename, Func<string, string> opener, IDictionary<string, int> specialnames)
		{
			XlatTable table = new XlatTable();
			table.SpecialNames = new Dictionary<string, int>(specialnames ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
			new XlatParser(table, opener).ParseFile(filename, null, 0);
			return table;
		}

		// Loads from a folder holding the table files and actionspecials.h; "xlat/" include prefixes resolve inside it
		public static XlatTable LoadFromFolder(string folder, string filename)
		{
			Func<string, string> opener = name =>
			{
				string path = Path.Combine(folder, Path.GetFileName(name.Replace('\\', '/')));
				return File.Exists(path) ? File.ReadAllText(path) : null;
			};
			string specials = opener("actionspecials.h");
			return Load(filename, opener, specials != null ? ParseSpecialNames(specials) : null);
		}

		// Special name to number, from actionspecials.h's DEFINE_SPECIAL lines
		public static Dictionary<string, int> ParseSpecialNames(string text)
		{
			Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach(Match m in Regex.Matches(text, @"^\s*DEFINE_SPECIAL\s*\(\s*(\w+)\s*,\s*(\d+)", RegexOptions.Multiline))
				result[m.Groups[1].Value] = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
			return result;
		}

		#endregion
	}

	#region ================== Expressions

	internal abstract class XlatExpr
	{
		public abstract int Evaluate(int tag);
		public abstract bool UsesTag { get; }
	}

	internal sealed class XlatConstExpr : XlatExpr
	{
		public readonly int Value;
		public XlatConstExpr(int value) { Value = value; }
		public override int Evaluate(int tag) { return Value; }
		public override bool UsesTag { get { return false; } }
	}

	internal sealed class XlatTagExpr : XlatExpr
	{
		public override int Evaluate(int tag) { return tag; }
		public override bool UsesTag { get { return true; } }
	}

	internal sealed class XlatUnaryExpr : XlatExpr
	{
		private readonly XlatExpr operand;
		public XlatUnaryExpr(XlatExpr operand) { this.operand = operand; }
		public override int Evaluate(int tag) { return unchecked(-operand.Evaluate(tag)); }
		public override bool UsesTag { get { return operand.UsesTag; } }
	}

	internal sealed class XlatBinaryExpr : XlatExpr
	{
		private readonly string op;
		private readonly XlatExpr left, right;

		public XlatBinaryExpr(string op, XlatExpr left, XlatExpr right)
		{
			this.op = op;
			this.left = left;
			this.right = right;
		}

		public override bool UsesTag { get { return left.UsesTag || right.UsesTag; } }

		public override int Evaluate(int tag)
		{
			int a = left.Evaluate(tag);
			int b = right.Evaluate(tag);
			unchecked
			{
				switch(op)
				{
					case "+": return a + b;
					case "-": return a - b;
					case "*": return a * b;
					case "/": return b == 0 ? 0 : a / b; // The engine's Div0Check gives 0
					case "%": return b == 0 ? 0 : a % b;
					case "&": return a & b;
					case "|": return a | b;
					case "^": return a ^ b;
					default: throw new InvalidOperationException(op);
				}
			}
		}
	}

	#endregion

	#region ================== Parser

	internal sealed class XlatParser
	{
		private enum TokenKind { End, Num, Ident, Str, Punct }

		private struct Token
		{
			public TokenKind Kind;
			public string Text;
			public int Value;
			public int Line;
		}

		private const int MAX_ARGS = 5;
		private const int MAX_LIST = 15;
		private const int MAX_BOOMISH = 16;
		private const int MAX_INCLUDE_DEPTH = 16;

		private readonly XlatTable table;
		private readonly Func<string, string> opener;
		private readonly Dictionary<string, int> symbols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private readonly Stack<string> includestack = new Stack<string>();

		private List<Token> tokens;
		private int pos;
		private string file;

		public XlatParser(XlatTable table, Func<string, string> opener)
		{
			this.table = table;
			this.opener = opener;
		}

		public void ParseFile(string name, string fromfile, int fromline)
		{
			string text = opener(name);
			if(text == null)
			{
				if(fromfile == null) throw new XlatParseException(name, 0, "file not found");
				throw new XlatParseException(fromfile, fromline, "include '" + name + "' not found");
			}
			if(includestack.Count > MAX_INCLUDE_DEPTH) throw new XlatParseException(fromfile, fromline, "includes nested too deep");

			// Save the includer's position
			List<Token> savedtokens = tokens;
			int savedpos = pos;
			string savedfile = file;
			includestack.Push(name);

			tokens = Tokenize(text, name);
			pos = 0;
			file = name;
			while(Peek().Kind != TokenKind.End) ParseStatement();

			includestack.Pop();
			tokens = savedtokens;
			pos = savedpos;
			file = savedfile;
		}

		#region ================== Tokens

		private static List<Token> Tokenize(string text, string filename)
		{
			List<Token> result = new List<Token>();
			int i = 0;
			int line = 1;
			while(true)
			{
				// Whitespace and comments
				while(i < text.Length)
				{
					char w = text[i];
					if(w == '\n') { line++; i++; }
					else if(char.IsWhiteSpace(w)) i++;
					else if(w == '/' && i + 1 < text.Length && text[i + 1] == '/')
					{
						while(i < text.Length && text[i] != '\n') i++;
					}
					else if(w == '/' && i + 1 < text.Length && text[i + 1] == '*')
					{
						i += 2;
						while(i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
						{
							if(text[i] == '\n') line++;
							i++;
						}
						i += 2;
					}
					else break;
				}

				if(i >= text.Length)
				{
					result.Add(new Token { Kind = TokenKind.End, Line = line });
					return result;
				}

				char c = text[i];
				Token t = new Token { Line = line };
				if(char.IsDigit(c))
				{
					int start = i;
					long value;
					if(c == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X'))
					{
						i += 2;
						int hexstart = i;
						while(i < text.Length && Uri.IsHexDigit(text[i])) i++;
						value = ParseSaturated(text.Substring(hexstart, i - hexstart), 16);
					}
					else
					{
						while(i < text.Length && char.IsDigit(text[i])) i++;
						if(i < text.Length && text[i] == '.') throw new XlatParseException(filename, line, "floating point numbers are not supported");
						value = ParseSaturated(text.Substring(start, i - start), 10);
					}
					t.Kind = TokenKind.Num;
					t.Value = unchecked((int)value); // strtoll then an int cast, as the engine's lexer does
					t.Text = text.Substring(start, i - start);
				}
				else if(char.IsLetter(c) || c == '_')
				{
					int start = i;
					while(i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
					t.Kind = TokenKind.Ident;
					t.Text = text.Substring(start, i - start);
				}
				else if(c == '#')
				{
					// #include and #define are the plain keywords
					i++;
					int start = i;
					while(i < text.Length && char.IsLetter(text[i])) i++;
					t.Kind = TokenKind.Ident;
					t.Text = text.Substring(start, i - start);
					if(!t.Text.Equals("include", StringComparison.OrdinalIgnoreCase) && !t.Text.Equals("define", StringComparison.OrdinalIgnoreCase))
						throw new XlatParseException(filename, line, "unexpected '#" + t.Text + "'");
				}
				else if(c == '"')
				{
					int start = ++i;
					while(i < text.Length && text[i] != '"') i++;
					t.Kind = TokenKind.Str;
					t.Text = text.Substring(start, i - start);
					i++;
				}
				else
				{
					t.Kind = TokenKind.Punct;
					string rest = text.Substring(i, Math.Min(3, text.Length - i));
					if(rest.StartsWith("<<=") || rest.StartsWith(">>=")) t.Text = rest.Substring(0, 3);
					else if(rest.StartsWith("|=")) t.Text = "|=";
					else if("^&-+*/%(),{}=;:[]|".IndexOf(c) >= 0) t.Text = c.ToString();
					else throw new XlatParseException(filename, line, "unexpected character '" + c + "'");
					i += t.Text.Length;
				}
				result.Add(t);
			}
		}

		// strtoll clamps out-of-range numbers to LLONG_MAX
		private static long ParseSaturated(string digits, int radix)
		{
			long v = 0;
			foreach(char ch in digits)
			{
				int d = ch <= '9' ? ch - '0' : char.ToLowerInvariant(ch) - 'a' + 10;
				if(v > (long.MaxValue - d) / radix) return long.MaxValue;
				v = v * radix + d;
			}
			return v;
		}

		private Token Peek(int ahead = 0)
		{
			int i = Math.Min(pos + ahead, tokens.Count - 1);
			return tokens[i];
		}

		private Token Next()
		{
			Token t = Peek();
			if(pos < tokens.Count - 1) pos++;
			return t;
		}

		private XlatParseException Error(string message)
		{
			return new XlatParseException(file, Peek().Line, message);
		}

		private void Warn(int line, string message)
		{
			table.Warnings.Add(new XlatWarning { File = file, Line = line, Message = message });
		}

		// An identifier the lexer would pass as SYM: not a keyword, symbol or special name
		private bool IsUnknownName(int ahead = 0)
		{
			Token t = Peek(ahead);
			return t.Kind == TokenKind.Ident && !IsReserved(t.Text) && !symbols.ContainsKey(t.Text) && !table.SpecialNames.ContainsKey(t.Text);
		}

		private static string Describe(Token t)
		{
			return t.Kind == TokenKind.End ? "end of file" : "'" + t.Text + "'";
		}

		private bool IsPunct(string p, int ahead = 0)
		{
			Token t = Peek(ahead);
			return t.Kind == TokenKind.Punct && t.Text == p;
		}

		private bool IsKeyword(string k, int ahead = 0)
		{
			Token t = Peek(ahead);
			return t.Kind == TokenKind.Ident && t.Text.Equals(k, StringComparison.OrdinalIgnoreCase);
		}

		private void Expect(string p)
		{
			if(!IsPunct(p)) throw Error("expected '" + p + "' but found " + Describe(Peek()));
			Next();
		}

		private void ExpectKeyword(string k)
		{
			if(!IsKeyword(k)) throw Error("expected '" + k + "' but found " + Describe(Peek()));
			Next();
		}

		private string ExpectIdent()
		{
			Token t = Peek();
			if(t.Kind != TokenKind.Ident || IsReserved(t.Text)) throw Error("expected a name but found " + Describe(t));
			Next();
			return t.Text;
		}

		private static readonly HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"arg2", "arg3", "arg4", "arg5", "bitmask", "clear", "define", "enum", "flags", "include",
			"lineflag", "lineid", "maxlinespecial", "nobitmask", "sector", "tag"
		};

		private static bool IsReserved(string s) { return reserved.Contains(s); }

		#endregion

		#region ================== Statements

		private void ParseStatement()
		{
			Token t = Peek();
			if(t.Kind == TokenKind.Ident)
			{
				switch(t.Text.ToLowerInvariant())
				{
					case "include":
						Next();
						Token name = Next();
						if(name.Kind != TokenKind.Str) throw new XlatParseException(file, name.Line, "include: string parameter expected");
						ParseFile(name.Text, file, name.Line);
						return;

					case "define":
						Next();
						string sym = ExpectIdent();
						Expect("(");
						int value = ParseConstExpr();
						Expect(")");
						symbols[sym] = value;
						return;

					case "enum": ParseEnum(); return;
					case "sector": ParseSector(); return;
					case "lineflag": ParseLineFlag(); return;

					case "maxlinespecial":
						Next();
						Expect("=");
						int max = ParseConstExpr();
						Expect(";");
						List<int> remove = new List<int>();
						foreach(int type in table.SimpleLines.Keys) if(type > max) remove.Add(type);
						foreach(int type in remove) table.SimpleLines.Remove(type);
						return;
				}
			}

			if(IsPunct("[")) { ParseBoom(); return; }
			ParseLineType();
		}

		private void ParseEnum()
		{
			ExpectKeyword("enum");
			Expect("{");
			int next = 0;
			while(!IsPunct("}"))
			{
				string sym = ExpectIdent();
				if(IsPunct("="))
				{
					Next();
					next = ParseConstExpr();
				}
				symbols[sym] = next;
				next = unchecked(next + 1);
				if(!IsPunct(",")) break;
				Next();
			}
			Expect("}");
		}

		private void ParseSector()
		{
			ExpectKeyword("sector");
			if(IsKeyword("bitmask"))
			{
				Next();
				XlatSectorMask mask = new XlatSectorMask { Mask = ParseConstExpr() };
				if(IsKeyword("clear"))
				{
					Next();
					mask.Shift = 1;
				}
				else if(IsPunct("<<=") || IsPunct(">>="))
				{
					mask.Op = Next().Text == "<<=" ? 1 : -1;
					mask.Shift = ParseConstExpr();
				}
				Expect(";");
				table.SectorMasks.Add(mask);
				return;
			}

			int from = ParseConstExpr();
			Expect("=");
			if(IsUnknownName() && IsPunct(";", 1))
			{
				Warn(Peek().Line, "unknown constant '" + Next().Text + "'");
				Next();
				return;
			}
			XlatSectorEntry entry = new XlatSectorEntry { NewType = ParseConstExpr(), BitmaskAllowed = true };
			if(IsKeyword("nobitmask"))
			{
				Next();
				entry.BitmaskAllowed = false;
			}
			Expect(";");
			table.Sectors[from] = entry;
		}

		private void ParseLineFlag()
		{
			ExpectKeyword("lineflag");
			int from = ParseConstExpr();
			bool ismask;
			if(IsPunct("=")) ismask = false;
			else if(IsPunct("&")) ismask = true;
			else throw Error("expected '=' or '&' but found " + Describe(Peek()));
			Next();
			int value = ParseConstExpr();
			Expect(";");
			if(from >= 0 && from < 16) table.LineFlags[from] = new XlatLineFlag { Defined = true, NewValue = value, IsMask = ismask };
		}

		private void ParseLineType()
		{
			int line = Peek().Line;
			int type = ParseConstExpr();
			Expect("=");
			int flags = ParseConstExpr();
			Expect(",");

			// An unknown special name skips the entry, leaving any earlier one for the type
			string unknown = null;
			int special = 0;
			if(IsUnknownName() && IsPunct("(", 1)) unknown = Next().Text;
			else special = ParseConstExpr() & 0xffff;

			Expect("(");
			List<XlatArg> args = new List<XlatArg>();
			if(!IsPunct(")"))
			{
				bool warned = false;
				while(true)
				{
					XlatExpr e = ParseExpr(true);
					if(args.Count < MAX_ARGS)
					{
						if(e is XlatTagExpr) args.Add(new XlatArg(XlatArgKind.Tag, 0, null));
						else if(!e.UsesTag) args.Add(new XlatArg(XlatArgKind.Const, e.Evaluate(0), null));
						else args.Add(new XlatArg(XlatArgKind.Expr, 0, e));
					}
					else if(!warned)
					{
						Warn(Peek().Line, "line special has too many arguments");
						warned = true;
					}
					if(!IsPunct(",")) break;
					Next();
				}
			}
			Expect(")");

			if(unknown != null)
			{
				Warn(line, unknown + " is undefined");
				return;
			}
			while(args.Count < MAX_ARGS) args.Add(XlatArg.Zero);
			table.SimpleLines[type] = new XlatLineEntry { Type = type, Special = special, Flags = flags, Args = args.ToArray(), File = file, Line = line };
		}

		// FBoomTranslator and FBoomArg field widths
		private void ParseBoom()
		{
			int line = Peek().Line;
			Expect("[");
			XlatBoomRange range = new XlatBoomRange();
			range.Special = ParseConstExpr() & 0xffff;
			Expect("]");
			Expect("(");
			range.FirstType = ParseConstExpr() & 0xffff;
			Expect(",");
			range.LastType = ParseConstExpr() & 0xffff;
			Expect(")");
			Expect("{");
			while(!IsPunct("}"))
			{
				XlatBoomArg arg = new XlatBoomArg();
				Token sel = Next();
				string s = sel.Kind == TokenKind.Ident ? sel.Text.ToLowerInvariant() : "";
				switch(s)
				{
					case "flags": arg.ArgNum = 4; break;
					case "arg2": arg.ArgNum = 0; break;
					case "arg3": arg.ArgNum = 1; break;
					case "arg4": arg.ArgNum = 2; break;
					case "arg5": arg.ArgNum = 3; break;
					default: throw new XlatParseException(file, sel.Line, "expected flags or arg2..arg5 but found " + Describe(sel));
				}

				if(IsPunct("|=")) arg.OrExisting = true;
				else if(!IsPunct("=")) throw Error("expected '=' or '|=' but found " + Describe(Peek()));
				Next();

				int value = ParseConstExpr();
				if(IsPunct("["))
				{
					Next();
					List<int> filters = new List<int>();
					List<int> values = new List<int>();
					for(int count = 0; ; count++)
					{
						if(count == MAX_LIST) Warn(Peek().Line, "lists can only have " + MAX_LIST + " elements");
						int filter = ParseConstExpr() & 0xffff;
						Expect(":");
						int result = ParseConstExpr() & 0xff;
						if(count < MAX_LIST)
						{
							filters.Add(filter);
							values.Add(result);
						}
						if(!IsPunct(",")) break;
						Next();
					}
					Expect("]");
					arg.AndValue = value & 0xffff;
					arg.Filters = filters.ToArray();
					arg.Values = values.ToArray();
				}
				else
				{
					arg.ConstantValue = value & 0xff;
				}
				range.Args.Add(arg);
			}
			Expect("}");
			if(table.BoomRanges.Count == MAX_BOOMISH) Warn(line, "too many BOOM translators");
			else table.BoomRanges.Add(range);
		}

		#endregion

		#region ================== Expressions

		private int ParseConstExpr()
		{
			return ParseExpr(false).Evaluate(0);
		}

		// Precedence from xlat_parser.y, loosest first: | ^ & (+ -) (* / %) unary minus
		private static readonly string[][] levels =
		{
			new[] { "|" }, new[] { "^" }, new[] { "&" }, new[] { "+", "-" }, new[] { "*", "/", "%" }
		};

		private XlatExpr ParseExpr(bool allowtag)
		{
			return ParseLevel(0, allowtag);
		}

		private XlatExpr ParseLevel(int level, bool allowtag)
		{
			if(level == levels.Length) return ParseUnary(allowtag);
			XlatExpr left = ParseLevel(level + 1, allowtag);
			while(Peek().Kind == TokenKind.Punct && Array.IndexOf(levels[level], Peek().Text) >= 0)
			{
				string op = Next().Text;
				XlatExpr right = ParseLevel(level + 1, allowtag);
				left = new XlatBinaryExpr(op, left, right);
			}
			return left;
		}

		private XlatExpr ParseUnary(bool allowtag)
		{
			if(IsPunct("-"))
			{
				Next();
				return new XlatUnaryExpr(ParseUnary(allowtag));
			}
			if(IsPunct("("))
			{
				Next();
				XlatExpr inner = ParseExpr(allowtag);
				Expect(")");
				return inner;
			}

			Token t = Peek();
			if(t.Kind == TokenKind.Num)
			{
				Next();
				return new XlatConstExpr(t.Value);
			}
			if(t.Kind == TokenKind.Ident)
			{
				if(allowtag && (IsKeyword("tag") || IsKeyword("lineid")))
				{
					Next();
					return new XlatTagExpr();
				}
				int value;
				if(!IsReserved(t.Text) && (symbols.TryGetValue(t.Text, out value) || table.SpecialNames.TryGetValue(t.Text, out value)))
				{
					Next();
					return new XlatConstExpr(value);
				}
				throw Error("'" + t.Text + "' is undefined");
			}
			throw Error("expected a value but found " + Describe(t));
		}

		#endregion
	}

	#endregion
}
