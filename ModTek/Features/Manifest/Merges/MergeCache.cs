﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BattleTech;
using ModTek.Features.Manifest.BTRL;
using ModTek.Misc;
using ModTek.UI;
using ModTek.Util;
using static ModTek.Features.Logging.MTLogger;
using CacheDB = System.Collections.Generic.Dictionary<ModTek.Features.Manifest.CacheKey, ModTek.Features.Manifest.Merges.MergeCacheEntry>;
using CacheKeyValue = System.Collections.Generic.KeyValuePair<ModTek.Features.Manifest.CacheKey, ModTek.Features.Manifest.Merges.MergeCacheEntry>;

namespace ModTek.Features.Manifest.Merges
{
    internal class MergeCache
    {
        private static string PersistentDirPath => FilePaths.MergeCacheDirectory;
        private readonly string PersistentFilePath;

        private readonly CacheDB CachedMerges; // stuff in here was merged
        private readonly CacheDB QueuedMerges = new CacheDB(); // stuff in here has merges queued

        private static bool HasChanges;

        internal MergeCache()
        {
            PersistentFilePath = Path.Combine(PersistentDirPath, "merge_cache.json");

            if (File.Exists(PersistentFilePath))
            {
                try
                {
                    CachedMerges = ModTekCacheStorage.ReadFrom<List<CacheKeyValue>>(PersistentFilePath)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);
                    return;
                }
                catch (Exception e)
                {
                    Log("MergeCache: Loading merge cache failed.", e);
                }
            }

            FileUtils.CleanDirectory(PersistentDirPath);

            // create a new one if it doesn't exist or couldn't be added'
            Log("MergeCache: Rebuilding cache.");
            CachedMerges = new CacheDB();
        }

        private readonly Stopwatch saveSW = new Stopwatch();
        internal void Save()
        {
            try
            {
                saveSW.Restart();
                if (!HasChanges)
                {
                    Log($"MergeCache: No changes detected, skipping save.");
                    return;
                }

                ModTekCacheStorage.WriteTo(CachedMerges.ToList(), PersistentFilePath);
                Log($"MergeCache: Saved to {PersistentFilePath}.");
                HasChanges = false;
            }
            catch (Exception e)
            {
                Log($"MergeCache: Couldn't write to {PersistentFilePath}", e);
            }
            finally
            {
                saveSW.Stop();
                LogIfSlow(saveSW);
            }
        }

        internal bool AddModEntry(ModEntry entry)
        {
            if (entry.ShouldMergeJSON && entry.IsJson)
            {
                AddTemp(entry);
                return true;
            }

            if (entry.ShouldAppendText && (entry.IsTxt || entry.IsCsv))
            {
                AddTemp(entry);
                return true;
            }

            return false;
        }

        private void AddTemp(ModEntry entry)
        {
            Log($"\tMerge: {entry}");
            var manifestEntry = BetterBTRL.Instance.EntryByIDAndType(entry.Id, entry.Type);
            if (manifestEntry == null)
            {
                Log($"\t\tError: Can't find referenced resource: {entry}");
                return;
            }

            var key = new CacheKey(entry);
            if (!QueuedMerges.TryGetValue(key, out var temp))
            {
                temp = new MergeCacheEntry(manifestEntry);
                QueuedMerges[key] = temp;
            }
            temp.Add(entry);
        }

        private void CacheUpdate(CacheKey key, MergeCacheEntry queuedEntry)
        {
            var manifestEntry = BetterBTRL.Instance.EntryByIDAndType(key.Id, key.Type);

            var json = ModsManifest.GetJson(manifestEntry);
            if (json == null)
            {
                return;
            }
            try
            {
                json = queuedEntry.Merge(json);
            }
            catch (Exception e)
            {
                Log($"MergeCache: Couldn't merge {queuedEntry.CachedAbsolutePath}", e);
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(queuedEntry.CachedAbsolutePath) ?? throw new InvalidOperationException());
                File.WriteAllText(queuedEntry.CachedAbsolutePath, json);
                queuedEntry.CachedUpdatedOn = DateTime.Now;
                queuedEntry.CacheHit = true;
                CachedMerges[key] = queuedEntry;
                HasChanges = true;
            }
            catch (Exception e)
            {
                Log($"MergeCache: Couldn't write cached merge result to {queuedEntry.CachedAbsolutePath}", e);
            }
        }

        private void CacheRemove(CacheKey key, MergeCacheEntry cacheEntry)
        {
            CachedMerges.Remove(key);
            try
            {
                if (File.Exists(cacheEntry.CachedAbsolutePath))
                {
                    File.Delete(cacheEntry.CachedAbsolutePath);
                }
            }
            catch (Exception e)
            {
                Log($"MergeCache: Error when deleting cached file for {key}", e);
            }
        }

        internal IEnumerable<ProgressReport> BuildCache()
        {
            var sliderText = "Building Merge Cache";
            yield return new ProgressReport(0, sliderText, "", true);

            var countCurrent = 0;
            var countMax = (float)QueuedMerges.Count;

            // find entries missing in cache
            foreach (var kv in QueuedMerges)
            {
                var key = kv.Key;
                var queuedEntry = kv.Value;

                yield return new ProgressReport(countCurrent++/countMax, sliderText, $"{key.Type}\n{key.Id}");

                if (CachedMerges.TryGetValue(key, out var cachedEntry))
                {
                    cachedEntry.CacheHit = true;

                    if (!cachedEntry.Equals(queuedEntry))
                    {
                        Log($"MergeCache: {key} outdated in cache.");
                        CacheUpdate(key, queuedEntry);
                    }
                }
                else
                {
                    Log($"MergeCache: {key} missing in cache.");
                    CacheUpdate(key, queuedEntry);
                }
            }

            yield return new ProgressReport(1, sliderText, $"Cleaning up", true);

            // find entries that shouldn't be in cache (anymore)
            foreach (var kv in CachedMerges.ToList())
            {
                var key = kv.Key;
                var cachedEntry = kv.Value;
                if (cachedEntry.CacheHit)
                {
                    continue;
                }

                Log($"MergeCache: {key} left over in cache.");
                CacheRemove(kv.Key, kv.Value);
            }

            yield return new ProgressReport(1, sliderText, $"Saving cache index", true);
            Save();

            // add merges to the manifest, always only overriding entries that are loaded by the game
            foreach (var kv in CachedMerges)
            {
                var key = kv.Key;
                var entry = kv.Value;

                var manifestEntry = new VersionManifestEntry(
                    key.Id,
                    entry.CachedAbsolutePath,
                    key.Type,
                    entry.CachedUpdatedOn,
                    "1"
                );
                BetterBTRL.Instance.AddMergeManifestEntry(manifestEntry);
            }
        }
    }
}
