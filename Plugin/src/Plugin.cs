﻿using System.Reflection;
using UnityEngine;
using BepInEx;
using LethalLib.Modules;
using BepInEx.Logging;
using System.IO;
using IPMaster.Configuration;
using GraphicsAPI.CustomPostProcessing;
using System;
using IPMaster.Stalker;
using System.Collections;
using UnityEngine.Audio;

namespace IPMaster
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
#if DEBUG
    [BepInDependency(TestingLib.Plugin.ModGUID, BepInDependency.DependencyFlags.SoftDependency)]
#endif
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = null!;
        internal static PluginConfig BoundConfig { get; private set; } = null!;
        public static AssetBundle? ModAssets;

        static EnemyType stalker = null!;
        static AudioMixer audioMixer = null!;

        private void Awake()
        {
            Logger = base.Logger;

            // If you don't want your mod to use a configuration file, you can remove this line, Configuration.cs, and other references.
            BoundConfig = new PluginConfig(Config);

            // This should be ran before Network Prefabs are registered.
            InitializeNetworkBehaviours();

            // We load the asset bundle that should be next to our DLL file, with the specified name.
            // You may want to rename your asset bundle from the AssetBundle Browser in order to avoid an issue with
            // asset bundle identifiers being the same between multiple bundles, allowing the loading of only one bundle from one mod.
            // In that case also remember to change the asset bundle copying code in the csproj.user file.
            var bundleName = "elekrisk.ipmaster.modassets";
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), bundleName));
            if (ModAssets == null)
            {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }

            // We load our assets from our asset bundle. Remember to rename them both here and in our Unity project.
            // ExampleEnemy = ModAssets.LoadAsset<EnemyType>("ExampleEnemy");
            // var ExampleEnemyTN = ModAssets.LoadAsset<TerminalNode>("ExampleEnemyTN");
            // var ExampleEnemyTK = ModAssets.LoadAsset<TerminalKeyword>("ExampleEnemyTK");

            // Optionally, we can list which levels we want to add our enemy to, while also specifying the spawn weight for each.
            /*
            var ExampleEnemyLevelRarities = new Dictionary<Levels.LevelTypes, int> {
                {Levels.LevelTypes.ExperimentationLevel, 10},
                {Levels.LevelTypes.AssuranceLevel, 40},
                {Levels.LevelTypes.VowLevel, 20},
                {Levels.LevelTypes.OffenseLevel, 30},
                {Levels.LevelTypes.MarchLevel, 20},
                {Levels.LevelTypes.RendLevel, 50},
                {Levels.LevelTypes.DineLevel, 25},
                // {Levels.LevelTypes.TitanLevel, 33},
                // {Levels.LevelTypes.All, 30},     // Affects unset values, with lowest priority (gets overridden by Levels.LevelTypes.Modded)
                {Levels.LevelTypes.Modded, 60},     // Affects values for modded moons that weren't specified
            };
            // We can also specify custom level rarities
            var ExampleEnemyCustomLevelRarities = new Dictionary<string, int> {
                {"EGyptLevel", 50},
                {"46 Infernis", 69},    // Either LLL or LE(C) name can be used, LethalLib will handle both
            };
            */

            // Network Prefabs need to be registered. See https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
            // LethalLib registers prefabs on GameNetworkManager.Start.
            // NetworkPrefabs.RegisterNetworkPrefab(ExampleEnemy.enemyPrefab);

            // For different ways of registering your enemy, see https://github.com/EvaisaDev/LethalLib/blob/main/LethalLib/Modules/Enemies.cs
            // Enemies.RegisterEnemy(ExampleEnemy, BoundConfig.SpawnWeight.Value, Levels.LevelTypes.All, ExampleEnemyTN, ExampleEnemyTK);
            // For using our rarity tables, we can use the following:
            // Enemies.RegisterEnemy(IPMaster, ExampleEnemyLevelRarities, ExampleEnemyCustomLevelRarities, ExampleEnemyTN, ExampleEnemyTK);

            stalker = ModAssets.LoadAsset<EnemyType>("Stalker");
            NetworkPrefabs.RegisterNetworkPrefab(stalker.enemyPrefab);
#pragma warning disable CS8604 // Possible null reference argument.
            Enemies.RegisterEnemy(stalker, 25, Levels.LevelTypes.All, null as TerminalNode);
#pragma warning restore CS8604 // Possible null reference argument.

            audioMixer = ModAssets.LoadAsset<AudioMixer>("StalkerAudioMixer");
            CustomPostProcessingManager.OnLoad += CustomPostProcessingManager_OnLoad;
#if DEBUG
            TestingLib.OnEvent.PlayerSpawn += OnPlayerSpawn;
#endif

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

#if DEBUG
        private void OnPlayerSpawn()
        {
            TestingLib.Tools.GiveItemToSelf(TestingLib.Lookup.Item.Shovel);
            TestingLib.Tools.TeleportSelf(TestingLib.Tools.TeleportLocation.Inside);
            // TestingLib.Tools.SpawnEnemyInFrontOfSelf(stalker.enemyName);
        }
#endif

        private void CustomPostProcessingManager_OnLoad(object sender, EventArgs e)
        {
            // Load shader thingy
            var stalkerMaterial = ModAssets!.LoadAsset<Material>("StalkerMaterial");

            PostProcess postProcess = new("elekrisk.IPMaster.Stalker", stalkerMaterial)
            {
                InjectionType = InjectionType.BeforePostProcess
            };

            var customPass = CustomPostProcessingManager.Instance.AddPostProcess(postProcess);
            GameObject handlerObj = new GameObject("PostProcessorHandler");
            PostProcessorHandler handler = handlerObj.AddComponent<PostProcessorHandler>();
            handler.Initialize(stalkerMaterial, customPass);
            handler.AudioMixer = audioMixer;
        }

        private static void InitializeNetworkBehaviours()
        {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }
    }
}