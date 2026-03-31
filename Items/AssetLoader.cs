using System.IO;
using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class AssetLoader
    {
        public static Sprite BatIcon { get; private set; }
        public static Sprite BomberIcon { get; private set; }
        public static Sprite MissileIcon { get; private set; }
        public static Sprite AC130Icon { get; private set; }
        public static Sprite FreezeIcon { get; private set; }
        public static Sprite LowGravityIcon { get; private set; }

        public static Sprite SniperRifleIcon { get; private set; }
        public static Sprite DonutIcon { get; private set; }

        public static Sprite JavelinIcon { get; private set; }
        public static Sprite StickyGrenadeIcon { get; private set; }

        public static Texture2D SniperScopeTexture { get; private set; }

        public static GameObject BatModelPrefab { get; private set; }
        public static GameObject BomberPrefab { get; private set; }
        public static GameObject BomberProxyPrefab { get; private set; }
        public static GameObject AC130Prefab { get; private set; }
        public static GameObject BomberTabletPrefab { get; private set; }
        public static GameObject MissileTabletPrefab { get; private set; }
        public static GameObject Ac130TabletPrefab { get; private set; }
        public static GameObject FreezeModelPrefab { get; private set; }
        public static GameObject LowGravityModelPrefab { get; private set; }

        public static GameObject SniperRiflePrefab { get; private set; }
        public static GameObject DonutPrefab { get; private set; }
        public static GameObject DonutHandheldPrefab { get; private set; }

        public static GameObject JavelinHandheldPrefab { get; private set; }
        public static GameObject JavelinTargetIndicatorPrefab { get; private set; }
        public static GameObject JavelinExplosionVfxPrefab { get; private set; }
        public static GameObject JavelinTrailVfxPrefab { get; private set; }

        // public static GameObject DonutLaserZoneRed { get; private set; }
        public static GameObject StickyGrenadePrefab { get; private set; }

        public static GameObject BearPrefab { get; private set; }
        public static GameObject TeddyBearPrefab { get; private set; }
        public static Sprite BearIcon { get; private set; }

        public static Sprite NukeIcon { get; private set; }
        public static Sprite BlackHoleGrenadeIcon { get; private set; }

        /// The networked black hole grenade projectile.
        /// Falls back to a runtime-built sphere if 'black_hole_grenade.prefab' is absent.
        public static GameObject BlackHoleGrenadePrefab { get; private set; }

        /// Local-only visual spawned on all clients during the suction phase.
        /// Not networked — each client instantiates and destroys its own copy.
        public static GameObject BlackHoleVfxPrefab { get; private set; }

        /// The networked wall that is spawned when the Placeable Wall item is used.
        /// Falls back to a runtime-built box if 'wall.prefab' is absent.
        public static GameObject WallPrefab { get; private set; }

        /// The model the player holds while the Placeable Wall item is equipped.
        /// Falls back to a small runtime box if 'wall_handheld.prefab' is absent.
        public static GameObject WallHandheldPrefab { get; private set; }

        public static Sprite WallIcon { get; private set; }

        public static Sprite AK47Icon { get; private set; }
        public static GameObject AK47Prefab { get; private set; }

        public static Sprite HarrierIcon { get; private set; }

        // --- Position Swap ---
        public static Sprite PositionSwapIcon { get; private set; }

        /// Model held while the Position Swap item is equipped.
        public static GameObject PositionSwapHandheldPrefab { get; private set; }

        // --- Poison Jar ---
        public static Sprite PoisonJarIcon { get; private set; }

        // --- Super Donut ---
        /// Falls back to <see cref="DonutIcon"/> at runtime if the asset is absent.
        public static Sprite SuperDonutIcon { get; private set; }

        /// Falls back to <see cref="DonutHandheldPrefab"/> at runtime if the asset is absent.
        public static GameObject SuperDonutHandheldPrefab { get; private set; }

        // --- Red Bull ---
        public static Sprite RedBullIcon { get; private set; }

        /// The can the player holds while the Red Bull item is equipped.
        /// Bundle asset name: <c>red_bull_can.prefab</c>
        public static GameObject RedBullHandheldPrefab { get; private set; }

        /// Local-only trail VFX parented to a player's transform while Red Bull is active.
        /// Not networked — each client instantiates its own copy via RedBullNetworkBridge.
        /// Bundle asset name: <c>red_bull_trail.prefab</c>
        public static GameObject RedBullTrailPrefab { get; private set; }

        // --- Gravity Gun ---
        /// Falls back to the rocket launcher icon at runtime if the asset is absent.
        public static Sprite ElectricGravityGunIcon { get; private set; }

        /// The model the player holds while the Gravity Gun item is equipped.
        /// Bundle asset name: <c>electric_whip.prefab</c>
        public static GameObject ElectricWhipHandheldPrefab { get; private set; }

        /// Local-only VFX parented to the tether target while the session is active.
        /// Not networked — each client instantiates its own copy.
        /// Bundle asset name: <c>gravity_tether_vfx.prefab</c>
        public static GameObject GravityGunTetherVfxPrefab { get; private set; }

        // --- Rocket Tether ---
        /// Falls back to the rocket launcher icon at runtime if the asset is absent.
        public static Sprite RocketTetherIcon { get; private set; }

        /// The model the player holds while the Rocket Tether item is equipped.
        /// Bundle asset name: <c>player_linker.prefab</c>
        public static GameObject RocketTetherPrefab { get; private set; }

        /// Local-only rocket VFX spawned above the target and moved upward each frame.
        /// Not networked — each client instantiates its own copy.
        /// Bundle asset name: <c>player_linker_rocket.prefab</c>
        public static GameObject RocketTetherRocketPrefab { get; private set; }

        // --- Jetpack ---
        /// Bundle asset name: <c>jetpack_icon.png</c>
        public static Sprite JetpackIcon { get; private set; }

        /// Local-only body-worn prefab (backpack), shown on the local player while the
        /// Jetpack is their active item. Not networked — managed by JetpackNetworkBridge.Update().
        /// Bundle asset name: <c>jetpack_equipped.prefab</c>
        public static GameObject JetpackEquippedPrefab { get; private set; }

        /// Local-only particle VFX (thrust flames/smoke). Not networked — each client
        /// instantiates its own copy via JetpackNetworkBridge.
        /// Bundle asset name: <c>jetpack_particles.prefab</c>
        public static GameObject JetpackParticlePrefab { get; private set; }

        // --- Drone Swarm ---
        public static Sprite DroneSwarmIcon { get; private set; }

        /// <summary>
        /// The model the player holds while the Drone Swarm item is equipped.
        /// Bundle asset name: <c>drone_controller.prefab</c>
        /// </summary>
        public static GameObject DroneControllerPrefab { get; private set; }

        /// <summary>
        /// The networked drone object spawned for each swarm member.
        /// Requires NetworkIdentity + NetworkTransform in the bundle.
        /// Bundle asset name: <c>drone.prefab</c>
        /// </summary>
        public static GameObject DronePrefab { get; private set; }

        /// <summary>
        /// Local-only VFX instantiated on all clients when a drone detonates.
        /// Not networked — each client instantiates and destroys its own copy.
        /// Bundle asset name: <c>drone_explosion.prefab</c>
        /// </summary>
        public static GameObject DroneExplosionVfxPrefab { get; private set; }

        /// The networked jar projectile. Bundle asset name: <c>poison_jar.prefab</c>
        public static GameObject PoisonJarPrefab { get; private set; }

        /// The model the player holds while the Poison Jar is equipped.
        /// Bundle asset name: <c>poison_jar_handheld.prefab</c>
        public static GameObject PoisonJarHandheldPrefab { get; private set; }

        /// Local-only splash VFX instantiated on all clients when the jar lands.
        /// Not networked — each client instantiates and destroys its own copy.
        /// Bundle asset name: <c>poison_splash.prefab</c>
        public static GameObject PoisonSplashPrefab { get; private set; }

        /// Local-only VFX parented to each player during the swap countdown.
        /// Bundle asset name: <c>position_swap_orb.prefab</c>
        public static GameObject PositionSwapOrbPrefab { get; private set; }

        /// Local-only VFX spawned at both swap positions on execution.
        /// Bundle asset name: <c>position_swap_smoke.prefab</c>
        public static GameObject PositionSwapSmokePrefab { get; private set; }

        /// <summary>
        /// The networked Harrier Jet object spawned by HarrierNetworkBridge.
        /// Requires NetworkIdentity + NetworkTransform in the bundle.
        /// Falls back to a runtime capsule if 'harrier_jet.prefab' is absent.
        /// </summary>
        public static GameObject HarrierPrefab { get; private set; }
        public static GameObject HarrierTabletPrefab { get; private set; }

        /// Local-only non-networked copy of WallPrefab used as the placement ghost.
        /// Network components and physics are stripped so it can be instantiated freely
        /// on the client without Mirror context.
        public static GameObject WallGhostPrefab { get; private set; }

        /// The model the player holds while the Nuke item is equipped.
        public static GameObject NuclearDetonatorPrefab { get; private set; }

        /// The networked bomb object that falls from the sky.
        /// Requires NetworkIdentity + NetworkTransform in the bundle.
        public static GameObject NukeBombPrefab { get; private set; }

        /// VFX prefab spawned on all clients when the nuke detonates.
        /// Reuses NukeVerticalExplosionFire.prefab already present in the bundle.
        public static GameObject NukeExplosionVfxPrefab { get; private set; }

        /// Impact sound played on all clients at detonation.
        /// Reuses etfx_explosion_nuke.wav already present in the bundle.
        public static AudioClip NukeExplosionClip { get; private set; }

        public static GameObject ConfettiBlastRainbow { get; private set; }

        public static GameObject BloodSplatterPrefab { get; private set; }

        /// Programmatically-built prefab for dropped custom items.
        /// Root carries NetworkIdentity, NetworkTransform, Rigidbody, SphereCollider,
        /// Entity, and DroppedCustomItem.  The visual child is added client-side in
        /// DroppedCustomItem.OnStartClient() from the synced ItemType.
        public static GameObject DroppedCustomItemPrefab { get; private set; }

        /// Secondary debris / dust VFX spawned at the crash site.
        public static GameObject ImpactVfxPrefab { get; private set; }

        // ----------------------------------------------------------------
        //  Audio
        // ----------------------------------------------------------------
        public static AudioClip AC130AboveClip { get; private set; }
        public static AudioClip HomerunAudioClip { get; private set; }

        // --- AC130 Mayday assets (placeholders — add to bundle when ready) ---
        /// Looping cockpit alarm that plays during the mayday dive.
        public static AudioClip MaydayAlarmClip { get; private set; }

        /// One-shot impact / explosion sound at crash site.
        public static AudioClip MaydayImpactClip { get; private set; }

        /// Smoke trail particle prefab — attached to the gunship during the dive.
        public static GameObject MaydaySmokeTrailPrefab { get; private set; }
        public static GameObject MaydayFireTrailPrefab { get; private set; }

        /// Impact explosion VFX prefab — spawned at the crash position.
        public static GameObject MaydayExplosionVfxPrefab { get; private set; }

        /// <summary>
        /// Local-only particle system prefab spawned near the camera when a warning fires.
        /// Bundle asset name: <c>warning_particle.prefab</c>
        /// The prefab should be a screen-space or billboard particle effect that looks good
        /// when attached 2.5 units in front of the main camera.
        /// </summary>
        public static GameObject WarningParticlePrefab { get; private set; }

        public static bool IsLoaded => _bundle != null;

        private static AssetBundle _bundle;

        public static void Load()
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string bundlePath = Path.Combine(pluginDir, "IssaPluginBundle", "issamod");

            if (!File.Exists(bundlePath))
            {
                bundlePath = Path.Combine(pluginDir, "issamod");
                if (!File.Exists(bundlePath))
                {
                    IssaPluginPlugin.Log.LogWarning(
                        "[Assets] Asset bundle not found. "
                            + "Place 'issamod' in IssaPluginBundle/ next to the plugin DLL."
                    );
                    return;
                }
            }

            _bundle = AssetBundle.LoadFromFile(bundlePath);
            if (_bundle == null)
            {
                IssaPluginPlugin.Log.LogError("[Assets] Failed to load asset bundle.");
                return;
            }

            LoadSpritesAndTextures();
            LoadSimpleItemPrefabs();
            LoadAircraftPrefabs();
            LoadDonutAssets();
            LoadStickyGrenadeAssets();
            LoadBearAssets();
            LoadNukeAssets(); // must precede LoadAudioAndVfx (shares MaydayExplosionVfxPrefab)
            LoadAudioAndVfx();
            LoadBlackHoleGrenadeAssets();
            LoadDroppedItemPrefab();
            LoadPlaceableWallAssets();
            LoadAK47Assets();
            LoadHarrierAssets();
            LoadPositionSwapAssets();
            LoadPoisonJarAssets();
            LoadDroneSwarmAssets();
            LoadRedBullAssets();
            LoadSuperDonutAssets();
            LoadGravityGunAssets();
            LoadRocketTetherAssets();
            LoadJetpackAssets();

            IssaPluginPlugin.Log.LogInfo("[Assets] IssaPluginBundle loaded.");
        }

        private static void LoadSpritesAndTextures()
        {
            BatIcon = LoadSprite("bat_icon.png");
            BomberIcon = LoadSprite("bomber_icon.png");
            MissileIcon = LoadSprite("missile_icon.png");
            AC130Icon = LoadSprite("ac130_icon.png");
            FreezeIcon = LoadSprite("freeze_effect_icon.png");
            LowGravityIcon = LoadSprite("gravity_remote_icon.png");
            SniperRifleIcon = LoadSprite("sniper_rifle_icon.png");
            DonutIcon = LoadSprite("donut_icon_v2.png");
            JavelinIcon = LoadSprite("javelin_icon.png");
            StickyGrenadeIcon = LoadSprite("spike_ball_icon.png");
            BearIcon = LoadSprite("bear_icon.png");
            NukeIcon = LoadSprite("nuke_icon.png");
            BlackHoleGrenadeIcon = LoadSprite("black_hole_grenade_icon.png");
            WallIcon = LoadSprite("wall_icon.png");
            AK47Icon = LoadSprite("ak47_icon.png");
            HarrierIcon = LoadSprite("harrier_icon.png");
            PositionSwapIcon = LoadSprite("position_swap_icon.png");
            PoisonJarIcon = LoadSprite("poison_bottle_icon.png");
            DroneSwarmIcon = LoadSprite("drone_swarm_icon.png");
            ElectricGravityGunIcon = LoadSprite("gravity_gun_icon.png");
            RedBullIcon = LoadSprite("redbull_icon.png");
            RocketTetherIcon = LoadSprite("rocket_tether_icon.png");

            SniperScopeTexture = LoadTexture2D("sniper_scope.png");
            if (SniperScopeTexture == null)
                IssaPluginPlugin.Log.LogError("[Assets] Failed to load sniper scope texture.");
        }

        /// Prefabs that need only a load + DisableRigidbody (no NetworkIdentity, no ClientSetup).
        private static void LoadSimpleItemPrefabs()
        {
            BatModelPrefab = Load<GameObject>("bat_model.prefab");
            DisableRigidbody(BatModelPrefab);

            FreezeModelPrefab = Load<GameObject>("snowball.prefab");
            DisableRigidbody(FreezeModelPrefab);

            LowGravityModelPrefab = Load<GameObject>("gravity_remote.prefab");
            DisableRigidbody(LowGravityModelPrefab);

            SniperRiflePrefab = Load<GameObject>("intervention.prefab");
            DisableRigidbody(SniperRiflePrefab);

            DonutHandheldPrefab = Load<GameObject>("donut_model.prefab");
            DisableRigidbody(DonutHandheldPrefab);

            JavelinHandheldPrefab = Load<GameObject>("javelin_rocket_launcher.prefab");
            DisableRigidbody(JavelinHandheldPrefab);
            JavelinTargetIndicatorPrefab = Load<GameObject>("javelin_target_indicator.prefab");
            JavelinExplosionVfxPrefab = Load<GameObject>("NukeVerticalExplosionFire.prefab");
            JavelinTrailVfxPrefab = Load<GameObject>("javelin_trail.prefab");

            TeddyBearPrefab = Load<GameObject>("teddy.prefab");
            DisableRigidbody(TeddyBearPrefab);

            // DonutLaserZoneRed = Load<GameObject>("laser_zone_red.prefab");
            ConfettiBlastRainbow = Load<GameObject>("ConfettiBlastRainbow.prefab");
            // BloodSplatterPrefab = Load<GameObject>("blood_splatter_critical.prefab");
            BloodSplatterPrefab = Load<GameObject>("blood_explosion_vfx.prefab");
        }

        private static void LoadAircraftPrefabs()
        {
            BomberPrefab = Load<GameObject>("bomber_model.prefab");

            BomberProxyPrefab = Load<GameObject>("bomber_proxy.prefab");
            EnsureNetworkIdentity(BomberProxyPrefab, 0xB0AA0001u);
            if (BomberProxyPrefab != null)
                BomberProxyPrefab.AddComponent<BomberProxyClientSetup>();

            AC130Prefab = Load<GameObject>("ac130_model.prefab");
            EnsureNetworkIdentity(AC130Prefab, 0xAC130001u);
            if (AC130Prefab != null)
                AC130Prefab.AddComponent<AC130ClientSetup>();

            BomberTabletPrefab = Load<GameObject>("stealth_bomber_tablet.prefab");
            DisableRigidbody(BomberTabletPrefab);
            MissileTabletPrefab = Load<GameObject>("predator_missile_tablet.prefab");
            DisableRigidbody(MissileTabletPrefab);
            Ac130TabletPrefab = Load<GameObject>("ac130_tablet.prefab");
            DisableRigidbody(Ac130TabletPrefab);
        }

        private static void LoadDonutAssets()
        {
            DonutPrefab = Load<GameObject>("donut_vehicle.prefab");
            EnsureNetworkIdentity(DonutPrefab, 0xF0000001u);
            DonutPrefab?.AddComponent<DonutClientSetup>();
            DisableRigidbody(DonutPrefab);
        }

        private static void LoadStickyGrenadeAssets()
        {
            StickyGrenadePrefab = Load<GameObject>("spike_ball.prefab");
            DisableRigidbody(StickyGrenadePrefab);
            EnsureNetworkIdentity(StickyGrenadePrefab, 0x5E47EC01u);
            StickyGrenadePrefab?.AddComponent<StickyGrenadeClientSetup>();
        }

        private static void LoadBearAssets()
        {
            BearPrefab = Load<GameObject>("bear.prefab");
            if (BearPrefab != null)
            {
                EnsureNetworkIdentity(BearPrefab, 0xBEA00001u);
                BearPrefab.AddComponent<BearClientSetup>();
                DisableRigidbody(BearPrefab); // BearBehaviour re-enables in Start()
            }
        }

        private static void LoadNukeAssets()
        {
            NuclearDetonatorPrefab = Load<GameObject>("nuclear_detonator.prefab");
            if (NuclearDetonatorPrefab != null)
                DisableRigidbody(NuclearDetonatorPrefab);

            NukeBombPrefab = Load<GameObject>("nuclear_bomb.prefab");
            if (NukeBombPrefab != null)
            {
                EnsureNetworkIdentity(NukeBombPrefab, 0xF1550001u);
                // Rigidbody starts kinematic; NukeBombBehaviour.Start() re-enables it.
                DisableRigidbody(NukeBombPrefab);
            }
        }

        /// AudioClips must be loaded by asset name without the file extension.
        /// Unity compiles audio into its own internal format at bundle-build time,
        /// so the original .ogg/.wav path is never valid at runtime.
        private static void LoadAudioAndVfx()
        {
            // AC130AboveClip = Load<AudioClip>("ac130_above.ogg");
            // HomerunAudioClip = Load<AudioClip>("homerun.ogg");

            // Mayday assets — optional until added to the bundle; all usage sites null-check.
            // MaydayAlarmClip = Load<AudioClip>("missile_locked.ogg");
            // MaydayImpactClip = Load<AudioClip>("etfx_explosion_nuke.wav");
            MaydaySmokeTrailPrefab = Load<GameObject>("smoke_prefab.prefab");
            MaydayFireTrailPrefab = Load<GameObject>("fire_torch_intense.prefab");
            MaydayExplosionVfxPrefab = Load<GameObject>("NukeVerticalExplosionFire.prefab");

            // Nuke-specific explosion VFX — dedicated prefab preferred, fall back to mayday VFX.
            NukeExplosionVfxPrefab =
                Load<GameObject>("nuclear_explosion.prefab") ?? MaydayExplosionVfxPrefab;
            NukeExplosionClip = MaydayImpactClip;

            WarningParticlePrefab = Load<GameObject>("warning_particle.prefab");
        }

        private static void LoadBlackHoleGrenadeAssets()
        {
            BlackHoleVfxPrefab = Load<GameObject>("black_hole.prefab");
            if (BlackHoleVfxPrefab != null)
            {
                // Strip Mirror network components so the VFX can be instantiated
                // as a local-only object without a network context.
                StripNetworkComponents(BlackHoleVfxPrefab);
                GameObject.DontDestroyOnLoad(BlackHoleVfxPrefab);
            }

            BlackHoleGrenadePrefab = Load<GameObject>("black_hole_grenade.prefab");
            if (BlackHoleGrenadePrefab != null)
            {
                EnsureNetworkIdentity(BlackHoleGrenadePrefab, 0xB14C0001u);
                DisableRigidbody(BlackHoleGrenadePrefab);
                return;
            }

            // Fallback: build a minimal networked sphere so the item works even
            // without a dedicated bundle asset.  Artists can replace this later.
            IssaPluginPlugin.Log.LogWarning(
                "[Assets] black_hole_grenade.prefab not found — using fallback sphere."
            );
            BlackHoleGrenadePrefab = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            BlackHoleGrenadePrefab.name = "BlackHoleGrenade_Fallback";
            BlackHoleGrenadePrefab.transform.localScale = Vector3.one * 0.4f;
            var bhRb =
                BlackHoleGrenadePrefab.GetComponent<Rigidbody>()
                ?? BlackHoleGrenadePrefab.AddComponent<Rigidbody>();
            bhRb.isKinematic = true;
            bhRb.useGravity = false;
            // Disable the collider so the template sitting at origin doesn't
            // interfere with in-scene physics.  Spawned instances re-enable it via
            // BlackHoleGrenadeBehaviour (Rigidbody is already kinematic/no-gravity).
            var bhCol = BlackHoleGrenadePrefab.GetComponent<SphereCollider>();
            if (bhCol)
                bhCol.enabled = false;
            EnsureNetworkIdentity(BlackHoleGrenadePrefab, 0xB14C0001u);
            GameObject.DontDestroyOnLoad(BlackHoleGrenadePrefab);
        }

        private static void LoadDroppedItemPrefab()
        {
            DroppedCustomItemPrefab = Load<GameObject>("DroppedCustomItem.prefab");
            if (DroppedCustomItemPrefab == null)
                return;

            EnsureNetworkIdentity(DroppedCustomItemPrefab, 0xD20D0001u);
            DroppedCustomItemPrefab.SetActive(false);
            // Force kinematic regardless of what the bundle has baked in.
            DisableRigidbody(DroppedCustomItemPrefab);
            // Make the pickup collider a trigger so it doesn't block player movement.
            // Physics.OverlapBoxNonAlloc uses QueryTriggerInteraction.Collide, so
            // triggers are still detected by PlayerInteractableTargeter.
            var dropCol = DroppedCustomItemPrefab.GetComponent<SphereCollider>();
            if (dropCol)
                dropCol.isTrigger = true;
            DroppedCustomItemPrefab.AddComponent<Entity>();
            DroppedCustomItemPrefab.AddComponent<DroppedCustomItem>();
            GameObject.DontDestroyOnLoad(DroppedCustomItemPrefab);
        }

        private static void LoadPlaceableWallAssets()
        {
            WallHandheldPrefab = Load<GameObject>("brick.prefab");
            if (WallHandheldPrefab != null)
            {
                DisableRigidbody(WallHandheldPrefab);
            }
            else
            {
                WallHandheldPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                WallHandheldPrefab.name = "PlaceableWallHandheld_Fallback";
                WallHandheldPrefab.transform.localScale = new Vector3(0.4f, 0.3f, 0.05f);
                DisableRigidbody(WallHandheldPrefab);
                GameObject.DontDestroyOnLoad(WallHandheldPrefab);
            }

            WallPrefab = Load<GameObject>("wall.prefab");
            if (WallPrefab != null)
            {
                EnsureNetworkIdentity(WallPrefab, 0x4411000Au);
                DisableRigidbody(WallPrefab);
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Assets] wall.prefab not found — using fallback box."
                );
                WallPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                WallPrefab.name = "PlaceableWall_Fallback";
                WallPrefab.transform.localScale = new Vector3(4f, 3f, 0.3f);
                var wallRb = WallPrefab.AddComponent<Rigidbody>();
                wallRb.isKinematic = true;
                wallRb.useGravity = false;
                EnsureNetworkIdentity(WallPrefab, 0x4411000Au);
                GameObject.DontDestroyOnLoad(WallPrefab);
            }

            // Build the ghost template from WallPrefab.
            // Instantiate at load time (before any network session is active) so
            // DestroyImmediate on Mirror components is safe and has no side-effects.
            if (WallPrefab == null)
                return;

            WallGhostPrefab = GameObject.Instantiate(WallPrefab);
            WallGhostPrefab.name = "PlaceableWall_GhostTemplate";
            StripNetworkComponents(WallGhostPrefab);
            // Disable physics — ghost is purely visual.
            foreach (var rb in WallGhostPrefab.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
            foreach (var col in WallGhostPrefab.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            WallGhostPrefab.SetActive(false);
            GameObject.DontDestroyOnLoad(WallGhostPrefab);
            IssaPluginPlugin.Log.LogInfo("[Assets] WallGhostPrefab created from WallPrefab.");
        }

        private static void LoadAK47Assets()
        {
            AK47Prefab = Load<GameObject>("ak47.prefab");
            if (AK47Prefab != null)
                DisableRigidbody(AK47Prefab);
        }

        private static void LoadHarrierAssets()
        {
            HarrierPrefab = Load<GameObject>("harrier.prefab");
            if (HarrierPrefab != null)
            {
                EnsureNetworkIdentity(HarrierPrefab, 0xA7700001u);
                DisableRigidbody(HarrierPrefab);
                HarrierPrefab.AddComponent<HarrierClientSetup>();
            }

            HarrierTabletPrefab = Load<GameObject>("harrier_tablet.prefab");
            if (HarrierTabletPrefab != null)
                DisableRigidbody(HarrierTabletPrefab);
        }

        private static void LoadPositionSwapAssets()
        {
            PositionSwapHandheldPrefab = Load<GameObject>("position_swap_handheld.prefab");
            if (PositionSwapHandheldPrefab != null)
                DisableRigidbody(PositionSwapHandheldPrefab);

            PositionSwapOrbPrefab = Load<GameObject>("position_swap_orb.prefab");
            if (PositionSwapOrbPrefab != null)
                StripNetworkComponents(PositionSwapOrbPrefab);

            PositionSwapSmokePrefab = Load<GameObject>("position_swap_smoke.prefab");
            if (PositionSwapSmokePrefab != null)
                StripNetworkComponents(PositionSwapSmokePrefab);
        }

        private static void LoadPoisonJarAssets()
        {
            PoisonJarHandheldPrefab = Load<GameObject>("posion_bottle.prefab");
            if (PoisonJarHandheldPrefab != null)
                DisableRigidbody(PoisonJarHandheldPrefab);

            PoisonJarPrefab = Load<GameObject>("posion_bottle.prefab");
            if (PoisonJarPrefab != null)
            {
                EnsureNetworkIdentity(PoisonJarPrefab, 0xD001A501u);
                DisableRigidbody(PoisonJarPrefab);
            }

            PoisonSplashPrefab = Load<GameObject>("poison_cloud_vfx.prefab");
            if (PoisonSplashPrefab != null)
                StripNetworkComponents(PoisonSplashPrefab);
        }

        private static void LoadDroneSwarmAssets()
        {
            DroneControllerPrefab = Load<GameObject>("drone_swarm_tablet.prefab");
            if (DroneControllerPrefab != null)
                DisableRigidbody(DroneControllerPrefab);

            DronePrefab = Load<GameObject>("drone.prefab");
            if (DronePrefab != null)
            {
                EnsureNetworkIdentity(DronePrefab, 0xD40E0001u);
                // Rigidbody starts kinematic; DroneBehaviour sets it up in Start().
                DisableRigidbody(DronePrefab);
            }

            DroneExplosionVfxPrefab = Load<GameObject>("drone_explosion.prefab");
            if (DroneExplosionVfxPrefab != null)
                StripNetworkComponents(DroneExplosionVfxPrefab);
        }

        private static void LoadGravityGunAssets()
        {
            ElectricWhipHandheldPrefab = Load<GameObject>("gravity_gun.prefab");
            DisableRigidbody(ElectricWhipHandheldPrefab);

            /// Local-only VFX parented to the tether target while the session is active.
            /// Not networked — each client instantiates its own copy.
            /// Bundle asset name: <c>gravity_tether_vfx.prefab</c>
            GravityGunTetherVfxPrefab = Load<GameObject>("gravity_gun_vfx.prefab");
            if (GravityGunTetherVfxPrefab != null)
                StripNetworkComponents(GravityGunTetherVfxPrefab);
        }

        private static void LoadRocketTetherAssets()
        {
            RocketTetherIcon = LoadSprite("rocket_tether_icon.png");
            RocketTetherPrefab = Load<GameObject>("player_linker.prefab");
            if (RocketTetherPrefab != null)
                DisableRigidbody(RocketTetherPrefab);
            RocketTetherRocketPrefab = Load<GameObject>("player_linker_rocket.prefab");
            if (RocketTetherRocketPrefab != null)
                StripNetworkComponents(RocketTetherRocketPrefab);
        }

        private static void LoadSuperDonutAssets()
        {
            // Optional dedicated assets — falls back to Donut assets at runtime if absent.
            // Add super_donut_icon.png and super_donut_model.prefab to the bundle to override.
            SuperDonutIcon = _bundle.LoadAsset<Texture2D>("super_donut_icon.png") is Texture2D tex
                ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f))
                : null;

            SuperDonutHandheldPrefab = _bundle.LoadAsset<GameObject>("super_donut_model.prefab");
            if (SuperDonutHandheldPrefab != null)
                DisableRigidbody(SuperDonutHandheldPrefab);
        }

        private static void LoadRedBullAssets()
        {
            RedBullHandheldPrefab = Load<GameObject>("redbull.prefab");
            if (RedBullHandheldPrefab != null)
                DisableRigidbody(RedBullHandheldPrefab);

            RedBullTrailPrefab = Load<GameObject>("red_bull_trail.prefab");
            if (RedBullTrailPrefab != null)
                StripNetworkComponents(RedBullTrailPrefab);
        }

        private static void LoadJetpackAssets()
        {
            JetpackIcon = LoadSprite("jetpack_icon.png");

            // Both prefabs below are local-only (instantiated without a network context),
            // so Mirror components must be stripped to prevent NullReferenceExceptions —
            // same pattern as RedBullTrailPrefab and BlackHoleVfxPrefab.
            JetpackEquippedPrefab = Load<GameObject>("jetpack.prefab");
            if (JetpackEquippedPrefab != null)
                StripNetworkComponents(JetpackEquippedPrefab);

            JetpackParticlePrefab = Load<GameObject>("jetpack_particles.prefab");
            if (JetpackParticlePrefab != null)
                StripNetworkComponents(JetpackParticlePrefab);
        }

        /// Ensures a prefab has a NetworkIdentity with a stable assetId so Mirror
        /// can spawn it on clients. If the prefab has no NetworkIdentity one is added.
        /// If the baked-in assetId is 0 (bundle built without Mirror's editor tool),
        /// the stable uint is written via reflection so RegisterPrefab doesn't skip it.
        private static void EnsureNetworkIdentity(GameObject prefab, uint stableAssetId)
        {
            if (prefab == null)
                return;

            var assetIdField = typeof(NetworkIdentity).GetField(
                "assetId",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            var ni = prefab.GetComponent<NetworkIdentity>();
            if (ni == null)
            {
                ni = prefab.AddComponent<NetworkIdentity>();
                assetIdField?.SetValue(ni, stableAssetId);
                IssaPluginPlugin.Log.LogInfo(
                    $"[Assets] Added NetworkIdentity to {prefab.name} with assetId={stableAssetId}."
                );
            }
            else if (ni.assetId == 0)
            {
                assetIdField?.SetValue(ni, stableAssetId);
                IssaPluginPlugin.Log.LogInfo(
                    $"[Assets] {prefab.name} had assetId=0; set stable assetId={stableAssetId}."
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[Assets] {prefab.name} already has NetworkIdentity (assetId={ni.assetId})."
                );
            }

            GameObject.DontDestroyOnLoad(prefab);
        }

        // Helper that warns on null.
        private static T Load<T>(string name)
            where T : UnityEngine.Object
        {
            var asset = _bundle.LoadAsset<T>(name);
            if (asset == null)
                IssaPluginPlugin.Log.LogError($"[Assets] Missing asset: {name}");
            return asset;
        }

        private static void DisableRigidbody(GameObject go)
        {
            if (go == null)
                return;
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                return;

            rb.isKinematic = true;
            rb.useGravity = false;
        }

        /// Destroys Mirror network tick components from a prefab that will only
        /// ever be used as a local visual (held item / dropped model).  Without
        /// this, NetworkTransformReliable and NetworkRigidbodyReliable start
        /// updating every frame and throw NullReferenceException because the
        /// prefab instance has no network context.
        private static void StripNetworkComponents(GameObject go)
        {
            if (go == null)
                return;
            foreach (var c in go.GetComponentsInChildren<NetworkBehaviour>(true))
                Object.DestroyImmediate(c);
            foreach (var ni in go.GetComponentsInChildren<NetworkIdentity>(true))
                Object.DestroyImmediate(ni);
        }

        private static Sprite LoadSprite(string name)
        {
            var tex = Load<Texture2D>(name);
            if (tex == null)
                return null;
            return Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f)
            );
        }

        private static Texture2D LoadTexture2D(string name)
        {
            return Load<Texture2D>(name);
        }

        public static void Unload()
        {
            _bundle?.Unload(true);
            _bundle = null;
        }
    }
}
