
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.Editing
{
	public enum ThingsFilterDisplayMode //mxd
	{
		ALWAYS,
		CLASSIC_MODES_ONLY,
		VISUAL_MODES_ONLY
	}
	
	public class ThingsFilter
	{
		#region ================== Constants

		#endregion

		#region ================== Variables

		// Display name of this filter
		protected string name;

		//mxd. Invert this filter?
		protected bool invert;

		//mxd. Display mode
		protected ThingsFilterDisplayMode displaymode;
		
		// Filter by category
		protected string categoryname;
		protected bool includesubcategories;

		// Rule list, combined with the other criteria by AND
		protected List<ThingsFilterRule> rules;
		protected ThingsFilterMatchMode matchmode;

		// Filter by properties
		protected int thingtype;		// -1 indicates not used
		protected int thingangle;		// -1 indicates not used
		protected int thingzheight;		// int.MinValue indicates not used
		
		// Filter by fields
		protected List<string> requiredfields;
		protected List<string> forbiddenfields;
		
		// Filter by action/tag
		protected int thingaction;		// -1 indicates not used
		protected int[] thingargs;		// -1 indicates not used
		protected int thingtag;			// -1 indicates not used
		
		// Filter by custom fields
		protected UniFields customfields;
		
		// List of things
		protected List<Thing> visiblethings;
		protected List<Thing> hiddenthings;
		protected Dictionary<Thing, bool> thingsvisiblestate;
		
		// Disposing
		protected bool isdisposed;

		#endregion

		#region ================== Properties

		public string Name { get { return name; } internal set { name = value; } }
		public string CategoryName { get { return categoryname; } internal set { categoryname = value; } }
		public bool IncludeSubcategories { get { return includesubcategories; } internal set { includesubcategories = value; } }
		public ThingsFilterMatchMode MatchMode { get { return matchmode; } internal set { matchmode = value; } }
		internal List<ThingsFilterRule> Rules { get { return rules; } }
		public bool Invert { get { return invert; } internal set { invert = value; } } //mxd
		public ThingsFilterDisplayMode DisplayMode { get { return displaymode; } internal set { displaymode = value; } } //mxd
		internal int ThingType { get { return thingtype; } set { thingtype = value; } }
		internal int ThingAngle { get { return thingangle; } set { thingangle = value; } }
		internal int ThingZHeight { get { return thingzheight; } set { thingzheight = value; } }
		internal int ThingAction { get { return thingaction; } set { thingaction = value; } }
		internal int[] ThingArgs { get { return thingargs; } set { Array.Copy(value, thingargs, Thing.NUM_ARGS); } }
		internal int ThingTag { get { return thingtag; } set { thingtag = value; } }
		internal UniFields ThingCustomFields { get { return customfields; } set { customfields = new UniFields(value); } }
		internal ICollection<string> RequiredFields { get { return requiredfields; } }
		internal ICollection<string> ForbiddenFields { get { return forbiddenfields; } }
		public ICollection<Thing> VisibleThings { get { return visiblethings; } }
		public ICollection<Thing> HiddenThings { get { return hiddenthings; } }
		internal bool IsDisposed { get { return isdisposed; } }
		public const string DEFAULT_NAME = "Unnamed filter"; //mxd
		
		#endregion
		
		#region ================== Constructor / Disposer
		
		// Copy constructor
		internal ThingsFilter(ThingsFilter f)
		{
			// Copy
			name = f.name;
			categoryname = f.categoryname;
			invert = f.invert; //mxd
			displaymode = f.displaymode; //mxd
			thingtype = f.thingtype;
			thingzheight = f.thingzheight;
			thingangle = f.thingangle;
			thingaction = f.thingaction;
			thingargs = new int[Thing.NUM_ARGS];
			Array.Copy(f.thingargs, thingargs, Thing.NUM_ARGS);
			thingtag = f.thingtag;
			customfields = new UniFields(f.customfields);
			requiredfields = new List<string>(f.requiredfields);
			forbiddenfields = new List<string>(f.forbiddenfields);
			includesubcategories = f.includesubcategories;
			matchmode = f.matchmode;
			rules = new List<ThingsFilterRule>(f.rules.Count);
			foreach(ThingsFilterRule r in f.rules) rules.Add(new ThingsFilterRule(r));
			
			AdjustForMapFormat();
			
			// We have no destructor
			GC.SuppressFinalize(this);
		}
		
		// Constructor for filter from configuration
		internal ThingsFilter(Configuration cfg, string path)
		{
			// Initialize
			requiredfields = new List<string>();
			forbiddenfields = new List<string>();
			thingargs = new int[Thing.NUM_ARGS];
			customfields = new UniFields();
			
			// Read settings from config
			name = cfg.ReadSetting(path + ".name", DEFAULT_NAME);
			categoryname = cfg.ReadSetting(path + ".category", "");
			invert = cfg.ReadSetting(path + ".invert", false); //mxd
			displaymode = (ThingsFilterDisplayMode)cfg.ReadSetting(path + ".displaymode", 0); //mxd
			thingtype = cfg.ReadSetting(path + ".type", -1);
			thingangle = cfg.ReadSetting(path + ".angle", -1);
			thingzheight = cfg.ReadSetting(path + ".zheight", int.MinValue);
			thingaction = cfg.ReadSetting(path + ".action", -1);
			for(int i = 0; i < Thing.NUM_ARGS; i++)
				thingargs[i] = cfg.ReadSetting(path + ".arg" + i.ToString(CultureInfo.InvariantCulture), -1);
			thingtag = cfg.ReadSetting(path + ".tag", -1);
			
			// Read flags
			// key is string, value must be boolean which indicates if
			// its a required field (true) or forbidden field (false).
			IDictionary fields = cfg.ReadSetting(path + ".fields", new Hashtable());
			foreach(DictionaryEntry de in fields)
			{
				// Add to the corresponding list
				if((bool)de.Value)
					requiredfields.Add(de.Key.ToString());
				else
					forbiddenfields.Add(de.Key.ToString());
			}
			
			// Custom fields
			IDictionary fieldvalues = cfg.ReadSetting(path + ".customfieldvalues", new Hashtable());
			foreach(DictionaryEntry fv in fieldvalues)
			{
				int ft = cfg.ReadSetting(path + ".customfieldtypes." + fv.Key, 0);
				customfields.Add(fv.Key.ToString(), new UniValue(ft, fv.Value));
			}

			// Rules, probed in written order (sections load sorted, so rule10 would come before rule2)
			includesubcategories = cfg.ReadSetting(path + ".subcategories", false);
			int mm = cfg.ReadSetting(path + ".matchmode", 0);
			matchmode = (mm == (int)ThingsFilterMatchMode.Any ? ThingsFilterMatchMode.Any : ThingsFilterMatchMode.All);
			rules = new List<ThingsFilterRule>();
			for(int i = 0; cfg.SettingExists(path + ".rules.rule" + i.ToString(CultureInfo.InvariantCulture)); i++)
				rules.Add(new ThingsFilterRule(cfg, path + ".rules.rule" + i.ToString(CultureInfo.InvariantCulture)));

			AdjustForMapFormat();
			
			// We have no destructor
			GC.SuppressFinalize(this);
		}

		// Constructor for a new filter
		internal ThingsFilter()
		{
			// Initialize everything as <any>
			requiredfields = new List<string>();
			forbiddenfields = new List<string>();
			customfields = new UniFields();
			categoryname = "";
			thingtype = -1;
			thingangle = -1;
			thingzheight = int.MinValue;
			thingaction = -1;
			thingargs = new int[Thing.NUM_ARGS];
			for(int i = 0 ; i < Thing.NUM_ARGS; i++) thingargs[i] = -1;
			thingtag = -1;
			name = DEFAULT_NAME;
			rules = new List<ThingsFilterRule>();
			matchmode = ThingsFilterMatchMode.All;
			includesubcategories = true;

			// We have no destructor
			GC.SuppressFinalize(this);
		}

		// Disposer
		internal virtual void Dispose()
		{
			// Not already disposed?
			if(!isdisposed)
			{
				// Clean up
				visiblethings = null;
				hiddenthings = null;
				thingsvisiblestate = null;
				
				// Done
				isdisposed = true;
			}
		}

		#endregion

		#region ================== Methods
		
		// This sets some fields to <any> when they are not valid for the current map format
		private void AdjustForMapFormat()
		{
			if((General.Map != null) && (General.Map.FormatInterface != null))
			{
				// Adjust as needed for map format
				if(!General.Map.FormatInterface.HasThingHeight) thingzheight = int.MinValue;
				if(!General.Map.FormatInterface.HasThingAction) thingaction = -1;
				if(!General.Map.FormatInterface.HasThingTag) thingtag = -1;
				if(!General.Map.FormatInterface.HasActionArgs)
				{
					for(int i = 0; i < Thing.NUM_ARGS; i++) thingargs[i] = -1;
				}
				if(!General.Map.FormatInterface.HasCustomFields) customfields.Clear();
			}
		}

		//mxd
		public void Validate() 
		{
			AdjustForMapFormat();

			//mxd. We don't want to keep unknown flags (like flags from different map format)
			if(General.Map.Config != null && General.Map.Config.ThingFlags != null)
			{
				List<String> unknownfields = new List<string>();
				foreach(String s in forbiddenfields)
				{
					if(!General.Map.Config.ThingFlags.ContainsKey(s)) unknownfields.Add(s);
				}

				if(unknownfields.Count > 0)
				{
					foreach(String s in unknownfields) forbiddenfields.Remove(s);
				}

				unknownfields.Clear();
				foreach(String s in requiredfields)
				{
					if(!General.Map.Config.ThingFlags.ContainsKey(s)) unknownfields.Add(s);
				}

				if(unknownfields.Count > 0)
				{
					foreach(String s in unknownfields) requiredfields.Remove(s);
				}
			}

			//Integrity check
			if(!IsValid())
				General.ErrorLogger.Add(ErrorType.Warning, "Things filter \"" + name + "\" has invalid properties. Configure the things filter to fix this!");

			if(HasInvalidRules(ThingsFilterContext.FromMap(General.Map.ConfigSettings != null ? General.Map.ConfigSettings.ThingsFilters : null)))
				General.ErrorLogger.Add(ErrorType.Warning, "Things filter \"" + name + "\" has invalid rules. Configure the things filter to fix this!");
		}

		//mxd
		public bool IsValid()
		{
			return (!string.IsNullOrEmpty(categoryname) 
				|| thingtype > 0 
				|| thingangle != -1
				|| thingzheight != int.MinValue 
				|| thingaction != -1 
				|| thingtag != -1
				|| requiredfields.Count > 0 
				|| forbiddenfields.Count > 0 
				|| customfields.Count > 0
				|| rules.Count > 0);
		}

		// Any rule that doesn't parse, names a missing filter or reaches a reference cycle
		internal bool HasInvalidRules(ThingsFilterContext ctx)
		{
			foreach(ThingsFilterRule r in rules)
				if(RuleError(r, ctx) != null) return true;
			return false;
		}

		// Why a rule is broken in this context, or null
		internal string RuleError(ThingsFilterRule r, ThingsFilterContext ctx)
		{
			if(!r.IsValidRule) return r.Error;
			if(r.Subject != ThingsFilterRuleSubject.MatchesFilter) return null;
			ThingsFilter target = ctx.Resolve(r.Key);
			if(target == null) return "No filter named \"" + r.Key + "\"";
			HashSet<ThingsFilter> visiting = new HashSet<ThingsFilter> { this };
			if(ReachesCycle(target, ctx, visiting)) return "Filter references form a cycle";
			return null;
		}

		private static bool ReachesCycle(ThingsFilter f, ThingsFilterContext ctx, HashSet<ThingsFilter> visiting)
		{
			if(!visiting.Add(f)) return true;
			try
			{
				foreach(ThingsFilterRule r in f.rules)
				{
					if(r.Subject != ThingsFilterRuleSubject.MatchesFilter || !r.IsValidRule) continue;
					ThingsFilter target = ctx.Resolve(r.Key);
					if(target != null && ReachesCycle(target, ctx, visiting)) return true;
				}
				return false;
			}
			finally
			{
				visiting.Remove(f);
			}
		}
		
		/// <summary>
		/// This checks if a thing is visible. Throws an exception when the specified Thing does not exist in the map (filter not updated?).
		/// </summary>
		public bool IsThingVisible(Thing t)
		{
			return (!t.IsDisposed && thingsvisiblestate[t]);
		}

		// This writes the filter to configuration
		internal void WriteSettings(Configuration cfg, string path)
		{
			// Write settings to config
			cfg.WriteSetting(path + ".name", name);
			cfg.WriteSetting(path + ".category", categoryname);
			cfg.WriteSetting(path + ".invert", invert); //mxd
			cfg.WriteSetting(path + ".displaymode", (int)displaymode); //mxd
			cfg.WriteSetting(path + ".type", thingtype);
			cfg.WriteSetting(path + ".angle", thingangle);
			cfg.WriteSetting(path + ".zheight", thingzheight);
			cfg.WriteSetting(path + ".action", thingaction);
			for(int i = 0; i < Thing.NUM_ARGS; i++)
				cfg.WriteSetting(path + ".arg" + i.ToString(CultureInfo.InvariantCulture), thingargs[i]);
			cfg.WriteSetting(path + ".tag", thingtag);
			
			// Write required fields to config
			foreach(string s in requiredfields)
				cfg.WriteSetting(path + ".fields." + s, true);
			
			// Write forbidden fields to config
			foreach(string s in forbiddenfields)
				cfg.WriteSetting(path + ".fields." + s, false);
			
			// Custom fields
			foreach(KeyValuePair<string, UniValue> u in customfields)
			{
				cfg.WriteSetting(path + ".customfieldtypes." + u.Key, u.Value.Type);
				cfg.WriteSetting(path + ".customfieldvalues." + u.Key, u.Value.Value);
			}

			cfg.WriteSetting(path + ".subcategories", includesubcategories);
			cfg.WriteSetting(path + ".matchmode", (int)matchmode);
			for(int i = 0; i < rules.Count; i++)
				rules[i].WriteSettings(cfg, path + ".rules.rule" + i.ToString(CultureInfo.InvariantCulture));
		}
		
		// This is called when the filter is activated
		internal virtual void Activate()
		{
			// Update the list of things
			Update();
		}
		
		// This is called when the filter is deactivates
		internal virtual void Deactivate()
		{
			// Clear lists
			visiblethings = null;
			hiddenthings = null;
			thingsvisiblestate = null;
		}
		
		/// <summary>
		/// This updates the list of things.
		/// </summary>
		public virtual void Update()
		{
			AdjustForMapFormat();
			
			// Make new list
			visiblethings = new List<Thing>(General.Map.Map.Things.Count);
			hiddenthings = new List<Thing>(General.Map.Map.Things.Count);
			thingsvisiblestate = new Dictionary<Thing, bool>(General.Map.Map.Things.Count);
			ThingsFilterContext ctx = ThingsFilterContext.FromMap(General.Map.ConfigSettings != null ? General.Map.ConfigSettings.ThingsFilters : null);
			foreach(Thing t in General.Map.Map.Things)
			{
				// Get thing info
				ThingTypeInfo ti = General.Map.Data.GetThingInfo(t.Type);
				bool qualifies = Qualifies(t, ti, ctx);

				// Put the thing in the lists. mxd: visiblethings and hiddenthings are only used from classic modes, IsThingVisible() is only used from visual mode.
				if(qualifies || displaymode == ThingsFilterDisplayMode.VISUAL_MODES_ONLY) visiblethings.Add(t); else hiddenthings.Add(t);
				thingsvisiblestate.Add(t, qualifies || (displaymode == ThingsFilterDisplayMode.CLASSIC_MODES_ONLY));
			}
		}

		// Whether a thing passes this filter: the property tabs, then the rule list, then invert
		internal bool Qualifies(Thing t, ThingTypeInfo ti, ThingsFilterContext ctx)
		{
				bool qualifies = true;

				// Check against simple properties
				qualifies &= (thingtype == -1) || (t.Type == thingtype);
				qualifies &= (thingangle == -1) || (Angle2D.RealToDoom(t.Angle) == thingangle);
				qualifies &= (thingzheight == int.MinValue) || ((int)(t.Position.z) == thingzheight);
				qualifies &= (thingaction == -1) || (t.Action == thingaction);
				qualifies &= (thingtag == -1) || (t.Tag == thingtag);
				for(int i = 0; i < Thing.NUM_ARGS; i++)
					qualifies &= (thingargs[i] == -1) || (t.Args[i] == thingargs[i]);
				
				// Still qualifies?
				if(qualifies)
				{
					// Check thing category
					if(ti.Category == null)
						qualifies = (categoryname.Length == 0);
					else if(includesubcategories)
						qualifies = (categoryname.Length == 0) || ThingsFilterRule.CategoryMatches(ti.Category.Name, categoryname, true);
					else
						qualifies = ((ti.Category.Name == categoryname) || (categoryname.Length == 0));
				}
				
				// Still qualifies?
				if(qualifies)
				{
					// Go for all required fields
					foreach(string s in requiredfields)
					{
						if(t.Flags.ContainsKey(s))
						{
							if(t.Flags[s] == false)
							{
								qualifies = false;
								break;
							}
						}
						else
						{
							qualifies = false;
							break;
						}
					}
				}

				// Still qualifies?
				if(qualifies)
				{
					// Go for all forbidden fields
					foreach(string s in forbiddenfields)
					{
						if(t.Flags.ContainsKey(s))
						{
							if(t.Flags[s])
							{
								qualifies = false;
								break;
							}
						}
					}
				}
				
				// Still qualifies?
				if(qualifies)
				{
					// Go for all required custom fields
					foreach(KeyValuePair<string, UniValue> kv in customfields)
					{
						if(t.Fields.ContainsKey(kv.Key))
						{
							if(!((t.Fields[kv.Key].Type == kv.Value.Type) && (t.Fields[kv.Key].Value.Equals(kv.Value.Value))))
							{
								qualifies = false;
								break;
							}
						}
						else
						{
							qualifies = false;
							break;
						}
					}
				}

				if(qualifies && rules.Count > 0) qualifies = RulesPass(t, ti, ctx);

				//mxd. Apply inversion
				qualifies ^= invert;
				return qualifies;
		}

		// Skips don't count; All passes when nothing fails, Any when something passes
		private bool RulesPass(Thing t, ThingTypeInfo ti, ThingsFilterContext ctx)
		{
			bool added = ctx.Visiting.Add(this);
			try
			{
				bool counted = false;
				foreach(ThingsFilterRule r in rules)
				{
					ThingsFilterRuleOutcome o = r.Evaluate(t, ti, ctx);
					if(o == ThingsFilterRuleOutcome.Skip) continue;
					counted = true;
					if(matchmode == ThingsFilterMatchMode.Any && o == ThingsFilterRuleOutcome.Pass) return true;
					if(matchmode == ThingsFilterMatchMode.All && o == ThingsFilterRuleOutcome.Fail) return false;
				}
				return (!counted || matchmode == ThingsFilterMatchMode.All);
			}
			finally
			{
				if(added) ctx.Visiting.Remove(this);
			}
		}

		// String representation
		public override string ToString()
		{
			return name;
		}
		
		#endregion
	}
}
