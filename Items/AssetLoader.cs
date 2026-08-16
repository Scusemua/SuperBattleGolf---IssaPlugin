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

        // ── ShapeShifter / SuperShapeShifter ──────────────────────────────────
        // ── Explosive Golf Balls ──────────────────────────────────────────────
        /// Item icon for Explosive Golf Balls. Null until explosive_golf_balls_icon.png is added to the bundle.
        public static Sprite ExplosiveGolfBallsIcon { get; private set; }

        /// Handheld model shown in the player's hand. Null until explosive_golf_balls_handheld.prefab is added.
        public static GameObject ExplosiveGolfBallsHandheldPrefab { get; private set; }

        // ── ShapeShifter shape prefabs ────────────────────────────────────────
        /// Visual-only shape prefabs spawned as children of the golf ball.
        /// Each must contain MeshFilter + MeshRenderer + MeshCollider (convex).
        public static GameObject ShapeShifterShapeCube { get; private set; }
        public static GameObject ShapeShifterShapeDisk { get; private set; }
        public static GameObject ShapeShifterShapeCylinder { get; private set; }
        public static GameObject ShapeShifterShapeCone { get; private set; }
        public static GameObject ShapeShifterShapeAcorn { get; private set; }
        public static GameObject ShapeShifterShapePyramid { get; private set; }
        public static GameObject ShapeShifterShapeIsosphere { get; private set; }

        /// Item icon for Shape Shifter. Null until cube_ball_icon.png is added to the bundle.
        public static Sprite ShapeShifterIcon { get; private set; }

        /// Item icon for Super Shape Shifter. Falls back to ShapeShifterIcon at runtime if absent.
        public static Sprite SuperShapeShifterIcon { get; private set; }

        /// Shared handheld/dropped prefab for both ShapeShifter and SuperShapeShifter.
        /// Null until cube_ball_handheld.prefab is added to the bundle.
        public static GameObject ShapeShifterHandheldPrefab { get; private set; }

        // ── Moon ──────────────────────────────────────────────────────────────
        /// Item icon for Majora's Moon. Null until moon_icon.png is added to the bundle.
        public static Sprite MoonIcon { get; private set; }

        /// Handheld model shown in the player's hand before activation. Null until moon_handheld.prefab is added.
        public static GameObject MoonHandheldPrefab { get; private set; }

        /// Client-only moon VFX prefab that approaches the course. Null until moon.prefab is added.
        private static GameObject _moonVfxPrefab;
        public static GameObject MoonVfxPrefab => Vfx(_moonVfxPrefab);

        /// Networked droppable-item prefab; carries NetworkIdentity, NetworkTransform,
        /// Rigidbody (kinematic), SphereCollider (trigger), Entity, and DroppedCustomItem.
        public static GameObject DroppedCustomItemPrefab { get; private set; }

        // ── Custom VFX kill switch ────────────────────────────────────────────
        //
        // A/B test hook. Every VFX prefab below is exposed through a property that
        // returns null when Global.CustomVfxEnabled is false. Callers already handle
        // a null prefab by skipping instantiation (they must, because assets can be
        // missing from the bundle), so flipping this off disables all custom particle
        // and trail effects at runtime with no other code changes.
        //
        // The point is to isolate whether the mod's own VFX — and the shaders they
        // were converted to during the built-in-to-URP migration — are responsible for
        // frame drops during items like the stealth bomber, predator missile, and any
        // speed boost.
        private static bool VfxOn => ModConfig.Global.CustomVfxEnabled.Value;

        private static GameObject Vfx(GameObject prefab) => VfxOn ? prefab : null;

        // ── Local-only VFX prefabs (Mirror components stripped) ───────────────
        private static GameObject _blackHoleVfxPrefab;
        public static GameObject BlackHoleVfxPrefab => Vfx(_blackHoleVfxPrefab);

        private static GameObject _positionSwapOrbPrefab;
        public static GameObject PositionSwapOrbPrefab => Vfx(_positionSwapOrbPrefab);

        private static GameObject _positionSwapSmokePrefab;
        public static GameObject PositionSwapSmokePrefab => Vfx(_positionSwapSmokePrefab);

        private static GameObject _poisonSplashPrefab;
        public static GameObject PoisonSplashPrefab => Vfx(_poisonSplashPrefab);

        private static GameObject _droneExplosionVfxPrefab;
        public static GameObject DroneExplosionVfxPrefab => Vfx(_droneExplosionVfxPrefab);

        private static GameObject _redBullTrailPrefab;
        public static GameObject RedBullTrailPrefab => Vfx(_redBullTrailPrefab);

        private static GameObject _gravityGunTetherVfxPrefab;
        public static GameObject GravityGunTetherVfxPrefab => Vfx(_gravityGunTetherVfxPrefab);

        private static GameObject _warningParticlePrefab;
        public static GameObject WarningParticlePrefab => Vfx(_warningParticlePrefab);

        private static GameObject _maydaySmokeTrailPrefab;
        public static GameObject MaydaySmokeTrailPrefab => Vfx(_maydaySmokeTrailPrefab);

        private static GameObject _maydayFireTrailPrefab;
        public static GameObject MaydayFireTrailPrefab => Vfx(_maydayFireTrailPrefab);

        private static GameObject _confettiBlastRainbow;
        public static GameObject ConfettiBlastRainbow => Vfx(_confettiBlastRainbow);

        private static GameObject _bloodSplatterPrefab;
        public static GameObject BloodSplatterPrefab => Vfx(_bloodSplatterPrefab);

        /// The blood splatter prefab ignoring the VFX toggle.
        ///
        /// This one is registered with Mirror as a spawnable networked prefab, and that
        /// registration has to happen regardless of the toggle: if it were skipped at
        /// connect time, turning VFX back on mid-session would leave clients unable to
        /// spawn the object. Only the instantiation path should honour the toggle.
        public static GameObject BloodSplatterPrefabRaw => _bloodSplatterPrefab;

        // ── Shared VFX prefabs (used by multiple items) ───────────────────────
        /// Shared explosion VFX.  Used by Javelin, Nuke, and AC130 Mayday.
        private static GameObject _nukeVerticalExplosionVfxPrefab;
        public static GameObject NukeVerticalExplosionVfxPrefab =>
            Vfx(_nukeVerticalExplosionVfxPrefab);

        /// Nuke-specific explosion VFX.  Falls back to NukeVerticalExplosionVfxPrefab
        /// if "nuclear_explosion.prefab" is absent from the bundle.
        private static GameObject _nukeExplosionVfxPrefab;
        public static GameObject NukeExplosionVfxPrefab => Vfx(_nukeExplosionVfxPrefab);

        // Javelin convenience aliases that point at the shared assets.
        public static GameObject JavelinExplosionVfxPrefab => NukeVerticalExplosionVfxPrefab;
        private static GameObject _javelinTrailVfxPrefab;
        public static GameObject JavelinTrailVfxPrefab => Vfx(_javelinTrailVfxPrefab);

        /// Explosion VFX for the AC130 Mayday crash.  Points at the shared
        /// NukeVerticalExplosionVfxPrefab.
        public static GameObject MaydayExplosionVfxPrefab => NukeVerticalExplosionVfxPrefab;

        // ── Impact / crash VFX ────────────────────────────────────────────────
        /// Secondary debris/dust VFX spawned at a crash site (e.g. AC130 impact).
        /// Bundle asset name: <c>impact_vfx.prefab</c>  (fill in if different).
        private static GameObject _impactVfxPrefab;
        public static GameObject ImpactVfxPrefab => Vfx(_impactVfxPrefab);

        // ── Teleporter ────────────────────────────────────────────────────────
        public static Sprite TeleporterIcon { get; private set; }
        public static GameObject TeleporterHandheldPrefab { get; private set; }
        private static GameObject _teleporterVfxPrefab;
        public static GameObject TeleporterVfxPrefab => Vfx(_teleporterVfxPrefab);

        // ── Flamethrower ──────────────────────────────────────────────────────
        public static Sprite FlamethrowerIcon { get; private set; }
        public static GameObject FlamethrowerPrefab { get; private set; }
        private static GameObject _flamethrowerParticlePrefab;
        public static GameObject FlamethrowerParticlePrefab => Vfx(_flamethrowerParticlePrefab);

        private static GameObject _flamethrowerVictimFirePrefab;
        public static GameObject FlamethrowerVictimFirePrefab => Vfx(_flamethrowerVictimFirePrefab);

        // ── Jetpack ───────────────────────────────────────────────────────────
        public static Sprite JetpackIcon { get; private set; }
        public static GameObject JetpackHandheldPrefab { get; private set; }
        private static GameObject _jetpackParticlePrefab;
        public static GameObject JetpackParticlePrefab => Vfx(_jetpackParticlePrefab);

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
        private static GameObject _spinachTrailPrefab;
        public static GameObject SpinachTrailPrefab => Vfx(_spinachTrailPrefab);

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
                Prefab(
                    p => _nukeVerticalExplosionVfxPrefab = p,
                    "NukeVerticalExplosionFire.prefab"
                ),
                // ── Local-only VFX (Mirror components stripped) ───────────────
                LocalVfxPrefab(p => _blackHoleVfxPrefab = p, "black_hole.prefab"),
                LocalVfxPrefab(p => _positionSwapOrbPrefab = p, "position_swap_orb.prefab"),
                LocalVfxPrefab(p => _positionSwapSmokePrefab = p, "position_swap_smoke.prefab"),
                LocalVfxPrefab(p => _poisonSplashPrefab = p, "poison_cloud_vfx.prefab"),
                LocalVfxPrefab(p => _droneExplosionVfxPrefab = p, "drone_explosion.prefab"),
                LocalVfxPrefab(p => _redBullTrailPrefab = p, "red_bull_trail.prefab"),
                LocalVfxPrefab(p => _gravityGunTetherVfxPrefab = p, "gravity_gun_vfx.prefab"),
                LocalVfxPrefab(p => _warningParticlePrefab = p, "warning_particle.prefab"),
                LocalVfxPrefab(p => _maydaySmokeTrailPrefab = p, "smoke_prefab.prefab"),
                LocalVfxPrefab(p => _maydayFireTrailPrefab = p, "fire_torch_intense.prefab"),
                LocalVfxPrefab(p => _confettiBlastRainbow = p, "ConfettiBlastRainbow.prefab"),
                LocalVfxPrefab(p => _bloodSplatterPrefab = p, "blood_explosion_vfx.prefab"),
                LocalVfxPrefab(p => _javelinTrailVfxPrefab = p, "javelin_trail.prefab"),
                // ── Audio ─────────────────────────────────────────────────────
                // AudioClips are addressed without file extensions — Unity compiles
                // audio to an internal format at bundle-build time.
                Audio(p => AC130AboveClip = p, "ac130_above"),
                Audio(p => HomerunAudioClip = p, "homerun"),
                Audio(p => MaydayAlarmClip = p, "missile_locked"),
                Audio(p => MaydayImpactClip = p, "etfx_explosion_nuke"),
                // ── Impact VFX ────────────────────────────────────────────────
                LocalVfxPrefab(p => _impactVfxPrefab = p, "NukeVerticalExplosionFire.prefab"),
                // ── Teleporter ────────────────────────────────────────────────
                // TODO: replace asset names with the real bundle names once known.
                SpriteAsset(p => TeleporterIcon = p, "teleporter_icon.png"),
                HandheldPrefab(p => TeleporterHandheldPrefab = p, "teleporter_handheld.prefab"),
                LocalVfxPrefab(p => _teleporterVfxPrefab = p, "position_swap_smoke.prefab"),
                // ── Flamethrower ──────────────────────────────────────────────
                // TODO: replace asset names with the real bundle names once known.
                SpriteAsset(p => FlamethrowerIcon = p, "flamethrower_icon.png"),
                HandheldPrefab(p => FlamethrowerPrefab = p, "flamethrower.prefab"),
                LocalVfxPrefab(p => _flamethrowerParticlePrefab = p, "flamethrower_vfx.prefab"),
                LocalVfxPrefab(
                    p => _flamethrowerVictimFirePrefab = p,
                    "flamethrower_victim_fire.prefab"
                ),
                // ── Jetpack ───────────────────────────────────────────────────
                // TODO: replace asset names with the real bundle names once known.
                SpriteAsset(p => JetpackIcon = p, "jetpack_icon.png"),
                HandheldPrefab(p => JetpackHandheldPrefab = p, "jetpack_handheld.prefab"),
                LocalVfxPrefab(p => _jetpackParticlePrefab = p, "jetpack_particles.prefab"),
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
                LocalVfxPrefab(p => _spinachTrailPrefab = p, "spinach_trail.prefab"),
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
                LocalVfxPrefab(p => _moonVfxPrefab = p, "moon.prefab"),
                // ── ShapeShifter / SuperShapeShifter ─────────────────────────
                Prefab(p => ShapeShifterShapeCube = p, "golf_ball_cube.prefab"),
                Prefab(p => ShapeShifterShapeDisk = p, "golf_ball_disk.prefab"),
                Prefab(p => ShapeShifterShapeCylinder = p, "golf_ball_cylinder.prefab"),
                Prefab(p => ShapeShifterShapeCone = p, "golf_ball_cone.prefab"),
                Prefab(p => ShapeShifterShapeAcorn = p, "golf_ball_acorn.prefab"),
                Prefab(p => ShapeShifterShapeIsosphere = p, "golf_ball_isosphere.prefab"),
                Prefab(p => ShapeShifterShapePyramid = p, "golf_ball_pyramid.prefab"),
                SpriteAsset(p => ShapeShifterIcon = p, "cube_ball_icon.png", optional: true),
                SpriteAsset(
                    p => SuperShapeShifterIcon = p,
                    "super_cube_ball_icon.png",
                    optional: true
                ),
                HandheldPrefab(
                    p => ShapeShifterHandheldPrefab = p,
                    "rubixcube.prefab",
                    optional: true,
                    fallback: BuildShapeShifterHandheldFallback
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
            //
            // Both sides use the raw backing fields: the public properties return null
            // while the VFX toggle is off, so reading them here would resolve the
            // fallback to null and permanently lose the reference for this session.
            _nukeExplosionVfxPrefab =
                LoadRaw<GameObject>("nuclear_explosion.prefab") ?? _nukeVerticalExplosionVfxPrefab;

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

            // SuperShapeShifter icon falls back to ShapeShifter icon when the dedicated one is absent.
            SuperShapeShifterIcon ??= ShapeShifterIcon;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Fallback builders
        //
        //  Called by an AssetDef when the named bundle asset is absent.
        //  Each builder returns a fully configured, DontDestroyOnLoad prefab.
        //  Kept here so the asset table remains readable and these details are
        //  all in one place.
        // ─────────────────────────────────────────────────────────────────────

        private static GameObject BuildShapeShifterHandheldFallback()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ShapeShifterHandheld_Fallback";
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
