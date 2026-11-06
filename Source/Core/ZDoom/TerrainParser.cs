using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;

namespace CodeImp.DoomBuilder.ZDoom
{
	internal sealed class TerrainParser : ZDTextParser
	{
		internal override ScriptType ScriptType { get { return ScriptType.TERRAIN; } }
		
		private readonly HashSet<string> terrainnames;
		public HashSet<string> TerrainNames { get { return terrainnames; } }

		public TerrainParser()
		{
			terrainnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		public override bool Parse(TextResourceData data, bool clearerrors)
		{
			//mxd. Already parsed?
			if(!base.AddTextResource(data))
			{
				if(clearerrors) ClearError();
				return true;
			}

			// Cannot process?
			if(!base.Parse(data, clearerrors)) return false;

			// Continue until at the end of the stream
			bool skipdefinitions = false;
			while(SkipWhitespace(true))
			{
				string token = ReadToken().ToLowerInvariant();
				if(string.IsNullOrEmpty(token)) continue;

				if(skipdefinitions)
				{
					do
					{
						SkipWhitespace(true);
						token = ReadToken();
					} while(!string.IsNullOrEmpty(token) && token != "endif");

					skipdefinitions = false;
					continue;
				}

				switch(token)
				{
					case "ifdoom":
					case "ifheretic":
					case "ifhexen":
					case "ifstrife":
						skipdefinitions = !GameType.BlockMatches(token.Substring(2), General.Map.Config.BaseGame);
						break;

					case "terrain":
						SkipWhitespace(true);
						token = ReadToken();
						if(string.IsNullOrEmpty(token))
						{
							ReportError("Expected terrain name");
							return false;
						}

						// Add to collection
						if(!terrainnames.Contains(token)) terrainnames.Add(token);
						break;

					case "{":
						// Skip inner properties
						do
						{
							SkipWhitespace(true);
							token = ReadToken();
						} while(!string.IsNullOrEmpty(token) && token != "}");
						break;
				}
			}

			return true;
		}
	}
}
