The `Configuration.cs` class has grown to be enormous. 

🧠 Recommended Architecture
1. Split into Per-Item Config Classes

Instead of:

```
public static ConfigEntry<float> BomberAltitude;
public static ConfigEntry<float> BomberSpeed;
...

```

Do this:

```
public class StealthBomberConfig
{
    public ConfigEntry<float> Altitude { get; private set; }
    public ConfigEntry<float> Speed { get; private set; }
    public ConfigEntry<float> RocketInterval { get; private set; }

    public StealthBomberConfig(ConfigFile cfg)
    {
        Altitude = cfg.Bind("StealthBomber", "Altitude", 50f, "...");
        Speed = cfg.Bind("StealthBomber", "Speed", 40f, "...");
        RocketInterval = cfg.Bind("StealthBomber", "RocketInterval", 0.15f, "...");
    }
}
```

2. Group Them in a Root Config Container

```
public static class ModConfig
{
    public static StealthBomberConfig StealthBomber { get; private set; }
    public static BaseballBatConfig BaseballBat { get; private set; }
    public static AC130Config AC130 { get; private set; }

    public static void Initialize(ConfigFile cfg)
    {
        StealthBomber = new StealthBomberConfig(cfg);
        BaseballBat = new BaseballBatConfig(cfg);
        AC130 = new AC130Config(cfg);
    }
}
```

3. Usage Becomes WAY Cleaner

Before:

```
Configuration.BomberAltitude.Value
```

After:

```
ModConfig.StealthBomber.Altitude.Value
```

This is huge for readability

4. Base Class for Consistency

You can standardize things:

```
public abstract class ConfigSection
{
    protected ConfigFile Cfg;

    protected ConfigSection(ConfigFile cfg)
    {
        Cfg = cfg;
    }
}
```

Then:

```
public class StealthBomberConfig : ConfigSection
{
    public ConfigEntry<float> Altitude { get; }
    
    public StealthBomberConfig(ConfigFile cfg) : base(cfg)
    {
        Altitude = cfg.Bind("StealthBomber", "Altitude", 50f, "...");
    }
}
```

5. Split Files (HIGHLY Recommended)

Instead of one giant file:

```
Config/
 ├── ModConfig.cs
 ├── BaseballBatConfig.cs
 ├── StealthBomberConfig.cs
 ├── AC130Config.cs
 ├── BearConfig.cs
```

6. Big Improvement: Remove String Duplication

Right now you repeat:

```
cfg.Bind("StealthBomber", "Altitude", ...)
```

You can centralize:

```
private const string Section = "StealthBomber";

Altitude = cfg.Bind(Section, "Altitude", 50f, "...");
```

7. Advanced Option (Very Clean): Attribute-Based Auto Binding

If you want to go next level, you can define configs like:

```
public class StealthBomberConfig
{
    [ConfigValue("Altitude", 50f, "Height above the map")]
    public ConfigEntry<float> Altitude { get; private set; }
}
```

Then use reflection to bind everything automatically.

This removes 80% of boilerplate—but adds complexity. Given your project size (~200k LOC), it might actually be worth it.

---

# How You Scale This to Your Full Config

For every section in your original file (AC130, Bear, Donut, etc.):

1. Create a class

```
public class AC130Config : ConfigSection
{
    protected override string SectionName => "AC130";

    [ConfigValue("Uses", 1f, "Number of uses")]
    public ConfigEntry<float> Uses { get; private set; }

    [ConfigValue("GiveKey", Key.F11, "Spawn key")]
    public ConfigEntry<Key> GiveKey { get; private set; }

    // Copy/paste rest → delete cfg.Bind → replace with attributes
}
```

2. Register it in ModConfig

```
public static AC130Config AC130 { get; private set; }
```

```
AC130 = new AC130Config(cfg);
```

⚡ Why This Is a HUGE Upgrade

Before:
- 1000+ lines in one file
- Tons of repeated cfg.Bind(...)
- Painful to navigate
- Easy to break

After:
- Each item = self-contained module
- Adding config = 1 line attribute
- No boilerplate binding code
- Clean usage:
```
ModConfig.AC130.Uses.Value
```

---

Larger Example:

```
// FULLY REFACTORED CONFIGURATION SYSTEM
// Drop-in replacement for your large Configuration.cs
// Uses reflection + attributes to eliminate boilerplate

using BepInEx.Configuration;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace YourModNamespace.Config
{
    #region Attribute

    [AttributeUsage(AttributeTargets.Property)]
    public class ConfigValueAttribute : Attribute
    {
        public string Key { get; }
        public object DefaultValue { get; }
        public string Description { get; }

        public ConfigValueAttribute(string key, object defaultValue, string description = "")
        {
            Key = key;
            DefaultValue = defaultValue;
            Description = description;
        }
    }

    #endregion

    #region Base Class

    public abstract class ConfigSection
    {
        protected abstract string SectionName { get; }

        protected ConfigSection(ConfigFile config)
        {
            BindAll(config);
        }

        private void BindAll(ConfigFile config)
        {
            var props = GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<ConfigValueAttribute>() != null);

            foreach (var prop in props)
            {
                var attr = prop.GetCustomAttribute<ConfigValueAttribute>();

                var entryType = typeof(ConfigEntry<>).MakeGenericType(prop.PropertyType.GenericTypeArguments[0]);

                var bindMethod = typeof(ConfigFile).GetMethods()
                    .First(m => m.Name == "Bind" && m.GetParameters().Length == 4)
                    .MakeGenericMethod(prop.PropertyType.GenericTypeArguments[0]);

                var entry = bindMethod.Invoke(config, new object[]
                {
                    SectionName,
                    attr.Key,
                    attr.DefaultValue,
                    new ConfigDescription(attr.Description)
                });

                prop.SetValue(this, entry);
            }
        }
    }

    #endregion

    #region Config Sections

    public class GeneralConfig : ConfigSection
    {
        protected override string SectionName => "General";

        public GeneralConfig(ConfigFile cfg) : base(cfg) { }

        [ConfigValue("EnableMod", true, "Enable or disable the mod")]
        public ConfigEntry<bool> EnableMod { get; private set; }
    }

    public class BatConfig : ConfigSection
    {
        protected override string SectionName => "Bat";

        public BatConfig(ConfigFile cfg) : base(cfg) { }

        [ConfigValue("Uses", 3f, "Number of uses")]
        public ConfigEntry<float> Uses { get; private set; }

        [ConfigValue("Force", 50f, "Knockback force")]
        public ConfigEntry<float> Force { get; private set; }
    }

    public class BomberConfig : ConfigSection
    {
        protected override string SectionName => "Bomber";

        public BomberConfig(ConfigFile cfg) : base(cfg) { }

        [ConfigValue("Uses", 1f, "Number of uses")]
        public ConfigEntry<float> Uses { get; private set; }

        [ConfigValue("Cooldown", 10f, "Cooldown between uses")]
        public ConfigEntry<float> Cooldown { get; private set; }
    }

    public class ExplosionConfig : ConfigSection
    {
        protected override string SectionName => "Explosions";

        public ExplosionConfig(ConfigFile cfg) : base(cfg) { }

        [ConfigValue("Radius", 10f, "Explosion radius")]
        public ConfigEntry<float> Radius { get; private set; }

        [ConfigValue("Force", 1000f, "Explosion force")]
        public ConfigEntry<float> Force { get; private set; }
    }

    public class SpawnConfig : ConfigSection
    {
        protected override string SectionName => "Spawns";

        public SpawnConfig(ConfigFile cfg) : base(cfg) { }

        [ConfigValue("SpawnRate", 0.5f, "Spawn chance")]
        public ConfigEntry<float> SpawnRate { get; private set; }
    }

    #endregion

    #region Root Config

    public static class ModConfig
    {
        public static GeneralConfig General { get; private set; }
        public static BatConfig Bat { get; private set; }
        public static BomberConfig Bomber { get; private set; }
        public static ExplosionConfig Explosions { get; private set; }
        public static SpawnConfig Spawns { get; private set; }

        public static void Init(ConfigFile cfg)
        {
            General = new GeneralConfig(cfg);
            Bat = new BatConfig(cfg);
            Bomber = new BomberConfig(cfg);
            Explosions = new ExplosionConfig(cfg);
            Spawns = new SpawnConfig(cfg);
        }
    }

    #endregion
}
```