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

using CUE4Parse.UE4.Versions;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SoulmaskDataMiner
{
	/// <summary>
	/// Locates, instantiates and runs data miners
	/// </summary>
	internal sealed class MineRunner : IDisposable
	{
		private readonly Config mConfig;

		private readonly ELanguage mLanguage;

		private readonly Logger mLogger;

		private readonly ProviderManager mProviderManager;

		private readonly List<IDataMiner> mMiners;

		private bool mRequireHierarchy;

		private bool mRequireLootDatabase;

		public MineRunner(Config config, ELanguage language, Logger logger)
		{
			mConfig = config;
			mLanguage = language;
			mLogger = logger;
			mProviderManager = new ProviderManager(config, language);
			mMiners = new();
			mRequireHierarchy = false;
			mRequireLootDatabase = false;
		}

		public bool Initialize()
		{
			if (!mProviderManager.Initialize(mLogger))
			{
				return false;
			}
			CreateMiners(mConfig.Miners);
			return true;
		}

		public void Dispose()
		{
			foreach (IDataMiner miner in mMiners)
			{
				if (miner is IDisposable disposable)
				{
					disposable.Dispose();
				}
			}
			mMiners.Clear();

			mProviderManager.Dispose();
		}

		public bool Run()
		{
			if (mRequireHierarchy)
			{
				GameClassHierarchy.Load(mProviderManager, mLogger);
			}

			if (mRequireLootDatabase)
			{
				mProviderManager.LoadLootDatabase(mLogger);
			}

			string sqlPath = Path.Combine(mConfig.OutputDirectory, "update.sql");
			using FileStream sqlFile = IOUtil.CreateFile(sqlPath, mLogger);
			using StreamWriter sqlStream = new(sqlFile, Encoding.UTF8) { NewLine = "\n" };
			SqlWriter sqlWriter = new(sqlStream);

			sqlWriter.WriteStartFile();

			var results = new System.Collections.Concurrent.ConcurrentDictionary<IDataMiner, (bool Success, BufferedSqlWriter SqlWriter, double ElapsedMs)>();

			System.Threading.Tasks.Parallel.ForEach(mMiners, miner =>
			{
				mLogger.Important($"Running data miner [{miner.Name}]...");

				BufferedSqlWriter bufferedWriter = new();
				long startTimestamp = Stopwatch.GetTimestamp();

				bool success = false;
				if (Debugger.IsAttached)
				{
					// Allow exceptions to escape for easier debugging
					success = miner.Run(mProviderManager, mConfig, mLogger, bufferedWriter);
				}
				else
				{
					try
					{
						success = miner.Run(mProviderManager, mConfig, mLogger, bufferedWriter);
					}
					catch (Exception ex)
					{
						mLogger.Log(LogLevel.Error, $"Data miner [{miner.Name}] failed! [{ex.GetType().FullName}] {ex.Message}");
					}
				}

				double elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
				results.TryAdd(miner, (success, bufferedWriter, elapsedMs));
			});

			bool overallSuccess = true;
			foreach (IDataMiner miner in mMiners)
			{
				if (results.TryGetValue(miner, out var result))
				{
					overallSuccess &= result.Success;

					sqlWriter.WriteStartSection(miner.Name);
					result.SqlWriter.Playback(sqlWriter);
					sqlWriter.WriteEndSection();

					mLogger.Information($"[{miner.Name}] completed in {result.ElapsedMs:0.##}ms");
				}
				else
				{
					mLogger.Log(LogLevel.Error, $"Data miner [{miner.Name}] was not executed or results were lost.");
					overallSuccess = false;
				}
			}

			if (mRequireLootDatabase)
			{
				sqlWriter.WriteStartSection("loot");
				mProviderManager.LootDatabase.SaveData(sqlWriter, mConfig, mLogger);
				sqlWriter.WriteEndSection();
			}

			sqlWriter.WriteEndFile();

			return overallSuccess;
		}

		public static void ListAllMiners(out List<string> defaultMiners, out List<string> additionalMiners)
		{
			defaultMiners = new();
			additionalMiners = new();

			foreach (MinerDescriptor desc in GetMinerDescriptors())
			{
				if (desc.IsDefault)
				{
					defaultMiners.Add(desc.Name);
				}
				else
				{
					additionalMiners.Add(desc.Name);
				}
			}

			defaultMiners.Sort();
			additionalMiners.Sort();
		}

		private void CreateMiners(IEnumerable<string>? minersToInclude)
		{
			mRequireHierarchy = false;
			mRequireLootDatabase = false;

			HashSet<string>? includeMiners = minersToInclude == null ? null : new HashSet<string>(minersToInclude.Select(m => m.ToLowerInvariant()));
			bool forceInclude = includeMiners?.Contains("all") ?? false;

			foreach (MinerDescriptor desc in GetMinerDescriptors())
			{
				if (includeMiners == null && !desc.IsDefault)
				{
					continue;
				}

				string nameLower = desc.Name.ToLowerInvariant();

				if (desc.RequireClassData && mProviderManager.ClassMetadata is null)
				{
					mLogger.Warning($"Skipping miner \"{desc.Name}\" because class metadata has not been loaded. Use the --classes parameter to specify a class metadata file to load.");
					includeMiners?.Remove(nameLower);
					continue;
				}

				if (!forceInclude && !(includeMiners?.Contains(nameLower) ?? true))
				{
					continue;
				}

				IDataMiner? miner;
				try
				{
					miner = (IDataMiner?)Activator.CreateInstance(desc.Type);
					if (miner == null)
					{
						mLogger.Log(LogLevel.Error, $"Could not create an instance of {desc.Type.Name}. Ensure it has a parameterless constructor. This miner will not run.");
						continue;
					}
				}
				catch (Exception ex)
				{
					mLogger.Log(LogLevel.Error, $"Could not create an instance of {desc.Type.Name}. This miner will not run. [{ex.GetType().FullName}] {ex.Message}");
					continue;
				}

				includeMiners?.Remove(nameLower);
				mMiners.Add(miner);
				mRequireHierarchy |= desc.RequireHierarchy;
				mRequireLootDatabase |= desc.RequireLootDatabase;
			}

			includeMiners?.RemoveWhere(n => n.Equals("all", StringComparison.OrdinalIgnoreCase));

			if (includeMiners?.Count > 0)
			{
				mLogger.Warning($"The following miners specified in the filter could not be located: {string.Join(',', includeMiners)}");
			}
			if (mMiners.Count == 0)
			{
				mLogger.Log(LogLevel.Error, "No data miners which match the passed in filter could run.");
			}
			else
			{
				mLogger.Important($"The following miners will be run: {string.Join(',', mMiners.Select(m => m.Name))}");
			}
		}

		private readonly record struct MinerDescriptor(
			Type Type,
			string Name,
			bool IsDefault,
			bool RequireHierarchy,
			bool RequireClassData,
			bool RequireLootDatabase);

		private static IReadOnlyList<MinerDescriptor>? sMinerDescriptors;

		// Single assembly scan; cached for the process lifetime. Reads names from MinerNameAttribute
		// so callers don't need to instantiate a miner just to learn its name.
		private static IReadOnlyList<MinerDescriptor> GetMinerDescriptors()
		{
			if (sMinerDescriptors is not null) return sMinerDescriptors;

			Type minerInterface = typeof(IDataMiner);
			List<MinerDescriptor> result = new();
			foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
			{
				if (type.IsAbstract || !minerInterface.IsAssignableFrom(type)) continue;

				string? name = type.GetCustomAttribute<MinerNameAttribute>()?.Name;
				if (name is null) continue;

				bool isDefault = type.GetCustomAttribute<DefaultEnabledAttribute>()?.IsEnabled ?? true;
				bool requireHierarchy = type.GetCustomAttribute<RequireHierarchyAttribute>()?.IsRequired ?? false;
				bool requireClassData = type.GetCustomAttribute<RequireClassDataAttribute>()?.IsRequired ?? false;
				bool requireLootDatabase = type.GetCustomAttribute<RequireLootDatabaseAttribute>()?.IsRequired ?? false;

				result.Add(new MinerDescriptor(type, name, isDefault, requireHierarchy, requireClassData, requireLootDatabase));
			}

			sMinerDescriptors = result;
			return result;
		}
	}
}
