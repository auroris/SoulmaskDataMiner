// Copyright 2026 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Newtonsoft.Json.Linq;
using SoulmaskDataMiner.IO;
using System.Text;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Mines the localized format strings for the in-game activity logs:
	///   - tribesman work log   (enum EJingYingRiZhiType)
	///   - clan / tribe log      (enum EGongHuiRiZhi)
	///   - task-state reasons    (FText namespace "WS", keys RenWuState_*)
	/// Emits one CSV per table, per language, into a "Log" subfolder.
	/// </summary>
	/// <remarks>
	/// Enum value -> member name comes from EnumsInfo.json (sibling of the --classes
	/// ClassesInfo.json), so [RequireClassData] gates this miner on that dump being present.
	/// The strings themselves are read from the localized resources for the current culture,
	/// which the provider has already switched to, so a normal multi-language run produces
	/// correctly translated output with no extra work.
	/// </remarks>
	[MinerName("Log"), RequireClassData(true)]
	internal class LogMiner : MinerBase
	{
		// EJingYingRiZhiType value -> the FText key of its in-game work-log line.
		//
		// The work-log line strings live only as runtime FText literals in the empty
		// namespace, keyed by an auto-generated GUID. (The enum also carries tooltip
		// metadata under UObjectToolTips/EJingYingRiZhiType.<member>, but that is editor
		// metadata is stale dev text, so it cannot be
		// used.) Each English string is noted for maintainability; if a game patch
		// re-authors a line its GUID changes and the miner logs a warning for that type
		// (it resolves to empty), flagging the entry that needs a refresh.
		private static readonly IReadOnlyDictionary<int, string> sWorkLogTextKeys = new Dictionary<int, string>
		{
			[0]  = "B5F9FACF488914030D225FBF4D92E6AA", // {0} work started
			[1]  = "410BCEB44B0D36973574F5924EA20782", // {0} work paused
			[2]  = "89EB9F904BAAEDBD5FC0C88F88AD0008", // Craft {1} in <{0}>.
			[3]  = "F91FF41D44273DA3B49E498ACB20E3B4", // Add {1} in <{0}>.
			[4]  = "946CB5A74BE17135A5323AA5508191A2", // Withdraw {1} from <{0}>.
			[5]  = "F4068B08489D28F27B96F5A2CA08F50E", // Go collect {2} in <{0}>({1}).
			[6]  = "9FFB1CC547714B137D4854BAA0E778D3", // Go collect resources in <{0}>.
			[7]  = "EB41920144D93DA4FB829DA3801712B4", // Go to <{0}> and manage nearby fields.
			[8]  = "B765D3964479221CB02AD3A5FB7E5FCE", // Attend to the crafting table {0}
			[9]  = "AB0FA9E84E901C4D95FE80828BBB609C", // Attend to the Crafting Table in <{0}>.
			[10] = "A91233AC4D7F731B6700BCB1F9AF4F0A", // Craft {1} at <{0}> to stop (reason: {2})
			[11] = "ACDF7FBA496D759485FD00863334A875", // Place {1} in <{0}> to stop (reason: {2})
			[12] = "C6E56A674C0CB1E17D21BFA968E51375", // Withdraw {1} from <{0}> to stop (reason: {2})
			[13] = "3FAAB4AB4FEEBB95AEAFA0AD7A15C789", // Stops collecting {2} at <{0}> {1} (reason: {3})
			[14] = "DA93D49C4F431225D6F6D1831E57C023", // Collect resources at <{0}> to stop (reason: {1}).
			[15] = "5D13139D419CA7F914021AA0313CC592", // Manage surrounding farmlands at <{0}> to stop (reason: {1}).
			[16] = "CF9D0770447E759269892184FC98B95B", // Stop attending to the Crafting Table {0} (reason: {1})
			[17] = "28F132E94C65B8781480D99848FDFCC4", // Craft {1} at <{0}> to complete.
			[18] = "86D31EA04B042E522C0656B551D1633D", // Place {1} in <{0}> to complete.
			[19] = "B44516FE466BFF65B269ADA3313298FB", // Withdraw {1} from <{0}> to complete.
			[20] = "A534751A4134454CEED63EBA0F7D2982", // Completes collecting {2} at <{0}>({1}).
			[21] = "98CFD41544A336D20908A5BC64474185", // Complete resource collection at <{0}>.
			[22] = "937DA9384DF9FD778A371682FAA6E822", // Maintain the camp in <{0}>.
			[23] = "30701CF04F4BF8FDF3EE3BAEA5ED9648", // Maintain the camp at <{0}> to stop (reason: {1}).
			[24] = "87BB30B040344AF3189AF9B7692A43A5", // Exploit <{0}>.
			[25] = "01AC935147C511EB0096CC8BF6AA02A7", // Go to <{0}> and manage nearby pens.
			[26] = "72DE7C694225B0C2813EDAADE5195002", // Manage and stop the pens around <{0}> (reason: {1}).
			[27] = "C359A89D41D76A778600ACB29FEA1C8A", // Upgrade building at <{0}>
			[28] = "F99A93A74D99B9D8DA229D896198498C", // Stopped upgrading building at <{0}> (reason: {1})
			[29] = "F1DC5D0846BCDF0EFF175791F448AEBE", // Sort items at <{0}>
			[30] = "C536A33D4CB3B8C574DA58BBB1B87F07", // Stopped sorting items at <{0}> (reason: {1})
			[31] = "D7D0DCF2474C4AED5BFFCC9904485F50", // Go to <{0}> for construction procurement
			[32] = "E798FE6F47F3EF7422C192ADC3E0F7DF", // Stopped construction procurement at <{0}> (Reason: {1})
			[33] = "B0E621764AFA30C16D921DA8E070E3A6", // Go to <{0}> to collect loot
			[34] = "F858E91745F1FC1005B618B11DDABA26", // Stopped collecting loot at <{0}> (Reason: {1})
		};

		private const string EmptyNamespace = "";
		private const string ClanLogNamespace = "UHUIGongHuiRiZhi";
		private const string ReasonNamespace = "WS";
		private const string ReasonKeyPrefix = "RenWuState_";

		private static readonly object sEnumCacheLock = new();
		private static Dictionary<string, IReadOnlyDictionary<int, string>>? sEnumCache;

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			string? enumsPath = GetEnumsInfoPath(config);
			if (enumsPath is null)
			{
				logger.Error("[Log] Could not locate EnumsInfo.json next to the --classes file. It is required to map enum values to member names.");
				return false;
			}

			IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> loc = providerManager.Provider.LocalizedResources;

			bool success = true;
			success &= WriteEnumLog(enumsPath, loc, config, logger, "EJingYingRiZhiType", "WorkLog.csv", ResolveWorkLog);
			success &= WriteEnumLog(enumsPath, loc, config, logger, "EGongHuiRiZhi", "ClanLog.csv", ResolveClanLog);
			success &= WriteReasons(loc, config, logger);
			return success;
		}

		private delegate string? ResolveText(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> loc, int value, string member);

		private string? ResolveWorkLog(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> loc, int value, string member)
		{
			return sWorkLogTextKeys.TryGetValue(value, out string? guid) ? Get(loc, EmptyNamespace, guid) : null;
		}

		private string? ResolveClanLog(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> loc, int value, string member)
		{
			// The clan-log widget's format-string map is the only localized source. Members
			// with no entry here have no in-game clan-log line and are left to the caller's
			// fallback rather than borrowing English-only enum tooltip metadata.
			return Get(loc, ClanLogNamespace, $"GongHuiRiZhiTxt {member}");
		}

		private bool WriteEnumLog(string enumsPath, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> loc, Config config, Logger logger, string enumName, string fileName, ResolveText resolve)
		{
			IReadOnlyDictionary<int, string>? members = LoadEnumMembers(enumsPath, enumName, logger);
			if (members is null) return false;

			string outPath = Path.Combine(config.OutputDirectory, Name, fileName);
			using FileStream stream = IOUtil.CreateFile(outPath, logger);
			using StreamWriter writer = new(stream, Encoding.UTF8);

			writer.WriteLine("id,member,name");

			int written = 0, skipped = 0;
			foreach (KeyValuePair<int, string> entry in members.OrderBy(kv => kv.Key))
			{
				if (IsSentinel(entry.Value)) continue;

				string? text = resolve(loc, entry.Key, entry.Value);
				if (string.IsNullOrEmpty(text)) { ++skipped; continue; }

				writer.WriteLine($"{entry.Key},{CsvStr(entry.Value)},{CsvStr(text)}");
				++written;
			}

			logger.Information($"[Log] {fileName}: wrote {written} entries" + (skipped > 0 ? $", {skipped} had no string" : "."));
			return true;
		}

		private bool WriteReasons(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> loc, Config config, Logger logger)
		{
			if (!loc.TryGetValue(ReasonNamespace, out IReadOnlyDictionary<string, string>? ws))
			{
				logger.Warning($"[Log] Localization namespace \"{ReasonNamespace}\" not found; skipping reasons.");
				return true;
			}

			string outPath = Path.Combine(config.OutputDirectory, Name, "Reason.csv");
			using FileStream stream = IOUtil.CreateFile(outPath, logger);
			using StreamWriter writer = new(stream, Encoding.UTF8);

			writer.WriteLine("key,name");

			int written = 0;
			foreach (KeyValuePair<string, string> pair in ws.Where(kv => kv.Key.StartsWith(ReasonKeyPrefix)).OrderBy(kv => kv.Key))
			{
				if (string.IsNullOrEmpty(pair.Value)) continue;
				string member = pair.Key.Substring(ReasonKeyPrefix.Length);
				writer.WriteLine($"{CsvStr(member)},{CsvStr(pair.Value)}");
				++written;
			}

			logger.Information($"[Log] Reason.csv: wrote {written} entries.");
			return true;
		}

		private static string? Get(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> loc, string ns, string key)
		{
			return loc.TryGetValue(ns, out IReadOnlyDictionary<string, string>? inner) && inner.TryGetValue(key, out string? value)
				? value
				: null;
		}

		private static bool IsSentinel(string member)
		{
			return member.Equals("Max", StringComparison.OrdinalIgnoreCase) || member.EndsWith("_MAX", StringComparison.Ordinal);
		}

		private static string? GetEnumsInfoPath(Config config)
		{
			if (config.ClassesPath is null) return null;
			string? dir = Path.GetDirectoryName(config.ClassesPath);
			if (dir is null) return null;
			string path = Path.Combine(dir, "EnumsInfo.json");
			return File.Exists(path) ? path : null;
		}

		private static IReadOnlyDictionary<int, string>? LoadEnumMembers(string enumsPath, string enumName, Logger logger)
		{
			lock (sEnumCacheLock)
			{
				sEnumCache ??= new();
				if (sEnumCache.TryGetValue(enumName, out IReadOnlyDictionary<int, string>? cached)) return cached;

				try
				{
					JObject root = JObject.Parse(File.ReadAllText(enumsPath));
					JArray data = (JArray)root["data"]!;
					foreach (JObject entry in data)
					{
						JProperty prop = entry.Properties().First();
						if (!prop.Name.Equals(enumName, StringComparison.Ordinal)) continue;

						JArray memberArray = (JArray)((JArray)prop.Value)[0];
						Dictionary<int, string> map = new();
						foreach (JObject member in memberArray)
						{
							JProperty mp = member.Properties().First();
							map[(int)mp.Value!] = mp.Name;
						}
						sEnumCache[enumName] = map;
						return map;
					}
					logger.Error($"[Log] Enum \"{enumName}\" not found in EnumsInfo.json.");
					return null;
				}
				catch (Exception ex)
				{
					logger.Error($"[Log] Failed to read EnumsInfo.json: [{ex.GetType().Name}] {ex.Message}");
					return null;
				}
			}
		}
	}
}
