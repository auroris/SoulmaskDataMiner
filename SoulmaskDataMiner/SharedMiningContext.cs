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

using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SoulmaskDataMiner.GameData;
using System.Diagnostics;
using System.Reflection;

namespace SoulmaskDataMiner
{
	/// <summary>
	/// Holds process-wide mining state that does not vary by language: the file
	/// provider (with its package cache), parsed class metadata, the loaded class
	/// hierarchy, and the loot database. Built once and shared across all
	/// per-language <see cref="ProviderManager"/> instances.
	/// </summary>
	/// <remarks>
	/// The provider's current culture is mutable via <see cref="SwitchCulture"/>;
	/// callers must drive that switch when moving between language batches.
	/// Concurrent reads against different cultures are NOT supported by the
	/// underlying CUE4Parse API — this context is designed for sequential
	/// language batches with parallel-miner execution within each batch.
	/// </remarks>
	internal sealed class SharedMiningContext : IDisposable
	{
		private readonly DefaultFileProvider mProvider;
		private readonly Dictionary<string, MetaClass>? mClassMetadata;
		private LootDatabase? mLootDatabase;
		private bool mHierarchyLoaded;

		/// <summary>Shared file provider. Package cache is preserved across language switches.</summary>
		public IFileProvider Provider => mProvider;

		/// <summary>Parsed class metadata (from <c>--classes</c>), or null if not supplied.</summary>
		public IReadOnlyDictionary<string, MetaClass>? ClassMetadata => mClassMetadata;

		/// <summary>Loaded loot database, or null if no miner required it.</summary>
		public LootDatabase? LootDatabase => mLootDatabase;

		/// <summary>True once <see cref="LoadHierarchy"/> has been called successfully.</summary>
		public bool IsHierarchyLoaded => mHierarchyLoaded;

		private SharedMiningContext(DefaultFileProvider provider, Dictionary<string, MetaClass>? classMetadata)
		{
			mProvider = provider;
			mClassMetadata = classMetadata;
		}

		/// <summary>
		/// Builds the shared context: opens the pak file, loads class metadata
		/// (if a path was supplied), and sets the initial culture. Returns null
		/// if a required resource fails to load.
		/// </summary>
		public static SharedMiningContext? Build(Config config, ELanguage initialLanguage, Logger logger)
		{
			DefaultFileProvider provider = new(Path.Combine(config.GameContentDirectory, "Paks"), SearchOption.TopDirectoryOnly, null, null);
			InitializeProvider(provider, config, initialLanguage);

			Dictionary<string, MetaClass>? classMetadata = null;
			if (config.ClassesPath is not null)
			{
				classMetadata = LoadClassMetaData(config.ClassesPath, logger);
				if (classMetadata is null)
				{
					provider.Dispose();
					return null;
				}
			}

			return new SharedMiningContext(provider, classMetadata);
		}

		/// <summary>
		/// Loads the blueprint class hierarchy. Idempotent — subsequent calls
		/// are no-ops. Locale-independent.
		/// </summary>
		public void LoadHierarchy(Logger logger)
		{
			if (mHierarchyLoaded) return;
			GameClassHierarchy.Load(mProvider, mClassMetadata, logger);
			mHierarchyLoaded = true;
		}

		/// <summary>
		/// Loads the loot database. Idempotent — subsequent calls are no-ops.
		/// Locale-independent (verified: no FText reads).
		/// </summary>
		public bool LoadLootDatabase(Logger logger)
		{
			if (mLootDatabase is not null) return true;
			LootDatabase db = new();
			if (!db.Load(mProvider, logger)) return false;
			mLootDatabase = db;
			return true;
		}

		/// <summary>
		/// Switches the provider's active culture. Affects all subsequent FText
		/// resolution. The package cache is preserved.
		/// </summary>
		public void SwitchCulture(string languageCode)
		{
			mProvider.ChangeCulture(languageCode);
		}

		public void Dispose()
		{
			mProvider.Dispose();
		}

		private static void InitializeProvider(DefaultFileProvider provider, Config config, ELanguage initialLanguage)
		{
			provider.Initialize();

			FAesKey key = config.EncryptionKey is null
				? new FAesKey(new byte[32])
				: new FAesKey(config.EncryptionKey);

			foreach (var vfsReader in provider.UnloadedVfs)
			{
				provider.SubmitKey(vfsReader.EncryptionKeyGuid, key);
			}

			provider.PostMount();
			provider.ChangeCulture(Config.GetLanguageCode(initialLanguage));
		}

		private static Dictionary<string, MetaClass>? LoadClassMetaData(string classesPath, Logger logger)
		{
			try
			{
				logger.Information("Loading class metadata...");
				long startTimestamp = Stopwatch.GetTimestamp();

				JObject root;
				using (FileStream file = File.OpenRead(classesPath))
				using (StreamReader sr = new(file))
				using (JsonReader reader = new JsonTextReader(sr))
				{
					root = JObject.Load(reader);
				}

				JArray classArray = (JArray)root["data"]!;

				Dictionary<string, MetaClass> classes = new();
				HashSet<string> propertiesToIgnore = new() { "__MDKClassSize" };

				foreach (JObject classObj in classArray)
				{
					foreach (JProperty @class in classObj.Properties())
					{
						string className = @class.Name.Substring(1); // Trim leading U, A, F, etc.
						if (classes.ContainsKey(className))
						{
							logger.Debug($"Found an additional instance of class \"{className}\" in metadata. Skipping.");
							continue;
						}

						string? classSuper = null;
						List<MetaClassProperty> classProperties = new();

						JArray classPropertyArray = (JArray)@class.Value;
						foreach (JObject classPropertyObj in classPropertyArray)
						{
							foreach (JProperty classProperty in classPropertyObj.Properties())
							{
								string propertyName = classProperty.Name;
								if (propertiesToIgnore.Contains(propertyName)) continue;

								if (propertyName.Equals("__InheritInfo"))
								{
									JArray classSuperArray = (JArray)classProperty.Value;
									if (classSuperArray.Count > 0)
									{
										classSuper = ((string)classSuperArray[0]!).Substring(1);
									}
									continue;
								}

								JArray propertyValue = (JArray)classProperty.Value;
								JArray propertyValue0 = (JArray)propertyValue[0];
								string propertyType = (string)propertyValue0[0]!;

								classProperties.Add(new() { Name = propertyName, Type = propertyType });
							}
						}

						classes.Add(className, new() { Name = className, Super = classSuper!, Properties = classProperties });
					}
				}

				logger.Information($"Class metadata loaded in {Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds:0.##}ms");
				return classes;
			}
			catch (Exception ex)
			{
				logger.Error($"Failed to load class metadata. [{ex.GetType().FullName}] {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Safe-checks that all Unreal Engine expected base classes required by the active miners
		/// are present in the class metadata dump. Prints warnings if expected classes are missing.
		/// </summary>
		public void ValidateSchema(Config config, Logger logger)
		{
			if (mClassMetadata is null) return;

			HashSet<string>? includeMiners = config.Miners == null ? null : new HashSet<string>(config.Miners.Select(m => m.ToLowerInvariant()));
			bool forceInclude = includeMiners?.Contains("all") ?? false;

			HashSet<string> expectedBaseClasses = new();

			Type minerInterface = typeof(IDataMiner);
			foreach (Type type in typeof(SharedMiningContext).Assembly.GetTypes())
			{
				if (!type.IsAbstract && minerInterface.IsAssignableFrom(type))
				{
					MinerNameAttribute? nameAttr = type.GetCustomAttribute<MinerNameAttribute>();
					if (nameAttr is null) continue;

					string nameLower = nameAttr.Name.ToLowerInvariant();
					if (includeMiners == null)
					{
						DefaultEnabledAttribute? defaultEnabledAttr = type.GetCustomAttribute<DefaultEnabledAttribute>();
						bool isDefault = defaultEnabledAttr?.IsEnabled ?? true;
						if (!isDefault) continue;
					}
					else if (!forceInclude && !includeMiners.Contains(nameLower))
					{
						continue;
					}

					RequiredBaseClassesAttribute? requiredAttr = type.GetCustomAttribute<RequiredBaseClassesAttribute>();
					if (requiredAttr is not null)
					{
						foreach (string baseClass in requiredAttr.BaseClassNames)
						{
							expectedBaseClasses.Add(baseClass);
						}
					}
				}
			}

			if (expectedBaseClasses.Count == 0) return;

			List<string> missingClasses = new();
			foreach (string baseClass in expectedBaseClasses)
			{
				if (!mClassMetadata.ContainsKey(baseClass))
				{
					missingClasses.Add(baseClass);
				}
			}

			if (missingClasses.Count > 0)
			{
				logger.Warning($"[Schema Validation] The following base classes are missing from class metadata: {string.Join(", ", missingClasses)}. Active miners relying on these classes will likely fail or skip all data records. Check that your ClassesInfo.json is generated from the correct game version.");
			}
			else
			{
				logger.Information("[Schema Validation] All expected base classes are verified in the class metadata.");
			}
		}
	}
}
