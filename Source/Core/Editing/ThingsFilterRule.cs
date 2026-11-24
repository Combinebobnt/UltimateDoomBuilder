#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;

#endregion

namespace CodeImp.DoomBuilder.Editing
{
	// Persisted as ints: never renumber, append new values
	public enum ThingsFilterRuleSubject
	{
		Type = 0,
		Angle = 1,
		ZHeight = 2,
		Tag = 3,
		Action = 4,
		Arg = 5,
		Category = 6,
		ClassName = 7,
		Title = 8,
		Flag = 9,
		Field = 10,
		UnknownType = 11,
		MatchesFilter = 12,
	}

	// Persisted as ints: never renumber, append new values
	public enum ThingsFilterRuleOperator
	{
		IsOneOf = 0,
		IsNotOneOf = 1,
		Less = 2,
		LessOrEqual = 3,
		Greater = 4,
		GreaterOrEqual = 5,
		Equals = 6,
		NotEquals = 7,
		Contains = 8,
		Matches = 9,
		IsTrue = 10,
		IsFalse = 11,
		Exists = 12,
		IsAbsent = 13,
	}

	// Persisted as ints: never renumber
	public enum ThingsFilterMatchMode
	{
		All = 0,
		Any = 1,
	}

	internal enum ThingsFilterRuleOutcome
	{
		Pass,
		Fail,
		Skip,
	}

	// What a rule may read about the map; built once per filter update
	internal sealed class ThingsFilterContext
	{
		public bool HasThingHeight = true;
		public bool HasThingAction = true;
		public bool HasThingTag = true;
		public bool HasActionArgs = true;
		public bool HasCustomFields = true;

		// Null means every flag name is known
		public ICollection<string> KnownFlags;

		// Filters "Matches filter" rules resolve against, first name match wins
		public IList<ThingsFilter> Filters = new List<ThingsFilter>();

		// Filters being evaluated up the current call chain
		public readonly HashSet<ThingsFilter> Visiting = new HashSet<ThingsFilter>();

		public ThingsFilter Resolve(string name)
		{
			if(Filters == null || name == null) return null;
			foreach(ThingsFilter f in Filters)
				if(f != null && f.Name == name) return f;
			return null;
		}

		public bool IsAvailable(ThingsFilterRule rule)
		{
			switch(rule.Subject)
			{
				case ThingsFilterRuleSubject.ZHeight: return HasThingHeight;
				case ThingsFilterRuleSubject.Action: return HasThingAction;
				case ThingsFilterRuleSubject.Tag: return HasThingTag;
				case ThingsFilterRuleSubject.Arg: return HasActionArgs;
				case ThingsFilterRuleSubject.Field: return HasCustomFields;
				case ThingsFilterRuleSubject.Flag: return KnownFlags == null || KnownFlags.Contains(rule.Key);
				default: return true;
			}
		}

		public static ThingsFilterContext FromMap(IEnumerable<ThingsFilter> filters)
		{
			ThingsFilterContext ctx = new ThingsFilterContext();
			ctx.Filters = (filters != null ? new List<ThingsFilter>(filters) : new List<ThingsFilter>());
			if(General.Map != null)
			{
				IMapSetIO io = General.Map.FormatInterface;
				if(io != null)
				{
					ctx.HasThingHeight = io.HasThingHeight;
					ctx.HasThingAction = io.HasThingAction;
					ctx.HasThingTag = io.HasThingTag;
					ctx.HasActionArgs = io.HasActionArgs;
					ctx.HasCustomFields = io.HasCustomFields;
				}
				if(General.Map.Config != null && General.Map.Config.ThingFlags != null)
					ctx.KnownFlags = General.Map.Config.ThingFlags.Keys;
			}
			return ctx;
		}
	}

	public sealed class ThingsFilterRule
	{
		#region ================== Variables

		private ThingsFilterRuleSubject subject;
		private ThingsFilterRuleOperator op;
		private string key;
		private string value;
		private bool includesubcategories;

		// Parsed matcher
		private bool isvalid;
		private string error;
		private List<KeyValuePair<int, int>> ranges;
		private double number;
		private bool boolvalue;
		private bool valueisnumber;
		private bool valueisbool;
		private Regex wildcard;
		private int argindex;

		#endregion

		#region ================== Properties

		public ThingsFilterRuleSubject Subject { get { return subject; } set { subject = value; Parse(); } }
		public ThingsFilterRuleOperator Operator { get { return op; } set { op = value; Parse(); } }
		public string Key { get { return key; } set { key = value ?? string.Empty; Parse(); } }
		public string Value { get { return value; } set { this.value = value ?? string.Empty; Parse(); } }
		public bool IncludeSubcategories { get { return includesubcategories; } set { includesubcategories = value; } }
		public bool IsValidRule { get { return isvalid; } }
		public string Error { get { return error; } }

		#endregion

		#region ================== Constructors

		public ThingsFilterRule(ThingsFilterRuleSubject subject, ThingsFilterRuleOperator op, string key, string value, bool includesubcategories = true)
		{
			this.subject = subject;
			this.op = op;
			this.key = key ?? string.Empty;
			this.value = value ?? string.Empty;
			this.includesubcategories = includesubcategories;
			Parse();
		}

		public ThingsFilterRule(ThingsFilterRule other)
			: this(other.subject, other.op, other.key, other.value, other.includesubcategories) { }

		internal ThingsFilterRule(Configuration cfg, string path)
		{
			subject = (ThingsFilterRuleSubject)cfg.ReadSetting(path + ".subject", -1);
			op = (ThingsFilterRuleOperator)cfg.ReadSetting(path + ".operator", -1);
			key = ReadString(cfg, path + ".key");
			value = ReadString(cfg, path + ".value");
			includesubcategories = cfg.ReadSetting(path + ".subcategories", false);
			Parse();
		}

		// A hand-written "value = 64;" is read as an int, so convert whatever is there
		private static string ReadString(Configuration cfg, string path)
		{
			object o = cfg.ReadSettingObject(path, string.Empty);
			return (o == null ? string.Empty : Convert.ToString(o, CultureInfo.InvariantCulture));
		}

		internal void WriteSettings(Configuration cfg, string path)
		{
			cfg.WriteSetting(path + ".subject", (int)subject);
			cfg.WriteSetting(path + ".operator", (int)op);
			cfg.WriteSetting(path + ".key", key);
			cfg.WriteSetting(path + ".value", value);
			cfg.WriteSetting(path + ".subcategories", includesubcategories);
		}

		#endregion

		#region ================== Subject and operator tables

		public static readonly ThingsFilterRuleSubject[] AllSubjects =
		{
			ThingsFilterRuleSubject.Type, ThingsFilterRuleSubject.Angle, ThingsFilterRuleSubject.ZHeight,
			ThingsFilterRuleSubject.Tag, ThingsFilterRuleSubject.Action, ThingsFilterRuleSubject.Arg,
			ThingsFilterRuleSubject.Category, ThingsFilterRuleSubject.ClassName, ThingsFilterRuleSubject.Title,
			ThingsFilterRuleSubject.Flag, ThingsFilterRuleSubject.Field, ThingsFilterRuleSubject.UnknownType,
			ThingsFilterRuleSubject.MatchesFilter,
		};

		private static readonly ThingsFilterRuleOperator[] ListOps = { ThingsFilterRuleOperator.IsOneOf, ThingsFilterRuleOperator.IsNotOneOf };
		private static readonly ThingsFilterRuleOperator[] NumberOps =
		{
			ThingsFilterRuleOperator.IsOneOf, ThingsFilterRuleOperator.IsNotOneOf,
			ThingsFilterRuleOperator.Less, ThingsFilterRuleOperator.LessOrEqual,
			ThingsFilterRuleOperator.Greater, ThingsFilterRuleOperator.GreaterOrEqual,
		};
		private static readonly ThingsFilterRuleOperator[] IsOps = { ThingsFilterRuleOperator.Equals, ThingsFilterRuleOperator.NotEquals };
		private static readonly ThingsFilterRuleOperator[] TextOps =
		{
			ThingsFilterRuleOperator.Equals, ThingsFilterRuleOperator.NotEquals,
			ThingsFilterRuleOperator.Contains, ThingsFilterRuleOperator.Matches,
		};
		private static readonly ThingsFilterRuleOperator[] BoolOps = { ThingsFilterRuleOperator.IsTrue, ThingsFilterRuleOperator.IsFalse };
		private static readonly ThingsFilterRuleOperator[] FieldOps =
		{
			ThingsFilterRuleOperator.Exists, ThingsFilterRuleOperator.IsAbsent,
			ThingsFilterRuleOperator.Equals, ThingsFilterRuleOperator.NotEquals,
			ThingsFilterRuleOperator.Less, ThingsFilterRuleOperator.LessOrEqual,
			ThingsFilterRuleOperator.Greater, ThingsFilterRuleOperator.GreaterOrEqual,
			ThingsFilterRuleOperator.Contains,
		};
		private static readonly ThingsFilterRuleOperator[] NoOps = new ThingsFilterRuleOperator[0];

		public static ThingsFilterRuleOperator[] OperatorsFor(ThingsFilterRuleSubject s)
		{
			switch(s)
			{
				case ThingsFilterRuleSubject.Type: return ListOps;
				case ThingsFilterRuleSubject.Angle:
				case ThingsFilterRuleSubject.ZHeight:
				case ThingsFilterRuleSubject.Tag:
				case ThingsFilterRuleSubject.Action:
				case ThingsFilterRuleSubject.Arg: return NumberOps;
				case ThingsFilterRuleSubject.Category: return IsOps;
				case ThingsFilterRuleSubject.ClassName:
				case ThingsFilterRuleSubject.Title: return TextOps;
				case ThingsFilterRuleSubject.Flag:
				case ThingsFilterRuleSubject.UnknownType:
				case ThingsFilterRuleSubject.MatchesFilter: return BoolOps;
				case ThingsFilterRuleSubject.Field: return FieldOps;
				default: return NoOps;
			}
		}

		public static string SubjectTitle(ThingsFilterRuleSubject s)
		{
			switch(s)
			{
				case ThingsFilterRuleSubject.Type: return "Type";
				case ThingsFilterRuleSubject.Angle: return "Angle";
				case ThingsFilterRuleSubject.ZHeight: return "Z height";
				case ThingsFilterRuleSubject.Tag: return "Tag";
				case ThingsFilterRuleSubject.Action: return "Action";
				case ThingsFilterRuleSubject.Arg: return "Argument";
				case ThingsFilterRuleSubject.Category: return "Category";
				case ThingsFilterRuleSubject.ClassName: return "Class name";
				case ThingsFilterRuleSubject.Title: return "Title";
				case ThingsFilterRuleSubject.Flag: return "Flag";
				case ThingsFilterRuleSubject.Field: return "UDMF field";
				case ThingsFilterRuleSubject.UnknownType: return "Unknown type";
				case ThingsFilterRuleSubject.MatchesFilter: return "Matches filter";
				default: return "Unknown subject " + (int)s;
			}
		}

		public static string OperatorTitle(ThingsFilterRuleSubject s, ThingsFilterRuleOperator o)
		{
			switch(o)
			{
				case ThingsFilterRuleOperator.IsOneOf: return "is one of";
				case ThingsFilterRuleOperator.IsNotOneOf: return "is not one of";
				case ThingsFilterRuleOperator.Less: return "<";
				case ThingsFilterRuleOperator.LessOrEqual: return "<=";
				case ThingsFilterRuleOperator.Greater: return ">";
				case ThingsFilterRuleOperator.GreaterOrEqual: return ">=";
				case ThingsFilterRuleOperator.Equals: return (s == ThingsFilterRuleSubject.Category ? "is" : "equals");
				case ThingsFilterRuleOperator.NotEquals: return (s == ThingsFilterRuleSubject.Category ? "is not" : "not equals");
				case ThingsFilterRuleOperator.Contains: return "contains";
				case ThingsFilterRuleOperator.Matches: return "matches";
				case ThingsFilterRuleOperator.IsTrue: return (s == ThingsFilterRuleSubject.Flag ? "is set" : "is true");
				case ThingsFilterRuleOperator.IsFalse: return (s == ThingsFilterRuleSubject.Flag ? "is not set" : "is false");
				case ThingsFilterRuleOperator.Exists: return "exists";
				case ThingsFilterRuleOperator.IsAbsent: return "is absent";
				default: return "unknown operator " + (int)o;
			}
		}

		// Whether the Key column means anything for this subject
		public static bool UsesKey(ThingsFilterRuleSubject s)
		{
			return s == ThingsFilterRuleSubject.Arg || s == ThingsFilterRuleSubject.Flag
				|| s == ThingsFilterRuleSubject.Field || s == ThingsFilterRuleSubject.MatchesFilter;
		}

		// Whether the Value column means anything for this subject and operator
		public static bool UsesValue(ThingsFilterRuleSubject s, ThingsFilterRuleOperator o)
		{
			switch(s)
			{
				case ThingsFilterRuleSubject.Flag:
				case ThingsFilterRuleSubject.UnknownType:
				case ThingsFilterRuleSubject.MatchesFilter: return false;
				case ThingsFilterRuleSubject.Field: return o != ThingsFilterRuleOperator.Exists && o != ThingsFilterRuleOperator.IsAbsent;
				default: return true;
			}
		}

		public static bool IsCompare(ThingsFilterRuleOperator o)
		{
			return o == ThingsFilterRuleOperator.Less || o == ThingsFilterRuleOperator.LessOrEqual
				|| o == ThingsFilterRuleOperator.Greater || o == ThingsFilterRuleOperator.GreaterOrEqual;
		}

		#endregion

		#region ================== Parsing

		private void Parse()
		{
			isvalid = false;
			error = null;
			ranges = null;
			wildcard = null;
			valueisnumber = false;
			valueisbool = false;
			argindex = -1;

			if(Array.IndexOf(AllSubjects, subject) < 0) { error = "Unknown subject " + (int)subject + " (written by a newer version?)"; return; }
			if(Array.IndexOf(OperatorsFor(subject), op) < 0) { error = "Operator \"" + OperatorTitle(subject, op) + "\" doesn't apply to " + SubjectTitle(subject); return; }

			switch(subject)
			{
				case ThingsFilterRuleSubject.Type:
				case ThingsFilterRuleSubject.Angle:
				case ThingsFilterRuleSubject.ZHeight:
				case ThingsFilterRuleSubject.Tag:
				case ThingsFilterRuleSubject.Action:
				case ThingsFilterRuleSubject.Arg:
					if(subject == ThingsFilterRuleSubject.Arg)
					{
						int i;
						if(!int.TryParse(key.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i) || i < 0 || i >= Thing.NUM_ARGS)
						{
							error = "Argument index must be 0 to " + (Thing.NUM_ARGS - 1);
							return;
						}
						argindex = i;
					}
					if(IsCompare(op))
					{
						int n;
						if(!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
						{
							error = "Expected one whole number";
							return;
						}
						number = n;
					}
					else
					{
						ranges = ParseNumberList(value, out error);
						if(ranges == null) return;
					}
					break;

				case ThingsFilterRuleSubject.Category:
					if(value.Trim().Length == 0) { error = "Pick a category"; return; }
					break;

				case ThingsFilterRuleSubject.ClassName:
				case ThingsFilterRuleSubject.Title:
					if(op == ThingsFilterRuleOperator.Matches) wildcard = WildcardRegex(value);
					break;

				case ThingsFilterRuleSubject.Flag:
					if(key.Trim().Length == 0) { error = "Pick a flag"; return; }
					break;

				case ThingsFilterRuleSubject.MatchesFilter:
					if(key.Length == 0) { error = "Pick a filter"; return; }
					break;

				case ThingsFilterRuleSubject.Field:
					if(key.Trim().Length == 0) { error = "Enter a field name"; return; }
					double d;
					valueisnumber = double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
					if(valueisnumber) number = d;
					if(IsCompare(op) && !valueisnumber) { error = "Expected a number"; return; }
					bool b;
					valueisbool = bool.TryParse(value.Trim(), out b);
					boolvalue = b;
					break;
			}

			isvalid = true;
		}

		private static readonly Regex numbertoken = new Regex(@"^(-?[0-9]+)(?:\s*(?:\.\.|-)\s*(-?[0-9]+))?$", RegexOptions.CultureInvariant);

		// "3001-3006, 9" or "-10..-1"; null and an error message when it doesn't parse
		internal static List<KeyValuePair<int, int>> ParseNumberList(string text, out string error)
		{
			error = null;
			List<KeyValuePair<int, int>> result = new List<KeyValuePair<int, int>>();
			foreach(string part in (text ?? string.Empty).Split(','))
			{
				string token = part.Trim();
				if(token.Length == 0) continue;
				Match m = numbertoken.Match(token);
				int a, b;
				if(!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out a))
				{
					error = "\"" + token + "\" is not a number or range";
					return null;
				}
				b = a;
				if(m.Groups[2].Success && !int.TryParse(m.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out b))
				{
					error = "\"" + token + "\" is not a number or range";
					return null;
				}
				if(b < a)
				{
					error = "Range \"" + token + "\" runs backwards";
					return null;
				}
				result.Add(new KeyValuePair<int, int>(a, b));
			}
			if(result.Count == 0)
			{
				error = "Enter at least one number";
				return null;
			}
			return result;
		}

		// The reverse of ParseNumberList: sorted, distinct, runs of three or more as ranges
		internal static string FormatNumberList(IEnumerable<int> numbers)
		{
			List<int> sorted = new List<int>(new SortedSet<int>(numbers));
			List<string> parts = new List<string>();
			for(int i = 0; i < sorted.Count; )
			{
				int j = i;
				while(j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
				string sep = (sorted[i] < 0 || sorted[j] < 0 ? ".." : "-");
				if(j - i >= 2) parts.Add(sorted[i].ToString(CultureInfo.InvariantCulture) + sep + sorted[j].ToString(CultureInfo.InvariantCulture));
				else for(int k = i; k <= j; k++) parts.Add(sorted[k].ToString(CultureInfo.InvariantCulture));
				i = j + 1;
			}
			return string.Join(", ", parts.ToArray());
		}

		private static Regex WildcardRegex(string pattern)
		{
			string re = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
			return new Regex(re, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
		}

		#endregion

		#region ================== Evaluation

		internal ThingsFilterRuleOutcome Evaluate(Thing t, ThingTypeInfo ti, ThingsFilterContext ctx)
		{
			if(Array.IndexOf(AllSubjects, subject) < 0) return ThingsFilterRuleOutcome.Fail;
			if(!ctx.IsAvailable(this)) return ThingsFilterRuleOutcome.Skip;
			if(!isvalid) return ThingsFilterRuleOutcome.Fail;
			return (Matches(t, ti, ctx) ? ThingsFilterRuleOutcome.Pass : ThingsFilterRuleOutcome.Fail);
		}

		private bool Matches(Thing t, ThingTypeInfo ti, ThingsFilterContext ctx)
		{
			switch(subject)
			{
				case ThingsFilterRuleSubject.Type: return MatchNumber(t.Type);
				case ThingsFilterRuleSubject.Angle: return MatchNumber(Angle2D.RealToDoom(t.Angle));
				case ThingsFilterRuleSubject.ZHeight: return MatchNumber((int)t.Position.z);
				case ThingsFilterRuleSubject.Tag: return MatchNumber(t.Tag);
				case ThingsFilterRuleSubject.Action: return MatchNumber(t.Action);
				case ThingsFilterRuleSubject.Arg: return MatchNumber(t.Args[argindex]);

				case ThingsFilterRuleSubject.Category:
				{
					string name = (ti != null && ti.Category != null ? ti.Category.Name : string.Empty);
					bool incat = CategoryMatches(name, value.Trim(), includesubcategories);
					return (op == ThingsFilterRuleOperator.Equals ? incat : !incat);
				}

				case ThingsFilterRuleSubject.ClassName: return MatchText(ti != null ? ti.ClassName : null);
				case ThingsFilterRuleSubject.Title: return MatchText(ti != null ? ti.Title : null);

				case ThingsFilterRuleSubject.Flag:
				{
					bool set;
					t.Flags.TryGetValue(key, out set);
					return (op == ThingsFilterRuleOperator.IsTrue ? set : !set);
				}

				case ThingsFilterRuleSubject.UnknownType:
				{
					bool unknown = (ti == null || !ti.IsKnown);
					return (op == ThingsFilterRuleOperator.IsTrue ? unknown : !unknown);
				}

				case ThingsFilterRuleSubject.Field: return MatchField(t);

				case ThingsFilterRuleSubject.MatchesFilter:
				{
					ThingsFilter target = ctx.Resolve(key);
					if(target == null || ctx.Visiting.Contains(target)) return false;
					bool result = target.Qualifies(t, ti, ctx);
					return (op == ThingsFilterRuleOperator.IsTrue ? result : !result);
				}

				default: return false;
			}
		}

		internal static bool CategoryMatches(string thingcategory, string category, bool includesubcategories)
		{
			if(string.Equals(thingcategory, category, StringComparison.OrdinalIgnoreCase)) return true;
			return includesubcategories && category.Length > 0
				&& thingcategory.StartsWith(category + ".", StringComparison.OrdinalIgnoreCase);
		}

		private bool MatchNumber(int n)
		{
			switch(op)
			{
				case ThingsFilterRuleOperator.IsOneOf: return InRanges(n);
				case ThingsFilterRuleOperator.IsNotOneOf: return !InRanges(n);
				case ThingsFilterRuleOperator.Less: return n < number;
				case ThingsFilterRuleOperator.LessOrEqual: return n <= number;
				case ThingsFilterRuleOperator.Greater: return n > number;
				case ThingsFilterRuleOperator.GreaterOrEqual: return n >= number;
				default: return false;
			}
		}

		private bool InRanges(int n)
		{
			foreach(KeyValuePair<int, int> r in ranges)
				if(n >= r.Key && n <= r.Value) return true;
			return false;
		}

		private bool MatchText(string s)
		{
			s = s ?? string.Empty;
			switch(op)
			{
				case ThingsFilterRuleOperator.Equals: return string.Equals(s, value, StringComparison.OrdinalIgnoreCase);
				case ThingsFilterRuleOperator.NotEquals: return !string.Equals(s, value, StringComparison.OrdinalIgnoreCase);
				case ThingsFilterRuleOperator.Contains: return s.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
				case ThingsFilterRuleOperator.Matches: return wildcard.IsMatch(s);
				default: return false;
			}
		}

		private bool MatchField(Thing t)
		{
			UniValue uv;
			bool present = t.Fields.TryGetValue(key.Trim(), out uv) && uv != null && uv.Value != null;
			if(op == ThingsFilterRuleOperator.Exists) return present;
			if(op == ThingsFilterRuleOperator.IsAbsent) return !present;
			if(!present) return (op == ThingsFilterRuleOperator.NotEquals);

			object v = uv.Value;
			bool equal;
			if(v is bool)
			{
				if(IsCompare(op)) return false;
				if(op == ThingsFilterRuleOperator.Contains) return Convert.ToString(v, CultureInfo.InvariantCulture).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
				equal = valueisbool && (bool)v == boolvalue;
			}
			else if(v is int || v is double || v is float || v is long || v is short || v is byte)
			{
				double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
				switch(op)
				{
					case ThingsFilterRuleOperator.Less: return d < number;
					case ThingsFilterRuleOperator.LessOrEqual: return d <= number;
					case ThingsFilterRuleOperator.Greater: return d > number;
					case ThingsFilterRuleOperator.GreaterOrEqual: return d >= number;
					case ThingsFilterRuleOperator.Contains: return Convert.ToString(v, CultureInfo.InvariantCulture).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
				}
				equal = valueisnumber && d == number;
			}
			else
			{
				string s = Convert.ToString(v, CultureInfo.InvariantCulture);
				if(IsCompare(op)) return false;
				if(op == ThingsFilterRuleOperator.Contains) return s.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
				equal = string.Equals(s, value, StringComparison.OrdinalIgnoreCase);
			}
			return (op == ThingsFilterRuleOperator.Equals ? equal : !equal);
		}

		public override string ToString()
		{
			string s = SubjectTitle(subject);
			if(UsesKey(subject)) s += " " + key;
			s += " " + OperatorTitle(subject, op);
			if(UsesValue(subject, op)) s += " " + value;
			return s;
		}

		#endregion
	}
}
