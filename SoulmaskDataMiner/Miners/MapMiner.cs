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

using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using SoulmaskDataMiner.MapUtil;
using SoulmaskDataMiner.MapUtil.Processor;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Mines map images and information about points of interest
	/// </summary>
	[MinerName("Map"), RequireHierarchy(true), RequireLootDatabase(true)]
	internal class MapMiner : MinerBase
	{
		private static readonly MapData sMapData;

		static MapMiner()
		{
			sMapData = new();
		}

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			Package levelsTablePackage = (Package)providerManager.Provider.LoadPackage("WS/Content/Blueprints/DataTable/DT_GameplayLevel.uasset");
			UDataTable levelsTable = (UDataTable)levelsTablePackage.ExportMap[0].ExportObject.Value;

			Dictionary<string, string> mapNameToLevelPath = new();
			foreach (var pair in levelsTable.RowMap)
			{
				string mapName = pair.Key.Text;
				string? mainLevelPath = pair.Value.Properties.FirstOrDefault(p => p.Name.Text.Equals("LevelPath_3_7DE224FB4A21A1EB5C1CAA8FE6D675FF"))?.Tag?.GetValue<string>();
				if (mainLevelPath is null)
				{
					logger.Warning($"Failed to find main level path for map {mapName}. Skipping.");
					continue;
				}
				mapNameToLevelPath.Add(mapName, mainLevelPath);
			}

			MapPoiStaticData? mapPoiStaticData = MapPoiStaticData.Build(mapNameToLevelPath, providerManager, logger);
			if (mapPoiStaticData is null)
			{
				// BuildPoiStaticData prints its own error messages, so we don't need one here
				return false;
			}

			bool success = true;
			List<MapInfo> allMapData = new();
			HashSet<NpcData> allBabies = new();
			Dictionary<string, IReadOnlyDictionary<int, EventData>> eventsPerMap = new();
			foreach (var pair in mapNameToLevelPath)
			{
				logger.Important($"Map: {pair.Key}");
				if (RunMap(
					pair.Key,
					pair.Value,
					mapPoiStaticData,
					providerManager,
					config,
					logger,
					allBabies,
					out MapInfo? mapData,
					out EventMap? eventsMap,
					out IReadOnlyDictionary<int, List<SpawnData>>? eventSpawnMap))
				{
					allMapData.Add(mapData);
					eventsPerMap.Add(pair.Key, EventUtil.ProcessEventMap(eventsMap, eventSpawnMap, pair.Key, logger));
				}
				else
				{
					success = false;
				}
			}

			WriteBabies(allBabies, config, logger, sqlWriter);
			WriteEventsCsv(eventsPerMap, config, logger);

			WriteSql(allMapData, sqlWriter, logger);
			sqlWriter.WriteEmptyLine();
			WriteEventSql(eventsPerMap, sqlWriter, logger);

			return success;
		}

		private bool RunMap(
			string mapName,
			string mainLevelPath,
			MapPoiStaticData mapPoiStaticData,
			IProviderManager providerManager,
			Config config,
			Logger logger,
			ISet<NpcData> allBabies,
			[NotNullWhen(true)] out MapInfo? mapData,
			[NotNullWhen(true)] out EventMap? eventsMap,
			[NotNullWhen(true)] out IReadOnlyDictionary<int, List<SpawnData>>? eventSpawnMap)
		{
			mapData = null;
			eventsMap = null;
			eventSpawnMap = null;

			MapLevelData? mapLevelData = MapLevelData.Load(mapName, mainLevelPath, providerManager, logger);
			if (mapLevelData is null)
			{
				logger.Error($"Failed to load level data for map {mapName}");
				return false;
			}

			bool success = true;

			logger.Information("Exporting map images...");
			if (!ExportMapImages(mapLevelData, providerManager, config, logger))
			{
				logger.Error("Failed to export all map images.");
				success = false;
			}

			logger.Information("<<< Begin processing map >>>");
			bool processSuccess = ProcessMap(mapLevelData, mapPoiStaticData, providerManager, logger, allBabies, out mapData, out eventsMap, out eventSpawnMap);
			logger.Information("<<< Finished processing map >>>");
			if (!processSuccess)
			{
				// ProcessMap prints its own error messages, so we don't need one here
				return false;
			}

			logger.Information("Exporting data...");
			WriteIcons(mapData!, config, logger);
			WriteCsv(mapData!, config, logger);

			return success;
		}

		private bool ExportMapImages(MapLevelData mapLevelData, IProviderManager providerManager, Config config, Logger logger)
		{
			string outDir = Path.Combine(config.OutputDirectory, Name, mapLevelData.MapName);

			bool success = true;

			UTexture2D? mapTexture = mapLevelData.WorldSettings.Properties.FirstOrDefault(p => p.Name.Text.Equals("Map2DTexture"))?.Tag?.GetValue<FPackageIndex>()?.ResolvedObject?.Load() as UTexture2D;
			if (mapTexture is not null)
			{
				success &= TextureExporter.ExportTexture(config,mapTexture, false, logger, outDir);
			}
			else
			{
				success = false;
			}

			UMaterial? mapMaskMaterial = mapLevelData.WorldSettings.Properties.FirstOrDefault(p => p.Name.Text.Equals("SlateBrushMapMiWu"))?.Tag?.GetValue<FStructFallback>()?
				.Properties.FirstOrDefault(p => p.Name.Text.Equals("ResourceObject"))?.Tag?.GetValue<FPackageIndex>()?.ResolvedObject?.Load() as UMaterial;
			if (mapMaskMaterial is not null)
			{
				foreach (UTexture2D? texture in mapMaskMaterial.ReferencedTextures)
				{
					if (texture is null || texture.Name.Equals(mapTexture?.Name)) continue;

					success &= TextureExporter.ExportTexture(config,texture, false, logger, outDir);
				}
			}

			return success;
		}

		private bool ProcessMap(
			MapLevelData mapLevelData,
			MapPoiStaticData poiStaticData,
			IProviderManager providerManager,
			Logger logger,
			ISet<NpcData> allBabies,
			[NotNullWhen(true)] out MapInfo? mapData,
			[NotNullWhen(true)] out EventMap? eventsMap,
			[NotNullWhen(true)] out IReadOnlyDictionary<int, List<SpawnData>>? eventSpawnMap)
		{
			mapData = null;
			eventsMap = null;
			eventSpawnMap = null;

			logger.Information("Loading dependencies...");

			MapPoiDatabase? poiDatabase = MapPoiLoader.Load(mapLevelData.MapName, poiStaticData, providerManager.Achievements, logger);
			if (poiDatabase is null)
			{
				logger.Error("Failed to load map POIs.");
				return false;
			}

			DungeonUtil dungeonUtil = new(mapLevelData);
			poiDatabase.DungeonMap = dungeonUtil.LoadDungeonData(logger)!;
			if (poiDatabase.DungeonMap is null)
			{
				logger.Error("Failed to load dungeons.");
				return false;
			}

			if (!MapObjectSearcher.FindTabletData(providerManager, poiDatabase, providerManager.Achievements, logger))
			{
				logger.Error("Failed to load tablets.");
				return false;
			}

			ChestDistributionMap chestDistributionMap = new(mapLevelData);
			IReadOnlyList<DistributionChestData>? distributedChests = chestDistributionMap.Load(logger);
			if (distributedChests is null)
			{
				logger.Error("Failed to load chest distribution data.");
				return false;
			}

			if (!MapObjectSearcher.FindMapObjects(providerManager, logger, mapLevelData, poiDatabase,
				out IReadOnlyList<FObjectExport>? poiObjects,
				out IReadOnlyList<FObjectExport>? tabletObjects,
				out IReadOnlyList<FObjectExport>? respawnObjects,
				out IReadOnlyList<SpawnerObject>? spawnerObjects,
				out IReadOnlyList<FObjectExport>? barracksObjects,
				out IReadOnlyList<ObjectWithDefaults>? fireflyObjects,
				out IReadOnlyList<ObjectWithDefaults>? chestObjects,
				out IReadOnlyList<FObjectExport>? dungeonObjects,
				out IReadOnlyList<FObjectExport>? arenaObjects,
				out IReadOnlyList<FObjectExport>? gamefunctionObjects,
				out IReadOnlyList<FObjectExport>? minePlatformObjects,
				out IReadOnlyList<FObjectExport>? mineralVeinObjects,
				out IReadOnlyList<FObjectExport>? eventManagerObjects))
			{
				logger.Error("Failed to find map objects.");
				return false;
			}

			FoliageUtil foliageUtil = new(sMapData);
			IReadOnlyDictionary<EProficiency, IReadOnlyDictionary<string, FoliageData>>? foliageData = foliageUtil.LoadFoliage(providerManager, mapLevelData, logger);
			if (foliageData is null)
			{
				logger.Error("Failed to load foliage.");
				return false;
			}

			IReadOnlyDictionary<int, ArenaRewardData>? arenaRewardMap = ArenaUtil.LoadRewardData(providerManager, logger);
			if (arenaRewardMap is null)
			{
				logger.Error("Failed to load arena reward data.");
				return false;
			}

			eventsMap = EventUtil.BuildEventMap(eventManagerObjects, logger);

			new PoiProcessor(sMapData).Process(poiDatabase, poiObjects, logger);
			new TabletProcessor(sMapData).Process(poiDatabase, tabletObjects, logger);
			new RespawnProcessor(sMapData).Process(poiDatabase, respawnObjects, logger);
			new SpawnProcessor(sMapData).Process(poiDatabase, spawnerObjects, barracksObjects, eventsMap, logger, allBabies);
			new CrowdProcessor(sMapData).Process(poiDatabase, fireflyObjects, logger);
			new ChestProcessor(sMapData).Process(poiDatabase, chestObjects, distributedChests, logger);
			new FoliageProcessor(sMapData).Process(poiDatabase, foliageData, logger);
			new DungeonProcessor(sMapData).Process(poiDatabase, dungeonObjects, logger);
			new WorldBossProcessor(sMapData).Process(poiDatabase, gamefunctionObjects, logger);
			new ArenaProcessor(sMapData).Process(poiDatabase, arenaObjects, arenaRewardMap, logger);
			new MinePlatformProcessor(sMapData).Process(poiDatabase, minePlatformObjects, logger);
			new MineralVeinProcessor(sMapData).Process(poiDatabase, mineralVeinObjects, providerManager, logger);

			new PoiProcessor(sMapData).FindPoiTextures(poiDatabase, logger);

			mapData = new(mapLevelData.MapName, poiDatabase.GetAllPois(), poiDatabase.AdditionalIconsToExport.ToArray());
			eventSpawnMap = (IReadOnlyDictionary<int, List<SpawnData>>)poiDatabase.EventSpawnMap;

			return true;
		}

		private void WriteIcons(MapInfo mapData, Config config, Logger logger)
		{
			string outDir = Path.Combine(config.OutputDirectory, Name, "Icons");

			HashSet<string> exported = new();
			foreach (var pair in mapData.POIs)
			{
				if (exported.Add(pair.Value[0].Icon.Name))
				{
					TextureExporter.ExportTexture(config,pair.Value[0].Icon, false, logger, outDir);
				}
				foreach (MapPoi poi in pair.Value)
				{
					if (poi.Achievement?.Icon is null) continue;

					if (exported.Add(poi.Achievement.Icon.Name))
					{
						TextureExporter.ExportTexture(config,poi.Achievement.Icon, false, logger, outDir);
					}
				}
			}

			foreach (UTexture2D icon in mapData.AdditionalMapIcons)
			{
				if (exported.Add(icon.Name))
				{
					TextureExporter.ExportTexture(config,icon, false, logger, outDir);
				}
			}
		}

		private void WriteCsv(MapInfo mapData, Config config, Logger logger)
		{
			string valOrNull(float value)
			{
				return value == 0.0f ? "" : value.ToString();
			}

			foreach (var pair in mapData.POIs)
			{
				string outPath = Path.Combine(config.OutputDirectory, Name, mapData.MapName, $"{pair.Key}.csv");
				using FileStream outFile = IOUtil.CreateFile(outPath, logger);
				using StreamWriter writer = new(outFile, Encoding.UTF8);

				writer.WriteLine("modes,gpIdx,gpName,key,type,posX,posY,posZ,mapX,mapY,mapR,title,name,desc,extra,region,npc,m,f,stat,occ,clantype,clanarea,clanocc,num,max,intr,player,building,loot,lootitem,lootmap,equipmap,collectmap,unlocks,icon,ach,achDesc,achIcon,inDun,dunInfo,bossInfo,arenaInfo,chestWeather");

				foreach (MapPoi poi in pair.Value)
				{
					if (poi.Location == FVector.ZeroVector)
					{
						logger.Debug($"POI with missing location: {poi.Title} ({poi.Type})");
						continue;
					}

					string spawnerSegment = ",,,,,,,,";
					string poiSegment = ",,";
					if (poi.GroupIndex == SpawnLayerGroup.PointOfInterest)
					{
						poiSegment = $"{CsvStr(poi.Achievement?.Name)},{CsvStr(poi.Achievement?.Description)},{CsvStr(poi.Achievement?.Icon?.Name)}";
					}
					else
					{
						spawnerSegment = $"{(int?)poi.NpcCategory},{poi.Male},{poi.Female},{CsvStr(poi.TribeStatus)},{CsvStr(poi.Occupation)},{poi.ClanType},{CsvStr(poi.ClanAreas)},{poi.ClanOccupations},{poi.SpawnCount},{poi.SpawnCountMax},{poi.SpawnInterval},{poi.PlayerExclusionRadius},{poi.BuildingExclusionRadius}";
					}

					string lootSegment = $"{CsvStr(poi.LootId)},{CsvStr(poi.LootItem)},{CsvStr(poi.LootMap)},{CsvStr(poi.Equipment)},{CsvStr(poi.CollectMap)}";

					string posSegment = ",,";
					if (poi.Location.HasValue)
					{
						posSegment = $"{poi.Location.Value.X:0},{poi.Location.Value.Y:0},{poi.Location.Value.Z:0}";
					}

					string gameMode = poi.GameModeMask.HasValue ? (poi.GameModeMask.Value).ToString() : string.Empty;

					writer.WriteLine(
						$"{gameMode},{(int)poi.GroupIndex},{CsvStr(MapStringUtil.GetGroupName(poi.GroupIndex))},{poi.Key},{CsvStr(poi.Type)},{posSegment},{poi.MapLocation.X:0},{poi.MapLocation.Y:0},{valOrNull(poi.MapRadius)},{CsvStr(poi.Title)},{CsvStr(poi.Name)},{CsvStr(poi.Description)},{CsvStr(poi.Extra)}," +
						$"{CsvStr(poi.Region)},{spawnerSegment},{lootSegment},{CsvStr(poi.Unlocks)},{CsvStr(poi.Icon?.Name)},{poiSegment},{poi.InDungeon},{CsvStr(poi.DungeonInfo)},{CsvStr(poi.BossInfo)},{CsvStr(poi.ArenaInfo)},{CsvStr(poi.ChestWeatherRule)}");
				}
			}
		}

		private void WriteSql(IEnumerable<MapInfo> allMapData, ISqlWriter sqlWriter, Logger logger)
		{
			// Schema
			// create table `poi` (
			//   `map` varchar(31) not null,
			//   `modes` tinyint unsigned,
			//   `gpIdx` int not null,
			//   `gpName` varchar(63) not null,
			//   `key` int,
			//   `type` varchar(63) not null,
			//   `posX` float,
			//   `posY` float,
			//   `posZ` float,
			//   `mapX` int not null,
			//   `mapY` int not null,
			//   `mapX2` int,
			//   `mapY2` int,
			//   `title` varchar(127),
			//   `name` varchar(127),
			//   `desc` varchar(511),
			//   `extra` varchar(511),
			//   `region` varchar(63),
			//   `npc` int,
			//   `m` bool,
			//   `f` bool,
			//   `stat` varchar(63),
			//   `occ` varchar(127),
			//   `clantype` int,
			//   `clanareas` varchar[31],
			//   `clanocc` varchar[31],
			//   `num` int,
			//   `max` int,
			//   `intr` float,
			//   `player` float,
			//   `building` float,
			//   `loot` varchar(127),
			//   `lootitem` varchar(127),
			//   `lootmap` varchar(255),
			//   `equipmap` varchar(2047),
			//   `collectmap` varchar(511),
			//   `unlocks` varchar(255),
			//   `icon` varchar(127),
			//   `ach` varchar(127),
			//   `achDesc` varchar(255),
			//   `achIcon` varchar(127),
			//   `inDun` bool,
			//   `dunInfo` varchar(1535),
			//   `bossInfo` varchar(1535),
			//   `arenaInfo` varchar(4095),
			//   `chestWeather` varchar(255)
			// )

			string valOrNull(float value)
			{
				return value == 0.0f ? "null" : value.ToString();
			}

			sqlWriter.WriteStartTable("poi");

			foreach (MapInfo mapData in allMapData)
			{
				foreach (var pair in mapData.POIs)
				{
					foreach (MapPoi poi in pair.Value)
					{
						// This is because some ancient tablets come from dungeons or pyramids instead of spawning in the world.
						if (poi.Location == FVector.ZeroVector) continue;

						string spawnerSegment = "null, null, null, null, null, null, null, null, null, null, null, null, null";
						string poiSegment = "null, null, null";
						if (poi.GroupIndex == SpawnLayerGroup.PointOfInterest)
						{
							poiSegment = $"{DbStr(poi.Achievement?.Name)}, {DbStr(poi.Achievement?.Description)}, {DbStr(poi.Achievement?.Icon?.Name)}";
						}
						else
						{
							spawnerSegment = $"{DbVal((int?)poi.NpcCategory)}, {DbBool(poi.Male)}, {DbBool(poi.Female)}, {DbStr(poi.TribeStatus)}, {DbStr(poi.Occupation)}, {DbVal(poi.ClanType)}, {DbStr(poi.ClanAreas)}, {DbStr(poi.ClanOccupations)}, {poi.SpawnCount}, {poi.SpawnCountMax}, {poi.SpawnInterval}, {poi.PlayerExclusionRadius}, {poi.BuildingExclusionRadius}";
						}

						string lootSegment = $"{DbStr(poi.LootId)}, {DbStr(poi.LootItem)}, {DbStr(poi.LootMap)}, {DbStr(poi.Equipment)}, {DbStr(poi.CollectMap)}";

						string posSegment = "null, null, null";
						if (poi.Location.HasValue)
						{
							posSegment = $"{poi.Location.Value.X:0}, {poi.Location.Value.Y:0}, {poi.Location.Value.Z:0}";
						}

						sqlWriter.WriteRow(
							$"{DbStr(mapData.MapName)}, {DbVal(poi.GameModeMask)}, {(int)poi.GroupIndex}, {DbStr(MapStringUtil.GetGroupName(poi.GroupIndex))}, {DbVal(poi.Key)}, {DbStr(poi.Type)}, {posSegment}, {poi.MapLocation.X:0}, {poi.MapLocation.Y:0}, {valOrNull(poi.MapRadius)}, {DbStr(poi.Title)}, {DbStr(poi.Name)}, {DbStr(poi.Description)}, {DbStr(poi.Extra)}, " +
							$"{DbStr(poi.Region)}, {spawnerSegment}, {lootSegment}, {DbStr(poi.Unlocks)}, {DbStr(poi.Icon?.Name)}, {poiSegment}, {DbBool(poi.InDungeon)}, {DbStr(poi.DungeonInfo)}, {DbStr(poi.BossInfo)}, {DbStr(poi.ArenaInfo)}, {DbStr(poi.ChestWeatherRule)}");
					}
				}
			}

			sqlWriter.WriteEndTable();
		}

		private static readonly MinerTable<NpcData> sBabiesTable = new(
			csvFileName: "babies.csv",
			sqlTableName: null,
			columns:
			[
				TableColumn.Str<NpcData>("class", b => b.CharacterClass.Owner!.Name),
				TableColumn.Str<NpcData>("name", b => b.Name),
			]);

		private void WriteBabies(IReadOnlySet<NpcData> babies, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			WriteTable(babies, sBabiesTable, config, logger, sqlWriter);
		}

		private void WriteEventsCsv(IReadOnlyDictionary<string, IReadOnlyDictionary<int, EventData>> eventsPerMap, Config config, Logger logger)
		{
			string outRoot = Path.Combine(config.OutputDirectory, Name);
			foreach (var mapPair in eventsPerMap.OrderBy(p => p.Key))
			{
				string outPath = Path.Combine(outRoot, mapPair.Key, "Events.csv");
				using FileStream outFile = IOUtil.CreateFile(outPath, logger);
				using StreamWriter writer = new(outFile, Encoding.UTF8);

				writer.WriteLine("id,modes,name,npcs");
				foreach (var eventPair in mapPair.Value.OrderBy(p => p.Key))
				{
					string eventNames = string.Join(",", eventPair.Value.Names);
					string npcNames = string.Join(",", new HashSet<string>(eventPair.Value.SpawnData.SelectMany(sd => sd.NpcNames)));
					writer.WriteLine($"{eventPair.Key},{eventPair.Value.ModeMask},{CsvStr(eventNames)},{CsvStr(npcNames)}");
				}
			}
		}

		// Row type for the aggregated event SQL table (one row per event per map).
		private readonly record struct EventRow(string Map, int Id, EventData Event);

		// Schema
		// create table `event` (
		//   `map` varchar(63) not null,
		//   `id` int not null,
		//   `modes` tinyint unsigned not null,
		//   `names` varchar(63) not null,
		//   `npcs` varchar(1023) not null
		// )
		private static readonly IReadOnlyList<TableColumn<EventRow>> sEventColumns =
		[
			TableColumn.Str<EventRow>("map", r => r.Map),
			TableColumn.Int<EventRow>("id", r => r.Id),
			TableColumn.Int<EventRow>("modes", r => r.Event.ModeMask),
			TableColumn.Str<EventRow>("names", r => $"[\"{string.Join("\",\"", r.Event.Names)}\"]"),
			TableColumn.Str<EventRow>("npcs", r => FormatNpcNamesJson(r.Event.SpawnData)),
		];

		private void WriteEventSql(IReadOnlyDictionary<string, IReadOnlyDictionary<int, EventData>> eventsPerMap, ISqlWriter sqlWriter, Logger logger)
		{
			IEnumerable<EventRow> rows = eventsPerMap
				.OrderBy(p => p.Key)
				.SelectMany(mp => mp.Value.OrderBy(ep => ep.Key)
					.Select(ep => new EventRow(mp.Key, ep.Key, ep.Value)));
			WriteSqlTable(rows, sEventColumns, "event", sqlWriter);
		}

		private static string FormatNpcNamesJson(IEnumerable<SpawnData> spawnData)
		{
			IEnumerable<string> npcNames = new HashSet<string>(spawnData.SelectMany(sd => sd.NpcNames));
			return npcNames.Any() ? $"[\"{string.Join("\",\"", npcNames)}\"]" : "[]";
		}

		private class MapInfo
		{
			public string MapName { get; }

			public IReadOnlyDictionary<string, List<MapPoi>> POIs { get; }

			public IReadOnlyList<UTexture2D> AdditionalMapIcons { get; }

			public MapInfo(string mapName, IReadOnlyDictionary<string, List<MapPoi>> pois, IReadOnlyList<UTexture2D> additionalMapIcons)
			{
				MapName = mapName;
				POIs = pois;
				AdditionalMapIcons = additionalMapIcons;
			}
		}
	}
}
