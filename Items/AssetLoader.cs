using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ─────────────────────────────────────────────────────────────────────────
    //  AssetLoader
    //
    //  All mod assets live in a single AssetBundle ("issamod") shipped next to
    //  the plugin DLL.  This class loads the bundle once at startup, populates
    //  every public property, and unloads cleanly on plugin shutdown.
    //
    //  HOW TO ADD A NEW ASSET
    //  ──────────────────────
    //  1. Add a public static property below (with an XML summary if the asset
    //     has a non-obvious purpose).
    //  2. Add one entry to the _assetDefs table in BuildAssetDefs(), using the
    //     helper that matches your asset's type/treatment:
    //
    //     • SpriteAsset(prop, "file.png")
    //         Loads a Texture2D and wraps it as a centered Sprite.
    //
    //     • Texture(prop, "file.png")
    //         Loads a raw Texture2D (e.g. for SniperScopeTexture).
    //
    //     • Prefab(prop, "name.prefab")
    //         Loads a GameObject. No further mutations.
    //
    //     • HandheldPrefab(prop, "name.prefab")
    //         Loads a GameObject and disables its Rigidbody so it can sit in
    //         the player's hand without interfering with physics.
    //
    //     • NetworkedPrefab(prop, "name.prefab", assetId, clientSetupType?)
    //         Loads a networked GameObject, assigns a stable Mirror assetId,
    //         disables its Rigidbody (re-enabled at runtime by the behaviour),
    //         and optionally adds a one-time ClientSetup component.
    //
    //     • LocalVfxPrefab(prop, "name.prefab")
    //         Loads a local-only VFX prefab and strips all Mirror components
    //         so it can be instantiated freely without a network context.
    //
    //     • Audio(prop, "name")
    //         Loads an AudioClip (no file extension — Unity compiles audio to
    //         an internal format at bundle-build time).
    //
    //  That's it.  No private Load*() method needed.
    // ─────────────────────────────────────────────────────────────────────────
    public static class AssetLoader
    {
        // ── Icons / Textures ──────────────────────────────────────────────────
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
        public static Sprite BearIcon { get; private set; }
        public static Sprite NukeIcon { get; private set; }
        public static Sprite BlackHoleGrenadeIcon { get; private set; }
        public static Sprite WallIcon { get; private set; }
        public static Sprite AK47Icon { get; private set; }
        public static Sprite HarrierIcon { get; private set; }
        public static Sprite PositionSwapIcon { get; private set; }
        public static Sprite PoisonJarIcon { get; private set; }
        public static Sprite DroneSwarmIcon { get; private set; }
        public static Sprite HunterDroneIcon { get; private set; }
        public static Sprite ElectricGravityGunIcon { get; private set; }
        public static Sprite RedBullIcon { get; private set; }
        public static Sprite WindStormIcon { get; private set; }
        public static Sprite UfoAbductionIcon { get; private set; }

        /// Falls back to DonutIcon at runtime if the asset is absent from the bundle.
        public static Sprite SuperDonutIcon { get; private set; }

        /// Used by SniperScopeOverlay as a raw texture (not a Sprite).
        public static Texture2D SniperScopeTexture { get; private set; }

        // ── Handheld / simple prefabs (no network, Rigidbody disabled) ────────
        public static GameObject BatModelPrefab { get; private set; }
        public static GameObject FreezeModelPrefab { get; private set; }
        public static GameObject LowGravityModelPrefab { get; private set; }
        public static GameObject SniperRiflePrefab { get; private set; }
        public static GameObject DonutHandheldPrefab { get; private set; }
        public static GameObject JavelinHandheldPrefab { get; private set; }
        public static GameObject TeddyBearPrefab { get; private set; }
        public static GameObject NuclearDetonatorPrefab { get; private set; }
        public static GameObject WallHandheldPrefab { get; private set; }
        public static GameObject AK47Prefab { get; private set; }
        public static GameObject HarrierTabletPrefab { get; private set; }
        public static GameObject PositionSwapHandheldPrefab { get; private set; }
        public static GameObject PoisonJarHandheldPrefab { get; private set; }
        public static GameObject DroneControllerPrefab { get; private set; }
        public static GameObject ElectricWhipHandheldPrefab { get; private set; }
        public static GameObject RedBullHandheldPrefab { get; private set; }
        public static GameObject WindStormModelPrefab { get; private set; }

        /// Falls back to DonutHandheldPrefab at runtime if the asset is absent.
        public static GameObject SuperDonutHandheldPrefab { get; private set; }

        // ── Tablet / UI prefabs (no network, Rigidbody disabled) ─────────────
        public static GameObject BomberTabletPrefab { get; private set; }
        public static GameObject MissileTabletPrefab { get; private set; }
        public static GameObject Ac130TabletPrefab { get; private set; }

        // ── Networked prefabs (Mirror NetworkIdentity + stable assetId) ───────
        public static GameObject BomberPrefab { get; private set; }
        public static GameObject BomberProxyPrefab { get; private set; }
        public static GameObject AC130Prefab { get; private set; }
        public static GameObject DonutPrefab { get; private set; }
        public static GameObject StickyGrenadePrefab { get; private set; }
        public static GameObject BearPrefab { get; private set; }
        public static GameObject NukeBombPrefab { get; private set; }
        public static GameObject BlackHoleGrenadePrefab { get; private set; }
        public static GameObject WallPrefab { get; private set; }
        public static GameObject HarrierPrefab { get; private set; }
        public static GameObject PoisonJarPrefab { get; private set; }
        public static GameObject DronePrefab { get; private set; }

        /// Handheld hunter drone model shown in the player's hand before use.
        /// Null until hunter_drone_handheld.prefab is added to the bundle.
        public static GameObject HunterDroneHandheldPrefab { get; private set; }

        /// Networked hunter drone projectile. Null until hunter_drone.prefab is added to the bundle.
        public static GameObject HunterDronePrefab { get; private set; }

        /// Handheld UFO model shown in the player's hand. Null until ufo_abduction_handheld.prefab is added.
        public static GameObject UfoAbductionHandheldPrefab { get; private set; }

        /// Client-only UFO VFX that flies over and abducts the victim. Null until ufo_abduction.prefab is added.
        public static GameObject UfoAbductionUfoPrefab { get; private set; }

        // ── CubeBall / SuperCubeBall shape prefabs ────────────────────────────
        /// Visual-only shape prefabs spawned as children of the golf ball.
        /// Each prefab must contain a MeshFilter + MeshRenderer + MeshCollider (convex).
        /// No Rigidbody, NetworkIdentity, or NetworkTransform needed.
        public static GameObject CubeBallShapeCube { get; private set; }
        public static GameObject CubeBallShapeDisk { get; private set; }
        public static GameObject CubeBallShapeCylinder { get; private set; }
        public static GameObject CubeBallShapeCone { get; private set; }
        public static GameObject CubeBallShapePyramid { get; private set; }

        // ── Explosive Golf Balls ──────────────────────────────────────────────
        /// Item icon for Explosive Golf Balls. Null until explosive_golf_balls_icon.png is added to the bundle.
        public static Sprite ExplosiveGolfBallsIcon { get; private set; }

        /// Handheld model shown in the player's hand. Null until explosive_golf_balls_handheld.prefab is added.
        public static GameObject ExplosiveGolfBallsHandheldPrefab { get; private set; }

        /// Item icon for Cube Ball. Null until cube_ball_icon.png is added to the bundle.
        public static Sprite CubeBallIcon { get; private set; }

        /// Item icon for Super Cube Ball. Falls back to CubeBallIcon at runtime if absent.
        public static Sprite SuperCubeBallIcon { get; private set; }

        /// Shared handheld/dropped prefab for both CubeBall and SuperCubeBall.
        /// Null until cube_ball_handheld.prefab is added to the bundle.
        public static GameObject CubeBallHandheldPrefab { get; private set; }

        // ── Moon ──────────────────────────────────────────────────────────────
        /// Item icon for Majora's Moon. Null until moon_icon.png is added to the bundle.
        public static Sprite MoonIcon { get; private set; }

        /// Handheld model shown in the player's hand before activation. Null until moon_handheld.prefab is added.
        public static GameObject MoonHandheldPrefab { get; private set; }

        /// Client-only moon VFX prefab that approaches the course. Null until moon.prefab is added.
        public static GameObject MoonVfxPrefab { get; private set; }

        /// Networked droppable-item prefab; carries NetworkIdentity, NetworkTransform,
        /// Rigidbody (kinematic), SphereCollider (trigger), Entity, and DroppedCustomItem.
        public static GameObject DroppedCustomItemPrefab { get; private set; }

        // ── Local-only VFX prefabs (Mirror components stripped) ───────────────
        public static GameObject BlackHoleVfxPrefab { get; private set; }
        public static GameObject PositionSwapOrbPrefab { get; private set; }
        public static GameObject PositionSwapSmokePrefab { get; private set; }
        public static GameObject PoisonSplashPrefab { get; private set; }
        public static GameObject DroneExplosionVfxPrefab { get; private set; }
        public static GameObject RedBullTrailPrefab { get; private set; }
        public static GameObject GravityGunTetherVfxPrefab { get; private set; }
        public static GameObject WarningParticlePrefab { get; private set; }
        public static GameObject MaydaySmokeTrailPrefab { get; private set; }
        public static GameObject MaydayFireTrailPrefab { get; private set; }
        public static GameObject ConfettiBlastRainbow { get; private set; }
        public static GameObject BloodSplatterPrefab { get; private set; }

        // ── Shared VFX prefabs (used by multiple items) ───────────────────────
        /// Shared explosion VFX.  Used by Javelin, Nuke, and AC130 Mayday.
        public static GameObject NukeVerticalExplosionVfxPrefab { get; private set; }

        /// Nuke-specific explosion VFX.  Falls back to NukeVerticalExplosionVfxPrefab
        /// if "nuclear_explosion.prefab" is absent from the bundle.
        public static GameObject NukeExplosionVfxPrefab { get; private set; }

        // Javelin convenience aliases that point at the shared assets.
        public static GameObject JavelinExplosionVfxPrefab => NukeVerticalExplosionVfxPrefab;
        public static GameObject JavelinTrailVfxPrefab { get; private set; }

        /// Explosion VFX for the AC130 Mayday crash.  Points at the shared
        /// NukeVerticalExplosionVfxPrefab.
        public static GameObject MaydayExplosionVfxPrefab => NukeVerticalExplosionVfxPrefab;

        // ── Impact / crash VFX ────────────────────────────────────────────────
        /// Secondary debris/dust VFX spawned at a crash site (e.g. AC130 impact).
        /// Bundle asset name: <c>impact_vfx.prefab</c>  (fill in if different).
        public static GameObject ImpactVfxPrefab { get; private set; }

        // ── Teleporter ────────────────────────────────────────────────────────
        public static Sprite TeleporterIcon { get; private set; }
        public static GameObject TeleporterHandheldPrefab { get; private set; }
        public static GameObject TeleporterVfxPrefab { get; private set; }

        // ── Flamethrower ──────────────────────────────────────────────────────
        public static Sprite FlamethrowerIcon { get; private set; }
        public static GameObject FlamethrowerPrefab { get; private set; }
        public static GameObject FlamethrowerParticlePrefab { get; private set; }
        public static GameObject FlamethrowerVictimFirePrefab { get; private set; }

        // ── Jetpack ───────────────────────────────────────────────────────────
        public static Sprite JetpackIcon { get; private set; }
        public static GameObject JetpackHandheldPrefab { get; private set; }
        public static GameObject JetpackParticlePrefab { get; private set; }

        /// The networked equipped-jetpack object visible on all clients.
        public static GameObject JetpackEquippedPrefab { get; private set; }

        // ── Rocket Tether ─────────────────────────────────────────────────────
        public static Sprite RocketTetherIcon { get; private set; }

        /// The networked handheld/model prefab for the Rocket Tether item.
        public static GameObject RocketTetherPrefab { get; private set; }

        /// Local-only rocket visual used during tethering.
        public static GameObject RocketTetherRocketPrefab { get; private set; }

        // ── Rocket Tether Grenade ─────────────────────────────────────────────
        public static Sprite RocketTetherGrenadeIcon { get; private set; }

        /// Networked grenade projectile for the Rocket Tether Grenade item.
        public static GameObject RocketTetherGrenadePrefab { get; private set; }

        /// Local-only explosion VFX spawned when the grenade detonates.
        public static GameObject RocketTetherGrenadeExplosionVfx { get; private set; }

        // ── Spinach ───────────────────────────────────────────────────────────
        public static Sprite SpinachIcon { get; private set; }
        public static GameObject SpinachPrefab { get; private set; }

        /// Local-only speed-boost trail VFX parented to the player.
        public static GameObject SpinachTrailPrefab { get; private set; }

        // ── First Place Star ──────────────────────────────────────────────────
        /// Local-only gold star VFX shown above the leading player.
        public static GameObject GoldStarPrefab { get; private set; }

        // ── Ghost prefab (built at load time from WallPrefab) ─────────────────
        /// Local-only ghost used by PlaceableWallPlacementPreview.
        /// Mirror and physics components are stripped; the prefab starts inactive.
        public static GameObject WallGhostPrefab { get; private set; }

        // ── Javelin secondary assets ──────────────────────────────────────────
        public static GameObject JavelinTargetIndicatorPrefab { get; private set; }

        // ── Audio ─────────────────────────────────────────────────────────────
        public static AudioClip AC130AboveClip { get; private set; }
        public static AudioClip HomerunAudioClip { get; private set; }

        /// Looping cockpit alarm played during the AC130 Mayday dive.
        public static AudioClip MaydayAlarmClip { get; private set; }

        /// One-shot impact/explosion sound at the Mayday crash site.
        /// Also reused as NukeExplosionClip.
        public static AudioClip MaydayImpactClip { get; private set; }

        /// Convenience alias — points at MaydayImpactClip.
        public static AudioClip NukeExplosionClip => MaydayImpactClip;

        // ── State ─────────────────────────────────────────────────────────────
        public static bool IsLoaded => _bundle != null;

        private static AssetBundle _bundle;

        // ─────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────

        public static void Load()
        {
            if (!TryLoadBundle())
                return;

            // Phase 1: load every asset declared in the table.
            foreach (var def in BuildAssetDefs())
                def.Execute(_bundle);

            // Phase 2: post-load mutations that require cross-asset references
            //          or programmatic construction.
            ApplyPostLoadMutations();

            IssaPluginPlugin.Log.LogInfo("[Assets] IssaPluginBundle loaded.");
        }

        public static void Unload()
        {
            _bundle?.Unload(true);
            _bundle = null;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Asset definition table
        //
        //  Each entry fully describes one asset: where to find it in the bundle,
        //  which property to write, and how to post-process it.  The Execute()
        //  method on each record performs the actual load + mutation.
        //
        //  Helper factories at the bottom of this region keep call-sites terse.
        // ─────────────────────────────────────────────────────────────────────

        private static IEnumerable<AssetDef> BuildAssetDefs()
        {
            return new AssetDef[]
            {
                // ── Icons (Sprite) ────────────────────────────────────────────
                SpriteAsset(p => BatIcon = p, "bat_icon.png"),
                SpriteAsset(p => BomberIcon = p, "bomber_icon.png"),
                SpriteAsset(p => MissileIcon = p, "missile_icon.png"),
                SpriteAsset(p => AC130Icon = p, "ac130_icon.png"),
                SpriteAsset(p => FreezeIcon = p, "freeze_effect_icon.png"),
                SpriteAsset(p => LowGravityIcon = p, "gravity_remote_icon.png"),
                SpriteAsset(p => SniperRifleIcon = p, "sniper_rifle_icon.png"),
                SpriteAsset(p => DonutIcon = p, "donut_icon_v2.png"),
                SpriteAsset(p => JavelinIcon = p, "javelin_icon.png"),
                SpriteAsset(p => StickyGrenadeIcon = p, "spike_ball_icon.png"),
                SpriteAsset(p => BearIcon = p, "bear_icon.png"),
                SpriteAsset(p => NukeIcon = p, "nuke_icon.png"),
                SpriteAsset(p => BlackHoleGrenadeIcon = p, "black_hole_grenade_icon.png"),
                SpriteAsset(p => WallIcon = p, "wall_icon.png"),
                SpriteAsset(p => AK47Icon = p, "ak47_icon.png"),
                SpriteAsset(p => HarrierIcon = p, "harrier_icon.png"),
                SpriteAsset(p => PositionSwapIcon = p, "position_swap_icon.png"),
                SpriteAsset(p => PoisonJarIcon = p, "poison_bottle_icon.png"),
                SpriteAsset(p => DroneSwarmIcon = p, "drone_swarm_icon.png"),
                SpriteAsset(p => HunterDroneIcon = p, "hunter_drone_icon.png", optional: true),
                HandheldPrefab(
                    p => HunterDroneHandheldPrefab = p,
                    "hunter_drone_handheld.prefab",
                    optional: true
                ),
                SpriteAsset(p => ElectricGravityGunIcon = p, "gravity_gun_icon.png"),
                SpriteAsset(p => RedBullIcon = p, "redbull_icon.png"),
                SpriteAsset(p => WindStormIcon = p, "wind_storm_icon.png", optional: true),
                SpriteAsset(p => SuperDonutIcon = p, "super_donut_icon.png", optional: true),
                // ── Textures ──────────────────────────────────────────────────
                Texture(p => SniperScopeTexture = p, "sniper_scope.png"),
                // ── Handheld prefabs (Rigidbody disabled) ─────────────────────
                HandheldPrefab(p => BatModelPrefab = p, "bat_model.prefab"),
                HandheldPrefab(p => FreezeModelPrefab = p, "snowball.prefab"),
                HandheldPrefab(p => LowGravityModelPrefab = p, "gravity_remote.prefab"),
                HandheldPrefab(p => SniperRiflePrefab = p, "intervention.prefab"),
                HandheldPrefab(p => DonutHandheldPrefab = p, "donut_model.prefab"),
                HandheldPrefab(p => JavelinHandheldPrefab = p, "javelin_rocket_launcher.prefab"),
                HandheldPrefab(p => TeddyBearPrefab = p, "teddy.prefab"),
                HandheldPrefab(p => NuclearDetonatorPrefab = p, "nuclear_detonator.prefab"),
                HandheldPrefab(
                    p => WallHandheldPrefab = p,
                    "brick.prefab",
                    optional: true,
                    fallback: BuildWallHandheldFallback
                ),
                HandheldPrefab(p => AK47Prefab = p, "ak47.prefab"),
                HandheldPrefab(p => HarrierTabletPrefab = p, "harrier_tablet.prefab"),
                HandheldPrefab(
                    p => PositionSwapHandheldPrefab = p,
                    "position_swap_handheld.prefab"
                ),
                HandheldPrefab(p => PoisonJarHandheldPrefab = p, "posion_bottle.prefab"),
                HandheldPrefab(p => DroneControllerPrefab = p, "drone_swarm_tablet.prefab"),
                HandheldPrefab(p => ElectricWhipHandheldPrefab = p, "gravity_gun.prefab"),
                HandheldPrefab(p => RedBullHandheldPrefab = p, "redbull.prefab"),
                HandheldPrefab(
                    p => WindStormModelPrefab = p,
                    "weather_remote.prefab",
                    optional: true
                ),
                HandheldPrefab(
                    p => SuperDonutHandheldPrefab = p,
                    "super_donut_model.prefab",
                    optional: true
                ),
                // ── Tablet / control-surface prefabs (Rigidbody disabled) ─────
                HandheldPrefab(p => BomberTabletPrefab = p, "stealth_bomber_tablet.prefab"),
                HandheldPrefab(p => MissileTabletPrefab = p, "predator_missile_tablet.prefab"),
                HandheldPrefab(p => Ac130TabletPrefab = p, "ac130_tablet.prefab"),
                // ── Simple (non-networked) prefabs ────────────────────────────
                Prefab(p => BomberPrefab = p, "bomber_model.prefab"),
                Prefab(p => JavelinTargetIndicatorPrefab = p, "javelin_target_indicator.prefab"),
                // ── Networked prefabs ─────────────────────────────────────────
                // assetId constants are stable across builds; they identify each prefab
                // to Mirror's spawning system.  Never reuse an id for a different prefab.
                NetworkedPrefab(
                    p => BomberProxyPrefab = p,
                    "bomber_proxy.prefab",
                    0xB0AA0001u,
                    typeof(BomberProxyClientSetup)
                ),
                NetworkedPrefab(
                    p => AC130Prefab = p,
                    "ac130_model.prefab",
                    0xAC130001u,
                    typeof(AC130ClientSetup)
                ),
                NetworkedPrefab(
                    p => DonutPrefab = p,
                    "donut_vehicle.prefab",
                    0xF0000001u,
                    typeof(DonutClientSetup)
                ),
                NetworkedPrefab(
                    p => StickyGrenadePrefab = p,
                    "spike_ball.prefab",
                    0x5E47EC01u,
                    typeof(StickyGrenadeClientSetup)
                ),
                NetworkedPrefab(
                    p => BearPrefab = p,
                    "bear.prefab",
                    0xBEA00001u,
                    typeof(BearClientSetup)
                ),
                NetworkedPrefab(p => NukeBombPrefab = p, "nuclear_bomb.prefab", 0xF1550001u),
                NetworkedPrefab(
                    p => HarrierPrefab = p,
                    "harrier.prefab",
                    0xA7700001u,
                    typeof(HarrierClientSetup),
                    optional: true,
                    fallback: BuildHarrierFallback
                ),
                NetworkedPrefab(
                    p => WallPrefab = p,
                    "wall.prefab",
                    0x4411000Au,
                    optional: true,
                    fallback: BuildWallFallback
                ),
                NetworkedPrefab(p => PoisonJarPrefab = p, "posion_bottle.prefab", 0xD001A501u),
                NetworkedPrefab(p => DronePrefab = p, "drone.prefab", 0xD40E0001u),
                NetworkedPrefab(
                    p => HunterDronePrefab = p,
                    "hunter_drone.prefab",
                    0xD40E0002u,
                    optional: true
                ),
                NetworkedPrefab(
                    p => BlackHoleGrenadePrefab = p,
                    "black_hole_grenade.prefab",
                    0xB14C0001u,
                    optional: true,
                    fallback: BuildBlackHoleGrenadeFallback
                ),
                // DroppedCustomItemPrefab needs extra component wiring — see PostLoad.
                NetworkedPrefab(
                    p => DroppedCustomItemPrefab = p,
                    "DroppedCustomItem.prefab",
                    0xD20D0001u
                ),
                // ── Shared VFX ────────────────────────────────────────────────
                Prefab(p => NukeVerticalExplosionVfxPrefab = p, "NukeVerticalExplosionFire.prefab"),
                // ── Local-only VFX (Mirror components stripped) ───────────────
                LocalVfxPrefab(p => BlackHoleVfxPrefab = p, "black_hole.prefab"),
                LocalVfxPrefab(p => PositionSwapOrbPrefab = p, "position_swap_orb.prefab"),
                LocalVfxPrefab(p => PositionSwapSmokePrefab = p, "position_swap_smoke.prefab"),
                LocalVfxPrefab(p => PoisonSplashPrefab = p, "poison_cloud_vfx.prefab"),
                LocalVfxPrefab(p => DroneExplosionVfxPrefab = p, "drone_explosion.prefab"),
                LocalVfxPrefab(p => RedBullTrailPrefab = p, "red_bull_trail.prefab"),
                LocalVfxPrefab(p => GravityGunTetherVfxPrefab = p, "gravity_gun_vfx.prefab"),
                LocalVfxPrefab(p => WarningParticlePrefab = p, "warning_particle.prefab"),
                LocalVfxPrefab(p => MaydaySmokeTrailPrefab = p, "smoke_prefab.prefab"),
                LocalVfxPrefab(p => MaydayFireTrailPrefab = p, "fire_torch_intense.prefab"),
                LocalVfxPrefab(p => ConfettiBlastRainbow = p, "ConfettiBlastRainbow.prefab"),
                LocalVfxPrefab(p => BloodSplatterPrefab = p, "blood_explosion_vfx.prefab"),
                LocalVfxPrefab(p => JavelinTrailVfxPrefab = p, "javelin_trail.prefab"),
                // ── Audio ─────────────────────────────────────────────────────
                // AudioClips are addressed without file extensions — Unity compiles
                // audio to an internal format at bundle-build time.
                Audio(p => AC130AboveClip = p, "ac130_above"),
                Audio(p => HomerunAudioClip = p, "homerun"),
                Audio(p => MaydayAlarmClip = p, "missile_locked"),
                Audio(p => MaydayImpactClip = p, "etfx_explosion_nuke"),
                // ── Impact VFX ────────────────────────────────────────────────
                LocalVfxPrefab(p => ImpactVfxPrefab = p, "NukeVerticalExplosionFire.prefab"),
                // ── Teleporter ────────────────────────────────────────────────
                // TODO: replace asset names with the real bundle names once known.
                SpriteAsset(p => TeleporterIcon = p, "teleporter_icon.png"),
                HandheldPrefab(p => TeleporterHandheldPrefab = p, "teleporter_handheld.prefab"),
                LocalVfxPrefab(p => TeleporterVfxPrefab = p, "position_swap_smoke.prefab"),
                // ── Flamethrower ──────────────────────────────────────────────
                // TODO: replace asset names with the real bundle names once known.
                SpriteAsset(p => FlamethrowerIcon = p, "flamethrower_icon.png"),
                HandheldPrefab(p => FlamethrowerPrefab = p, "flamethrower.prefab"),
                LocalVfxPrefab(p => FlamethrowerParticlePrefab = p, "flamethrower_vfx.prefab"),
                LocalVfxPrefab(
                    p => FlamethrowerVictimFirePrefab = p,
                    "flamethrower_victim_fire.prefab"
                ),
                // ── Jetpack ───────────────────────────────────────────────────
                // TODO: replace asset names with the real bundle names once known.
                SpriteAsset(p => JetpackIcon = p, "jetpack_icon.png"),
                HandheldPrefab(p => JetpackHandheldPrefab = p, "jetpack_handheld.prefab"),
                LocalVfxPrefab(p => JetpackParticlePrefab = p, "jetpack_particles.prefab"),
                NetworkedPrefab(p => JetpackEquippedPrefab = p, "jetpack.prefab", 0x00000000u), // TODO: assign a stable assetId
                // ── Rocket Tether ─────────────────────────────────────────────
                SpriteAsset(p => RocketTetherIcon = p, "rocket_tether_icon.png"),
                HandheldPrefab(p => RocketTetherPrefab = p, "rocket_tether_handheld.prefab"),
                LocalVfxPrefab(p => RocketTetherRocketPrefab = p, "player_linker_rocket.prefab"),
                // ── Rocket Tether Grenade ─────────────────────────────────────
                SpriteAsset(p => RocketTetherGrenadeIcon = p, "rocket_tether_grenade_icon.png"),
                NetworkedPrefab(
                    p => RocketTetherGrenadePrefab = p,
                    "toy_rocket.prefab",
                    0x00000000u
                ), // TODO: assign a stable assetId
                LocalVfxPrefab(
                    p => RocketTetherGrenadeExplosionVfx = p,
                    "rocket_tether_grenade_explosion_vfx.prefab"
                ),
                // ── Spinach ───────────────────────────────────────────────────
                SpriteAsset(p => SpinachIcon = p, "spinach_icon.png"),
                HandheldPrefab(p => SpinachPrefab = p, "spinach.prefab"),
                LocalVfxPrefab(p => SpinachTrailPrefab = p, "spinach_trail.prefab"),
                // ── UFO Abduction ─────────────────────────────────────────────
                SpriteAsset(p => UfoAbductionIcon = p, "ufo_abduction_icon.png", optional: true),
                HandheldPrefab(
                    p => UfoAbductionHandheldPrefab = p,
                    "ufo_abduction_handheld.prefab",
                    optional: true
                ),
                LocalVfxPrefab(p => UfoAbductionUfoPrefab = p, "ufo_abduction.prefab"),
                // ── Moon ──────────────────────────────────────────────────────
                SpriteAsset(p => MoonIcon = p, "moon_icon.png", optional: true),
                HandheldPrefab(p => MoonHandheldPrefab = p, "moon_handheld.prefab", optional: true),
                LocalVfxPrefab(p => MoonVfxPrefab = p, "moon.prefab"),
                // ── CubeBall / SuperCubeBall ──────────────────────────────────
                Prefab(p => CubeBallShapeCube = p, "golf_ball_cube.prefab"),
                Prefab(p => CubeBallShapeDisk = p, "golf_ball_disk.prefab"),
                Prefab(p => CubeBallShapeCylinder = p, "golf_ball_cylinder.prefab"),
                Prefab(p => CubeBallShapeCone = p, "golf_ball_cone.prefab"),
                Prefab(p => CubeBallShapePyramid = p, "golf_ball_pyramid.prefab"),
                SpriteAsset(p => CubeBallIcon = p, "cube_ball_icon.png", optional: true),
                SpriteAsset(p => SuperCubeBallIcon = p, "super_cube_ball_icon.png", optional: true),
                HandheldPrefab(
                    p => CubeBallHandheldPrefab = p,
                    "rubixcube.prefab",
                    optional: true,
                    fallback: BuildCubeBallHandheldFallback
                ),
                // ── Explosive Golf Balls ──────────────────────────────────────
                SpriteAsset(
                    p => ExplosiveGolfBallsIcon = p,
                    "exploding_ball_icon.png",
                    optional: true
                ),
                HandheldPrefab(
                    p => ExplosiveGolfBallsHandheldPrefab = p,
                    "bomb_for_exploding_balls.prefab",
                    optional: true
                ),
                // ── First Place Star ──────────────────────────────────────────
                LocalVfxPrefab(p => GoldStarPrefab = p, "gold_star.prefab"),
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Post-load mutations
        //
        //  Anything that requires cross-referencing already-loaded assets, or
        //  that mutates a prefab in ways that go beyond what an AssetDef can
        //  express, lives here.
        // ─────────────────────────────────────────────────────────────────────

        private static void ApplyPostLoadMutations()
        {
            // NukeExplosionVfxPrefab prefers a dedicated asset; falls back to the
            // shared nuke-fire VFX that Javelin and Mayday already use.
            NukeExplosionVfxPrefab =
                LoadRaw<GameObject>("nuclear_explosion.prefab") ?? NukeVerticalExplosionVfxPrefab;

            // DroppedCustomItemPrefab needs two components wired in code, and its
            // collider must be a trigger (so it doesn't block player movement).
            if (DroppedCustomItemPrefab != null)
            {
                DroppedCustomItemPrefab.SetActive(false);
                var col = DroppedCustomItemPrefab.GetComponent<SphereCollider>();
                if (col != null)
                    col.isTrigger = true;
                DroppedCustomItemPrefab.AddComponent<Entity>();
                DroppedCustomItemPrefab.AddComponent<DroppedCustomItem>();
            }

            // WallGhostPrefab is a stripped, inactive copy of WallPrefab used as the
            // placement-preview ghost.  It is built here (after WallPrefab is loaded)
            // rather than in the asset-def table because it requires the wall itself.
            if (WallPrefab != null)
                WallGhostPrefab = BuildWallGhost(WallPrefab);

            // SuperDonut fallbacks: use Donut assets when the dedicated ones are absent.
            SuperDonutIcon ??= DonutIcon;
            SuperDonutHandheldPrefab ??= DonutHandheldPrefab;

            // SuperCubeBall icon falls back to CubeBall icon when the dedicated one is absent.
            SuperCubeBallIcon ??= CubeBallIcon;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Fallback builders
        //
        //  Called by an AssetDef when the named bundle asset is absent.
        //  Each builder returns a fully configured, DontDestroyOnLoad prefab.
        //  Kept here so the asset table remains readable and these details are
        //  all in one place.
        // ─────────────────────────────────────────────────────────────────────

        private static GameObject BuildCubeBallHandheldFallback()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "CubeBallHandheld_Fallback";
            go.transform.localScale = Vector3.one * 0.15f;
            Object.DontDestroyOnLoad(go);
            go.SetActive(false);
            return go;
        }

        private static GameObject BuildWallHandheldFallback()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PlaceableWallHandheld_Fallback";
            go.transform.localScale = new Vector3(0.4f, 0.3f, 0.05f);
            DisableRigidbody(go);
            GameObject.DontDestroyOnLoad(go);
            return go;
        }

        private static GameObject BuildWallFallback()
        {
            IssaPluginPlugin.Log.LogWarning("[Assets] wall.prefab not found — using fallback box.");
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PlaceableWall_Fallback";
            go.transform.localScale = new Vector3(4f, 3f, 0.3f);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            EnsureNetworkIdentity(go, 0x4411000Au);
            GameObject.DontDestroyOnLoad(go);
            return go;
        }

        private static GameObject BuildHarrierFallback()
        {
            IssaPluginPlugin.Log.LogWarning(
                "[Assets] harrier.prefab not found — using fallback capsule."
            );
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Harrier_Fallback";
            DisableRigidbody(go);
            EnsureNetworkIdentity(go, 0xA7700001u);
            go.AddComponent<HarrierClientSetup>();
            GameObject.DontDestroyOnLoad(go);
            return go;
        }

        private static GameObject BuildBlackHoleGrenadeFallback()
        {
            IssaPluginPlugin.Log.LogWarning(
                "[Assets] black_hole_grenade.prefab not found — using fallback sphere."
            );
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BlackHoleGrenade_Fallback";
            go.transform.localScale = Vector3.one * 0.4f;
            var rb = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var col = go.GetComponent<SphereCollider>();
            if (col != null)
                col.enabled = false;
            EnsureNetworkIdentity(go, 0xB14C0001u);
            GameObject.DontDestroyOnLoad(go);
            return go;
        }

        private static GameObject BuildWallGhost(GameObject wallPrefab)
        {
            var ghost = GameObject.Instantiate(wallPrefab);
            ghost.name = "PlaceableWall_GhostTemplate";
            StripNetworkComponents(ghost);
            foreach (var rb in ghost.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
            foreach (var col in ghost.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            ghost.SetActive(false);
            GameObject.DontDestroyOnLoad(ghost);
            IssaPluginPlugin.Log.LogInfo("[Assets] WallGhostPrefab created from WallPrefab.");
            return ghost;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Asset definition record + factory helpers
        // ─────────────────────────────────────────────────────────────────────

        /// Describes one asset: how to load it, and what to do with the result.
        private abstract class AssetDef
        {
            public abstract void Execute(AssetBundle bundle);
        }

        // -- Sprite -------------------------------------------------------
        private sealed class SpriteDef : AssetDef
        {
            private readonly System.Action<Sprite> _set;
            private readonly string _name;
            private readonly bool _optional;

            public SpriteDef(System.Action<Sprite> set, string name, bool optional)
            {
                _set = set;
                _name = name;
                _optional = optional;
            }

            public override void Execute(AssetBundle bundle)
            {
                var tex = _optional
                    ? LoadOptionalRaw<Texture2D>(bundle, _name)
                    : LoadRequiredRaw<Texture2D>(bundle, _name);
                if (tex != null)
                    _set(
                        Sprite.Create(
                            tex,
                            new Rect(0, 0, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f)
                        )
                    );
            }
        }

        // -- Texture2D -------------------------------------------------------
        private sealed class TextureDef : AssetDef
        {
            private readonly System.Action<Texture2D> _set;
            private readonly string _name;

            public TextureDef(System.Action<Texture2D> set, string name)
            {
                _set = set;
                _name = name;
            }

            public override void Execute(AssetBundle bundle) =>
                _set(LoadRequiredRaw<Texture2D>(bundle, _name));
        }

        // -- AudioClip -------------------------------------------------------
        private sealed class AudioDef : AssetDef
        {
            private readonly System.Action<AudioClip> _set;
            private readonly string _name;

            public AudioDef(System.Action<AudioClip> set, string name)
            {
                _set = set;
                _name = name;
            }

            public override void Execute(AssetBundle bundle) =>
                _set(LoadOptionalRaw<AudioClip>(bundle, _name));
        }

        // -- Plain prefab (no mutations) -------------------------------------
        private sealed class PrefabDef : AssetDef
        {
            private readonly System.Action<GameObject> _set;
            private readonly string _name;

            public PrefabDef(System.Action<GameObject> set, string name)
            {
                _set = set;
                _name = name;
            }

            public override void Execute(AssetBundle bundle) =>
                _set(LoadRequiredRaw<GameObject>(bundle, _name));
        }

        // -- Handheld prefab (Rigidbody disabled) ---------------------------
        private sealed class HandheldPrefabDef : AssetDef
        {
            private readonly System.Action<GameObject> _set;
            private readonly string _name;
            private readonly bool _optional;
            private readonly System.Func<GameObject> _fallback;

            public HandheldPrefabDef(
                System.Action<GameObject> set,
                string name,
                bool optional,
                System.Func<GameObject> fallback
            )
            {
                _set = set;
                _name = name;
                _optional = optional;
                _fallback = fallback;
            }

            public override void Execute(AssetBundle bundle)
            {
                var go = _optional
                    ? LoadOptionalRaw<GameObject>(bundle, _name)
                    : LoadRequiredRaw<GameObject>(bundle, _name);
                if (go == null && _fallback != null)
                    go = _fallback();
                if (go != null)
                    DisableRigidbody(go);
                _set(go);
            }
        }

        // -- Networked prefab -----------------------------------------------
        private sealed class NetworkedPrefabDef : AssetDef
        {
            private readonly System.Action<GameObject> _set;
            private readonly string _name;
            private readonly uint _assetId;
            private readonly System.Type _clientSetupType;
            private readonly bool _optional;
            private readonly System.Func<GameObject> _fallback;

            public NetworkedPrefabDef(
                System.Action<GameObject> set,
                string name,
                uint assetId,
                System.Type clientSetupType,
                bool optional,
                System.Func<GameObject> fallback
            )
            {
                _set = set;
                _name = name;
                _assetId = assetId;
                _clientSetupType = clientSetupType;
                _optional = optional;
                _fallback = fallback;
            }

            public override void Execute(AssetBundle bundle)
            {
                var go = _optional
                    ? LoadOptionalRaw<GameObject>(bundle, _name)
                    : LoadRequiredRaw<GameObject>(bundle, _name);
                if (go == null && _fallback != null)
                    go = _fallback();
                if (go == null)
                {
                    _set(null);
                    return;
                }
                EnsureNetworkIdentity(go, _assetId);
                DisableRigidbody(go);
                if (_clientSetupType != null)
                    go.AddComponent(_clientSetupType);
                _set(go);
            }
        }

        // -- Local VFX prefab (network components stripped) -----------------
        private sealed class LocalVfxPrefabDef : AssetDef
        {
            private readonly System.Action<GameObject> _set;
            private readonly string _name;

            public LocalVfxPrefabDef(System.Action<GameObject> set, string name)
            {
                _set = set;
                _name = name;
            }

            public override void Execute(AssetBundle bundle)
            {
                var go = LoadOptionalRaw<GameObject>(bundle, _name);
                if (go != null)
                {
                    StripNetworkComponents(go);
                    GameObject.DontDestroyOnLoad(go);
                }
                else
                {
                    IssaPluginPlugin.Log.LogWarning(
                        $"[AssetLoader] LocalVfxPrefab not found in bundle: '{_name}'"
                    );
                }
                _set(go);
            }
        }

        // ── Factory helpers ──────────────────────────────────────────────────
        // These produce terse, readable entries in BuildAssetDefs().

        private static AssetDef SpriteAsset(
            System.Action<UnityEngine.Sprite> set,
            string name,
            bool optional = false
        ) => new SpriteDef(set, name, optional);

        private static AssetDef Texture(System.Action<Texture2D> set, string name) =>
            new TextureDef(set, name);

        private static AssetDef Audio(System.Action<AudioClip> set, string name) =>
            new AudioDef(set, name);

        private static AssetDef Prefab(System.Action<GameObject> set, string name) =>
            new PrefabDef(set, name);

        private static AssetDef HandheldPrefab(
            System.Action<GameObject> set,
            string name,
            bool optional = false,
            System.Func<GameObject> fallback = null
        ) => new HandheldPrefabDef(set, name, optional, fallback);

        private static AssetDef NetworkedPrefab(
            System.Action<GameObject> set,
            string name,
            uint assetId,
            System.Type clientSetupType = null,
            bool optional = false,
            System.Func<GameObject> fallback = null
        ) => new NetworkedPrefabDef(set, name, assetId, clientSetupType, optional, fallback);

        // Convenience overload: clientSetupType is positional-third and common enough to warrant one.
        private static AssetDef NetworkedPrefab(
            System.Action<GameObject> set,
            string name,
            uint assetId,
            System.Type clientSetupType
        ) => new NetworkedPrefabDef(set, name, assetId, clientSetupType, false, null);

        private static AssetDef LocalVfxPrefab(System.Action<GameObject> set, string name) =>
            new LocalVfxPrefabDef(set, name);

        // ─────────────────────────────────────────────────────────────────────
        //  Low-level utilities
        // ─────────────────────────────────────────────────────────────────────

        /// Finds and opens the asset bundle.  Returns true on success.
        private static bool TryLoadBundle()
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
                    return false;
                }
            }

            _bundle = AssetBundle.LoadFromFile(bundlePath);
            if (_bundle == null)
            {
                IssaPluginPlugin.Log.LogError("[Assets] Failed to load asset bundle.");
                return false;
            }
            return true;
        }

        /// Loads from the bundle and logs an error if the asset is missing.
        private static T LoadRequiredRaw<T>(AssetBundle bundle, string name)
            where T : Object
        {
            var asset = bundle.LoadAsset<T>(name);
            if (asset == null)
                IssaPluginPlugin.Log.LogError($"[Assets] Missing required asset: {name}");
            return asset;
        }

        /// Loads from the bundle; returns null silently if the asset is absent.
        private static T LoadOptionalRaw<T>(AssetBundle bundle, string name)
            where T : Object => bundle.LoadAsset<T>(name);

        /// Loads from the already-open bundle (used in ApplyPostLoadMutations).
        private static T LoadRaw<T>(string name)
            where T : Object => _bundle?.LoadAsset<T>(name);

        /// Ensures the prefab has a NetworkIdentity with a stable assetId.
        /// If no NetworkIdentity exists one is added.  If the baked-in assetId
        /// is 0 (bundle built without Mirror's editor tool), the stable uint is
        /// written via reflection so Mirror's RegisterPrefab doesn't skip it.
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
                    $"[Assets] Added NetworkIdentity to {prefab.name} (assetId={stableAssetId:X8})."
                );
            }
            else if (ni.assetId == 0)
            {
                assetIdField?.SetValue(ni, stableAssetId);
                IssaPluginPlugin.Log.LogInfo(
                    $"[Assets] {prefab.name} had assetId=0; set stable assetId={stableAssetId:X8}."
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[Assets] {prefab.name} already has NetworkIdentity (assetId={ni.assetId:X8})."
                );
            }

            GameObject.DontDestroyOnLoad(prefab);
        }

        /// Sets a Rigidbody to kinematic/no-gravity so a prefab template sitting
        /// at the world origin does not participate in physics.
        /// The relevant behaviour re-enables it in Start() when a real instance spawns.
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

        /// Removes Mirror NetworkBehaviour and NetworkIdentity components from a
        /// prefab that will only ever be used as a local-only visual.
        /// Without this, NetworkTransformReliable and NetworkRigidbodyReliable start
        /// updating every frame and throw NullReferenceException because the prefab
        /// instance has no network context.
        private static void StripNetworkComponents(GameObject go)
        {
            if (go == null)
                return;
            foreach (var c in go.GetComponentsInChildren<NetworkBehaviour>(true))
                Object.DestroyImmediate(c);
            foreach (var ni in go.GetComponentsInChildren<NetworkIdentity>(true))
                Object.DestroyImmediate(ni);
        }
    }
}
